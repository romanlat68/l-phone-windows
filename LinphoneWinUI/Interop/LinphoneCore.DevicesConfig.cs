using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using static LinphoneWinUI.Interop.NativeMethods;

namespace LinphoneWinUI.Interop;

// Аудио/видео устройства, кодеки, шифрование медиа, проверка микрофона, NAT/TURN,
// провизионинг и настройки приложения в rc [app] (тема, флаги, строки).
// Часть LinphoneCore — выполняется на потоке ядра под сериализацией фасада.
internal sealed partial class LinphoneCore
{
    // ------------------------------------------------------------------
    // Аудиоустройства
    // ------------------------------------------------------------------
    /// <summary>Микрофоны (устройства с возможностью записи).</summary>
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => EnumerateDevices(record: true);

    /// <summary>Динамики (устройства с возможностью воспроизведения).</summary>
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => EnumerateDevices(record: false);

    /// <summary>Перечитать список звуковых устройств. Желательно вызывать с MTA-потока
    /// (в STA-потоке WinUI mswasapi нестабильно находит конечные точки).</summary>
    public void ReloadSoundDevices()
    {
        if (_core != IntPtr.Zero) linphone_core_reload_sound_devices(_core);
    }

    // --- Звонки: эхоподавитель и DTMF ---
    public bool GetEchoCancellation() => _core != IntPtr.Zero && linphone_core_echo_cancellation_enabled(_core) != 0;
    public void SetEchoCancellation(bool on)
    {
        if (_core != IntPtr.Zero) linphone_core_enable_echo_cancellation(_core, on ? 1 : 0);
        SyncConfig();
    }

    /// <summary>"rfc2833" или "info".</summary>
    public string GetDtmfMethod()
    {
        if (_core != IntPtr.Zero && linphone_core_get_use_info_for_dtmf(_core) != 0) return "info";
        return "rfc2833";
    }
    public void SetDtmfMethod(string method)
    {
        if (_core == IntPtr.Zero) return;
        bool info = method == "info";
        linphone_core_set_use_rfc2833_for_dtmf(_core, info ? 0 : 1);
        linphone_core_set_use_info_for_dtmf(_core, info ? 1 : 0);
        SyncConfig();
    }

    // --- Камера ---
    public List<string> GetCameras()
    {
        var result = new List<string>();
        if (_core == IntPtr.Zero) return result;
        linphone_core_reload_video_devices(_core);
        IntPtr list = linphone_core_get_video_devices_list(_core); // строки strdup'нуты — наши
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr data = bctbx_list_get_data(it);
            string? name = PtrToLocalName(data);
            if (data != IntPtr.Zero) { try { bctbx_free(data); } catch { } }
            if (!string.IsNullOrEmpty(name)) result.Add(name);
        }
        if (list != IntPtr.Zero) bctbx_list_free(list);
        return result;
    }
    public string? CurrentCamera => _core == IntPtr.Zero ? null : PtrToUtf8(linphone_core_get_video_device(_core));
    public void SetCamera(string name) { if (_core != IntPtr.Zero) linphone_core_set_video_device(_core, name); }

    // --- Шифрование медиа ---
    public string GetMediaEncryption() => _core == IntPtr.Zero ? "none" :
        (LinphoneMediaEncryption)linphone_core_get_media_encryption(_core) switch
        {
            LinphoneMediaEncryption.SRTP => "srtp",
            LinphoneMediaEncryption.ZRTP => "zrtp",
            LinphoneMediaEncryption.DTLS => "dtls",
            _ => "none",
        };
    public void SetMediaEncryption(string enc)
    {
        if (_core == IntPtr.Zero) return;
        var e = enc switch
        {
            "srtp" => LinphoneMediaEncryption.SRTP,
            "zrtp" => LinphoneMediaEncryption.ZRTP,
            "dtls" => LinphoneMediaEncryption.DTLS,
            _ => LinphoneMediaEncryption.None,
        };
        linphone_core_set_media_encryption(_core, (int)e);
        SyncConfig();
    }

    // --- Кодеки ---
    public List<CodecInfo> GetAudioCodecs() => GetCodecs(audio: true);
    public List<CodecInfo> GetVideoCodecs() => GetCodecs(audio: false);

    // get_*_payload_types возвращает СВЕЖИЕ обёртки с ref=1, но их указатели (Pt) живут в UI
    // (переключатели/порядок кодеков) — освобождать сразу нельзя. Держим последнюю пачку и
    // отпускаем её при следующем запросе того же вида (UI к тому моменту перечитал список) и в Dispose.
    private readonly List<IntPtr> _heldAudioPts = new(), _heldVideoPts = new();

    private List<CodecInfo> GetCodecs(bool audio)
    {
        var result = new List<CodecInfo>();
        if (_core == IntPtr.Zero) return result;
        var held = audio ? _heldAudioPts : _heldVideoPts;
        foreach (IntPtr old in held) { try { linphone_payload_type_unref(old); } catch { } }
        held.Clear();
        IntPtr list = audio ? linphone_core_get_audio_payload_types(_core) : linphone_core_get_video_payload_types(_core);
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr pt = bctbx_list_get_data(it);
            if (pt == IntPtr.Zero) continue;
            string mime = PtrToUtf8(linphone_payload_type_get_mime_type(pt)) ?? "?";
            int rate = linphone_payload_type_get_clock_rate(pt);
            bool en = linphone_payload_type_enabled(pt) != 0;
            result.Add(new CodecInfo { Label = audio ? $"{mime} · {rate} Hz" : mime, Enabled = en, Pt = pt });
            held.Add(pt);
        }
        if (list != IntPtr.Zero) bctbx_list_free(list);
        return result;
    }

    private void ReleaseHeldPayloadTypes()
    {
        foreach (var held in new[] { _heldAudioPts, _heldVideoPts })
        {
            foreach (IntPtr pt in held) { try { linphone_payload_type_unref(pt); } catch { } }
            held.Clear();
        }
    }

    public void SetCodecEnabled(IntPtr pt, bool on)
    {
        if (pt != IntPtr.Zero) linphone_payload_type_enable(pt, on ? 1 : 0);
        SyncConfig();
    }

    /// <summary>Задаёт порядок (приоритет) кодеков. pts — указатели payload type в нужном порядке.</summary>
    public void SetAudioCodecsOrder(IEnumerable<IntPtr> pts) => SetCodecsOrder(pts, audio: true);
    public void SetVideoCodecsOrder(IEnumerable<IntPtr> pts) => SetCodecsOrder(pts, audio: false);

    private void SetCodecsOrder(IEnumerable<IntPtr> pts, bool audio)
    {
        if (_core == IntPtr.Zero) return;
        IntPtr list = IntPtr.Zero;
        foreach (var pt in pts)
            if (pt != IntPtr.Zero) list = bctbx_list_append(list, pt);
        if (audio) linphone_core_set_audio_payload_types(_core, list);
        else linphone_core_set_video_payload_types(_core, list);
        if (list != IntPtr.Zero) bctbx_list_free(list);
        SyncConfig();
    }

    /// <summary>Сбросить настройки на диск (rc), чтобы пережили перезапуск.</summary>
    public void SyncConfig()
    {
        if (_core == IntPtr.Zero) return;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg != IntPtr.Zero) linphone_config_sync(cfg);
    }

    // --- URL внешней адресной книги (хранится в rc, секция [app]) ---
    public string GetPhonebookUrl()
    {
        if (_core == IntPtr.Zero) return "";
        IntPtr cfg = linphone_core_get_config(_core);
        return cfg == IntPtr.Zero ? "" : (PtrToUtf8(linphone_config_get_string(cfg, "app", "phonebook_url", "")) ?? "");
    }

    public void SetPhonebookUrl(string url)
    {
        if (_core == IntPtr.Zero) return;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero) return;
        linphone_config_set_string(cfg, "app", "phonebook_url", url ?? "");
        linphone_config_sync(cfg);
    }

    // --- HTTP-авторизация телефонной книги (для удалённых книг; локальным не нужна) ---
    // Логин — в rc [app]; пароль — в общем DPAPI-хранилище cred.dat под псевдо-сервером
    // "phonebook" (ключ "user@phonebook"), открытым текстом в rc не попадает.
    private const string PhonebookCredServer = "phonebook";

    /// <summary>Учётные данные книги: (логин, пароль); пустой логин = авторизации нет.</summary>
    public (string User, string Password) GetPhonebookAuth()
    {
        string user = GetAppString("phonebook_user", "");
        return (user, user.Length > 0 ? LoadPasswordLocal(user, PhonebookCredServer) : "");
    }

    /// <summary>Задать учётные данные книги. Пустой логин убирает авторизацию (и пароль);
    /// пустой пароль при заданном логине — оставить прежний (как в аккаунтах/TURN).</summary>
    public void SetPhonebookAuth(string user, string password)
    {
        user = (user ?? "").Trim();
        string old = GetAppString("phonebook_user", "");
        if (old.Length > 0 && old != user) RemovePasswordLocal(old, PhonebookCredServer);
        SetAppString("phonebook_user", user);
        if (user.Length > 0 && !string.IsNullOrEmpty(password))
            SavePasswordLocal(user, PhonebookCredServer, password);
    }

    // --- Общий секрет HMAC для хука статуса (/prov/status): подписывает управляющий запрос,
    // чтобы кто угодно в LAN не мог менять presence коллег. Хранится в DPAPI cred.dat под
    // псевдо-сервером "presencehook" (в rc открытым текстом не попадает). ---
    private const string HookCredServer = "presencehook";
    private const string HookCredUser = "hook";

    // --- Токен локального API: тем же путём, что остальные секреты. ---
    // Он давал право звонить с телефона сотрудника, а лежал в linphonerc открытым текстом —
    // единственный секрет приложения вне DPAPI. Каталог профиля закрыт ACL, но эта защита
    // отваливается ровно там, где файл покидает машину: резервная копия, перенаправленная
    // папка, диагностический архив для поддержки. DPAPI переживает всё это — ключ привязан
    // к учётной записи Windows.
    private const string ApiCredServer = "localapi";
    private const string ApiCredUser = "token";

    /// <summary>Токен локального API ("" = не задан).</summary>
    public string GetApiToken()
    {
        string t = LoadPasswordLocal(ApiCredUser, ApiCredServer);
        if (t.Length > 0) return t;
        // Миграция: токен из старых версий лежит в rc открытым текстом. Переносим в DPAPI
        // и стираем из конфига — иначе он останется там навсегда.
        string legacy = GetAppString("api_token", "");
        if (legacy.Length == 0) return "";
        SavePasswordLocal(ApiCredUser, ApiCredServer, legacy);
        SetAppString("api_token", "");
        Log?.Invoke("Токен локального API перенесён из конфигурации в защищённое хранилище");
        return legacy;
    }

    /// <summary>Задать токен; пустой — убрать.</summary>
    public void SetApiToken(string token)
    {
        token = (token ?? "").Trim();
        if (token.Length == 0) RemovePasswordLocal(ApiCredUser, ApiCredServer);
        else SavePasswordLocal(ApiCredUser, ApiCredServer, token);
        SetAppString("api_token", "");   // в rc его быть не должно ни при каких обстоятельствах
    }

    /// <summary>Секрет подписи хука статуса ("" = подпись не используется).</summary>
    public string GetPresenceHookSecret() => LoadPasswordLocal(HookCredUser, HookCredServer);

    /// <summary>Задать секрет; пустой — убрать (хук пойдёт без подписи).</summary>
    public void SetPresenceHookSecret(string secret)
    {
        secret = (secret ?? "").Trim();
        if (secret.Length == 0) RemovePasswordLocal(HookCredUser, HookCredServer);
        else SavePasswordLocal(HookCredUser, HookCredServer, secret);
    }

    // --- Голосовая почта: номер для прослушивания (feature-код АТС; *97 = «моя почта» FreePBX) ---
    public string GetVoicemailNumber() => GetAppString("voicemail_number", "*97");
    public void SetVoicemailNumber(string number) => SetAppString("voicemail_number", (number ?? "").Trim());

    // --- Рингтон ---
    public string GetRingtone() => _core == IntPtr.Zero ? "" : (PtrToUtf8(linphone_core_get_ring(_core)) ?? "");
    public void SetRingtone(string? path)
    {
        if (_core == IntPtr.Zero) return;
        linphone_core_set_ring(_core, string.IsNullOrEmpty(path) ? null : path);
        SyncConfig();
    }

    /// <summary>Встроенные рингтоны из SDK (share/sounds/linphone/rings, *.mkv/*.wav).</summary>
    public List<string> GetBuiltInRingtones()
    {
        var list = new List<string>();
        try
        {
            string dir = Path.Combine(SdkRoot, "share", "sounds", "linphone", "rings");
            if (Directory.Exists(dir))
                foreach (string f in Directory.EnumerateFiles(dir))
                    if (f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                        list.Add(f);
        }
        catch { }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }
    // ------------------------------------------------------------------
    // Проверка устройств: запись микрофона и неблокирующее воспроизведение
    // ------------------------------------------------------------------
    private IntPtr _recorder;

    /// <summary>Начинает запись с текущего микрофона в WAV-файл. true — запись пошла.</summary>
    public bool StartMicRecording(string wavPath)
    {
        if (_core == IntPtr.Zero) return false;
        StopMicRecording();
        IntPtr prm = linphone_core_create_recorder_params(_core);
        if (prm == IntPtr.Zero) return false;
        linphone_recorder_params_set_file_format(prm, 1); // MediaFileFormat.Wav
        _recorder = linphone_core_create_recorder(_core, prm);
        try { linphone_recorder_params_unref(prm); } catch { }
        if (_recorder == IntPtr.Zero) return false;
        if (linphone_recorder_open(_recorder, wavPath) != 0 || linphone_recorder_start(_recorder) != 0)
        {
            StopMicRecording();
            return false;
        }
        Log?.Invoke("Проверка микрофона: запись начата");
        return true;
    }

    /// <summary>Останавливает запись и финализирует WAV-файл.</summary>
    public void StopMicRecording()
    {
        if (_recorder == IntPtr.Zero) return;
        try { linphone_recorder_pause(_recorder); } catch { }
        try { linphone_recorder_close(_recorder); } catch { }
        try { linphone_recorder_unref(_recorder); } catch { }
        _recorder = IntPtr.Zero;
    }


    // --- Удалённое конфигурирование (provisioning) ---
    public string GetProvisioningUri() => _core == IntPtr.Zero ? "" : (PtrToUtf8(linphone_core_get_provisioning_uri(_core)) ?? "");
    public void SetProvisioningUri(string uri)
    {
        if (_core == IntPtr.Zero) return;
        linphone_core_set_provisioning_uri(_core, string.IsNullOrEmpty(uri) ? null : uri);
        SyncConfig();
    }

    // --- TLS: чей сертификат считаем доверенным ---
    /// <summary>Текущее доверие TLS: путь к файлу CA и включена ли проверка сертификата сервера.
    /// Проверка по умолчанию ВКЛЮЧЕНА, а CA — бандл, приехавший с SDK; своего центра
    /// сертификации до этого задать было нельзя, из-за чего самоподписанные АТС (типовой
    /// FreePBX) давали пользователю голое «Failed» без объяснения.</summary>
    public (string CaFile, bool Verify) GetTls()
    {
        if (_core == IntPtr.Zero) return ("", true);
        string ca = "";
        try { ca = PtrToUtf8(linphone_core_get_root_ca(_core)) ?? ""; }
        catch (EntryPointNotFoundException) { /* нет в этой сборке SDK */ }
        // Признак «проверять» держим в rc сами: геттера у liblinphone нет.
        return (ca, GetAppFlag("tls_verify", true));
    }

    /// <summary>Задать доверие TLS. caFile пустой — вернуться к бандлу SDK.
    /// verify=false выключает проверку и сертификата, и совпадения имени: это осознанное
    /// понижение безопасности ради самоподписанных серверов, UI обязан предупредить.</summary>
    /// <returns>false — применить не удалось (ядро не поднято или нет функций в SDK).</returns>
    public bool SetTls(string caFile, bool verify)
    {
        if (_core == IntPtr.Zero) return false;
        try
        {
            string ca = (caFile ?? "").Trim();
            if (ca.Length > 0 && File.Exists(ca)) linphone_core_set_root_ca(_core, ca);
            else if (ca.Length == 0) linphone_core_set_root_ca(_core, DefaultRootCa());
            else return false;   // путь задан, но файла нет — молча подменять доверие нельзя

            linphone_core_verify_server_certificates(_core, verify ? 1 : 0);
            linphone_core_verify_server_cn(_core, verify ? 1 : 0);
        }
        catch (EntryPointNotFoundException) { return false; }
        SetAppFlag("tls_verify", verify);
        SyncConfig();
        return true;
    }

    /// <summary>Бандл CA, приехавший с SDK (share/linphone/rootca.pem) — состояние «как из коробки».</summary>
    private string DefaultRootCa()
    {
        string p = Path.Combine(SdkRoot, "share", "linphone", "rootca.pem");
        return File.Exists(p) ? p : "";
    }

    // --- NAT: STUN-сервер + ICE + TURN ---
    public (string Stun, bool Ice, bool Turn, string TurnUser) GetNat()
    {
        if (_core == IntPtr.Zero) return ("", false, false, "");
        IntPtr p = linphone_core_get_nat_policy(_core);
        if (p == IntPtr.Zero) return ("", false, false, "");
        bool turn = false; string turnUser = "";
        try
        {
            turn = linphone_nat_policy_turn_enabled(p) != 0;
            turnUser = PtrToUtf8(linphone_nat_policy_get_stun_server_username(p)) ?? "";
        }
        catch (EntryPointNotFoundException) { /* нет в этой сборке SDK */ }
        return (PtrToUtf8(linphone_nat_policy_get_stun_server(p)) ?? "", linphone_nat_policy_ice_enabled(p) != 0,
                turn, turnUser);
    }

    /// <summary>Применить сетевые настройки. TURN использует тот же адрес, что и STUN;
    /// turnPass пустой = не менять сохранённый пароль (он хранится в auth_info liblinphone).
    /// turnRealm — realm TURN-сервера (coturn: <c>realm=</c> в turnserver.conf); заполнен —
    /// liblinphone кладёт в linphonerc хеш ha1 вместо открытого пароля, пустой — открытый текст.</summary>
    /// <returns>false — применить НЕ удалось (ядро не поднято либо в сборке SDK нет политики NAT).
    /// Вызывающий обязан сказать об этом пользователю, а не рапортовать «применено».</returns>
    public bool SetNat(string stun, bool ice, bool turn, string turnUser, string turnPass, string turnRealm)
    {
        if (_core == IntPtr.Zero) return false;
        // У свежего Core нет объекта политики (её никто не создавал), поэтому
        // linphone_core_get_nat_policy возвращает ноль — раньше метод здесь молча выходил,
        // и настройки STUN/ICE/TURN не сохранялись. Создаём политику при первом применении.
        IntPtr created = IntPtr.Zero;
        IntPtr p = linphone_core_get_nat_policy(_core);
        if (p == IntPtr.Zero)
        {
            try { p = created = linphone_core_create_nat_policy(_core); }
            catch (EntryPointNotFoundException) { return false; }   // нет в этой сборке SDK
            if (p == IntPtr.Zero) return false;
        }
        try
        {
            bool useStun = !string.IsNullOrWhiteSpace(stun);
            linphone_nat_policy_set_stun_server(p, useStun ? stun.Trim() : null);
            linphone_nat_policy_enable_stun(p, useStun ? 1 : 0);
            linphone_nat_policy_enable_ice(p, ice ? 1 : 0);
            string user = (turnUser ?? "").Trim();
            try
            {
                linphone_nat_policy_enable_turn(p, turn && useStun ? 1 : 0); // TURN без сервера бессмыслен
                linphone_nat_policy_set_stun_server_username(p, user.Length > 0 ? user : null);
            }
            catch (EntryPointNotFoundException) { /* нет в этой сборке SDK */ }
            // Пароль TURN liblinphone ищет в auth_info по логину.
            if (turn && user.Length > 0 && !string.IsNullOrEmpty(turnPass))
            {
                RemoveTurnAuthInfo(user);   // без этого каждое «Применить» плодило дубли auth_info в rc
                // Realm обязателен, чтобы пароль не лёг в rc открытым: зная realm, liblinphone
                // считает ha1 = MD5(user:realm:pass) и пишет в конфиг только его. Тот же ha1 —
                // ключ MESSAGE-INTEGRITY для long-term credentials TURN, авторизация не страдает.
                // Realm должен совпадать с серверным: не совпал — TURN ответит 401.
                string realm = (turnRealm ?? "").Trim();
                IntPtr auth = linphone_factory_create_auth_info(
                    _factory, user, null, turnPass, null, realm.Length > 0 ? realm : null, null);
                if (auth != IntPtr.Zero)
                {
                    linphone_core_add_auth_info(_core, auth); // копирует — свою ссылку отпускаем
                    try { linphone_auth_info_unref(auth); } catch { }
                }
            }
            linphone_core_set_nat_policy(_core, p);   // Core берёт свою ссылку
        }
        finally
        {
            // Своя ссылка на созданную политику больше не нужна — у Core собственная.
            // В finally: иначе исключение из середины метода утекало бы политикой.
            if (created != IntPtr.Zero)
            {
                try { linphone_nat_policy_unref(created); } catch (EntryPointNotFoundException) { }
            }
        }
        SyncConfig();
        return true;
    }

    /// <summary>Удалить ТОЛЬКО свою (TURN) запись авторизации с этим логином.
    /// Раньше здесь звался общий <c>RemoveAuthInfo(user, "")</c>, а он ищет с domain=null —
    /// в этом режиме liblinphone игнорирует и realm, и домен и отдаёт ЛЮБУЮ запись с таким
    /// username. Если TURN-логин совпадал с номером внутреннего (обычное дело, когда coturn
    /// поднят на своей АТС), «Применить» стирало учётку регистрации аккаунта — телефон
    /// переставал регистрироваться. Признак «наша»: у TURN-записи нет домена, у аккаунтной
    /// он всегда есть (см. [auth_info_N] domain= в linphonerc).</summary>
    /// <summary>Есть ли сохранённый пароль TURN для этого логина. Само значение вернуть нельзя —
    /// при заданном realm liblinphone хранит только хеш ha1, — но факт наличия сказать можно,
    /// и это ровно то, чего не хватало: пустое поле пароля выглядит одинаково и когда пароль
    /// сохранён, и когда его нет вовсе.</summary>
    public bool HasTurnPassword(string login)
    {
        if (_core == IntPtr.Zero || string.IsNullOrEmpty(login)) return false;
        for (IntPtr it = linphone_core_get_auth_info_list(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr ai = bctbx_list_get_data(it);
            if (ai == IntPtr.Zero) continue;
            if (!string.Equals(PtrToUtf8(linphone_auth_info_get_username(ai)), login, StringComparison.Ordinal)) continue;
            // Записи аккаунтов отличаются заполненным domain — тот же признак, что при удалении.
            if ((PtrToUtf8(linphone_auth_info_get_domain(ai)) ?? "").Length > 0) continue;
            return true;
        }
        return false;
    }

    private void RemoveTurnAuthInfo(string login)
    {
        if (_core == IntPtr.Zero || string.IsNullOrEmpty(login)) return;
        // Сначала собираем указатели: удаление на ходу инвалидирует список ядра.
        var doomed = new List<IntPtr>();
        for (IntPtr it = linphone_core_get_auth_info_list(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr ai = bctbx_list_get_data(it);
            if (ai == IntPtr.Zero) continue;
            if (!string.Equals(PtrToUtf8(linphone_auth_info_get_username(ai)), login, StringComparison.Ordinal)) continue;
            if ((PtrToUtf8(linphone_auth_info_get_domain(ai)) ?? "").Length > 0) continue;   // запись аккаунта — не трогаем
            doomed.Add(ai);
        }
        foreach (IntPtr ai in doomed) linphone_core_remove_auth_info(_core, ai);
    }

    // --- Устройство звонка (рингер) ---
    public string GetRingerDeviceId() => _core == IntPtr.Zero ? "" : (PtrToUtf8(linphone_core_get_ringer_device(_core)) ?? "");
    public void SetRingerDevice(string id)
    {
        if (_core != IntPtr.Zero) { linphone_core_set_ringer_device(_core, id); SyncConfig(); }
    }

    // --- Тема оформления (хранится в rc, секция [app]) ---
    public string GetTheme()
    {
        if (_core == IntPtr.Zero) return "system";
        IntPtr cfg = linphone_core_get_config(_core);
        return cfg == IntPtr.Zero ? "system" : (PtrToUtf8(linphone_config_get_string(cfg, "app", "theme", "system")) ?? "system");
    }

    public void SetTheme(string theme)
    {
        if (_core == IntPtr.Zero) return;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero) return;
        linphone_config_set_string(cfg, "app", "theme", theme);
        linphone_config_sync(cfg);
    }

    // --- Прочие флаги приложения в rc [app] ---
    public bool GetAppFlag(string key, bool def)
    {
        if (_core == IntPtr.Zero) return def;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero) return def;
        return (PtrToUtf8(linphone_config_get_string(cfg, "app", key, def ? "1" : "0")) ?? "") == "1";
    }

    public void SetAppFlag(string key, bool value)
    {
        if (_core == IntPtr.Zero) return;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero) return;
        linphone_config_set_string(cfg, "app", key, value ? "1" : "0");
        linphone_config_sync(cfg);
    }

    public string GetAppString(string key, string def)
    {
        if (_core == IntPtr.Zero) return def;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero) return def;
        return PtrToUtf8(linphone_config_get_string(cfg, "app", key, def)) ?? def;
    }

    public void SetAppString(string key, string value)
    {
        if (_core == IntPtr.Zero) return;
        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero) return;
        linphone_config_set_string(cfg, "app", key, value);
        linphone_config_sync(cfg);
    }

    public string? CurrentInputDeviceId => DeviceId(_core == IntPtr.Zero ? IntPtr.Zero : linphone_core_get_default_input_audio_device(_core));
    public string? CurrentOutputDeviceId => DeviceId(_core == IntPtr.Zero ? IntPtr.Zero : linphone_core_get_default_output_audio_device(_core));

    public void SetInputDevice(string id) => ApplyDevice(id, input: true);
    public void SetOutputDevice(string id) => ApplyDevice(id, input: false);

    // Состав устройств на прошлом перечислении (отдельно для микрофонов и динамиков) —
    // чтобы не повторять одну и ту же строку журнала.
    private readonly Dictionary<bool, string> _lastDeviceSignature = new();

    private List<AudioDeviceInfo> EnumerateDevices(bool record)
    {
        var matching = new List<AudioDeviceInfo>();
        var all = new List<AudioDeviceInfo>();
        if (_core == IntPtr.Zero) return matching;

        int wantMask = record ? 1 : 2; // 1 = Record, 2 = Play
        IntPtr list = linphone_core_get_audio_devices(_core);
        bool extended = false;
        if (list == IntPtr.Zero)
        {
            list = linphone_core_get_extended_audio_devices(_core); // расширенный список (все)
            extended = true;
        }
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr dev = bctbx_list_get_data(it);
            if (dev == IntPtr.Zero) continue;
            int caps = linphone_audio_device_get_capabilities(dev);
            string id = PtrToUtf8(linphone_audio_device_get_id(dev)) ?? "";
            string name = PtrToLocalName(linphone_audio_device_get_device_name(dev)) ?? id;
            var info = new AudioDeviceInfo(id, name);
            all.Add(info);
            if ((caps & wantMask) != 0) matching.Add(info);
            try { linphone_audio_device_unref(dev); } catch { } // элементы списка с ref=1 — наши
        }
        if (list != IntPtr.Zero) bctbx_list_free(list);

        // Если по флагам capabilities ничего не подошло, но устройства есть — показываем все.
        var result = matching.Count > 0 ? matching : all;

        // Пишем в журнал ТОЛЬКО когда состав изменился. Раньше строка уходила на каждое
        // перечисление, и опрос по таймеру залил лог тысячами одинаковых записей —
        // за один прогон набралось почти пять тысяч строк, из-за чего в журнале стало
        // невозможно найти ничего другого.
        string signature = string.Join(",", result.Select(d => d.Id));
        if (!_lastDeviceSignature.TryGetValue(record, out string? prev) || prev != signature)
        {
            _lastDeviceSignature[record] = signature;
            Log?.Invoke($"Аудио ({(record ? "микрофон" : "динамик")}): всего {all.Count}, по флагам {matching.Count}{(extended ? " (extended)" : "")}");
        }
        return result;
    }

    private void ApplyDevice(string id, bool input)
    {
        if (_core == IntPtr.Zero) return;
        bool applied = false;
        IntPtr list = linphone_core_get_audio_devices(_core);
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr dev = bctbx_list_get_data(it);
            if (dev == IntPtr.Zero) continue;
            // Без break: каждый элемент списка надо unref'нуть (они с ref=1 — наши).
            if (!applied && PtrToUtf8(linphone_audio_device_get_id(dev)) == id)
            {
                if (input)
                {
                    linphone_core_set_default_input_audio_device(_core, dev);
                    linphone_core_set_input_audio_device(_core, dev);
                }
                else
                {
                    linphone_core_set_default_output_audio_device(_core, dev);
                    linphone_core_set_output_audio_device(_core, dev);
                }
                Log?.Invoke($"Аудио: выбран {(input ? "микрофон" : "динамик")} «{PtrToUtf8(linphone_audio_device_get_device_name(dev))}»");
                applied = true;
            }
            try { linphone_audio_device_unref(dev); } catch { }
        }
        if (list != IntPtr.Zero) bctbx_list_free(list);
    }

    private static string? DeviceId(IntPtr dev) =>
        dev == IntPtr.Zero ? null : PtrToUtf8(linphone_audio_device_get_id(dev));
}
