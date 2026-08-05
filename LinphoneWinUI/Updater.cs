using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LinphoneWinUI;

/// <summary>Описание доступной версии из update.json.</summary>
public sealed record UpdateInfo(string Version, string Url, string Notes);

/// <summary>
/// Проверка и установка обновлений.
///
/// Источник — статический <c>update.json</c> на raw.githubusercontent.com, а установщик берётся
/// из release-ассетов. Вместо GitHub API это сделано намеренно: у неавторизованных запросов к API
/// лимит 60 в час НА IP, а десятки рабочих мест сидят за одним внешним адресом — лимит выбирался бы
/// в первые минуты рабочего дня, и обновления просто перестали бы находиться. raw и release-ассеты
/// раздаются через CDN и под этот лимит не попадают.
///
/// Доверие обеспечивает НЕ транспорт, а подпись: скачанный файл проверяется через WinVerifyTrust,
/// и отпечаток подписавшего сверяется с нашим. Подменённый установщик не пройдёт проверку даже
/// если его отдали по открытому HTTP.
/// </summary>
public static class Updater
{
    /// <summary>Отпечатки сертификатов, которыми подписываются наши сборки (см. docs/code-signing.md).
    ///
    /// СПИСОК, а не одно значение, и это принципиально. Ротация сертификата с единственным
    /// зашитым отпечатком — операция без права на ошибку: клиент проверяет подпись отпечатком
    /// ИЗ СВОЕЙ сборки, поэтому сборка, подписанная новым сертификатом, будет отвергнута всеми
    /// уже установленными клиентами. Обновиться, чтобы узнать новый отпечаток, они тоже не
    /// смогут — замкнутый круг, из которого выход только через ручную переустановку у каждого.
    ///
    /// Порядок ротации: (1) добавить БУДУЩИЙ отпечаток сюда вторым и выпустить эту версию;
    /// (2) дождаться, пока она разойдётся; (3) только потом начать подписывать новым ключом;
    /// (4) старый отпечаток убрать ещё через релиз.
    ///
    /// Жёсткость проверки осознанная: лучше не обновиться, чем поставить чужое.</summary>
    public static readonly string[] TrustedThumbprints =
    {
        "3249411F7BECD99BED5C27193A01683463B8AD99",   // рабочий (самоподписанный, см. docs/code-signing.md)
        "8FBBDC6720DA19411888D29902322352CF7BAB76",   // РЕЗЕРВНЫЙ, до 03.08.2051 — ключ хранится офлайн
    };

    /// <summary>Где лежит описание версии. Меняется в rc [app] update_url.</summary>
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/romanlat68/l-phone-windows/main/update.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static Updater()
    {
        // GitHub отдаёт 403 запросам без User-Agent.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("L-Phone-Updater");
    }

    /// <summary>Разбор update.json. Возвращает null на любом мусоре — обновление не то место,
    /// где стоит падать: не разобрали, значит просто не предлагаем.</summary>
    public static UpdateInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            string? url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            string notes = root.TryGetProperty("notes", out var n) ? (n.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url)) return null;
            // Ссылку принимаем только https: иначе update.json смог бы увести клиент на любой хост
            // по открытому каналу. Подпись мы всё равно проверим, но лишний шаг к атаке не нужен.
            if (!url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return new UpdateInfo(version!.TrimStart('v', 'V'), url, notes);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Строго ли candidate новее current. Нечисловые части версии игнорируем:
    /// «1.2.0» и «v1.2» должны сравниваться одинаково.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var a = ParseVersion(candidate);
        var b = ParseVersion(current);
        return a != null && b != null && a > b;
    }

    private static Version? ParseVersion(string? s)
    {
        s = (s ?? "").Trim().TrimStart('v', 'V');
        if (s.Length == 0) return null;
        // Version.TryParse не берёт «1.2» без второй точки в некоторых формах — дополняем.
        int dots = 0;
        foreach (char c in s) if (c == '.') dots++;
        while (dots < 2) { s += ".0"; dots++; }
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Текущая версия приложения.</summary>
    public static string CurrentVersion =>
        typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Есть ли версия новее текущей. null — обновления нет либо проверить не удалось
    /// (сеть, мусор в json): молчим, это фоновая проверка, а не действие пользователя.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string? feedUrl = null, CancellationToken ct = default)
    {
        // Адрес приходит из rc [app] update_url, то есть из файла, который правят руками.
        // Мусор в нём НЕ должен доходить до HttpClient: относительный или битый URI даёт
        // InvalidOperationException, и он не ловился фильтром ниже — исключение уходило из
        // async void обработчика и спасалось только глобальным перехватчиком XAML, оставляя
        // запись в crash.log. Проверено на живой машине (crash.log от 03.08).
        if (!TryFeedUri(feedUrl, out Uri? uri)) return null;
        try
        {
            var info = Parse(await Http.GetStringAsync(uri, ct));
            return info != null && IsNewer(info.Version, CurrentVersion) ? info : null;
        }
        // Ловим ШИРОКО и сознательно: это фоновая проверка обновления, у неё нет ни одного
        // исключения, ради которого стоило бы потревожить пользователя. Перечислять типы
        // оказалось ошибкой — список всегда неполон (HttpIOException, OperationCanceledException,
        // InvalidOperationException…), а цена промаха здесь несоразмерна пользе точности.
        catch (Exception) { return null; }
    }

    /// <summary>Годен ли адрес ленты обновлений: абсолютный http/https и ничего больше.
    /// file:// и произвольные схемы отсюда открыли бы путь к чтению чужих файлов, а
    /// относительный адрес — к исключению внутри HttpClient.</summary>
    internal static bool TryFeedUri(string? feedUrl, out Uri? uri)
    {
        string raw = string.IsNullOrWhiteSpace(feedUrl) ? DefaultFeedUrl : feedUrl!.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }
        uri = null;
        return false;
    }

    /// <summary>Скачать установщик во временный файл. Возвращает путь или null.</summary>
    public static async Task<string?> DownloadAsync(UpdateInfo info, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "L-Phone-update");
        try
        {
            EnsurePrivateDir(dir);
            string path = Path.Combine(dir, $"L-Phone-Setup-{info.Version}.exe");
            using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = File.Create(path);
                await src.CopyToAsync(dst, ct);
            }
            return path;
        }
        // См. CheckAsync: перечислять типы здесь так же бессмысленно. Не скачали — не
        // предлагаем обновиться, и это всё, что вызывающему нужно знать.
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Каталог загрузки, закрытый от других пользователей машины.
    ///
    /// Между <see cref="IsTrusted"/> и запуском установщика есть окно, в котором файл можно
    /// подменить (TOCTOU), а ставится он с правами администратора. Общий %TEMP% для этого
    /// открыт: подкаталог в нём наследует права, позволяющие другому пользователю писать
    /// в наши файлы. Закрываем каталог явно — тогда подменять некому, и проверка подписи
    /// остаётся действительной до самого запуска.
    /// </summary>
    private static void EnsurePrivateDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            // Каталог мог создать кто-то другой (тот же %TEMP%) — его содержимому доверять
            // нельзя. Сносим и создаём заново со своим DACL.
            try
            {
                var owner = new DirectoryInfo(dir).GetAccessControl()
                    .GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
                var self = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                if (owner == null || self == null || !owner.Equals(self)) Directory.Delete(dir, recursive: true);
                else return;
            }
            catch { try { Directory.Delete(dir, recursive: true); } catch { } }
        }

        var sec = new System.Security.AccessControl.DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const System.Security.AccessControl.InheritanceFlags inherit =
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
            System.Security.AccessControl.InheritanceFlags.ObjectInherit;
        foreach (var sid in new[]
        {
            System.Security.Principal.WindowsIdentity.GetCurrent().User,
            new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
        })
        {
            if (sid == null) continue;
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                sid, System.Security.AccessControl.FileSystemRights.FullControl, inherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
        }
        try { new DirectoryInfo(dir).Create(sec); }        // атомарно: без окна с чужим DACL
        catch { Directory.CreateDirectory(dir); }          // не смогли — лучше без ACL, чем без обновлений
    }

    /// <summary>Подписан ли файл НАШИМ сертификатом и цела ли подпись.
    /// Это единственная настоящая защита канала обновлений: без неё подменённый на пути файл
    /// установился бы с правами администратора.</summary>
    public static bool IsTrusted(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            // 1. Подпись цела и цепочка строится (WinVerifyTrust — та же проверка, что у Windows).
            if (!VerifyTrust(path)) return false;
            // 2. Подписал именно наш сертификат, а не любой валидный.
            // Разбор блоба — через X509CertificateLoader (конструктор X509Certificate2 устарел).
            // А вот САМО чтение сертификата подписи из Authenticode-файла замены в .NET пока
            // не имеет: X509CertificateLoader работает с байтами, а достать их из подписанного
            // exe умеет только CreateFromSignedFile. Подавляем адресно — заменить нечем, и
            // молча выключать проверку отпечатка (единственную защиту канала обновлений) хуже.
#pragma warning disable SYSLIB0057
            byte[] raw = X509Certificate.CreateFromSignedFile(path).GetRawCertData();
#pragma warning restore SYSLIB0057
            using var cert = X509CertificateLoader.LoadCertificate(raw);
            return IsOurThumbprint(cert.Thumbprint);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException)
        {
            return false;   // не подписан вовсе
        }
    }

    /// <summary>Наш ли это отпечаток. Сравнение регистронезависимое и терпимо к пробелам:
    /// certutil и оснастка сертификатов показывают отпечаток блоками по два символа, и
    /// скопированное оттуда значение иначе молча не совпало бы ни с чем.</summary>
    internal static bool IsOurThumbprint(string? thumbprint)
    {
        string t = Normalize(thumbprint);
        if (t.Length == 0) return false;
        foreach (string known in TrustedThumbprints)
            if (t.Equals(Normalize(known), StringComparison.OrdinalIgnoreCase)) return true;
        return false;

        static string Normalize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s!.Length);
            foreach (char c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }
    }

    // --- WinVerifyTrust ---------------------------------------------------------------
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    // Коды WinVerifyTrust, которые означают «проверить отзыв НЕ УДАЛОСЬ» (а не «отозван»).
    private const int CERT_E_REVOKED = unchecked((int)0x800B010C);
    private const int CERT_E_REVOCATION_FAILURE = unchecked((int)0x800B010E);
    private const int CRYPT_E_REVOCATION_OFFLINE = unchecked((int)0x80092013);
    private const int CRYPT_E_NO_REVOCATION_CHECK = unchecked((int)0x80092012);
    private const int CRYPT_E_NO_REVOCATION_DLL = unchecked((int)0x80092011);

    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;

    /// <summary>Подпись цела, цепочка строится и сертификат НЕ отозван.
    ///
    /// Отзыв проверяем, но его недоступность фатальной не считаем — и это не лень. Наш
    /// сертификат самоподписанный (см. docs/code-signing.md), точек распространения CRL и
    /// OCSP в нём нет вовсе, поэтому «проверить не удалось» — штатный ответ, а не признак
    /// подмены; безусловное требование проверки отзыва просто отключило бы обновления
    /// у всех. Зато явный ответ «отозван» отвергаем жёстко: ровно ради него отзыв и нужен.
    /// Настоящая защита канала здесь — пиннинг отпечатка (см. TrustedThumbprints).</summary>
    private static bool VerifyTrust(string path)
    {
        int rc = VerifyTrustCore(path, WTD_REVOKE_WHOLECHAIN);
        if (rc == 0) return true;
        if (rc == CERT_E_REVOKED) return false;   // отозван — дальше не смотрим
        // Отзыв определить не удалось: перепроверяем саму подпись без него.
        if (rc is CERT_E_REVOCATION_FAILURE or CRYPT_E_REVOCATION_OFFLINE
               or CRYPT_E_NO_REVOCATION_CHECK or CRYPT_E_NO_REVOCATION_DLL)
            return VerifyTrustCore(path, WTD_REVOKE_NONE) == 0;
        return false;
    }

    private static int VerifyTrustCore(string path, uint revocationChecks)
    {
        var file = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = Marshal.StringToCoTaskMemUni(path),
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };
        IntPtr pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(file, pFile, false);

        var data = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            dwUIChoice = 2,             // WTD_UI_NONE — молча, без диалогов
            fdwRevocationChecks = revocationChecks,
            dwUnionChoice = 1,          // WTD_CHOICE_FILE
            pFile = pFile,
            dwStateAction = 0,
            dwProvFlags = 0x00000010,   // WTD_SAFER_FLAG
        };
        try
        {
            Guid action = WintrustActionGenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            Marshal.FreeCoTaskMem(file.pcwszFilePath);
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeCoTaskMem(pFile);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
}
