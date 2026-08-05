using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static LinphoneWinUI.Interop.NativeMethods;

namespace LinphoneWinUI.Interop;

// Аккаунты (мультиаккаунт) и хранение SIP-паролей (cred.dat, DPAPI).
// Часть LinphoneCore — выполняется на потоке ядра под сериализацией фасада.
internal sealed partial class LinphoneCore
{
    /// <summary>Логин аккаунта по умолчанию (из сохранённой конфигурации), либо null.</summary>
    public string? GetDefaultAccountUsername()
    {
        if (_core == IntPtr.Zero) return null;
        IntPtr acc = linphone_core_get_default_account(_core);
        if (acc == IntPtr.Zero) return null;
        IntPtr p = linphone_account_get_params(acc);
        if (p == IntPtr.Zero) return null;
        IntPtr addr = linphone_account_params_get_identity_address(p);
        if (addr == IntPtr.Zero) return null;
        return PtrToUtf8(linphone_address_get_username(addr));
    }

    /// <summary>Данные аккаунта по умолчанию для шапки/совместимости (null, если аккаунтов нет).</summary>
    public AccountInfo? GetAccountInfo()
    {
        if (_core == IntPtr.Zero) return null;
        IntPtr acc = linphone_core_get_default_account(_core);
        return acc == IntPtr.Zero ? null : ReadAccount(acc, acc);
    }

    /// <summary>Все настроенные аккаунты (для списка в настройках и переключателя в шапке).</summary>
    public List<AccountInfo> GetAccounts()
    {
        var result = new List<AccountInfo>();
        if (_core == IntPtr.Zero) return result;
        IntPtr def = linphone_core_get_default_account(_core);
        IntPtr list = linphone_core_get_account_list(_core); // const, НЕ освобождать
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr acc = bctbx_list_get_data(it);
            if (acc == IntPtr.Zero) continue;
            var info = ReadAccount(acc, def);
            if (info.HasValue) result.Add(info.Value);
        }
        return result;
    }

    /// <summary>Разбирает один LinphoneAccount в AccountInfo. defaultAcc — аккаунт по умолчанию для пометки.</summary>
    private AccountInfo? ReadAccount(IntPtr acc, IntPtr defaultAcc)
    {
        IntPtr p = linphone_account_get_params(acc);
        if (p == IntPtr.Zero) return null;

        string login = "", displayName = "";
        IntPtr identity = linphone_account_params_get_identity_address(p);
        if (identity != IntPtr.Zero)
        {
            login = PtrToUtf8(linphone_address_get_username(identity)) ?? "";
            displayName = PtrToUtf8(linphone_address_get_display_name(identity)) ?? "";
        }

        string server = ""; int port = 0;
        IntPtr srv = linphone_account_params_get_server_address(p);
        if (srv != IntPtr.Zero)
        {
            server = PtrToUtf8(linphone_address_get_domain(srv)) ?? "";
            port = linphone_address_get_port(srv);
        }

        string transport = (LinphoneTransportType)linphone_account_params_get_transport(p) switch
        {
            LinphoneTransportType.Tcp => "tcp",
            LinphoneTransportType.Tls => "tls",
            _ => "udp",
        };
        int expires = linphone_account_params_get_expires(p);
        string sip = $"sip:{login}@{server}"; // канонический ключ аккаунта
        string password = LoadPasswordLocal(login, server);
        if (string.IsNullOrEmpty(password)) password = FindPassword(login, server);
        var state = (LinphoneRegistrationState)linphone_account_get_state(acc);

        return new AccountInfo(displayName, login, password, server, port, transport, expires, sip,
            acc == defaultAcc, state);
    }

    /// <summary>Ищет сохранённый пароль для логина (с доменом и без — на случай несовпадения realm).</summary>
    private string FindPassword(string login, string server)
    {
        if (_core == IntPtr.Zero || string.IsNullOrEmpty(login)) return "";
        IntPtr a = linphone_core_find_auth_info(_core, null, login, server);
        if (a == IntPtr.Zero) a = linphone_core_find_auth_info(_core, null, login, null);
        return a == IntPtr.Zero ? "" : (PtrToUtf8(linphone_auth_info_get_password(a)) ?? "");
    }

    // liblinphone после digest-аутентификации стирает plaintext-пароль (хранит ha1),
    // поэтому свои пароли храним сами в локальном файле профиля. Формат: по строке на
    // аккаунт «key<TAB>password», где key = "login@server"; файл целиком зашифрован DPAPI
    // (ключ пользователя Windows — другой пользователь/машина расшифровать не смогут).
    // Старые незашифрованные файлы (и совсем старый формат login\npassword) читаются на
    // совместимость и сразу перешифровываются.
    // Путь и кэш — ЭКЗЕМПЛЯРНЫЕ, а не статические. Статика делала хранилище паролей
    // непроверяемым: интеграционный тест с тестовым профилем всё равно писал бы в рабочий
    // cred.dat пользователя и подменял его пароли. При тестовом профиле файл ложится рядом
    // с ним, боевое поведение не меняется.
    private string CredPath => _profileDirOverride != null
        ? Path.Combine(_profileDirOverride, "cred.dat")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                       "LinphoneWinUI", "cred.dat");

    // Дополнительная энтропия DPAPI: привязывает блоб к приложению (не секрет).
    private static readonly byte[] CredEntropy = System.Text.Encoding.UTF8.GetBytes("L-Phone.cred.v1");

    // Кэш расшифрованных паролей: ReadAccount дергается на каждое событие регистрации
    // (re-REGISTER каждые expires × N аккаунтов) — без кэша это файл + DPAPI на UI-потоке
    // по многу раз в минуту. Инвалидация — в SaveCreds.
    private Dictionary<string, string>? _credCache;

    private Dictionary<string, string> LoadCreds()
    {
        if (_credCache != null) return _credCache;
        var dict = LoadCredsFromDisk();
        _credCache = dict;
        return dict;
    }

    private Dictionary<string, string> LoadCredsFromDisk()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(CredPath)) return dict;
            byte[] raw = File.ReadAllBytes(CredPath);
            string text;
            bool legacyPlaintext = false;
            try
            {
                text = System.Text.Encoding.UTF8.GetString(
                    System.Security.Cryptography.ProtectedData.Unprotect(
                        raw, CredEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }
            catch
            {
                // Не DPAPI-блоб — старый незашифрованный файл.
                text = System.Text.Encoding.UTF8.GetString(raw);
                legacyPlaintext = true;
            }

            if (!text.Contains('\t'))
            {
                // Совсем старый формат: login\npassword (один аккаунт, ключ = только login).
                int nl = text.IndexOf('\n');
                if (nl > 0) dict[text[..nl].Trim()] = text[(nl + 1)..].TrimEnd('\r', '\n');
            }
            else
            {
                foreach (var line in text.Split('\n'))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0) dict[line[..tab]] = line[(tab + 1)..].TrimEnd('\r');
                }
            }

            if (legacyPlaintext && dict.Count > 0) SaveCreds(dict); // миграция: перешифровать на диске
        }
        catch { }
        return dict;
    }

    private void SaveCreds(Dictionary<string, string> dict)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CredPath)!);
            string text = string.Join("\n", dict.Select(kv => kv.Key + "\t" + kv.Value));
            File.WriteAllBytes(CredPath, System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(text), CredEntropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser));
            _credCache = dict;   // кэш всегда отражает то, что на диске
        }
        catch (Exception ex)
        {
            _credCache = null;   // не удалось записать — не доверяем кэшу
            Log?.Invoke("ОШИБКА: пароль аккаунта не сохранён на диск (cred.dat): " + ex.Message);
        }
    }

    private void SavePasswordLocal(string login, string server, string password)
    {
        var dict = LoadCreds();
        dict[$"{login}@{server}"] = password;
        SaveCreds(dict);
    }

    private string LoadPasswordLocal(string login, string server)
    {
        var dict = LoadCreds();
        if (dict.TryGetValue($"{login}@{server}", out var pwd)) return pwd;
        if (dict.TryGetValue(login, out var legacy)) return legacy; // старый формат / ключ без сервера
        return "";
    }

    private void RemovePasswordLocal(string login, string server)
    {
        var dict = LoadCreds();
        if (dict.Remove($"{login}@{server}") | dict.Remove(login)) SaveCreds(dict);
    }

    /// <summary>Добавляет новый или обновляет существующий аккаунт (по identity sip:login@server),
    /// НЕ затрагивая остальные. Регистрирует его. makeDefault или первый аккаунт → делает основным.</summary>
    public void AddOrUpdateAccount(string displayName, string login, string password, string server, int port, string transport, int expires, bool makeDefault)
    {
        if (_core == IntPtr.Zero) throw new InvalidOperationException("Core не запущен.");

        // Пустое поле пароля → берём ранее сохранённый (liblinphone обратно plaintext не отдаёт).
        if (string.IsNullOrEmpty(password))
        {
            password = LoadPasswordLocal(login, server);
            if (string.IsNullOrEmpty(password)) password = FindPassword(login, server);
        }

        string key = $"sip:{login}@{server}";
        var accounts = SnapshotAccounts();
        IntPtr def = linphone_core_get_default_account(_core);
        bool hadAny = accounts.Count > 0, wasDefault = false;

        // Удаляем существующий аккаунт с таким же identity (обновление = пересоздание).
        // Был ли он основным — запоминаем ДО удаления (после удаления core переназначит default).
        foreach (IntPtr acc in accounts)
        {
            var info = ReadAccount(acc, def);
            if (info.HasValue && info.Value.SipAddress == key)
            {
                wasDefault = acc == def;
                linphone_core_remove_account(_core, acc);
            }
        }
        // Старую auth_info под этот логин убираем, чтобы не плодить дубли.
        RemoveAuthInfo(login, server);

        // Все созданные здесь объекты приходят с ref=1 и наши: add_auth_info копирует,
        // set_*_address и create/add_account берут свои ссылки — наши отпускаем.
        IntPtr auth = linphone_factory_create_auth_info(_factory, login, null, password, null, null, server);
        linphone_core_add_auth_info(_core, auth);
        if (auth != IntPtr.Zero) { try { linphone_auth_info_unref(auth); } catch { } }

        IntPtr ap = linphone_core_create_account_params(_core);

        IntPtr identity = linphone_factory_create_address(_factory, key);
        if (identity == IntPtr.Zero)
        {
            if (ap != IntPtr.Zero) { try { linphone_account_params_unref(ap); } catch { } }
            throw new InvalidOperationException($"Некорректный SIP identity: {key}");
        }
        if (!string.IsNullOrWhiteSpace(displayName))
            linphone_address_set_display_name(identity, displayName);
        linphone_account_params_set_identity_address(ap, identity);
        try { linphone_address_unref(identity); } catch { }

        string hostPort = port > 0 ? $"{server}:{port}" : server;
        IntPtr serverAddr = linphone_factory_create_address(_factory, $"sip:{hostPort};transport={transport}");
        if (serverAddr == IntPtr.Zero)
        {
            if (ap != IntPtr.Zero) { try { linphone_account_params_unref(ap); } catch { } }
            throw new InvalidOperationException($"Некорректный адрес сервера: {hostPort}");
        }
        linphone_account_params_set_server_address(ap, serverAddr);
        try { linphone_address_unref(serverAddr); } catch { }

        if (expires > 0) linphone_account_params_set_expires(ap, expires);
        linphone_account_params_enable_register(ap, 1);

        IntPtr account = linphone_core_create_account(_core, ap);
        if (ap != IntPtr.Zero) { try { linphone_account_params_unref(ap); } catch { } }
        linphone_core_add_account(_core, account);
        // Первый аккаунт всегда default; обновляемый сохраняет прежний статус основного.
        if (makeDefault || !hadAny || wasDefault)
            linphone_core_set_default_account(_core, account);
        if (account != IntPtr.Zero) { try { linphone_account_unref(account); } catch { } }

        if (!string.IsNullOrEmpty(password)) SavePasswordLocal(login, server, password);
        SyncConfig();
        Log?.Invoke($"Аккаунт сохранён: {key} (transport={transport})");
    }

    /// <summary>Удаляет конкретный аккаунт (по sip:login@server) и его учётные данные.</summary>
    public void RemoveAccount(string sipIdentity)
    {
        if (_core == IntPtr.Zero || string.IsNullOrEmpty(sipIdentity)) return;
        IntPtr def = linphone_core_get_default_account(_core);
        bool removedDefault = false;
        string login = "", server = "";

        foreach (IntPtr acc in SnapshotAccounts())
        {
            var info = ReadAccount(acc, def);
            if (info.HasValue && info.Value.SipAddress == sipIdentity)
            {
                login = info.Value.Login; server = info.Value.Server;
                removedDefault = acc == def;
                linphone_core_remove_account(_core, acc);
            }
        }

        if (login.Length > 0) { RemoveAuthInfo(login, server); RemovePasswordLocal(login, server); }

        // Если убрали основной — назначаем основным первый из оставшихся.
        if (removedDefault)
        {
            var rest = SnapshotAccounts();
            if (rest.Count > 0) linphone_core_set_default_account(_core, rest[0]);
        }
        SyncConfig();
        Log?.Invoke($"Аккаунт удалён: {sipIdentity}");
    }

    /// <summary>Делает указанный аккаунт основным (исходящие пойдут с него).</summary>
    public void SetDefaultAccount(string sipIdentity)
    {
        if (_core == IntPtr.Zero || string.IsNullOrEmpty(sipIdentity)) return;
        foreach (IntPtr acc in SnapshotAccounts())
        {
            var info = ReadAccount(acc, IntPtr.Zero);
            if (info.HasValue && info.Value.SipAddress == sipIdentity)
            {
                linphone_core_set_default_account(_core, acc);
                SyncConfig();
                Log?.Invoke($"Основной аккаунт: {sipIdentity}");
                return;
            }
        }
    }

    /// <summary>Снимок указателей аккаунтов в C#-список — чтобы безопасно удалять/менять
    /// аккаунты, не итерируя по нативному списку core во время его изменения.</summary>
    private List<IntPtr> SnapshotAccounts()
    {
        var result = new List<IntPtr>();
        if (_core == IntPtr.Zero) return result;
        IntPtr list = linphone_core_get_account_list(_core); // const, НЕ освобождать
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr acc = bctbx_list_get_data(it);
            if (acc != IntPtr.Zero) result.Add(acc);
        }
        return result;
    }

    /// <summary>Удаляет сохранённую auth_info под данный логин/сервер (по realm и без realm).</summary>
    private void RemoveAuthInfo(string login, string server)
    {
        if (_core == IntPtr.Zero || string.IsNullOrEmpty(login)) return;
        foreach (var domain in new string?[] { server, null })
        {
            IntPtr a = linphone_core_find_auth_info(_core, null, login, domain);
            if (a != IntPtr.Zero) linphone_core_remove_auth_info(_core, a);
        }
    }
}
