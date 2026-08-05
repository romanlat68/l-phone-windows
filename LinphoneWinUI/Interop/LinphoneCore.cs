using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static LinphoneWinUI.Interop.NativeMethods;

namespace LinphoneWinUI.Interop;

/// <summary>Аудиоустройство (микрофон или динамик) для выпадающих списков настроек.
/// Класс (не struct) — чтобы корректно работал DisplayMemberPath в ComboBox.</summary>
public record AudioDeviceInfo(string Id, string Name);

/// <summary>Данные аккаунта для формы настроек и списка аккаунтов.
/// SipAddress (sip:login@server) служит стабильным ключом аккаунта.</summary>
public readonly record struct AccountInfo(
    string DisplayName, string Login, string Password, string Server, int Port, string Transport, int Expires, string SipAddress,
    bool IsDefault = false, LinphoneRegistrationState State = LinphoneRegistrationState.None);

/// <summary>Кодек (payload type) для списка с переключателями.</summary>
public sealed class CodecInfo
{
    public string Label { get; init; } = "";
    public bool Enabled { get; set; }
    public IntPtr Pt { get; init; } // указатель на LinphonePayloadType (живёт в core)
}

/// <summary>Запись журнала звонков для списка во вкладке «Журнал».</summary>
public sealed class CallLogEntry
{
    public string Remote { get; init; } = "";   // полный SIP-адрес для перезвона
    public string Title { get; init; } = "";     // номер или имя собеседника
    public string Subtitle { get; init; } = "";  // когда звонили и сколько говорили
    /// <summary>Стрелка направления. Отдельно от имени: она красится своим цветом,
    /// а в одной строке с именем это было невозможно.</summary>
    public string Arrow { get; init; } = "";
    public bool Missed { get; init; }
    public bool Incoming { get; init; } // направление: true — входящий, false — исходящий
    public long StartUnix { get; init; } // начало звонка, Unix-секунды (0 — неизвестно)

    /// <summary>Чем был зашифрован разговор: "srtp"/"zrtp"/"dtls" либо "" — защиты не было
    /// или запись сделана до появления пометок. Приходит из нашей метки в ref_key: в журнале
    /// самого liblinphone поля шифрования нет.</summary>
    public string Encryption { get; init; } = "";

    /// <summary>Показывать ли замок в строке журнала. Открытые разговоры не помечаем ничем:
    /// перечёркнутый замок на каждом внешнем звонке стал бы фоном, который перестают замечать.</summary>
    public bool IsEncrypted => Encryption.Length > 0;

    /// <summary>Подсказка к замку: какой именно механизм. ZRTP оговариваем особо — без сверки
    /// контрольной фразы он не защищает от подмены собеседника.</summary>
    public string EncryptionTip => Encryption switch
    {
        "srtp" => Loc.F("St_EncWas", "SRTP"),
        "zrtp" => Loc.F("St_EncWas", "ZRTP") + " " + Loc.S("St_EncZrtpNote"),
        "dtls" => Loc.F("St_EncWas", "DTLS-SRTP"),
        _ => "",
    };
}

/// <summary>Снимок состояния звонка, передаётся в событии CallStateChanged.
/// Reason заполняется в терминальных состояниях (End/Error) — для сообщения пользователю.</summary>
public readonly record struct CallEvent(
    LinphoneCallState State,
    string? Message,
    string? Remote,
    bool IsIncoming,
    int DurationSeconds,
    LinphoneReason Reason = LinphoneReason.None,
    /// <summary>Короткий Call-ID для журнала. Без него две параллельные линии в логе
    /// неразличимы: подряд идут «Звонок: End» и «Звонок: StreamsRunning» — и непонятно, чьи.</summary>
    string? CallId = null,
    /// <summary>АТС просит ответить автоматически (интерком, оповещение, домофон) через
    /// столько секунд. null — обычный звонок. Заполняется только для входящего.</summary>
    int? IntercomAnswerAfter = null);

/// <summary>
/// Защита разговора: чем зашифровано медиа и шла ли сигнализация по TLS.
///
/// Два факта вместе, а не порознь, потому что по отдельности они обманывают. SRTP в режиме
/// SDES (наш режим по умолчанию) передаёт ключ сессии ВНУТРИ SDP, то есть в теле SIP-сообщения.
/// Если аккаунт работает по UDP или TCP, этот ключ едет открытым текстом и любой на пути
/// расшифрует разговор — а закрытый замок в интерфейсе при этом обещает защиту. Это хуже,
/// чем отсутствие шифрования: человек принимает решение говорить свободно, опираясь на
/// индикатор, который в таком сочетании неверен.
/// </summary>
public readonly record struct CallSecurity(string Encryption, bool SignallingTls)
{
    public static CallSecurity None => new("none", false);

    /// <summary>Медиа зашифровано (о чём договорились стороны, а не что мы предложили).</summary>
    public bool IsEncrypted => Encryption is "srtp" or "zrtp" or "dtls";

    /// <summary>Шифрование есть, но ключ передавался по незащищённому каналу. Касается только
    /// SDES-SRTP: у ZRTP ключ вырабатывается в самом медиапотоке (Диффи — Хеллман), у
    /// DTLS-SRTP — в DTLS-рукопожатии, и сигнализация им не нужна.</summary>
    public bool IsWeak => Encryption == "srtp" && !SignallingTls;
}

/// <summary>Строка чата для UI: текст и флаг «исходящее».</summary>
public sealed record ChatLine(string Text, bool Mine);

/// <summary>Переписка для списка чатов. Peer — ПОЛНЫЙ SIP-адрес комнаты (sip:user@host):
/// комнаты различаются доменом, и открытие/прочтение по одному username путало комнаты
/// (сообщение от 376@домен-АТС и своя переписка 376@домен-аккаунта — разные). User — короткое
/// имя для показа.</summary>
public sealed record ChatConversation(string Peer, string User, string LastMessage, int Unread, long LastUpdate);

/// <summary>
/// Внутренняя обёртка над liblinphone: инициализация, насос iterate(), регистрация,
/// звонки, чат, presence. НЕ потокобезопасна — все вызовы (включая Iterate) обязаны
/// идти под одним замком; этим занимается фасад <see cref="LinphoneService"/>,
/// который крутит iterate на выделенном потоке. События поднимаются С ПОТОКА ЯДРА —
/// подписчики маршалятся в UI сами (DispatcherQueue.TryEnqueue).
/// </summary>
internal sealed partial class LinphoneCore : IDisposable
{
    // Где лежит установленный Linphone SDK 5.4 (liblinphone.dll + зависимости + плагины).
    // Вшитый рантайм liblinphone рядом с exe (папка "linphone") — для автономной установки;
    // если его нет (режим разработки) — берём установленный SDK из Program Files.
    private static readonly string SdkRoot = ResolveSdkRoot();
    // В layout NuGet-пакета нативные DLL лежат прямо в SdkRoot (рядом с exe),
    // в установленном/бандл-варианте — в подпапке bin.
    private static readonly string SdkBin =
        File.Exists(Path.Combine(SdkRoot, "liblinphone.dll")) ? SdkRoot : Path.Combine(SdkRoot, "bin");

    private static string ResolveSdkRoot()
    {
        // 1) NuGet-пакет LinphoneSDK.Windows: нативные DLL рядом с exe, ресурсы в share\,
        //    плагины в mediastreamer\plugins\.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "liblinphone.dll")))
            return AppContext.BaseDirectory;
        // 2) Автономный бандл рядом с exe (installer): linphone\bin\liblinphone.dll.
        string bundled = Path.Combine(AppContext.BaseDirectory, "linphone");
        if (File.Exists(Path.Combine(bundled, "bin", "liblinphone.dll"))) return bundled;
        // 3) Установленный Linphone Desktop.
        return @"C:\Program Files\Linphone";
    }

    private IntPtr _factory;
    private IntPtr _core;
    private IntPtr _cbs;
    private IntPtr _logService;

    // Делегаты держим в полях — иначе их соберёт GC, и native-вызов колбэка упадёт.
    private readonly RegistrationStateChangedCb _onRegistration;
    private readonly CallStateChangedCb _onCall;
    private readonly MessageReceivedCb _onMessage;
    private readonly NotifyPresenceReceivedCb _onPresence;
    private readonly TransferStateChangedCb _onTransfer;
    private readonly MessageWaitingIndicationChangedCb _onMwi;
    private readonly NotifyReceivedCb _onNotify;   // держим ссылку: иначе GC съест делегат под нативным колбэком
    private readonly AudioDevicesListUpdatedCb _onAudioDevices;

    public event Action<LinphoneRegistrationState, string?>? RegistrationStateChanged;
    public event Action<CallEvent>? CallStateChanged;
    public event Action<string>? Log;
    /// <summary>Входящее сообщение чата: (имя/номер отправителя, текст).</summary>
    public event Action<string, string>? ChatMessageReceived;
    /// <summary>Изменился статус присутствия кого-то из друзей (избранного).</summary>
    public event Action? PresenceChanged;
    /// <summary>Результат перевода звонка: true = собеседник дозвонился до цели, false = не удалось.</summary>
    public event Action<bool>? TransferResult;
    /// <summary>MWI (голосовая почта): (есть ожидающие, число новых). NOTIFY message-summary от АТС.</summary>
    public event Action<bool, int>? VoicemailChanged;

    public string CoreVersion { get; private set; } = "?";
    public bool IsStarted => _core != IntPtr.Zero;

    // --- Снимок состояния для UI ---
    // Обновляется на потоке ядра в OnCallStateChanged; UI читает БЕЗ перехода на поток ядра.
    // Раньше UI дергал CallCount/IsVideoActive синхронно (напр. IsVideoActive на каждом
    // переключении вкладки), и каждый такой вызов ждал, пока ядро выйдет из iterate().
    private volatile int _callCountSnapshot;
    private volatile bool _videoActiveSnapshot;

    /// <summary>Число звонков (снимок; безопасно читать с любого потока).</summary>
    public int CallCountSnapshot => _callCountSnapshot;

    /// <summary>Активен ли видеопоток текущего звонка (снимок; безопасно с любого потока).</summary>
    public bool VideoActiveSnapshot => _videoActiveSnapshot;

    /// <summary>Каталог профиля liblinphone (ProgramData\L-Phone\SID): linphonerc, sqlite-базы,
    /// liblinphone.log. Нужен диагностике — раньше «Удалить логи» искал liblinphone.log
    /// в LOCALAPPDATA и не находил.</summary>
    public string ProfileDir { get; private set; } = "";

    /// <summary>Каталог профиля вместо боевого (ProgramData\L-Phone\SID). Задают ТОЛЬКО тесты:
    /// иначе интеграционный прогон писал бы в рабочий linphonerc пользователя и его же базы —
    /// то есть портил бы настройки и журнал звонков ради проверки.</summary>
    private readonly string? _profileDirOverride;

    public LinphoneCore(string? profileDirOverride = null)
    {
        _profileDirOverride = profileDirOverride;
        // Мост для логов из статических методов (SaveCreds, HardenDirectoryAcl): раньше их
        // ошибки уходили в Debug.WriteLine, которого в проде нет — саппорт не видел, что
        // пароль не сохранился или каталог профиля остался открытым.
        _staticLog = s => Log?.Invoke(s);
        _onRegistration = OnRegistrationStateChanged;
        _onCall = OnCallStateChanged;
        _onMessage = OnMessageReceived;
        _onPresence = OnNotifyPresenceReceived;
        _onNotify = OnNotifyReceived;
        _onTransfer = OnTransferStateChanged;
        _onMwi = OnMwiChanged;
        _onAudioDevices = OnAudioDevicesListUpdated;
    }

    private static readonly int AnsiCodePage = (int)GetACP();
    static LinphoneCore()
    {
        // Для CP1251 и прочих системных кодовых страниц (имена устройств).
        try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { }
    }

    /// <summary>Имя устройства: на Windows mediastreamer отдаёт его в системной ANSI-кодировке
    /// (напр. CP1251). Если как UTF-8 не читается (есть «�») — декодируем как ANSI.</summary>
    private static string? PtrToLocalName(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        int len = 0;
        while (System.Runtime.InteropServices.Marshal.ReadByte(ptr, len) != 0) len++;
        if (len == 0) return "";
        var bytes = new byte[len];
        System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, len);
        string utf8 = System.Text.Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('�')) return utf8;
        try { return System.Text.Encoding.GetEncoding(AnsiCodePage).GetString(bytes); } catch { return utf8; }
    }

    /// <summary>
    /// Создаёт и запускает Core. Должно вызываться один раз на UI-потоке.
    /// </summary>
    public void Start()
    {
        if (_core != IntPtr.Zero) return;

        // 1. Нативные зависимости liblinphone.dll лежат в SDK\bin — добавляем в путь поиска DLL.
        SetDllDirectory(SdkBin);
        if (LoadLibrary(Path.Combine(SdkBin, "liblinphone.dll")) == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Не удалось загрузить liblinphone.dll из {SdkBin}. Проверь, что SDK установлен.");

        _factory = linphone_factory_get();
        if (_factory == IntPtr.Zero)
            throw new InvalidOperationException("linphone_factory_get() вернул NULL.");

        // ВАЖНО: все нативные файлы liblinphone (лог, конфиг, sqlite-базы) кладём в ASCII-путь
        // под C:\ProgramData\L-Phone. На профилях с кириллицей (C:\Users\Роман\...) soci/sqlite
        // НЕ открывают базы по не-ASCII пути → linphone_core_start() возвращает -2.
        // Подкаталог — SID пользователя (чистый ASCII: S-1-5-21-...): у каждого пользователя
        // машины свой профиль и свои пароли, каталог закрыт ACL от других пользователей
        // (раньше общий C:\ProgramData\L-Phone с linphonerc был читаем всеми).
        string sharedBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Phone");
        string logDir = _profileDirOverride ?? Path.Combine(sharedBase, CurrentUserSid());
        if (_profileDirOverride != null)
        {
            // Тестовый профиль — временный каталог, который создаёт и удаляет сам тест:
            // ни миграции со старого общего каталога, ни ACL, ни чистки логов тут не нужно.
            Directory.CreateDirectory(logDir);
            ProfileDir = logDir;
        }
        else
        {
            if (!EnsureOwnProfileDir(logDir))
            {
                // Каталог занят другим пользователем машины — работаем в личном. Миграцию из
                // общего каталога при этом НЕ делаем: раз рядом хозяйничает посторонний,
                // переносить оттуда что-либо тем более нельзя.
                // ПОРЯДОК ВАЖЕН: сначала создать каталог, только потом приводить к ASCII.
                // AsciiSafePath берёт короткое имя (8.3) у СУЩЕСТВУЮЩЕГО пути; для ещё не
                // созданного он возвращает путь как есть, и под кириллическим профилем
                // (C:\Users\Роман\…) soci/sqlite не открыли бы базы — linphone_core_start
                // вернул бы -2, то есть телефон не запустился бы вовсе.
                logDir = FallbackProfileDir;
                Directory.CreateDirectory(logDir);
                logDir = AsciiSafePath(logDir);
                HardenDirectoryAcl(logDir);
                ProfileDir = logDir;
                PruneCoreLogs(logDir);
            }
            else
            {
                ProfileDir = logDir;
                MigrateSharedProfile(sharedBase, logDir);
                // ВАЖНО: hardening строго ПОСЛЕ миграции. File.Move в пределах тома сохраняет
                // файлу старый DACL (унаследованный от общего каталога — «читаем всеми»);
                // SetAccessControl на каталоге перераспространяет наследование на уже лежащие
                // внутри файлы и вычищает эти ACE (поведение SetSecurityInfo, проверено).
                // Поменять порядок = снова открыть linphonerc всем пользователям машины.
                HardenDirectoryAcl(logDir);
                PruneCoreLogs(logDir);
            }
        }
        _logService = linphone_logging_service_get();
        if (_logService != IntPtr.Zero)
        {
            linphone_logging_service_set_log_level(_logService, (int)LinphoneLogLevel.Message);
            linphone_logging_service_set_log_file(_logService, logDir, "liblinphone.log", (UIntPtr)(4 * 1024 * 1024));
        }

        // 2. Каталог ресурсов SDK (share): belr-грамматики, звуки, rootca.
        // Без него liblinphone падает с fatal "Unable to load VCARD grammar".
        // ОБЯЗАТЕЛЬНО до create_core: грамматики читаются при инициализации Core.
        // ВАЖНО: путь к ресурсам/плагинам приводим к ASCII short-path (8.3). Если приложение
        // распаковано в кириллическую папку (C:\Users\Роман\Desktop\111), mediastreamer НЕ
        // открывает плагины/грамматики по не-ASCII пути — звуковые устройства не находятся,
        // грамматики не грузятся. AsciiSafePath даёт C:\Users\ROMAN~1\... (латиница).
        string shareDir = Path.Combine(SdkRoot, "share");
        if (Directory.Exists(shareDir))
        {
            string safeShare = AsciiSafePath(shareDir);
            linphone_factory_set_top_resources_dir(_factory, safeShare);
            Log?.Invoke($"resources dir: {safeShare}");
        }

        // 3. Плагины mediastreamer (mswasapi — захват/воспроизведение звука на Windows, кодеки).
        string? msPlugins = FindMsPluginsDir();
        if (msPlugins != null)
        {
            string safePlugins = AsciiSafePath(msPlugins);
            linphone_factory_set_msplugins_dir(_factory, safePlugins);
            Log?.Invoke($"ms plugins dir: {safePlugins}");
        }
        else
        {
            Log?.Invoke("ВНИМАНИЕ: каталог плагинов mediastreamer не найден — звука может не быть.");
        }

        // 4. Конфиг и базы — в том же ASCII-каталоге (см. выше про кириллицу).
        string configDir = logDir;
        string configPath = Path.Combine(configDir, "linphonerc");

        // Каталог данных liblinphone (sqlite-базы). По умолчанию это %LOCALAPPDATA%\linphone,
        // которого на чистом ПК нет → sqlite не может создать файл в несуществующей папке, и
        // linphone_core_start() возвращает -2 («could not connect database»). Указываем свою
        // (она уже создана выше, поэтому базы создадутся успешно).
        linphone_factory_set_data_dir(_factory, configDir);
        linphone_factory_set_cache_dir(_factory, configDir);
        Log?.Invoke($"data dir: {configDir}");

        // 5. Создаём Core.
        _core = linphone_factory_create_core_3(_factory, configPath, null, IntPtr.Zero);
        if (_core == IntPtr.Zero)
            throw new InvalidOperationException("linphone_factory_create_core_3() вернул NULL.");

        // Представляемся своим именем: в SIP-трассах и CDR на стороне АТС продукт должен быть
        // виден как L-Phone, а не как дефолтный User-Agent liblinphone — иначе диагностика
        // «какой клиент это сделал» на смешанном парке (софтфоны + Fanvil) невозможна.
        try
        {
            string ver = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.1.0";
            linphone_core_set_user_agent(_core, "L-Phone", ver);
        }
        catch (EntryPointNotFoundException) { /* нет в этой сборке SDK */ }

        // Session timers (RFC 4028). Раньше оставались на умолчаниях SDK и нигде не были
        // задокументированы. Без них «повисший» звонок (собеседник пропал из сети, файрвол
        // тихо съел путь) висит в интерфейсе вечно — особенно за строгими NAT, где BYE
        // просто не доходит. Периодический re-INVITE/UPDATE подтверждает, что звонок жив,
        // и снимает его, когда подтверждения нет. 1800 с — то же значение, что по умолчанию
        // у Asterisk (sess_expires), поэтому стороны договариваются без спора; 90 с —
        // минимум, предписанный RFC 4028. 0 в rc [app] session_expires выключает механизм.
        try
        {
            // TryParse при неудаче ОБНУЛЯЕТ out-переменную, затирая значение по умолчанию,
            // — поэтому дефолт восстанавливаем явно, а не полагаемся на инициализацию.
            if (!int.TryParse(GetAppString("session_expires", "1800"), out int expires)) expires = 1800;
            if (expires > 0)
            {
                linphone_core_set_session_expires_min_value(_core, 90);
                linphone_core_set_session_expires_value(_core, expires);
                Log?.Invoke($"Session timers: {expires} с (min 90)");
            }
            else Log?.Invoke("Session timers выключены (rc [app] session_expires=0)");
        }
        catch (EntryPointNotFoundException ex)
        {
            // Молча пропустить нельзя: «повисшие» звонки — симптом трудноуловимый, и знать,
            // что механизм не включился, нужно заранее, а не при разборе жалобы.
            Log?.Invoke("Session timers недоступны в этой сборке SDK: " + ex.Message);
        }

        // Шифрование медиа по умолчанию — SDES/SRTP. Оставалось «none», то есть клиент предлагал
        // голый RTP/AVP и разговор удалённого сотрудника шёл через открытый интернет открытым.
        // Эндпоинты АТС настроены как sdes + optimistic, поэтому включение безопасно: если
        // вторая сторона SRTP не умеет, звонок пройдёт как раньше, а не сорвётся.
        // ВАЖНО: mandatory НЕ включаем — иначе звонки абонентам без SRTP перестанут проходить.
        // Однократно (флаг [app] srtp_default), чтобы осознанно выбранное «нет» не возвращалось.
        try
        {
            if (GetAppString("srtp_default", "0") != "1")
            {
                if (linphone_core_media_encryption_supported(_core, (int)LinphoneMediaEncryption.SRTP) != 0)
                {
                    linphone_core_set_media_encryption(_core, (int)LinphoneMediaEncryption.SRTP);
                    Log?.Invoke("Шифрование медиа по умолчанию: SRTP (SDES)");
                }
                else Log?.Invoke("SRTP не собран в этой сборке SDK — шифрование медиа осталось выключенным");
                SetAppString("srtp_default", "1");
            }
        }
        catch (EntryPointNotFoundException ex)
        {
            Log?.Invoke("Не удалось включить SRTP по умолчанию: " + ex.Message);
        }

        // 6. Колбэки до старта.
        _cbs = linphone_factory_create_core_cbs(_factory);
        linphone_core_cbs_set_registration_state_changed(_cbs, _onRegistration);
        linphone_core_cbs_set_call_state_changed(_cbs, _onCall);
        linphone_core_cbs_set_message_received(_cbs, _onMessage);
        linphone_core_cbs_set_notify_presence_received(_cbs, _onPresence);
        try { linphone_core_cbs_set_notify_received(_cbs, _onNotify); }
        catch (EntryPointNotFoundException) { Log?.Invoke("BLF: подписка на dialog недоступна в этой сборке SDK"); }
        try { linphone_core_cbs_set_transfer_state_changed(_cbs, _onTransfer); } catch { /* нет в этой сборке SDK */ }
        try { linphone_core_cbs_set_message_waiting_indication_changed(_cbs, _onMwi); } catch { /* нет в этой сборке SDK */ }
        try { linphone_core_cbs_set_audio_devices_list_updated(_cbs, _onAudioDevices); } catch { /* нет в этой сборке SDK */ }
        linphone_core_add_callbacks(_core, _cbs);

        // 7. Старт.
        int rc = linphone_core_start(_core);
        if (rc != 0)
            throw new InvalidOperationException($"linphone_core_start() вернул {rc}.");

        linphone_core_set_network_reachable(_core, 1);

        // Keep-alive задаём ЯВНО, а не полагаемся на умолчание сборки SDK (его же может
        // перетереть провизионинг). За NAT это решает, дойдёт ли до нас входящий: маршрутизатор
        // держит пробитый UDP-порт обычно 30 секунд, а регистрация живёт час — без периодического
        // пакета телефон звонит наружу нормально, а дозвониться до него нельзя, и выглядит это
        // как «АТС сломалась». 25 секунд — под типовой таймаут conntrack с запасом.
        // 0 в rc [app] keepalive_period выключает механизм.
        if (!int.TryParse(GetAppString("keepalive_period", "25"), out int ka)) ka = 25;
        try { linphone_core_enable_keep_alive(_core, ka > 0 ? 1 : 0); }
        catch (EntryPointNotFoundException ex)
        {
            // Молчать нельзя: «входящие иногда не доходят» — жалоба, которую без этой строки
            // будут искать на стороне АТС неделями.
            Log?.Invoke("Keep-alive недоступен в этой сборке SDK: " + ex.Message);
            ka = 0;
        }
        if (ka > 0) ApplyKeepAlivePeriod(ka);
        else Log?.Invoke("SIP keep-alive выключен (rc [app] keepalive_period=0)");

        // Отключаем conference-factory: с ним liblinphone делает chat групповым (INVITE на
        // conf-сервер, «звонок от anonymous»); без него — прямой SIP MESSAGE, как надо Asterisk.
        try { linphone_core_set_conference_factory_uri(_core, null); } catch { }

        CoreVersion = PtrToUtf8(linphone_core_get_version()) ?? "?";
        Log?.Invoke($"Core запущен. liblinphone {CoreVersion}");
        WarnIfNoSoundDevices(msPlugins);
    }

    /// <summary>
    /// Задать период keep-alive, чего бы это ни стоило.
    ///
    /// Прямого API в поставляемом рантайме НЕТ: <c>linphone_core_set_keep_alive_period</c>
    /// не экспортирован в liblinphone 5.4.122 (проверено запуском — в журнале была строка
    /// «Keep-alive недоступен в этой сборке SDK: Unable to find an entry point…»). То есть
    /// заявленная защита от закрытия NAT-биндинга не действовала вовсе: период оставался
    /// умолчанием библиотеки, а нужная строка в журнале не появлялась никогда — и проверить
    /// это по логу было нельзя, потому что отсутствие строки выглядело как «просто не
    /// записали». За NAT это ровно тот отказ, ради которого настройка и заводилась:
    /// маршрутизатор закрывает пробитый UDP-порт примерно через 30 секунд, и входящие
    /// перестают доходить, хотя регистрация выглядит живой.
    ///
    /// Запасной путь — та же настройка через конфиг: сам liblinphone хранит период в
    /// <c>[sip] keepalive_period</c> (миллисекунды) и читает его оттуда. Значение,
    /// записанное ДО старта Core, применяется штатно.
    /// </summary>
    private void ApplyKeepAlivePeriod(int seconds)
    {
        try
        {
            linphone_core_set_keep_alive_period(_core, (uint)(seconds * 1000));
            Log?.Invoke($"SIP keep-alive: каждые {seconds} с");
            return;
        }
        catch (EntryPointNotFoundException) { /* нет в этой сборке SDK — идём через конфиг */ }

        IntPtr cfg = linphone_core_get_config(_core);
        if (cfg == IntPtr.Zero)
        {
            Log?.Invoke("ВНИМАНИЕ: период SIP keep-alive задать не удалось — за NAT входящие "
                        + "могут перестать доходить через несколько минут тишины");
            return;
        }
        linphone_config_set_int(cfg, "sip", "keepalive_period", seconds * 1000);
        linphone_config_sync(cfg);
        Log?.Invoke($"SIP keep-alive: каждые {seconds} с (через конфиг — прямого API нет в этой сборке SDK)");
    }

    /// <summary>Диагностика «тихого отказа звука»: ноль устройств почти всегда означает,
    /// что не загрузился плагин mswasapi (битая распаковка бандла, отсутствие каких-то
    /// VC++-библиотек и т.п.) — раньше это выяснялось только на первом звонке без звука.</summary>
    private void WarnIfNoSoundDevices(string? msPluginsDir)
    {
        try
        {
            if (GetInputDevices().Count > 0 || GetOutputDevices().Count > 0) return;
            string dir = msPluginsDir ?? "(не найден)";
            bool pluginPresent = msPluginsDir != null &&
                (File.Exists(Path.Combine(msPluginsDir, "libmswasapi.dll")) ||
                 File.Exists(Path.Combine(msPluginsDir, "mswasapi.dll")));
            Log?.Invoke("ВНИМАНИЕ: mediastreamer не нашёл НИ ОДНОГО звукового устройства — звука не будет. "
                + $"Каталог плагинов: {dir}; libmswasapi.dll {(pluginPresent ? "НА МЕСТЕ (не загрузился — смотри liblinphone.log: причина отказа LoadLibrary)" : "ОТСУТСТВУЕТ (неполная распаковка бандла)")}. "
                + "Типовые причины: неполный VC++ runtime (msvcp140*/vcruntime140*), смешение файлов разных версий в одной папке.");
        }
        catch { }
    }

    /// <summary>Должно вызываться регулярно (~20 мс) на потоке ядра.</summary>
    public void Iterate()
    {
        if (_core == IntPtr.Zero) return;
        linphone_core_iterate(_core);
        UpdateAudioSnapshots();
    }

    // --- Снимки для индикатора микрофона ---
    // Раньше интерфейс опрашивал уровни и список устройств СИНХРОННО восемь раз в секунду:
    // два перехода на поток ядра на каждый тик полоски, причём второй (перечисление
    // аудиоустройств) — дорогой, с обходом списка и unref каждого элемента. Пока ядро
    // свободно, это микросекунды; как только оно уходит в DNS или TLS внутри iterate,
    // интерфейс встаёт целиком — ровно в тот момент, когда пользователю нужнее всего
    // видеть, что происходит со звонком. Теперь значения считаются здесь, на потоке ядра,
    // а интерфейс читает готовые поля, ничего не ожидая.
    private volatile float _micLevelSnapshot;
    private volatile float _spkLevelSnapshot;
    private volatile bool _hasRealMicSnapshot = true;
    private int _iterateTicks;

    /// <summary>Уровень микрофона 0..1 (снимок; безопасно читать с любого потока).</summary>
    public float MicLevelSnapshot => _micLevelSnapshot;

    /// <summary>Уровень динамика 0..1 (снимок; безопасно читать с любого потока).</summary>
    public float SpeakerLevelSnapshot => _spkLevelSnapshot;

    /// <summary>Есть ли НАСТОЯЩИЙ микрофон (не только псевдоустройство «по умолчанию»).
    /// Снимок: перечисление устройств слишком дорого для опроса из интерфейса.</summary>
    public bool HasRealMicSnapshot => _hasRealMicSnapshot;

    private void UpdateAudioSnapshots()
    {
        int tick = ++_iterateTicks;
        // Уровни — раз в 100 мс: полоска обновляется восемь раз в секунду, чаще незачем.
        if (tick % 5 == 0)
        {
            var (mic, spk) = CurrentAudioLevels();
            _micLevelSnapshot = mic;
            _spkLevelSnapshot = spk;
        }
        // Наличие микрофона — раз в 2 с и только во время разговора: вне его полоски нет,
        // а перечисление устройств стоит дорого.
        if (tick % 100 == 0 && _callCountSnapshot > 0) RefreshMicPresence();
        // Архив SIP-трасс не должен расти всю смену — см. PruneCoreLogs.
        PruneLogsPeriodically(tick);
    }

    /// <summary>Пересчитать «есть ли живой микрофон». Считаем ТОЛЬКО настоящие устройства:
    /// «Default Capture» присутствует всегда, даже когда за ним ничего нет, и на машине без
    /// встроенного микрофона список после снятия гарнитуры состоит из одного псевдоустройства —
    /// а человек при этом видел бы полоску уровня и думал, что его слышно.</summary>
    private void RefreshMicPresence()
    {
        try
        {
            bool real = false;
            foreach (var d in GetInputDevices())
                if (!AudioDeviceChoice.IsSystemDefault(d.Id)) { real = true; break; }
            _hasRealMicSnapshot = real;
        }
        catch { /* не смогли перечислить — оставляем прежнее значение */ }
    }

    /// <summary>
    /// Исходящий звонок. target — номер/логин или полный SIP-URI.
    /// Голый номер liblinphone сам дополнит доменом аккаунта по умолчанию,
    /// поэтому НЕ склеиваем домен вручную (иначе при авто-регистрации он был бы пуст).
    /// </summary>
    public void Call(string target)
    {
        if (_core == IntPtr.Zero) throw new InvalidOperationException("Core не запущен.");

        // record_file задаём заранее в параметрах вызова — иначе рекордер не попадёт в медиаграф
        // и запись не пишется на диск (см. docs/dev-notes.md).
        IntPtr p = BuildCallParamsWithRecording(IntPtr.Zero);
        IntPtr call = p != IntPtr.Zero
            ? linphone_core_invite_with_params(_core, target, p)
            : linphone_core_invite(_core, target);
        if (p != IntPtr.Zero) linphone_call_params_unref(p);

        if (call == IntPtr.Zero)
            Log?.Invoke($"Не удалось инициировать звонок на {target}");
        else
            Log?.Invoke($"Звоним: {target}");
    }

    public void Hangup()
    {
        // В конференции «Сброс» завершает всю беседу (у неё нет «текущего» звонка).
        if (IsInConference) { TerminateConference(); return; }
        // Завершаем текущий разговор; удержанный (второй) остаётся — его можно возобновить.
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero)
        {
            // Финализируем запись ДО завершения, пока медиа живо (иначе MKV не запишется).
            StopRecording(call);   // финализируем файл, пока медиа живо (иначе MKV не запишется)
            linphone_call_terminate(call);
        }
        else if (_core != IntPtr.Zero) linphone_core_terminate_all_calls(_core);
    }

    // ---------------- Локальная конференция (3 участника: мы + два звонка) ----------------
    // ВАЖНО: конференцию отслеживаем СВОИМ флагом. В liblinphone 5.x обычный звонок внутренне
    // тоже «конференция» (новая архитектура медиасессий), и linphone_core_is_in_conference
    // возвращает true даже для одиночного разговора — надпись «Конференция» появлялась
    // сразу после снятия трубки.
    private bool _conferenceActive;

    /// <summary>Мы в конференции, созданной кнопкой «Конференция» (а не во «внутренней» liblinphone).</summary>
    public bool IsInConference => _conferenceActive && CallCount > 1;

    /// <summary>Поставить текущий разговор на удержание (перед приглашением третьего участника).</summary>
    public void HoldCurrent()
    {
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero) linphone_call_pause(call);
    }

    /// <summary>Объединяет все текущие звонки (активный + удержанные) в локальную конференцию.
    /// Сначала современным путём (параметры → конференция → участники), потому что
    /// linphone_core_add_all_to_conference объявлен устаревшим и в SDK 5.5 может исчезнуть —
    /// тогда конференция отвалилась бы молча при обновлении рантайма. Старый путь остаётся
    /// запасным для сборок SDK, где нового API ещё нет.</summary>
    public bool MergeCallsIntoConference()
    {
        if (_core == IntPtr.Zero || CallCount < 2) return false;
        _conferenceActive = MergeViaConferenceApi() || MergeViaDeprecatedApi();
        return _conferenceActive;
    }

    private bool MergeViaConferenceApi()
    {
        IntPtr prm = IntPtr.Zero, conf = IntPtr.Zero;
        try
        {
            prm = linphone_core_create_conference_params_2(_core, IntPtr.Zero);
            if (prm == IntPtr.Zero) return false;
            conf = linphone_core_create_conference_with_params(_core, prm);
            if (conf == IntPtr.Zero) return false;

            // Собираем указатели заранее: add_participant меняет состояние звонков,
            // а список ядра при этом перестраивается.
            var calls = new List<IntPtr>();
            for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
            {
                IntPtr c = bctbx_list_get_data(it);
                if (c != IntPtr.Zero) calls.Add(c);
            }
            int added = 0;
            foreach (IntPtr c in calls)
                if (linphone_conference_add_participant(conf, c) == 0) added++;
            return added >= 2;   // конференция из одного участника смысла не имеет
        }
        catch (EntryPointNotFoundException) { return false; }   // старая сборка SDK — пойдём запасным путём
        finally
        {
            if (conf != IntPtr.Zero) { try { linphone_conference_unref(conf); } catch { } }
            if (prm != IntPtr.Zero) { try { linphone_conference_params_unref(prm); } catch { } }
        }
    }

    private bool MergeViaDeprecatedApi()
    {
        try { return linphone_core_add_all_to_conference(_core) == 0; }
        catch (EntryPointNotFoundException) { Log?.Invoke("Конференции нет в этой сборке SDK"); return false; }
        catch { return false; }
    }

    /// <summary>Завершает конференцию вместе со всеми её звонками.</summary>
    public void TerminateConference()
    {
        StopConferenceRecording();   // закрыть файл, пока конференция ещё существует
        _conferenceActive = false;
        if (_core == IntPtr.Zero) return;
        try { linphone_core_terminate_conference(_core); } catch { }
        linphone_core_terminate_all_calls(_core); // добиваем звонки, если остались вне конференции
    }

    public void AcceptIncoming()
    {
        IntPtr call = IncomingCall();   // именно звонящий (в т.ч. второй во время разговора)
        if (call == IntPtr.Zero) return;
        // record_file задаём в параметрах приёма — иначе рекордер не попадёт в граф (см. docs/dev-notes.md).
        IntPtr p = BuildCallParamsWithRecording(call);
        if (p != IntPtr.Zero)
        {
            linphone_call_accept_with_params(call, p); // liblinphone сам поставит текущий разговор на удержание
            linphone_call_params_unref(p);
        }
        else linphone_call_accept(call);
    }

    public void DeclineIncoming()
    {
        IntPtr call = IncomingCall();
        if (call != IntPtr.Zero)
            linphone_call_terminate(call);
    }

    /// <summary>Отклонить входящий с причиной «занято» (для режима «Не беспокоить»).</summary>
    public void DeclineBusy()
    {
        IntPtr call = IncomingCall();
        if (call == IntPtr.Zero) return;
        try { linphone_call_decline(call, (int)LinphoneReason.Busy); }
        catch (EntryPointNotFoundException) { linphone_call_terminate(call); } // нет в этой сборке SDK
        catch { }
    }

    /// <summary>Сколько звонков сейчас в системе (для UI второго вызова / переключения).</summary>
    public int CallCount => _core == IntPtr.Zero ? 0 : linphone_core_get_calls_nb(_core);

    /// <summary>Звонок в состоянии «входящий» (даже если уже идёт другой разговор), иначе Zero.
    /// ВАЖНО: без фолбэка на текущий звонок! Иначе клик «Отклонить/Принять» в момент, когда
    /// звонящий уже положил трубку, срабатывал по активному разговору и обрывал его.</summary>
    private IntPtr IncomingCall()
    {
        if (_core == IntPtr.Zero) return IntPtr.Zero;
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c != IntPtr.Zero && (LinphoneCallState)linphone_call_get_state(c) == LinphoneCallState.IncomingReceived)
                return c;
        }
        return IntPtr.Zero;
    }

    /// <summary>Переключиться между двумя звонками: возобновить удержанный (активный встанет на удержание).</summary>
    public void SwapCalls()
    {
        if (_core == IntPtr.Zero) return;
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c == IntPtr.Zero) continue;
            var st = (LinphoneCallState)linphone_call_get_state(c);
            if (st is LinphoneCallState.Paused or LinphoneCallState.PausedByRemote)
            {
                linphone_call_resume(c);
                return;
            }
        }
    }

    /// <summary>Отправить DTMF-тон в активный звонок (RFC 2833 / SIP INFO по настройкам).
    ///
    /// В конференции «текущего» звонка нет: linphone_core_get_current_call возвращает NULL
    /// (то же, что у ToggleMute/ToggleHold/ToggleRecording, и там обход уже написан). Без него
    /// нажатие цифры не делало НИЧЕГО — ни тона, ни ошибки, ни строки в журнале, — при том что
    /// интерфейс исправно показывал нажатую цифру, то есть подтверждал отправку того, что не
    /// отправлено. Провести абонента через IVR из конференции было нельзя.</summary>
    public void SendDtmf(char digit)
    {
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero) { linphone_call_send_dtmf(call, (byte)digit); return; }
        if (_core == IntPtr.Zero) return;

        int sent = 0;
        foreach (IntPtr c in RecordableCalls())   // живые нетерминальные линии — тот же набор
        {
            try { linphone_call_send_dtmf(c, (byte)digit); sent++; }
            catch (Exception ex) { Log?.Invoke("DTMF не отправлен на линию: " + ex.Message); }
        }
        // Тихий отказ недопустим: разбор жалобы «телефон не набирает добавочный» иначе
        // начинается и заканчивается ничем.
        if (sent == 0) Log?.Invoke("DTMF не отправлен: активной линии нет");
    }

    private bool _dtmfToneOk = true;

    /// <summary>Локальный «бип» при нажатии (если функция есть в этой сборке SDK).</summary>
    public void PlayLocalDtmf(char digit)
    {
        if (_core == IntPtr.Zero || !_dtmfToneOk) return;
        try { linphone_core_play_dtmf(_core, (byte)digit, 100); }
        catch (EntryPointNotFoundException) { _dtmfToneOk = false; } // нет в этой сборке — молча отключаем
        catch { /* не критично */ }
    }

    /// <summary>Слепой перевод (blind transfer) текущего звонка на номер/адрес.</summary>
    public bool Transfer(string target)
    {
        if (_core == IntPtr.Zero || string.IsNullOrWhiteSpace(target)) return false;
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return false;
        IntPtr addr = linphone_core_interpret_url(_core, target.Trim());
        if (addr == IntPtr.Zero) return false;
        NoteTransferStarted();   // следующий итог от ядра относится к ЭТОМУ REFER
        bool ok = linphone_call_transfer_to(call, addr) == 0;
        linphone_address_unref(addr);
        // REFER не ушёл — итога не будет вовсе, ждать его нечего.
        if (!ok) _transferReported = true;
        return ok;
    }

    /// <summary>Сопровождаемый перевод (attended): соединяет удержанного абонента с текущим
    /// собеседником (REFER с Replaces уходит удержанному). Сценарий: разговор с A → A на
    /// удержание (принят/сделан звонок B) → «Соединить» → A и B разговаривают, мы выходим.</summary>
    public bool TransferToHeldCall()
    {
        if (_core == IntPtr.Zero) return false;
        IntPtr current = CurrentCall;
        if (current == IntPtr.Zero) return false;
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c == IntPtr.Zero || c == current) continue;
            if ((LinphoneCallState)linphone_call_get_state(c) is LinphoneCallState.Paused or LinphoneCallState.PausedByRemote)
            {
                NoteTransferStarted();   // см. OnTransferStateChanged
                bool ok = linphone_call_transfer_to_another(c, current) == 0;
                if (!ok) _transferReported = true;
                return ok;
            }
        }
        return false;
    }

    // Чат (SIP MESSAGE) вынесен в LinphoneCore.Chat.cs

    // ------------------------------------------------------------------
    // Presence (статус коллег) — для избранного
    // ------------------------------------------------------------------
    private void OnNotifyPresenceReceived(IntPtr core, IntPtr friend) =>
        SafeCallback(nameof(OnNotifyPresenceReceived), () => PresenceChanged?.Invoke());

    // ------------------------------------------------------------------
    // BLF: подписка «Event: dialog» (dialog-info+xml, RFC 4235)
    // ------------------------------------------------------------------
    /// <summary>Линия коллеги сменила состояние: номер, что с ним и кто на том конце
    /// (пусто, если АТС не сообщила). Поднимается С ПОТОКА ЯДРА.</summary>
    public event Action<string, DialogState, string>? DialogStateChanged;

    // Активные подписки: номер → LinphoneEvent (наша ссылка, освобождаем при отписке).
    private readonly Dictionary<string, IntPtr> _dialogSubs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Подписаться на состояние линий указанных внутренних. Повторный вызов
    /// досоздаёт недостающие и снимает лишние — список избранного меняется на ходу.
    /// Presence отвечает на вопрос «свободен ли», dialog — «звонит ли прямо сейчас»;
    /// перехват осмыслен только во втором случае, поэтому нужна отдельная подписка.</summary>
    public void SyncDialogSubscriptions(IEnumerable<string> extensions)
    {
        if (_core == IntPtr.Zero) return;
        var want = new HashSet<string>(
            extensions.Select(PhoneNumberUtils.SipUser).Where(e => e.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        foreach (string gone in _dialogSubs.Keys.Where(k => !want.Contains(k)).ToList())
            UnsubscribeDialog(gone);

        foreach (string ext in want)
        {
            if (_dialogSubs.ContainsKey(ext)) continue;
            IntPtr ev = SubscribeDialog(ext);
            if (ev != IntPtr.Zero) _dialogSubs[ext] = ev;
        }
    }

    private IntPtr SubscribeDialog(string ext)
    {
        IntPtr addr = linphone_core_interpret_url(_core, ext);
        if (addr == IntPtr.Zero) { Log?.Invoke($"BLF {ext}: адрес не разобран"); return IntPtr.Zero; }
        try
        {
            // 3600 с — как у presence-подписок SDK; сервер вправе укоротить, liblinphone продлит сам.
            IntPtr ev = linphone_core_create_subscribe(_core, addr, "dialog", 3600);
            if (ev == IntPtr.Zero) { Log?.Invoke($"BLF {ext}: create_subscribe вернул NULL"); return IntPtr.Zero; }
            int rc = linphone_event_send_subscribe(ev, IntPtr.Zero);
            if (rc != 0)
            {
                // Молчать нельзя: без этой строки «пульт не работает» неотличимо от
                // «подписка не поддерживается сервером».
                Log?.Invoke($"BLF {ext}: send_subscribe вернул {rc}");
                try { linphone_event_unref(ev); } catch { }
                return IntPtr.Zero;
            }
            return ev;
        }
        catch (EntryPointNotFoundException) { Log?.Invoke($"BLF {ext}: нет API подписки в сборке SDK"); return IntPtr.Zero; }
        finally { linphone_address_unref(addr); }
    }

    private void UnsubscribeDialog(string ext)
    {
        if (!_dialogSubs.Remove(ext, out IntPtr ev) || ev == IntPtr.Zero) return;
        try { linphone_event_terminate(ev); } catch { }
        try { linphone_event_unref(ev); } catch { }
    }

    /// <summary>NOTIFY по нашей подписке. Событий может быть несколько видов — берём только dialog;
    /// presence приходит своим колбэком и разбирается SDK.</summary>
    private void OnNotifyReceived(IntPtr core, IntPtr ev, string notifiedEvent, IntPtr body)
    {
        try
        {
            if (!string.Equals(notifiedEvent, "dialog", StringComparison.OrdinalIgnoreCase)) return;
            if (body == IntPtr.Zero) return;
            string xml = PtrToUtf8(linphone_content_get_utf8_text(body)) ?? "";
            string ext = DialogInfoParser.Entity(xml);
            var state = DialogInfoParser.Parse(xml);
            // Тело не разобрали — молчим: «не знаю» не должно гасить лампу.
            if (ext.Length > 0 && state != DialogState.Unknown)
                DialogStateChanged?.Invoke(ext, state, DialogInfoParser.RemoteIdentity(xml));
        }
        catch (Exception e) { Log?.Invoke("BLF: NOTIFY не разобран — " + e.Message); }
    }

    /// <summary>Список аудиоустройств изменился. Поднимается С ПОТОКА ЯДРА.</summary>
    public event Action? AudioDevicesChanged;

    private void OnAudioDevicesListUpdated(IntPtr core) =>
        SafeCallback(nameof(OnAudioDevicesListUpdated), () =>
        {
            // Гарнитуру воткнули или выдернули — снимок «есть ли микрофон» обязан обновиться
            // сразу, а не через две секунды: полоска уровня отвечает на вопрос «меня слышно?».
            RefreshMicPresence();
            AudioDevicesChanged?.Invoke();
        });

    /// <summary>Идентификаторы доступных устройств записи/воспроизведения.</summary>
    public IReadOnlyList<string> AudioDeviceIds(bool input) =>
        (input ? GetInputDevices() : GetOutputDevices()).Select(d => d.Id).ToList();

    /// <summary>MWI (message-summary) от АТС — новые сообщения голосовой почты. Asterisk шлёт
    /// unsolicited NOTIFY (mailbox на endpoint'е) — подписка не нужна, linphoneEvent = Zero.</summary>
    private void OnMwiChanged(IntPtr core, IntPtr linphoneEvent, IntPtr mwi) =>
        SafeCallback(nameof(OnMwiChanged), () =>
        {
            if (mwi == IntPtr.Zero) return;
            try
            {
                bool waiting = linphone_message_waiting_indication_has_message_waiting(mwi) != 0;
                int nbNew = (int)linphone_message_waiting_indication_get_nb_new(mwi);
                VoicemailChanged?.Invoke(waiting, nbNew);
            }
            catch (EntryPointNotFoundException) { /* нет функций в этой сборке SDK */ }
        });

    /// <summary>Итог перевода по этому REFER уже сообщён. Взводится в момент отправки REFER.</summary>
    private bool _transferReported = true;

    /// <summary>
    /// Состояние трансфер-звонка (результат REFER).
    ///
    /// ВАЖНО: итог сообщается РОВНО ОДИН РАЗ на один REFER, и решает его ПЕРВОЕ значимое
    /// состояние, а не последнее. Колбэк отдаёт состояние переведённого звонка, а его жизнь
    /// на успехе не заканчивается: по RFC 3515 результат приходит серией NOTIFY с sipfrag
    /// (100 Trying → 200 OK), после чего звонок живёт обычной жизнью и когда-нибудь получает
    /// End. Прежний код поднимал на этом «перевод не удался» — то есть по УСПЕШНОМУ переводу
    /// приходили оба события, и провал последним. Пользователь видел красное «Перевод не
    /// удался» через несколько секунд после того, как перевод прошёл, и перезванивал уже
    /// переведённому абоненту. Парковку ломало тем же: флаг парковки съедало первое событие,
    /// а второе уходило по ветке обычного перевода.
    /// </summary>
    private void OnTransferStateChanged(IntPtr core, IntPtr transfered, int newCallState) =>
        SafeCallback(nameof(OnTransferStateChanged), () =>
        {
            if (_transferReported) return;
            var st = (LinphoneCallState)newCallState;
            // Успех: собеседник дозвонился до цели.
            if (st is LinphoneCallState.Connected or LinphoneCallState.StreamsRunning)
            {
                _transferReported = true;
                TransferResult?.Invoke(true);
            }
            // Провал: терминальное состояние БЕЗ предшествующего соединения (занято,
            // не найден, отказ). После успеха сюда уже не попадём — флаг взведён.
            else if (st is LinphoneCallState.Error or LinphoneCallState.End)
            {
                _transferReported = true;
                TransferResult?.Invoke(false);
            }
        });

    /// <summary>Начинаем новый перевод: следующий итог от ядра относится к нему.</summary>
    private void NoteTransferStarted() => _transferReported = false;

    /// <summary>Подписаться на присутствие указанных абонентов: создаёт/находит друзей в списке
    /// по умолчанию, УБИРАЕТ лишних и включает SUBSCRIBE. Требует поддержки presence на сервере.</summary>
    public void SyncPresenceFriends(IEnumerable<string> peers)
    {
        if (_core == IntPtr.Zero) return;
        IntPtr list = linphone_core_get_default_friend_list(_core);
        if (list == IntPtr.Zero) return;
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string peer in peers)
        {
            if (string.IsNullOrWhiteSpace(peer)) continue;
            wanted.Add(PhoneNumberUtils.SipUser(peer));
            IntPtr addr = linphone_core_interpret_url(_core, peer.Trim());
            if (addr == IntPtr.Zero) continue;
            if (linphone_friend_list_find_friend_by_address(list, addr) == IntPtr.Zero)
            {
                string? full = TakeUtf8String(linphone_address_as_string(addr));
                if (!string.IsNullOrEmpty(full))
                {
                    IntPtr fr = linphone_core_create_friend_with_address(_core, full);
                    if (fr != IntPtr.Zero)
                    {
                        linphone_friend_enable_subscribes(fr, 1);
                        linphone_friend_list_add_friend(list, fr); // список берёт свою ссылку
                        try { linphone_friend_unref(fr); } catch { }
                    }
                }
            }
            linphone_address_unref(addr);
        }
        RemoveStaleFriends(list, wanted);
        linphone_friend_list_enable_subscriptions(list, 1);
    }

    /// <summary>Убирает из списка друзей всех, кого нет среди желаемых. Друзья живут в БД
    /// liblinphone и переживают рестарты: внешние номера, попавшие туда до фильтра внутренних
    /// (или убранные из избранного), продолжали подписываться — Asterisk на каждый их SUBSCRIBE
    /// писал NOTICE «does not exist … or has no associated hint».</summary>
    private void RemoveStaleFriends(IntPtr list, HashSet<string> wanted)
    {
        // Сначала снимок (как SnapshotAccounts): удалять из нативного списка во время
        // итерации по нему нельзя.
        var stale = new List<IntPtr>();
        for (IntPtr it = linphone_friend_list_get_friends(list); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr fr = bctbx_list_get_data(it);
            if (fr == IntPtr.Zero) continue;
            IntPtr addr = linphone_friend_get_address(fr);   // const — не освобождать
            string user = addr == IntPtr.Zero ? "" : (PtrToUtf8(linphone_address_get_username(addr)) ?? "");
            if (user.Length == 0 || !wanted.Contains(user)) stale.Add(fr);
        }
        foreach (IntPtr fr in stale) linphone_friend_list_remove_friend(list, fr);
        if (stale.Count > 0) Log?.Invoke($"Presence: убрано лишних подписок: {stale.Count}");
    }

    /// <summary>Включает/выключает публикацию своего присутствия (PUBLISH) на всех аккаунтах и
    /// ставит статус Online/Offline — чтобы коллеги, подписанные на нас, видели нас «в сети».
    /// Требует поддержки presence на сервере.</summary>
    public void SetPresencePublish(bool on)
    {
        if (_core == IntPtr.Zero) return;
        foreach (IntPtr acc in SnapshotAccounts())
        {
            IntPtr p = linphone_account_get_params(acc);
            if (p == IntPtr.Zero) continue;
            IntPtr clone = linphone_account_params_clone(p);
            if (clone == IntPtr.Zero) continue;
            linphone_account_params_enable_publish(clone, on ? 1 : 0);
            linphone_account_params_set_publish_expires(clone, 3600);
            linphone_account_set_params(acc, clone);
            linphone_account_params_unref(clone);
        }
        // При включении публикации применяем выбранный статус, при выключении — Offline.
        if (on) ApplyStatus(_myStatus);
        else linphone_core_set_consolidated_presence(_core, (int)LinphoneConsolidatedPresence.Offline);
    }

    private PresenceStatus _myStatus = PresenceStatus.Available;
    public PresenceStatus MyStatus => _myStatus;

    /// <summary>Установить свой статус (запоминается и публикуется, если публикация включена).</summary>
    public void SetMyStatus(PresenceStatus s)
    {
        _myStatus = s;
        ApplyStatus(s);
    }

    /// <summary>Во время разговора показывать «Разговаривает», по завершении вернуть выбранный статус.</summary>
    public void PublishOnThePhone(bool onCall) => ApplyStatus(onCall ? PresenceStatus.OnThePhone : _myStatus);

    private void ApplyStatus(PresenceStatus s)
    {
        if (_core == IntPtr.Zero) return;
        switch (s)
        {
            case PresenceStatus.Available:
                linphone_core_set_consolidated_presence(_core, (int)LinphoneConsolidatedPresence.Online); break;
            case PresenceStatus.DoNotDisturb:
                linphone_core_set_consolidated_presence(_core, (int)LinphoneConsolidatedPresence.DoNotDisturb); break;
            case PresenceStatus.Offline:
                linphone_core_set_consolidated_presence(_core, (int)LinphoneConsolidatedPresence.Offline); break;
            default:
                // OnThePhone / Away публикуются PIDF-активностью (Asterisk мапит в CustomPresence).
                int act = s == PresenceStatus.OnThePhone
                    ? (int)LinphonePresenceActivity.OnThePhone
                    : (int)LinphonePresenceActivity.Away;
                IntPtr model = linphone_presence_model_new_with_activity(act, null);
                if (model != IntPtr.Zero)
                {
                    linphone_core_set_presence_model(_core, model); // core берёт свою ссылку
                    try { linphone_presence_model_unref(model); } catch { }
                }
                break;
        }
    }

    /// <summary>Статусы сразу для списка абонентов — ОДИН переход на поток ядра вместо N.
    /// Избранное перечитывается на каждый presence-NOTIFY (после дебаунса), и N+1 синхронных
    /// вызовов из UI заметно подтормаживали список.</summary>
    public Dictionary<string, PresenceStatus> GetStatuses(IEnumerable<string> peers)
    {
        var result = new Dictionary<string, PresenceStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (string peer in peers)
            if (!string.IsNullOrWhiteSpace(peer) && !result.ContainsKey(peer))
                result[peer] = GetStatus(peer);
        return result;
    }

    /// <summary>Статус абонента для показа (активность из presence-модели, иначе сводный статус).</summary>
    public PresenceStatus GetStatus(string peer)
    {
        if (_core == IntPtr.Zero || string.IsNullOrWhiteSpace(peer)) return PresenceStatus.Offline;
        IntPtr list = linphone_core_get_default_friend_list(_core);
        if (list == IntPtr.Zero) return PresenceStatus.Offline;
        IntPtr addr = linphone_core_interpret_url(_core, peer.Trim());
        if (addr == IntPtr.Zero) return PresenceStatus.Offline;
        IntPtr fr = linphone_friend_list_find_friend_by_address(list, addr);
        linphone_address_unref(addr);
        if (fr == IntPtr.Zero) return PresenceStatus.Offline;

        var cons = (LinphoneConsolidatedPresence)linphone_friend_get_consolidated_presence(fr);
        if (cons == LinphoneConsolidatedPresence.Offline) return PresenceStatus.Offline;

        IntPtr model = linphone_friend_get_presence_model(fr);
        if (model != IntPtr.Zero)
        {
            IntPtr activity = linphone_presence_model_get_activity(model);
            if (activity != IntPtr.Zero)
                switch ((LinphonePresenceActivity)linphone_presence_activity_get_type(activity))
                {
                    case LinphonePresenceActivity.OnThePhone: return PresenceStatus.OnThePhone;
                    // Любая «отошёл»-активность чужого клиента (обед/совещание/отпуск) — наш «Отошёл».
                    case LinphonePresenceActivity.Lunch:
                    case LinphonePresenceActivity.Away:
                    case LinphonePresenceActivity.Meeting:
                    case LinphonePresenceActivity.Vacation: return PresenceStatus.Away;
                }
        }
        return cons switch
        {
            LinphoneConsolidatedPresence.Online => PresenceStatus.Available,
            LinphoneConsolidatedPresence.DoNotDisturb => PresenceStatus.DoNotDisturb,
            LinphoneConsolidatedPresence.Busy => PresenceStatus.OnThePhone,
            _ => PresenceStatus.Offline,
        };
    }

    // Состояние микрофона держим снимком и объявляем событием, а не возвращаем из команды.
    // Разница принципиальная: команда уходит на поток ядра, и её результат вызывающий получал
    // синхронным ожиданием, которое по таймауту отдавало «микрофон включён» — после чего ядро
    // всё-таки выключало микрофон, и интерфейс показывал ровно обратное правде. Вопрос
    // «меня слышно?» — не тот, на который можно отвечать наугад.
    private volatile bool _micMutedSnapshot;

    /// <summary>Микрофон заглушён (снимок; безопасно читать с любого потока).</summary>
    public bool MicMutedSnapshot => _micMutedSnapshot;

    /// <summary>Микрофон включили или выключили. Поднимается С ПОТОКА ЯДРА.</summary>
    public event Action<bool>? MicMuteChanged;

    private void SetMicMutedSnapshot(bool muted)
    {
        if (_micMutedSnapshot == muted) return;
        _micMutedSnapshot = muted;
        MicMuteChanged?.Invoke(muted);
    }

    /// <summary>Переключить микрофон. Новое состояние приходит событием
    /// <see cref="MicMuteChanged"/> — интерфейс рисует по нему, а не по возврату.</summary>
    public void ToggleMute()
    {
        if (_core == IntPtr.Zero) return;
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero)
        {
            bool muted = linphone_call_get_microphone_muted(call) != 0;
            linphone_call_set_microphone_muted(call, muted ? 0 : 1);
            // Читаем обратно, а не полагаемся на «должно было получиться»: ядро могло
            // не применить изменение, и тогда врать интерфейсу нельзя.
            SetMicMutedSnapshot(linphone_call_get_microphone_muted(call) != 0);
            return;
        }
        // Конференция: «текущего» звонка нет (get_current_call = NULL) — глушим микрофон
        // на уровне ядра. Обратно включается там же или автоматически при конце звонков.
        try
        {
            bool enabled = linphone_core_mic_enabled(_core) != 0;
            linphone_core_enable_mic(_core, enabled ? 0 : 1);
            SetMicMutedSnapshot(linphone_core_mic_enabled(_core) == 0);
        }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Поставить звонок на удержание или снять.
    ///
    /// Ничего не возвращает СОЗНАТЕЛЬНО. linphone_call_pause только отправляет re-INVITE, и
    /// сервер вправе ответить отказом; прежний вариант рапортовал «поставлено» сразу после
    /// отправки, и кнопка показывала «Возобновить» при живом, никуда не убранном разговоре —
    /// человек считал, что собеседник его не слышит, и говорил вслух то, что не предназначалось
    /// для его ушей. Настоящее состояние приходит событием смены состояния звонка
    /// (Pausing → Paused либо, при отказе, оставшийся StreamsRunning), и рисовать надо по нему.</summary>
    public void ToggleHold()
    {
        if (_core == IntPtr.Zero) return;
        // Активный разговор есть — ставим на удержание.
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero)
        {
            linphone_call_pause(call);
            return;
        }
        // Активного нет: удержанный звонок НЕ является «текущим» для liblinphone
        // (get_current_call для него возвращает NULL) — ищем его в списке и возобновляем.
        // PausedByRemote не трогаем: нас удержала другая сторона, resume не поможет.
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c == IntPtr.Zero) continue;
            if ((LinphoneCallState)linphone_call_get_state(c) is LinphoneCallState.Paused or LinphoneCallState.Pausing)
            {
                linphone_call_resume(c);
                return;
            }
        }
    }

    public int CurrentCallDuration
    {
        get
        {
            IntPtr call = CurrentCall;
            return call == IntPtr.Zero ? 0 : linphone_call_get_duration(call);
        }
    }

    // Пишущиеся ПРЯМО СЕЙЧАС звонки. Список, а не флаг: в конференции линий несколько, у каждой
    // свой файл (он задан в параметрах вызова при установке — см. BuildCallParamsWithRecording).
    // С одним флагом запись в конференции была невозможна в принципе: ToggleRecording искал
    // «текущий» звонок, а у конференции его нет (get_current_call возвращает NULL), и кнопка
    // молча не делала ничего — ни файла, ни строки в журнале.
    private readonly List<IntPtr> _recordingCalls = new();

    // Путь файла — на КАЖДУЮ линию, а не один на всех. Общее поле при двух звонках перетиралось
    // вторым вызовом, и пользователю показывался путь файла ЧУЖОГО разговора. Ключ — указатель
    // звонка, как у кодека и шифрования; чистится там же, на Released.
    private readonly Dictionary<IntPtr, string> _recordPathByCall = new();
    private string? _recordPath;   // файл, о котором сообщаем пользователю (последний начатый)

    public bool IsRecording => _recordingCalls.Count > 0 || _recordingConference != IntPtr.Zero;
    public string? RecordPath => _recordPath;

    // Состояние записи держим снимком и объявляем событием — по той же причине, что состояние
    // микрофона. Команда уходит на поток ядра, и её результат вызывающий получал синхронным
    // ожиданием, которое по просрочке отдавало «не пишется», после чего ядро всё-таки начинало
    // писать. Для продукта, который записывает разговоры, значение имеет файл, а не кнопка.
    private volatile bool _recordingSnapshot;

    /// <summary>Идёт ли запись (снимок; безопасно читать с любого потока).</summary>
    public bool RecordingSnapshot => _recordingSnapshot;

    /// <summary>Запись началась или закончилась. Поднимается С ПОТОКА ЯДРА.</summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>Пересчитать снимок после любой мутации записи и сообщить об изменении.</summary>
    private void SyncRecordingSnapshot()
    {
        bool now = IsRecording;
        if (_recordingSnapshot == now) return;
        _recordingSnapshot = now;
        RecordingChanged?.Invoke(now);
    }

    /// <summary>Каталог записей по умолчанию — Документы/LPhoneRecordings.</summary>
    public static string DefaultRecordingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LPhoneRecordings");

    /// <summary>Каталог для записей разговоров (rc [app] recordings_dir); пусто = по умолчанию.</summary>
    public string GetRecordingsDir()
    {
        string v = GetAppString("recordings_dir", "");
        return string.IsNullOrWhiteSpace(v) ? DefaultRecordingsDir : v;
    }

    /// <summary>Задать каталог записей. Пусто/null — вернуться к каталогу по умолчанию.</summary>
    public void SetRecordingsDir(string? path) =>
        SetAppString("recordings_dir", string.IsNullOrWhiteSpace(path) ? "" : path!.Trim());

    /// <summary>Сгенерировать путь нового файла записи (.mkv — liblinphone пишет звонки в Matroska).
    /// Прямые слэши надёжнее для mediastreamer. Создаёт каталог, если его нет.</summary>
    private string NextRecordFile()
    {
        string dir = GetRecordingsDir();
        try { Directory.CreateDirectory(dir); } catch { }
        // Каталог мог оказаться под кириллическим профилем (C:\Users\Роман\Документы) —
        // mediastreamer не откроет такой путь. Приводим к ASCII short-path (8.3).
        // Разделители в дате не для красоты: маскировка номеров в app.log режет любые
        // последовательности от семи цифр, и «20260803» превращалось в «2026**03» — найти
        // файл по строке из журнала было нельзя.
        return AsciiSafePath(Path.Combine(dir, $"call-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv")).Replace('\\', '/');
    }

    /// <summary>Возвращает путь, гарантированно открываемый нативными библиотеками:
    /// если в нём есть не-ASCII символы, подменяет на short-path (8.3, всегда латиница).
    /// Файла ещё нет — 8.3 берём у каталога-родителя и дописываем ASCII-имя файла.</summary>
    public static string AsciiSafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || IsAscii(path)) return path;
        try
        {
            if (File.Exists(path) || Directory.Exists(path)) { string s = ShortPath(path); if (s.Length > 0) return s; }
            string? dir = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            if (dir != null && Directory.Exists(dir) && IsAscii(name))
            {
                string sd = ShortPath(dir);
                if (sd.Length > 0) return Path.Combine(sd, name);
            }
        }
        catch { }
        return path; // не удалось — вернём как есть (лучше, чем упасть)
    }

    private static bool IsAscii(string s) { foreach (char c in s) if (c > 127) return false; return true; }

    private static string ShortPath(string path)
    {
        var sb = new System.Text.StringBuilder(300);
        uint n = GetShortPathNameW(path, sb, (uint)sb.Capacity);
        return n > 0 && n < sb.Capacity ? sb.ToString() : "";
    }

    /// <summary>Параметры звонка с заранее заданным record_file (чтобы рекордерный фильтр
    /// попал в медиаграф при старте потока — иначе поздний set_record_file не пишет на диск).
    /// Возвращает указатель параметров (нужно unref после invite/accept) или Zero.</summary>
    private IntPtr BuildCallParamsWithRecording(IntPtr callOrZero)
    {
        if (_core == IntPtr.Zero) return IntPtr.Zero;
        IntPtr p = linphone_core_create_call_params(_core, callOrZero);
        if (p == IntPtr.Zero) return IntPtr.Zero;
        string path = NextRecordFile();
        _pendingRecordPath = path;   // связать с линией сможем только когда ядро её создаст
        linphone_call_params_set_record_file(p, path);
        return p;
    }

    /// <summary>Файл, выданный последнему устанавливаемому звонку: указателя линии в этот момент
    /// ещё нет (исходящий) либо он относится к другой стороне (входящий). Привязываем к линии
    /// на первом же её состоянии — см. HandleCallState.</summary>
    private string? _pendingRecordPath;

    /// <summary>Начать/остановить запись текущего разговора. Путь уже задан при установке вызова
    /// (см. <see cref="BuildCallParamsWithRecording"/>).
    ///
    /// Результат наружу НЕ возвращается: он приходит событием <see cref="RecordingChanged"/> и
    /// снимком <see cref="RecordingSnapshot"/>. Прежний возврат вызывающий получал синхронным
    /// ожиданием, и по просрочке (start_recording лезет в файловую систему — под антивирусом
    /// или на сетевом диске это секунды) кнопка гасла, а запись при этом ШЛА. Для продукта,
    /// который пишет разговоры, значение имеет файл, а не кнопка.</summary>
    public void ToggleRecording()
    {
        if (_core == IntPtr.Zero) return;
        if (_recordingCalls.Count > 0 || _recordingConference != IntPtr.Zero) { StopAllRecording(); return; }
        StartRecording();
        SyncRecordingSnapshot();
    }

    private void StartRecording()
    {
        // Конференцию пишем ЦЕЛИКОМ, одним файлом с её микшированным звуком. Запускать
        // рекордеры отдельных линий бесполезно: в конференции медиаграф пересобирается, и
        // рекордер, заданный в параметрах вызова, в него не попадает — start_recording
        // отрабатывает без ошибки, а файла не появляется (проверено живым звонком).
        IntPtr conf = CurrentConference();
        if (conf != IntPtr.Zero)
        {
            string path = NextRecordFile();
            try
            {
                if (linphone_conference_start_recording(conf, path) == 0)
                {
                    _recordingConference = conf;
                    _recordPath = path;
                    Log?.Invoke("Запись конференции начата: " + path);
                    return;
                }
                Log?.Invoke("Запись конференции не начата: ядро вернуло ошибку");
            }
            catch (EntryPointNotFoundException)
            {
                Log?.Invoke("Запись конференции недоступна в этой сборке SDK");
            }
            return;
        }

        foreach (IntPtr c in RecordableCalls())
        {
            try
            {
                linphone_call_start_recording(c);
                _recordingCalls.Add(c);
                // Файл этой линии, а не «последний выданный»: при двух звонках общее поле
                // перетиралось, и пользователю показывался путь чужого разговора.
                if (_recordPathByCall.TryGetValue(c, out string? p)) _recordPath = p;
            }
            catch (Exception ex) { Log?.Invoke("Запись: не удалось начать на линии — " + ex.Message); }
        }
        if (_recordingCalls.Count == 0)
        {
            // Молчать нельзя: раньше кнопка «Запись» в конференции не оставляла ни файла,
            // ни следа, и понять, пишется ли разговор, было невозможно.
            Log?.Invoke("Запись не начата: активного разговора нет");
            return;
        }
        Log?.Invoke($"Запись начата, линий: {_recordingCalls.Count}");
    }

    private IntPtr _recordingConference;   // конференция, которая пишется сейчас (const, не освобождать)

    /// <summary>Текущая локальная конференция, либо Zero. Берём через любой её звонок:
    /// linphone_core_get_conference объявлен устаревшим и в обёртке SDK уже отсутствует.</summary>
    private IntPtr CurrentConference()
    {
        if (_core == IntPtr.Zero) return IntPtr.Zero;
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c == IntPtr.Zero) continue;
            try
            {
                IntPtr conf = linphone_call_get_conference(c);
                if (conf != IntPtr.Zero) return conf;
            }
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
        }
        return IntPtr.Zero;
    }

    /// <summary>Финализировать запись конференции, пока её медиа ещё живо.</summary>
    private void StopConferenceRecording()
    {
        if (_recordingConference == IntPtr.Zero) return;
        try { linphone_conference_stop_recording(_recordingConference); } catch { }
        _recordingConference = IntPtr.Zero;
        Log?.Invoke("Запись конференции остановлена");
        SyncRecordingSnapshot();
    }

    /// <summary>Что писать в обычном разговоре: его самого (конференция обрабатывается отдельно).</summary>
    private List<IntPtr> RecordableCalls()
    {
        var result = new List<IntPtr>();
        IntPtr cur = CurrentCall;
        if (cur != IntPtr.Zero) { result.Add(cur); return result; }
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c != IntPtr.Zero && !IsTerminal((LinphoneCallState)linphone_call_get_state(c))) result.Add(c);
        }
        return result;
    }

    /// <summary>Остановить запись одной линии (её звонок завершается).</summary>
    private void StopRecording(IntPtr call)
    {
        if (!_recordingCalls.Remove(call)) return;
        try { linphone_call_stop_recording(call); } catch { }
        SyncRecordingSnapshot();
    }

    /// <summary>Остановить запись (конференции и/или отдельных линий) и финализировать файлы.</summary>
    private void StopAllRecording()
    {
        StopConferenceRecording();
        foreach (IntPtr c in _recordingCalls) { try { linphone_call_stop_recording(c); } catch { } }
        if (_recordingCalls.Count > 0) Log?.Invoke("Запись остановлена");
        _recordingCalls.Clear();
        SyncRecordingSnapshot();
    }

    /// <summary>Передёрнуть сеть и перерегистрироваться (для авто-переподключения при смене сети).
    /// Во время разговора сеть НЕ передёргиваем: set_network_reachable(0) рвёт медиапотоки
    /// активного звонка (например, при подключении VPN, не меняющем основной маршрут) —
    /// ограничиваемся обновлением регистраций.</summary>
    /// <summary>Явно объявить сеть доступной или недоступной.
    ///
    /// Нужно перед засыпанием машины: без un-REGISTER Asterisk считает контакт живым до конца
    /// expires (по умолчанию час) и всё это время шлёт INVITE в никуда — звонящий слышит гудки,
    /// сотрудник не слышит ничего. Отдельно от <see cref="RefreshNetwork"/>, потому что там
    /// сеть передёргивается парой и только вне разговора, а здесь состояние выставляется
    /// осознанно и надолго.</summary>
    public void SetNetworkReachable(bool reachable)
    {
        if (_core == IntPtr.Zero) return;
        linphone_core_set_network_reachable(_core, reachable ? 1 : 0);
        Log?.Invoke(reachable ? "Сеть объявлена доступной" : "Сеть объявлена недоступной (снимаем регистрацию)");
    }

    public void RefreshNetwork()
    {
        if (_core == IntPtr.Zero) return;
        if (CallCount == 0)
        {
            linphone_core_set_network_reachable(_core, 0);
            linphone_core_set_network_reachable(_core, 1);
        }
        linphone_core_refresh_registers(_core);
    }

    // Устройства, кодеки, шифрование, NAT/TURN, провизионинг и настройки rc [app]
    // вынесены в LinphoneCore.DevicesConfig.cs

    // ------------------------------------------------------------------
    // Журнал звонков
    // ------------------------------------------------------------------
    public IReadOnlyList<CallLogEntry> GetCallHistory()
    {
        var result = new List<CallLogEntry>();
        if (_core == IntPtr.Zero) return result;

        IntPtr list = linphone_core_get_call_logs(_core); // const, НЕ освобождать
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr log = bctbx_list_get_data(it);
            if (log == IntPtr.Zero) continue;

            IntPtr addr = linphone_call_log_get_remote_address(log);
            string remote = addr == IntPtr.Zero ? "" : (TakeUtf8String(linphone_address_as_string(addr)) ?? "");
            string name = addr == IntPtr.Zero ? remote : (PtrToUtf8(linphone_address_get_username(addr)) ?? remote);

            bool incoming = linphone_call_log_get_dir(log) == (int)LinphoneCallDir.Incoming;
            bool missed = (LinphoneCallStatus)linphone_call_log_get_status(log) == LinphoneCallStatus.Missed;
            long start = linphone_call_log_get_start_date(log);
            int dur = linphone_call_log_get_duration(log);

            string sub = start > 0
                ? CallLogFormat.Subtitle(DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime, DateTime.Now, dur)
                : CallLogFormat.Duration(dur);

            // Шифрование — наша пометка в ref_key: своего поля защиты у call_log нет.
            string enc = "";
            try { enc = CallLogTag.Encryption(PtrToUtf8(linphone_call_log_get_ref_key(log))); }
            catch (EntryPointNotFoundException) { }

            result.Add(new CallLogEntry
            {
                Remote = remote, Title = name, Subtitle = sub,
                Arrow = incoming ? "↙" : "↗",   // вниз-влево / вверх-вправо
                Missed = missed, Incoming = incoming, StartUnix = start,
                Encryption = enc,
            });
        }
        return result;
    }

    public void ClearCallHistory()
    {
        if (_core != IntPtr.Zero) linphone_core_clear_call_logs(_core);
    }

    /// <summary>Сколько пропущенных звонков новее указанного момента (Unix-секунды).
    /// Используется для бейджа непросмотренных пропущенных на вкладке «Журнал».</summary>
    public int GetMissedSince(long sinceUnixSeconds)
    {
        if (_core == IntPtr.Zero) return 0;
        int n = 0;
        IntPtr list = linphone_core_get_call_logs(_core); // const, НЕ освобождать
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr log = bctbx_list_get_data(it);
            if (log == IntPtr.Zero) continue;
            if ((LinphoneCallStatus)linphone_call_log_get_status(log) == LinphoneCallStatus.Missed
                && linphone_call_log_get_start_date(log) > sinceUnixSeconds) n++;
        }
        return n;
    }

    // ------------------------------------------------------------------
    // Видео
    // ------------------------------------------------------------------
    /// <summary>Подключает окна рендера (собеседник + превью) и включает захват/отображение видео.</summary>
    public void SetupVideo()
    {
        if (_core == IntPtr.Zero) return;

        // Звонки по умолчанию АУДИО: видео не инициируем и не принимаем автоматически.
        // (Иначе исходящие уходят как видео, а на входящих видео может ронять рендер.)
        IntPtr policy = linphone_factory_create_video_activation_policy(_factory);
        if (policy != IntPtr.Zero)
        {
            // Сами видео не инициируем (исходящие — аудио), но входящее видео ПРИНИМАЕМ.
            // Направление принятия — SendRecv: на RecvOnly-ответ телефоны (проверено на
            // Fanvil 378) шлют повторный re-INVITE и затем BYE — звонок разваливался при
            // включении видео собеседником. Камеру пользователь всё равно контролирует
            // кнопкой «Видео» (ToggleVideo переводит поток в RecvOnly/SendRecv).
            linphone_video_activation_policy_set_automatically_initiate(policy, 0);
            linphone_video_activation_policy_set_automatically_accept(policy, 1);
            linphone_video_activation_policy_set_automatically_accept_direction(policy, (int)LinphoneMediaDirection.SendRecv);
            linphone_core_set_video_activation_policy(_core, policy);
        }

        // LINPHONE_VIDEO_DISPLAY_AUTO = (void*)-1: liblinphone сам создаёт окно видео на
        // своём потоке. Передавать ему чужой HWND НЕЛЬЗЯ — рендер падает в EGL-инициализации
        // (проверено на child/top-level, любых классах и размерах окна). Встраивание в экран
        // звонка делает MainWindow РЕПАРЕНТИНГОМ этого окна (см. NativeVideoAdopter).
        linphone_core_set_native_video_window_id(_core, new IntPtr(-1));
        // Отображение включаем сразу: входящее видео (авто-приём RecvOnly) должно
        // рендериться и до того, как пользователь включит свою камеру.
        linphone_core_enable_video_display(_core, 1);
    }

    /// <summary>Запрошен ли АТС автоответ (интерком, оповещение, домофон) и через сколько
    /// секунд. null — обычный звонок. Читаем ПРЕДЛОЖЕННЫЕ другой стороной параметры: только
    /// в них видны произвольные заголовки входящего INVITE.</summary>
    private int? IntercomDelay(IntPtr call)
    {
        if (call == IntPtr.Zero) return null;
        try
        {
            IntPtr prm = linphone_call_get_remote_params(call);
            if (prm == IntPtr.Zero) return null;
            string? alertInfo = PtrToUtf8(linphone_call_params_get_custom_header(prm, "Alert-Info"));
            string? callInfo = PtrToUtf8(linphone_call_params_get_custom_header(prm, "Call-Info"));
            return IntercomHeaders.AutoAnswerDelaySeconds(alertInfo, callInfo);
        }
        catch (EntryPointNotFoundException) { return null; }   // нет доступа к заголовкам в этой сборке SDK
    }

    /// <summary>Короткий Call-ID звонка для журнала (первые 8 символов SIP Call-ID).
    /// Полный слишком длинный для каждой строки, а восьми хватает, чтобы различить линии
    /// и сопоставить их с трассой в liblinphone.log и с CDR на стороне АТС.</summary>
    private static string? ShortCallId(IntPtr call)
    {
        if (call == IntPtr.Zero) return null;
        try
        {
            IntPtr log = linphone_call_get_call_log(call);
            if (log == IntPtr.Zero) return null;
            string? id = PtrToUtf8(linphone_call_log_get_call_id(log));
            if (string.IsNullOrEmpty(id)) return null;
            return id!.Length <= 8 ? id : id[..8];
        }
        catch (EntryPointNotFoundException) { return null; }
    }

    /// <summary>Согласованный кодек, запомненный на StreamsRunning: к завершению звонка
    /// медиасессия уже разобрана и узнать его неоткуда. Ключ — указатель звонка, чтобы в
    /// конференции линии не путались между собой.</summary>
    private readonly Dictionary<IntPtr, string> _codecByCall = new();

    /// <summary>Защита медиа, запомненная на StreamsRunning — по той же причине, что и кодек:
    /// current_params к завершению звонка отдаёт уже разобранную сессию, и вопрос «был ли этот
    /// разговор зашифрован» получал ответ «нет» независимо от того, как было на самом деле.</summary>
    private readonly Dictionary<IntPtr, string> _encByCall = new();

    private void RememberCodec(IntPtr call)
    {
        if (call == IntPtr.Zero) return;
        try
        {
            IntPtr cur = linphone_call_get_current_params(call);
            if (cur == IntPtr.Zero) return;
            IntPtr pt = linphone_call_params_get_used_audio_payload_type(cur);
            if (pt == IntPtr.Zero) return;
            string? mime = PtrToUtf8(linphone_payload_type_get_mime_type(pt));
            if (!string.IsNullOrEmpty(mime)) _codecByCall[call] = mime!;
        }
        catch (EntryPointNotFoundException) { }
    }

    private void RememberEncryption(IntPtr call)
    {
        if (call == IntPtr.Zero) return;
        string enc = CurrentCallEncryptionOf(call);
        // "none" не запоминаем: при повторном StreamsRunning (перевод, hold) сессия может
        // на мгновение оказаться без параметров, и это затёрло бы верный ответ.
        if (enc != "none") _encByCall[call] = enc;
    }

    /// <summary>Чем БЫЛ зашифрован звонок — из запомненного на StreamsRunning.</summary>
    private string RememberedEncryption(IntPtr call) =>
        _encByCall.TryGetValue(call, out string? e) ? e : "none";

    /// <summary>Громкость микрофона и динамика в текущем разговоре, шкала 0..1.
    /// Опрашивается несколько раз в секунду, поэтому здесь ни одного выделения памяти
    /// и ни одной строки — только два вызова в библиотеку.</summary>
    public (float Mic, float Speaker) CurrentAudioLevels()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return (0f, 0f);
        try
        {
            return (AudioLevel.Normalize(linphone_call_get_record_volume(call)),
                    AudioLevel.Normalize(linphone_call_get_play_volume(call)));
        }
        catch (EntryPointNotFoundException) { return (0f, 0f); }
    }

    /// <summary>Качество ТЕКУЩЕГО разговора. Опрашивается по таймеру, поэтому дешёвый:
    /// одна копия статистики, которую тут же освобождаем. Без звонка — None.</summary>
    public CallQuality CurrentQuality()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return CallQuality.None;
        try
        {
            float rating = linphone_call_get_current_quality(call);
            IntPtr stats = linphone_call_get_audio_stats(call);   // КОПИЯ — освобождаем сами
            if (stats == IntPtr.Zero) return CallQuality.None;
            try
            {
                float loss = linphone_call_stats_get_receiver_loss_rate(stats);
                float rtt = linphone_call_stats_get_round_trip_delay(stats);
                float jitter = linphone_call_stats_get_receiver_interarrival_jitter(stats);
                // RTT и джиттер библиотека отдаёт в СЕКУНДАХ — в миллисекундах эти числа
                // выглядели бы правдоподобно и были бы неверны в тысячу раз.
                return new CallQuality(rating, loss, (int)(rtt * 1000), (int)(jitter * 1000));
            }
            finally { try { linphone_call_stats_unref(stats); } catch { } }
        }
        catch (EntryPointNotFoundException) { return CallQuality.None; }
    }

    /// <summary>Итог звонка одной строкой: длительность, кодек, шифрование, потери, задержка,
    /// причина завершения. Раньше разбор жалобы «плохо было слышно» начинался с переписки —
    /// в журнале не было ничего, кроме последовательности состояний.</summary>
    private string CallSummary(IntPtr call, LinphoneReason reason)
    {
        var parts = new List<string>();
        int dur = 0;
        try { dur = linphone_call_get_duration(call); } catch { }
        parts.Add($"длит={dur / 60:00}:{dur % 60:00}");

        // Кодек берём из запомненного при StreamsRunning: к моменту End медиасессия уже
        // разобрана, и used_audio_payload_type возвращает NULL — в первой же живой проверке
        // строка вышла без кодека именно поэтому.
        if (_codecByCall.TryGetValue(call, out string? codec) && codec.Length > 0) parts.Add("кодек=" + codec);

        parts.Add("шифр=" + RememberedEncryption(call));

        try
        {
            IntPtr stats = linphone_call_get_audio_stats(call);   // КОПИЯ — освобождаем сами
            if (stats != IntPtr.Zero)
            {
                try
                {
                    parts.Add($"потери={linphone_call_stats_get_receiver_loss_rate(stats):0.0}%");
                    float rtt = linphone_call_stats_get_round_trip_delay(stats);
                    // 0 означает «RTCP ещё не дал оценку», а не «идеальная линия».
                    if (rtt > 0) parts.Add($"задержка={rtt * 1000:0} мс");
                }
                finally { try { linphone_call_stats_unref(stats); } catch { } }
            }
        }
        catch (EntryPointNotFoundException) { }

        if (reason != LinphoneReason.None) parts.Add("причина=" + reason);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Записать в журнал, чем был зашифрован разговор.
    ///
    /// У call_log в liblinphone нет поля защиты медиа — вообще нет такого API. Поэтому
    /// пишем сами в ref_key (см. CallLogTag): без этого узнать, был ли конкретный разговор
    /// зашифрован, можно было только из текстового лога, а туда пользователь не ходит.
    ///
    /// Незашифрованный звонок не помечаем: отсутствие метки и так значит «защиты не было»,
    /// а лишняя запись в базу на каждый звонок ни к чему.
    /// </summary>
    private void TagCallLogEncryption(IntPtr call)
    {
        try
        {
            string tag = CallLogTag.Format(RememberedEncryption(call));
            if (tag.Length == 0) return;

            IntPtr log = linphone_call_get_call_log(call);
            if (log != IntPtr.Zero) linphone_call_log_set_ref_key(log, tag);
        }
        catch (EntryPointNotFoundException) { /* сборка SDK без ref_key — журнал просто без замков */ }
        catch (Exception ex) { Log?.Invoke("Пометка журнала не записана: " + ex.Message); }
    }

    /// <summary>Чем зашифрован КОНКРЕТНЫЙ звонок (см. CurrentCallEncryption).</summary>
    private static string CurrentCallEncryptionOf(IntPtr call)
    {
        if (call == IntPtr.Zero) return "none";
        try
        {
            IntPtr cur = linphone_call_get_current_params(call);
            if (cur == IntPtr.Zero) return "none";
            return (LinphoneMediaEncryption)linphone_call_params_get_media_encryption(cur) switch
            {
                LinphoneMediaEncryption.SRTP => "srtp",
                LinphoneMediaEncryption.ZRTP => "zrtp",
                LinphoneMediaEncryption.DTLS => "dtls",
                _ => "none",
            };
        }
        catch (EntryPointNotFoundException) { return "none"; }
    }

    /// <summary>Защита ТЕКУЩЕГО разговора целиком: чем зашифровано медиа и по какому каналу
    /// шла сигнализация. Одним вызовом, потому что порознь эти два факта вводят в заблуждение
    /// (см. <see cref="CallSecurity"/>).</summary>
    public CallSecurity CurrentCallSecurity()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return CallSecurity.None;
        return new CallSecurity(CurrentCallEncryptionOf(call), SignallingIsTls(call));
    }

    /// <summary>Идёт ли сигнализация звонка по TLS. Берём транспорт аккаунта, через который
    /// установлен звонок; если ядро его не сообщает — аккаунт по умолчанию (исходящие идут
    /// через него).</summary>
    private bool SignallingIsTls(IntPtr call)
    {
        try
        {
            IntPtr acc = IntPtr.Zero;
            try { acc = linphone_call_get_account(call); }
            catch (EntryPointNotFoundException) { /* нет в этой сборке SDK — возьмём основной */ }
            if (acc == IntPtr.Zero) acc = linphone_core_get_default_account(_core);
            if (acc == IntPtr.Zero) return false;
            IntPtr p = linphone_account_get_params(acc);
            if (p == IntPtr.Zero) return false;
            return (LinphoneTransportType)linphone_account_params_get_transport(p) == LinphoneTransportType.Tls;
        }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>Чем зашифрован ТЕКУЩИЙ разговор: "none" / "srtp" / "zrtp" / "dtls".
    ///
    /// Берём именно current_params — то, о чём стороны договорились в SDP, а не то, что мы
    /// предложили. Настройка «шифрование по умолчанию SRTP» про конкретный звонок не говорит
    /// ничего: режим optimistic специально пропускает звонок открытым, если вторая сторона
    /// SRTP не умеет. До сих пор пользователь не мог узнать, защищён ли разговор, — включённое
    /// шифрование без видимого подтверждения это театр, а не безопасность.</summary>
    public string CurrentCallEncryption() => CurrentCallEncryptionOf(CurrentCall);

    /// <summary>Активен ли видеопоток текущего звонка (в любую сторону) — показывать ли
    /// область видео на экране разговора.</summary>
    public bool IsVideoActive()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return false;
        IntPtr cur = linphone_call_get_current_params(call);
        if (cur == IntPtr.Zero || linphone_call_params_video_enabled(cur) == 0) return false;
        return linphone_call_params_get_video_direction(cur) != (int)LinphoneMediaDirection.Inactive;
    }

    // Камера «выключена» — вместо кадров шлётся статичная картинка (см. ToggleVideo).
    private bool _cameraMuted;
    private string? _cameraBeforeMute;
    private bool _userWantsCamera;   // пользователь сам нажал «Видео» в этом звонке

    /// <summary>Виртуальная «камера» SDK, отдающая статичную картинку вместо кадров.</summary>
    private const string StaticImageDevice = "StaticImage: Static picture";

    /// <summary>Отправляем ли мы РЕАЛЬНЫЕ кадры (камера включена и не подменена картинкой).</summary>
    public bool IsCameraOn() => IsSendingVideo() && !_cameraMuted;

    // Состояние камеры — снимком и событием, как микрофон и запись. Прежний возврат из
    // ToggleVideo вызывающий получал ожиданием, и по просрочке кнопка показывала состояние,
    // обратное тому, которое ядро устанавливало секундой позже.
    private volatile bool _cameraOnSnapshot;

    /// <summary>Моя камера передаёт кадры (снимок; безопасно читать с любого потока).</summary>
    public bool CameraOnSnapshot => _cameraOnSnapshot;

    /// <summary>Камеру включили или выключили. Поднимается С ПОТОКА ЯДРА.</summary>
    public event Action<bool>? CameraStateChanged;

    private void SyncCameraSnapshot()
    {
        bool now = IsCameraOn();
        if (_cameraOnSnapshot == now) return;
        _cameraOnSnapshot = now;
        CameraStateChanged?.Invoke(now);
    }

    /// <summary>
    /// Кнопка «Видео». Видео в звонке ещё нет — добавляем поток (re-INVITE, SendRecv).
    /// Видео уже идёт — камеру НЕ гасим через RecvOnly-re-INVITE: телефоны (проверено на
    /// Fanvil 378) отвечают на такой re-INVITE 200 OK и тут же шлют BYE — звонок рвался.
    /// Вместо этого подменяем источник на статичную картинку: SDP не меняется, звонок цел,
    /// собеседник перестаёт видеть нас.
    ///
    /// Результат наружу НЕ возвращается — он приходит событием <see cref="CameraStateChanged"/>
    /// и снимком <see cref="CameraOnSnapshot"/> (см. ToggleMute: ждать ответа ядра на клике
    /// нельзя, а рисовать «как должно было получиться» — значит однажды соврать).
    /// </summary>
    public void ToggleVideo()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return;

        if (IsVideoActive())
        {
            SetCameraMuted(!_cameraMuted);
            _userWantsCamera = !_cameraMuted;
            return;
        }

        // Видео в звонке нет — поднимаем захват/отображение и добавляем видеопоток.
        _cameraMuted = false;
        _userWantsCamera = true;
        linphone_core_enable_video_capture(_core, 1);
        linphone_core_enable_video_display(_core, 1);
        IntPtr p = linphone_core_create_call_params(_core, call);
        if (p == IntPtr.Zero) return;
        linphone_call_params_enable_video(p, 1);
        linphone_call_params_set_video_direction(p, (int)LinphoneMediaDirection.SendRecv);
        linphone_call_update(call, p);
        linphone_call_params_unref(p);   // параметры наши — иначе утечка на каждое включение видео
        _videoActiveSnapshot = true;     // не ждём StreamsRunning: UI покажет область сразу
        // Кадры пойдут только после re-INVITE, но решение пользователя уже принято, и кнопка
        // обязана отозваться немедленно — иначе он нажмёт её второй раз.
        _cameraOnSnapshot = true;
        CameraStateChanged?.Invoke(true);
        Log?.Invoke("Видео включено");
    }

    /// <summary>Подменить источник видео статичной картинкой (или вернуть камеру).
    /// SDP не трогаем — звонок не рвётся (см. ToggleVideo).</summary>
    private void SetCameraMuted(bool muted)
    {
        if (_core == IntPtr.Zero || muted == _cameraMuted) return;
        _cameraMuted = muted;
        if (muted)
        {
            string? cur = CurrentCamera;
            if (!string.IsNullOrEmpty(cur) && cur != StaticImageDevice) _cameraBeforeMute = cur;
            try { linphone_core_set_video_device(_core, StaticImageDevice); } catch { }
        }
        else if (!string.IsNullOrEmpty(_cameraBeforeMute))
        {
            try { linphone_core_set_video_device(_core, _cameraBeforeMute); } catch { }
        }
        Log?.Invoke(muted ? "Моя камера выключена (статичная картинка)" : "Моя камера включена");
        SyncCameraSnapshot();
    }

    /// <summary>
    /// Включает локальное превью с выбранной камеры (для проверки в настройках).
    /// Окно превью создаёт сам liblinphone на своём потоке (NATIVE_PREVIEW = -1):
    /// встроить превью в наш HWND нельзя — рендер падает (см. SetupVideo).
    /// </summary>
    public void StartCameraPreview()
    {
        if (_core == IntPtr.Zero) return;
        linphone_core_enable_video_capture(_core, 1);
        linphone_core_set_native_preview_window_id(_core, new IntPtr(-1)); // AUTO: своё плавающее окно
        linphone_core_enable_video_preview(_core, 1);
    }

    /// <summary>Гасит локальное превью камеры (и закрывает окно, созданное liblinphone).</summary>
    public void StopCameraPreview()
    {
        if (_core == IntPtr.Zero) return;
        linphone_core_enable_video_preview(_core, 0);
    }

    /// <summary>Отправляем ли мы сейчас своё видео (камеру).</summary>
    public bool IsSendingVideo()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return false;
        IntPtr cur = linphone_call_get_current_params(call);
        if (cur == IntPtr.Zero || linphone_call_params_video_enabled(cur) == 0) return false;
        int dir = linphone_call_params_get_video_direction(cur);
        return dir == (int)LinphoneMediaDirection.SendRecv || dir == (int)LinphoneMediaDirection.SendOnly;
    }

    // Аккаунты (мультиаккаунт) и хранение SIP-паролей вынесены в LinphoneCore.Accounts.cs

    /// <summary>Уровень логов liblinphone: подробный (Debug) или обычный (Message).</summary>
    public void SetVerboseLogs(bool verbose)
    {
        if (_logService != IntPtr.Zero)
            linphone_logging_service_set_log_level(_logService,
                (int)(verbose ? LinphoneLogLevel.Debug : LinphoneLogLevel.Message));
    }

    /// <summary>Снимок текущего звонка (для синхронизации UI), либо null если звонка нет.</summary>
    public CallEvent? CurrentCallSnapshot()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return null;
        var state = (LinphoneCallState)linphone_call_get_state(call);
        string? remote = null;
        IntPtr addr = linphone_call_get_remote_address(call);
        if (addr != IntPtr.Zero)
            remote = TakeUtf8String(linphone_address_as_string(addr));
        bool incoming = linphone_call_get_dir(call) == (int)LinphoneCallDir.Incoming;
        return new CallEvent(state, null, remote, incoming, linphone_call_get_duration(call));
    }

    /// <summary>Звонок в терминальном состоянии (завершается/завершён/ошибка).</summary>
    private static bool IsTerminal(LinphoneCallState st) =>
        st is LinphoneCallState.End or LinphoneCallState.Released or LinphoneCallState.Error;

    /// <summary>Снимок звонка для показа: активный, иначе любой оставшийся (например, удержанный).
    /// Нужно, чтобы после завершения одного из двух звонков показать второй, а не уйти в набор.</summary>
    public CallEvent? DisplayCallSnapshot()
    {
        if (_core == IntPtr.Zero) return null;
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero)
            for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
            {
                IntPtr c = bctbx_list_get_data(it);
                // Пропускаем завершающийся звонок: на End он ещё в списке (уйдёт на Released),
                // иначе после сброса покажем уже завершённый вызов вместо оставшегося.
                if (c != IntPtr.Zero && !IsTerminal((LinphoneCallState)linphone_call_get_state(c))) { call = c; break; }
            }
        if (call == IntPtr.Zero) return null;
        var state = (LinphoneCallState)linphone_call_get_state(call);
        IntPtr addr = linphone_call_get_remote_address(call);
        string? remote = addr == IntPtr.Zero ? null : TakeUtf8String(linphone_address_as_string(addr));
        bool incoming = linphone_call_get_dir(call) == (int)LinphoneCallDir.Incoming;
        return new CallEvent(state, null, remote, incoming, linphone_call_get_duration(call));
    }

    private IntPtr CurrentCall =>
        _core == IntPtr.Zero ? IntPtr.Zero : linphone_core_get_current_call(_core);

    // ------------------------------------------------------------------
    // Колбэки из native (выполняются во время Iterate() на UI-потоке)
    // ------------------------------------------------------------------
    /// <summary>Сработала защита от бесконечных попыток с неверным паролем: аккаунт (identity) остановлен.</summary>
    public event Action<string>? RegistrationAuthStopped;

    // Подряд идущие auth-ошибки на каждый аккаунт. liblinphone с неверным паролем
    // ретраит REGISTER вечно — fail2ban на Asterisk банит IP всего офиса.
    // Ключ — identity (sip:login@server), а НЕ указатель proxy config: после пересоздания
    // аккаунта аллокатор возвращает тот же адрес, и счётчик чужих ошибок доставался новому.
    private readonly Dictionary<string, int> _authFailures = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxAuthFailures = 3;

    /// <summary>
    /// Тело нативного колбэка. Обёртка обязательна для КАЖДОГО из них.
    ///
    /// Managed-исключение, поднявшееся через границу native → managed, не перехватывается
    /// ни обработчиком XAML, ни TaskScheduler: рантайм считает такой стек невосстановимым
    /// и убивает процесс на месте. То есть любая мелочь в разборе события (неожиданный NULL,
    /// строка в чужой кодировке, исключение подписчика) роняет софтфон посреди разговора —
    /// худший из возможных отказов для этого продукта.
    ///
    /// Здесь исключение обязано умереть, оставив след в журнале. Само событие при этом
    /// теряется — это осознанный размен: потерянное обновление интерфейса лучше, чем
    /// оборванный звонок.
    /// </summary>
    private void SafeCallback(string name, Action body)
    {
        try { body(); }
        catch (Exception ex)
        {
            // Логирование тоже под защитой: подписчик Log живёт в приложении и тоже может бросить.
            try { Log?.Invoke($"ОШИБКА в колбэке {name}: {ex.GetType().Name}: {ex.Message}"); } catch { }
        }
    }

    private void OnRegistrationStateChanged(IntPtr core, IntPtr proxyConfig, int state, string? message) =>
        SafeCallback(nameof(OnRegistrationStateChanged),
            () => HandleRegistrationState(proxyConfig, state, message));

    private void HandleRegistrationState(IntPtr proxyConfig, int state, string? message)
    {
        var st = (LinphoneRegistrationState)state;
        if (proxyConfig != IntPtr.Zero)
        {
            string identity = ProxyIdentity(proxyConfig);
            if (st == LinphoneRegistrationState.Ok)
                _authFailures.Remove(identity);
            else if (st == LinphoneRegistrationState.Failed && IsAuthFailure(proxyConfig))
            {
                int n = _authFailures.TryGetValue(identity, out int v) ? v + 1 : 1;
                _authFailures[identity] = n;
                if (n >= MaxAuthFailures)
                {
                    _authFailures.Remove(identity);
                    StopRegistration(proxyConfig);
                    RegistrationStateChanged?.Invoke(st, message);
                    RegistrationAuthStopped?.Invoke(ProxyIdentity(proxyConfig));
                    return;
                }
            }
        }
        RegistrationStateChanged?.Invoke(st, message);
    }

    private static bool IsAuthFailure(IntPtr proxyConfig)
    {
        try
        {
            var reason = (LinphoneReason)linphone_proxy_config_get_error(proxyConfig);
            return reason is LinphoneReason.Forbidden or LinphoneReason.Unauthorized;
        }
        catch { return false; } // нет функции в этой сборке SDK — защиту не включаем
    }

    /// <summary>Выключает REGISTER у аккаунта (до пересохранения аккаунта пользователем).</summary>
    private void StopRegistration(IntPtr proxyConfig)
    {
        try
        {
            linphone_proxy_config_edit(proxyConfig);
            linphone_proxy_config_enable_register(proxyConfig, 0);
            linphone_proxy_config_done(proxyConfig);
            Log?.Invoke("Регистрация остановлена: сервер отвергает учётные данные "
                        + $"({MaxAuthFailures} попытки подряд). Проверьте логин/пароль.");
        }
        catch { }
    }

    private string ProxyIdentity(IntPtr proxyConfig)
    {
        try
        {
            IntPtr addr = linphone_proxy_config_get_identity_address(proxyConfig);
            return addr == IntPtr.Zero ? "" : (TakeUtf8String(linphone_address_as_string(addr)) ?? "");
        }
        catch { return ""; }
    }

    private void OnCallStateChanged(IntPtr core, IntPtr call, int state, string? message) =>
        SafeCallback(nameof(OnCallStateChanged), () => HandleCallState(call, state, message));

    private void HandleCallState(IntPtr call, int state, string? message)
    {
        string? remote = null;
        bool incoming = false;
        int duration = 0;
        if (call != IntPtr.Zero)
        {
            IntPtr addr = linphone_call_get_remote_address(call);
            if (addr != IntPtr.Zero)
                remote = TakeUtf8String(linphone_address_as_string(addr));
            incoming = linphone_call_get_dir(call) == (int)LinphoneCallDir.Incoming;
            duration = linphone_call_get_duration(call);
        }
        // Если завершается записываемый звонок — финализируем файл (иначе MKV не запишется на диск).
        var st = (LinphoneCallState)state;
        // Останавливаем запись ТОЛЬКО завершающейся линии: если писались отдельные звонки,
        // остальные продолжают. Запись конференции финализируем на первом же завершении:
        // конференция всё равно распадается, а файл надо закрыть, пока медиа живо.
        if (call != IntPtr.Zero && IsTerminal(st))
        {
            StopRecording(call);
            StopConferenceRecording();
        }
        // Файл записи, выданный этой линии при установке вызова, связываем с её указателем:
        // раньше путь был один на всех, и второй звонок перетирал его первому.
        if (call != IntPtr.Zero && _pendingRecordPath is { Length: > 0 } pending && !IsTerminal(st))
        {
            if (_recordPathByCall.TryAdd(call, pending)) _pendingRecordPath = null;
        }
        // Видео принято автоматически (собеседник включил камеру), а пользователь свою
        // камеру не включал — не начинаем вещать: подменяем источник картинкой. Ответить
        // RecvOnly нельзя (некоторые клиенты рвут звонок), поэтому глушим именно источник.
        if (st == LinphoneCallState.StreamsRunning && !_userWantsCamera && IsVideoActive() && !_cameraMuted)
            SetCameraMuted(true);

        if (CallCount == 0)
        {
            // Камера была подменена статичной картинкой — возвращаем настоящую,
            // иначе следующий видеозвонок молча уйдёт с картинкой.
            SetCameraMuted(false);
            _userWantsCamera = false;
            StopAllRecording(); _recordPath = null;   // подстраховка: звонков не осталось
            _pendingRecordPath = null;
            // Мьют уровня ядра (конференция) переживает звонки — включаем микрофон обратно,
            // иначе следующий разговор молча начнётся с выключенным микрофоном.
            try { if (linphone_core_mic_enabled(_core) == 0) linphone_core_enable_mic(_core, 1); } catch { }
            SetMicMutedSnapshot(false);   // звонков нет — состояние кнопки обязано вернуться в исходное
        }
        if (CallCount <= 1) _conferenceActive = false;   // конференция кончилась вместе со вторым звонком
        // Разговор завершён, а второй звонок остался на удержании — возобновляем его сами
        // (как iPhone): иначе абонент слушает музыку удержания, пока не бросит трубку.
        // Пробуем и на End, и на Released: на End медиа завершающегося звонка может быть ещё
        // не отпущено и resume не пройдёт — вторая попытка добьёт.
        // ВАЖНО: resume НЕ из этого колбэка — liblinphone не рекомендует мутировать состояние
        // звонков изнутри их же call_state_changed. Откладываем через очередь потока ядра
        // (Defer): исполнится сразу после выхода из iterate, тем же потоком.
        if (IsTerminal(st) && CurrentCall == IntPtr.Zero && _core != IntPtr.Zero)
        {
            if (Defer is { } defer) defer(ResumeHeldAfterTerminated);
            else ResumeHeldAfterTerminated();   // фасада нет (тесты/до Start) — как раньше, на месте
        }
        // Причина завершения — для человеческого сообщения в UI (Занято/Нет ответа/…).
        var reason = LinphoneReason.None;
        if (call != IntPtr.Zero && IsTerminal(st))
        {
            try { reason = (LinphoneReason)linphone_call_get_reason(call); } catch { }
        }
        string? callId = ShortCallId(call);

        // Кодек запоминаем, пока медиасессия жива: к завершению звонка его уже не узнать.
        if (st == LinphoneCallState.StreamsRunning)
        {
            // Сверяем снимок микрофона с тем, что реально в звонке: медиасессия пересобирается
            // при удержании, переводе и включении видео, и состояние мьюта могло измениться
            // не по нашей команде.
            if (call != IntPtr.Zero)
            {
                try { SetMicMutedSnapshot(linphone_call_get_microphone_muted(call) != 0); } catch { }
            }
            RememberCodec(call);
            RememberEncryption(call);
            // Пометку журнала ставим ИМЕННО ЗДЕСЬ. На End она не доживает до базы: строка
            // журнала пишется вместе с завершением звонка, и ref_key, выставленный в тот же
            // момент, в неё уже не попадает — проверено живьём, refkey оставался пустым.
            TagCallLogEncryption(call);
        }

        // Итог звонка — одной строкой и ровно один раз (на End; Released приходит следом
        // за ним и повторил бы её). К этому моменту медиа ещё живо и статистика доступна.
        if (call != IntPtr.Zero && st == LinphoneCallState.End)
        {
            try { Log?.Invoke($"ИТОГ звонка {callId}: {CallSummary(call, reason)}"); }
            catch (Exception ex) { Log?.Invoke("Итог звонка не собран: " + ex.Message); }
        }
        // Указатель звонка после Released переиспользуется аллокатором — запись обязана уйти,
        // иначе следующий звонок унаследует чужой кодек, а словарь будет расти весь сеанс.
        if (call != IntPtr.Zero && st == LinphoneCallState.Released)
        {
            _codecByCall.Remove(call);
            _encByCall.Remove(call);
            _recordPathByCall.Remove(call);
        }

        // Снимок ДО события: подписчик (UI) прочитает уже согласованные значения без RunOnCore.
        _callCountSnapshot = CallCount;
        _videoActiveSnapshot = IsVideoActive();
        SyncCameraSnapshot();
        SyncRecordingSnapshot();

        // Заголовки интеркома есть только у входящего INVITE — читаем ровно там.
        int? intercom = st == LinphoneCallState.IncomingReceived ? IntercomDelay(call) : null;

        CallStateChanged?.Invoke(
            new CallEvent(st, message, remote, incoming, duration, reason, callId, intercom));
    }

    /// <summary>Отложить действие до выхода из нативного колбэка: фасад подставляет сюда
    /// очередь потока ядра (исполнится тем же потоком сразу после iterate).</summary>
    internal Action<Action>? Defer;

    /// <summary>Пробросить сообщение в общий лог приложения (диагностика диспетчера).</summary>
    internal void RaiseLog(string message) => Log?.Invoke(message);

    /// <summary>Возобновляет удержанный звонок после завершения разговора. Вызывается ВНЕ
    /// call_state_changed (через Defer); завершающиеся звонки отсеиваются по состоянию —
    /// на момент исполнения End-звонок мог ещё не уйти из списка (Paused он быть не может).</summary>
    private void ResumeHeldAfterTerminated()
    {
        if (_core == IntPtr.Zero || CurrentCall != IntPtr.Zero) return;
        for (IntPtr it = linphone_core_get_calls(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr c = bctbx_list_get_data(it);
            if (c == IntPtr.Zero) continue;
            if ((LinphoneCallState)linphone_call_get_state(c) == LinphoneCallState.Paused)
            {
                linphone_call_resume(c);
                Log?.Invoke("Возобновляем удержанный звонок после завершения разговора");
                break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Личный профиль в ProgramData: SID-подкаталог, миграция, ACL
    // ------------------------------------------------------------------
    /// <summary>SID текущего пользователя (S-1-5-…, чистый ASCII) для имени личного подкаталога.</summary>
    private static string CurrentUserSid()
    {
        try
        {
            return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "default";
        }
        catch { return "default"; }
    }

    /// <summary>Старые версии держали профиль прямо в C:\ProgramData\L-Phone — общий для всех
    /// пользователей машины и читаемый всеми (включая linphonerc с учётными данными).
    /// Переносим файлы в личный SID-подкаталог. Каждый старт подбираем остатки: если в прошлый
    /// раз часть файлов была занята (например, вторым экземпляром до single-instance) — доедет
    /// сейчас. Подкаталоги (SID-профили, свои и чужих пользователей) не трогаем; старый профиль
    /// состоял только из файлов. Перенесённые файлы сохраняют старый «открытый» DACL — его
    /// вычищает HardenDirectoryAcl, который вызывается сразу после (см. Start).
    ///
    /// ВЛАДЕЛЕЦ ПРОВЕРЯЕТСЯ У КАЖДОГО ФАЙЛА, и это не перестраховка. Родительский каталог
    /// открыт «Пользователям» на запись (иначе первый запуск без прав администратора не создал
    /// бы свой подкаталог), поэтому другой пользователь машины может заранее положить туда свой
    /// linphonerc. Через него задаются provisioning_uri (скачивание конфигурации с сервера
    /// атакующего), root_ca и каталог плагинов — то есть это захват настроек SIP с обычной
    /// учётки. <see cref="EnsureOwnProfileDir"/> закрывает вариант «создать подкаталог с чужим
    /// SID», но без этой проверки подложенный файл приезжал бы в наш свежесозданный профиль
    /// сам, а HardenDirectoryAcl затем делал бы его нашим.</summary>
    private static void MigrateSharedProfile(string oldDir, string newDir)
    {
        try
        {
            foreach (string f in Directory.EnumerateFiles(oldDir))
            {
                var owner = FileOwner(f);
                // Владельца не прочитали — тоже не переносим: «не знаю, чей файл» в этом
                // месте равнозначно «чужой». Цена ошибки несимметрична: неперенесённый файл
                // стоит настроек прошлой версии, перенесённый чужой — контроля над SIP-аккаунтом.
                if (owner == null || !IsSelfOrSystem(owner))
                {
                    _staticLog?.Invoke($"Миграция профиля: {Path.GetFileName(f)} создан другим "
                                       + "пользователем — файл НЕ перенесён (возможна подмена конфигурации)");
                    continue;
                }
                string dest = Path.Combine(newDir, Path.GetFileName(f));
                try { if (!File.Exists(dest)) File.Move(f, dest); } catch { /* файл занят — пропустим */ }
            }
        }
        catch { }
    }

    /// <summary>Владелец файла (SID) либо null, если прочитать не удалось.</summary>
    private static System.Security.Principal.SecurityIdentifier? FileOwner(string path)
    {
        try
        {
            return System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(path))
                .GetOwner(typeof(System.Security.Principal.SecurityIdentifier))
                as System.Security.Principal.SecurityIdentifier;
        }
        catch { return null; }
    }

    /// <summary>Закрывает каталог профиля от других пользователей машины: полный доступ только
    /// текущему пользователю, SYSTEM и администраторам (наследование от ProgramData отрезаем).</summary>
    /// <summary>Создать каталог профиля так, чтобы им нельзя было завладеть заранее.
    /// Родительский C:\ProgramData\L-Phone открыт «Пользователям» на запись (иначе первый запуск
    /// без прав администратора не создал бы свой подкаталог), а значит другой пользователь машины
    /// может СОЗДАТЬ каталог с чужим SID до первого запуска жертвы и положить туда свой linphonerc.
    /// Через него управляются provisioning_uri (скачивание конфигурации с сервера атакующего),
    /// root_ca и каталог плагинов — то есть это захват настроек SIP с обычной учётки.
    /// Поэтому: создаём сразу с нужным DACL, а у существующего каталога проверяем владельца.</summary>
    /// <returns>false — каталог занят ДРУГИМ пользователем машины; работать в нём нельзя,
    /// вызывающий обязан уйти в <see cref="FallbackProfileDir"/>.</returns>
    private static bool EnsureOwnProfileDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            var owner = ProfileDirOwner(dir);
            // Читать чужой каталог нельзя: там может лежать подложенный linphonerc с чужим
            // provisioning_uri и root_ca, то есть захват настроек SIP с обычной учётки.
            //
            // Но и ОТКАЗЫВАТЬ В ЗАПУСКЕ нельзя, а раньше делалось именно это. Родительский
            // каталог открыт «Пользователям» на запись (иначе первый запуск без прав
            // администратора не создал бы свой подкаталог), поэтому любой сотрудник
            // терминального сервера мог заранее создать каталог с ЧУЖИМ SID и навсегда лишить
            // коллегу телефона: отбитая атака «подмена конфигурации» превращалась в проходящую
            // «телефон не запускается», снимаемую только руками администратора на каждой машине.
            // Уходим в заведомо свой каталог — защита сохраняется целиком, побочный эффект уходит.
            if (owner != null && !IsSelfOrSystem(owner))
            {
                _staticLog?.Invoke($"ВНИМАНИЕ: каталог профиля {dir} создан другим пользователем "
                    + $"({owner.Value}) — возможна подмена конфигурации. Работаю в личном каталоге.");
                return false;
            }
            return true;
        }
        try { new DirectoryInfo(dir).Create(ProfileDirSecurity()); }   // атомарно: без окна с чужим DACL
        catch (Exception ex)
        {
            _staticLog?.Invoke($"Каталог профиля создан без явного DACL: {ex.Message}");
            Directory.CreateDirectory(dir);
        }
        return true;
    }

    /// <summary>Запасной каталог профиля, когда основной занят другим пользователем машины.
    /// LOCALAPPDATA принадлежит пользователю, и подложить туда конфигурацию посторонний не
    /// может. К ASCII приводит ВЫЗЫВАЮЩИЙ — уже после создания каталога (см. Start).</summary>
    private static string FallbackProfileDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LinphoneWinUI", "profile");

    private static System.Security.Principal.SecurityIdentifier? ProfileDirOwner(string dir)
    {
        try
        {
            return System.IO.FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(dir))
                .GetOwner(typeof(System.Security.Principal.SecurityIdentifier))
                as System.Security.Principal.SecurityIdentifier;
        }
        catch { return null; }   // не смогли прочитать — не мешаем запуску
    }

    private static bool IsSelfOrSystem(System.Security.Principal.SecurityIdentifier owner)
    {
        try
        {
            // Владельцем может быть сам пользователь либо (для админов, по политике Windows)
            // группа «Администраторы» / SYSTEM — всё это свои.
            var self = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (self != null && owner.Equals(self)) return true;
            foreach (var wk in new[]
            {
                System.Security.Principal.WellKnownSidType.LocalSystemSid,
                System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid,
            })
                if (owner.IsWellKnown(wk)) return true;
            return false;
        }
        catch { return true; }
    }

    /// <summary>Логи ядра пишутся с уровнем Message, то есть SIP-сообщения целиком: кто кому
    /// звонил и когда. liblinphone ротирует по размеру, но число файлов не ограничивает — на
    /// рабочем профиле накопилось 74 МБ незамаскированных трасс. Оставляем только текущий и один
    /// предыдущий: диагностика «что было в прошлый запуск» сохраняется, архив за месяц — нет.
    ///
    /// Зовётся при старте И ПЕРИОДИЧЕСКИ (см. PruneLogsPeriodically). Одного вызова при старте
    /// НЕ ХВАТАЕТ, и это видно на живой машине: софтфон работает сутками, ротация происходит
    /// ВНУТРИ сессии, и к следующему запуску там лежало четыре файла вместо двух — 13 МБ
    /// трасс с шестью тысячами заголовков From/To. Ровно этот архив и уезжает в поддержку.</summary>
    private static void PruneCoreLogs(string dir)
    {
        try
        {
            foreach (var f in new DirectoryInfo(dir).GetFiles("liblinphone.log_*"))
                if (f.Name != "liblinphone.log_1") { try { f.Delete(); } catch { } }
        }
        catch { }
    }

    // Проверяем раз в ~5 минут: 50 тиков насоса в секунду × 300 с. Обращение к файловой
    // системе редкое и дешёвое (перечисление двух-четырёх имён), зато архив трасс перестаёт
    // расти без ограничений в долгих сессиях.
    private const int PruneLogsEveryTicks = 15000;

    private void PruneLogsPeriodically(int tick)
    {
        if (tick % PruneLogsEveryTicks != 0 || ProfileDir.Length == 0) return;
        PruneCoreLogs(ProfileDir);
    }

    private static System.Security.AccessControl.DirectorySecurity ProfileDirSecurity()
    {
        var sec = new System.Security.AccessControl.DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const System.Security.AccessControl.InheritanceFlags inherit =
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
            System.Security.AccessControl.InheritanceFlags.ObjectInherit;
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        foreach (var sid in new[]
        {
            user,
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
        })
        {
            if (sid == null) continue;
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                sid, System.Security.AccessControl.FileSystemRights.FullControl, inherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
        }
        return sec;
    }

    private static void HardenDirectoryAcl(string dir)
    {
        try
        {
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (user == null) return;
            var di = new DirectoryInfo(dir);
            var sec = new System.Security.AccessControl.DirectorySecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const System.Security.AccessControl.InheritanceFlags inherit =
                System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                System.Security.AccessControl.InheritanceFlags.ObjectInherit;
            foreach (var sid in new[]
            {
                user,
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
            })
                sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    sid, System.Security.AccessControl.FileSystemRights.FullControl, inherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
            System.IO.FileSystemAclExtensions.SetAccessControl(di, sec);
        }
        catch (Exception ex)
        {
            _staticLog?.Invoke("ОШИБКА: не удалось закрыть каталог профиля от других пользователей (ACL): " + ex.Message);
        }
    }

    /// <summary>Лог для статических методов (единственный экземпляр ядра подставляет свой Log в конструкторе).</summary>
    private static Action<string>? _staticLog;

    // ------------------------------------------------------------------
    private static string? FindMsPluginsDir()
    {
        string[] candidates =
        {
            Path.Combine(SdkRoot, "mediastreamer", "plugins"),        // layout NuGet-пакета
            Path.Combine(SdkRoot, "lib", "mediastreamer", "plugins"), // установленный/бандл
            Path.Combine(SdkRoot, "lib", "mediastreamer2", "plugins"),
            Path.Combine(SdkRoot, "lib", "mediastreamer"),
            SdkBin,
        };
        foreach (var c in candidates)
            if (Directory.Exists(c))
                return c;
        return null;
    }

    private static string? PtrToUtf8(IntPtr ptr) =>
        ptr == IntPtr.Zero ? null : System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);

    /// <summary>Читает строку, которой ВЛАДЕЕТ вызывающий (например, из linphone_address_as_string —
    /// она каждый раз malloc'ится), и освобождает нативный буфер. PtrToUtf8 — для const-строк.</summary>
    private static string? TakeUtf8String(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        string? s = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);
        try { bctbx_free(ptr); } catch { }
        return s;
    }

    public void Dispose()
    {
        if (_core != IntPtr.Zero)
        {
            // Финализируем запись ДО остановки core, пока медиа живо — иначе MKV останется
            // без индекса и не откроется. Все линии: в конференции их несколько.
            StopAllRecording();
            StopMicRecording();          // не бросаем recorder проверки микрофона
            ReleaseHeldPayloadTypes();   // обёртки кодеков, удерживаемые для UI
            linphone_core_stop(_core);
            _core = IntPtr.Zero;
        }
        if (_cbs != IntPtr.Zero)
        {
            try { linphone_core_cbs_unref(_cbs); } catch { }
            _cbs = IntPtr.Zero;
        }
    }
}
