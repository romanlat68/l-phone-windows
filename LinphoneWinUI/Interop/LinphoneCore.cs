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
    public string Title { get; init; } = "";     // стрелка + номер/имя
    public string Subtitle { get; init; } = "";  // дата/время + длительность либо «Пропущенный»
    public bool Missed { get; init; }
    public bool Incoming { get; init; } // направление: true — входящий, false — исходящий
    public long StartUnix { get; init; } // начало звонка, Unix-секунды (0 — неизвестно)
}

/// <summary>Снимок состояния звонка, передаётся в событии CallStateChanged.
/// Reason заполняется в терминальных состояниях (End/Error) — для сообщения пользователю.</summary>
public readonly record struct CallEvent(
    LinphoneCallState State,
    string? Message,
    string? Remote,
    bool IsIncoming,
    int DurationSeconds,
    LinphoneReason Reason = LinphoneReason.None);

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

    public LinphoneCore()
    {
        // Мост для логов из статических методов (SaveCreds, HardenDirectoryAcl): раньше их
        // ошибки уходили в Debug.WriteLine, которого в проде нет — саппорт не видел, что
        // пароль не сохранился или каталог профиля остался открытым.
        _staticLog = s => Log?.Invoke(s);
        _onRegistration = OnRegistrationStateChanged;
        _onCall = OnCallStateChanged;
        _onMessage = OnMessageReceived;
        _onPresence = OnNotifyPresenceReceived;
        _onTransfer = OnTransferStateChanged;
        _onMwi = OnMwiChanged;
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
        string logDir = Path.Combine(sharedBase, CurrentUserSid());
        EnsureOwnProfileDir(logDir);
        ProfileDir = logDir;
        MigrateSharedProfile(sharedBase, logDir);
        // ВАЖНО: hardening строго ПОСЛЕ миграции. File.Move в пределах тома сохраняет файлу
        // старый DACL (унаследованный от общего каталога — «читаем всеми»); SetAccessControl
        // на каталоге перераспространяет наследование на уже лежащие внутри файлы и вычищает
        // эти ACE (поведение SetSecurityInfo, проверено). Поменять порядок = снова открыть
        // linphonerc всем пользователям машины.
        HardenDirectoryAcl(logDir);
        PruneCoreLogs(logDir);
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

        // 6. Колбэки до старта.
        _cbs = linphone_factory_create_core_cbs(_factory);
        linphone_core_cbs_set_registration_state_changed(_cbs, _onRegistration);
        linphone_core_cbs_set_call_state_changed(_cbs, _onCall);
        linphone_core_cbs_set_message_received(_cbs, _onMessage);
        linphone_core_cbs_set_notify_presence_received(_cbs, _onPresence);
        try { linphone_core_cbs_set_transfer_state_changed(_cbs, _onTransfer); } catch { /* нет в этой сборке SDK */ }
        try { linphone_core_cbs_set_message_waiting_indication_changed(_cbs, _onMwi); } catch { /* нет в этой сборке SDK */ }
        linphone_core_add_callbacks(_core, _cbs);

        // 7. Старт.
        int rc = linphone_core_start(_core);
        if (rc != 0)
            throw new InvalidOperationException($"linphone_core_start() вернул {rc}.");

        linphone_core_set_network_reachable(_core, 1);

        // Отключаем conference-factory: с ним liblinphone делает chat групповым (INVITE на
        // conf-сервер, «звонок от anonymous»); без него — прямой SIP MESSAGE, как надо Asterisk.
        try { linphone_core_set_conference_factory_uri(_core, null); } catch { }

        CoreVersion = PtrToUtf8(linphone_core_get_version()) ?? "?";
        Log?.Invoke($"Core запущен. liblinphone {CoreVersion}");
        WarnIfNoSoundDevices(msPlugins);
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

    /// <summary>Должно вызываться регулярно (~20 мс) на UI-потоке.</summary>
    public void Iterate()
    {
        if (_core != IntPtr.Zero)
            linphone_core_iterate(_core);
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
            if (_recording) { try { linphone_call_stop_recording(call); } catch { } _recording = false; }
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

    /// <summary>Объединяет все текущие звонки (активный + удержанные) в локальную конференцию.</summary>
    public bool MergeCallsIntoConference()
    {
        if (_core == IntPtr.Zero || CallCount < 2) return false;
        try
        {
            _conferenceActive = linphone_core_add_all_to_conference(_core) == 0;
            return _conferenceActive;
        }
        catch (EntryPointNotFoundException) { Log?.Invoke("Конференции нет в этой сборке SDK"); return false; }
        catch { return false; }
    }

    /// <summary>Завершает конференцию вместе со всеми её звонками.</summary>
    public void TerminateConference()
    {
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

    /// <summary>Отправить DTMF-тон в активный звонок (RFC 2833 / SIP INFO по настройкам).</summary>
    public void SendDtmf(char digit)
    {
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero)
            linphone_call_send_dtmf(call, (byte)digit);
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
        bool ok = linphone_call_transfer_to(call, addr) == 0;
        linphone_address_unref(addr);
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
                return linphone_call_transfer_to_another(c, current) == 0;
        }
        return false;
    }

    // Чат (SIP MESSAGE) вынесен в LinphoneCore.Chat.cs

    // ------------------------------------------------------------------
    // Presence (статус коллег) — для избранного
    // ------------------------------------------------------------------
    private void OnNotifyPresenceReceived(IntPtr core, IntPtr friend) => PresenceChanged?.Invoke();

    /// <summary>MWI (message-summary) от АТС — новые сообщения голосовой почты. Asterisk шлёт
    /// unsolicited NOTIFY (mailbox на endpoint'е) — подписка не нужна, linphoneEvent = Zero.</summary>
    private void OnMwiChanged(IntPtr core, IntPtr linphoneEvent, IntPtr mwi)
    {
        if (mwi == IntPtr.Zero) return;
        try
        {
            bool waiting = linphone_message_waiting_indication_has_message_waiting(mwi) != 0;
            int nbNew = (int)linphone_message_waiting_indication_get_nb_new(mwi);
            VoicemailChanged?.Invoke(waiting, nbNew);
        }
        catch (EntryPointNotFoundException) { /* нет функций в этой сборке SDK */ }
    }

    /// <summary>Состояние трансфер-звонка (результат REFER). Connected — собеседник дозвонился
    /// до цели; Error/End — перевод не удался. Прочие промежуточные состояния игнорируем.</summary>
    private void OnTransferStateChanged(IntPtr core, IntPtr transfered, int newCallState)
    {
        var st = (LinphoneCallState)newCallState;
        if (st is LinphoneCallState.Connected or LinphoneCallState.StreamsRunning)
            TransferResult?.Invoke(true);
        else if (st is LinphoneCallState.Error or LinphoneCallState.End)
            TransferResult?.Invoke(false);
    }

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

    /// <summary>Переключить микрофон. Возвращает новое состояние (true = заглушён).</summary>
    public bool ToggleMute()
    {
        if (_core == IntPtr.Zero) return false;
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero)
        {
            bool muted = linphone_call_get_microphone_muted(call) != 0;
            linphone_call_set_microphone_muted(call, muted ? 0 : 1);
            return !muted;
        }
        // Конференция: «текущего» звонка нет (get_current_call = NULL) — глушим микрофон
        // на уровне ядра. Обратно включается там же или автоматически при конце звонков.
        try
        {
            bool enabled = linphone_core_mic_enabled(_core) != 0;
            linphone_core_enable_mic(_core, enabled ? 0 : 1);
            return enabled;   // был включён → теперь заглушен
        }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>Поставить звонок на удержание / снять. Возвращает true, если поставлен на удержание.</summary>
    public bool ToggleHold()
    {
        if (_core == IntPtr.Zero) return false;
        // Активный разговор есть — ставим на удержание.
        IntPtr call = CurrentCall;
        if (call != IntPtr.Zero)
        {
            linphone_call_pause(call);
            return true;
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
                return false;
            }
        }
        return false;
    }

    public int CurrentCallDuration
    {
        get
        {
            IntPtr call = CurrentCall;
            return call == IntPtr.Zero ? 0 : linphone_call_get_duration(call);
        }
    }

    private bool _recording;
    private string? _recordPath;   // файл записи для текущего звонка (задаётся при установке вызова)
    public bool IsRecording => _recording;
    public string? RecordPath => _recordPath;

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
        return AsciiSafePath(Path.Combine(dir, $"call-{DateTime.Now:yyyyMMdd-HHmmss}.mkv")).Replace('\\', '/');
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
        _recordPath = NextRecordFile();
        linphone_call_params_set_record_file(p, _recordPath);
        return p;
    }

    /// <summary>Начать/остановить запись текущего разговора. Путь уже задан при установке вызова
    /// (см. <see cref="BuildCallParamsWithRecording"/>). Возвращает новое состояние записи.</summary>
    public bool ToggleRecording()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return false;
        if (_recording)
        {
            linphone_call_stop_recording(call);
            _recording = false;
        }
        else
        {
            linphone_call_start_recording(call);
            _recording = true;
        }
        return _recording;
    }

    /// <summary>Передёрнуть сеть и перерегистрироваться (для авто-переподключения при смене сети).
    /// Во время разговора сеть НЕ передёргиваем: set_network_reachable(0) рвёт медиапотоки
    /// активного звонка (например, при подключении VPN, не меняющем основной маршрут) —
    /// ограничиваемся обновлением регистраций.</summary>
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

            string arrow = incoming ? "↙" : "↗"; // вниз-влево / вверх-вправо
            string when = start > 0 ? FormatCallDate(DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime) : "";
            string sub = missed ? $"{Loc.S("St_Missed")} · {when}"
                       : dur > 0 ? $"{when} · {dur / 60:00}:{dur % 60:00}"
                       : when;

            result.Add(new CallLogEntry
            {
                Remote = remote, Title = $"{arrow}  {name}", Subtitle = sub,
                Missed = missed, Incoming = incoming, StartUnix = start,
            });
        }
        return result;

        // Сегодня — только время; иначе короткая дата + время. Порядок/разделители — по локали ОС
        // (раньше был жёсткий «dd.MM HH:mm»).
        static string FormatCallDate(DateTime dt)
        {
            var c = System.Globalization.CultureInfo.CurrentCulture;
            string time = dt.ToString("t", c);
            if (dt.Date == DateTime.Today) return time;
            string date = dt.ToString("d", c);
            return $"{date} {time}";
        }
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

    /// <summary>
    /// Кнопка «Видео». Видео в звонке ещё нет — добавляем поток (re-INVITE, SendRecv).
    /// Видео уже идёт — камеру НЕ гасим через RecvOnly-re-INVITE: телефоны (проверено на
    /// Fanvil 378) отвечают на такой re-INVITE 200 OK и тут же шлют BYE — звонок рвался.
    /// Вместо этого подменяем источник на статичную картинку: SDP не меняется, звонок цел,
    /// собеседник перестаёт видеть нас. Возвращает новое состояние «моя камера включена».
    /// </summary>
    public bool ToggleVideo()
    {
        IntPtr call = CurrentCall;
        if (call == IntPtr.Zero) return false;

        if (IsVideoActive())
        {
            SetCameraMuted(!_cameraMuted);
            _userWantsCamera = !_cameraMuted;
            return !_cameraMuted;
        }

        // Видео в звонке нет — поднимаем захват/отображение и добавляем видеопоток.
        _cameraMuted = false;
        _userWantsCamera = true;
        linphone_core_enable_video_capture(_core, 1);
        linphone_core_enable_video_display(_core, 1);
        IntPtr p = linphone_core_create_call_params(_core, call);
        if (p == IntPtr.Zero) return false;
        linphone_call_params_enable_video(p, 1);
        linphone_call_params_set_video_direction(p, (int)LinphoneMediaDirection.SendRecv);
        linphone_call_update(call, p);
        _videoActiveSnapshot = true;   // не ждём StreamsRunning: UI покажет область сразу
        Log?.Invoke("Видео включено");
        return true;
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

    private void OnRegistrationStateChanged(IntPtr core, IntPtr proxyConfig, int state, string? message)
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

    private void OnCallStateChanged(IntPtr core, IntPtr call, int state, string? message)
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
        if (_recording && call != IntPtr.Zero && IsTerminal(st))
        {
            try { linphone_call_stop_recording(call); } catch { }
            _recording = false;   // запись остановлена сразу: не оставляем флаг «горящим» на оставшемся звонке
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
            _recording = false; _recordPath = null;   // подстраховка: звонков не осталось
            // Мьют уровня ядра (конференция) переживает звонки — включаем микрофон обратно,
            // иначе следующий разговор молча начнётся с выключенным микрофоном.
            try { if (linphone_core_mic_enabled(_core) == 0) linphone_core_enable_mic(_core, 1); } catch { }
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
        // Снимок ДО события: подписчик (UI) прочитает уже согласованные значения без RunOnCore.
        _callCountSnapshot = CallCount;
        _videoActiveSnapshot = IsVideoActive();

        CallStateChanged?.Invoke(new CallEvent(st, message, remote, incoming, duration, reason));
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
    /// вычищает HardenDirectoryAcl, который вызывается сразу после (см. Start).</summary>
    private static void MigrateSharedProfile(string oldDir, string newDir)
    {
        try
        {
            foreach (string f in Directory.EnumerateFiles(oldDir))
            {
                string dest = Path.Combine(newDir, Path.GetFileName(f));
                try { if (!File.Exists(dest)) File.Move(f, dest); } catch { /* файл занят — пропустим */ }
            }
        }
        catch { }
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
    private static void EnsureOwnProfileDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            var owner = ProfileDirOwner(dir);
            if (owner != null && !IsSelfOrSystem(owner))
                throw new InvalidOperationException(
                    $"Каталог профиля {dir} создан другим пользователем ({owner.Value}). " +
                    "Это может быть подменённая конфигурация — запуск остановлен. " +
                    "Удалите каталог силами администратора и запустите приложение снова.");
            return;
        }
        try { new DirectoryInfo(dir).Create(ProfileDirSecurity()); }   // атомарно: без окна с чужим DACL
        catch (Exception ex)
        {
            _staticLog?.Invoke($"Каталог профиля создан без явного DACL: {ex.Message}");
            Directory.CreateDirectory(dir);
        }
    }

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
    /// предыдущий: диагностика «что было в прошлый запуск» сохраняется, архив за месяц — нет.</summary>
    private static void PruneCoreLogs(string dir)
    {
        try
        {
            foreach (var f in new DirectoryInfo(dir).GetFiles("liblinphone.log_*"))
                if (f.Name != "liblinphone.log_1") { try { f.Delete(); } catch { } }
        }
        catch { }
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
            // Финализируем запись разговора ДО остановки core, пока медиа живо —
            // иначе MKV останется без индекса и не откроется.
            if (_recording)
            {
                IntPtr call = CurrentCall;
                if (call != IntPtr.Zero) { try { linphone_call_stop_recording(call); } catch { } }
                _recording = false;
            }
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
