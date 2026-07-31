using System;
using System.Runtime.InteropServices;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Тонкий P/Invoke-слой над C API liblinphone (SDK 5.4.x, liblinphone.dll): Core,
/// регистрация, звонки, чат, presence, устройства, кодеки, конфигурация.
/// Строки C API — UTF-8 (LPUTF8Str). bool_t передаём как int (0/1) — безопасно
/// независимо от фактической ширины bool_t в bctoolbox.
/// </summary>
internal static class NativeMethods
{
    private const string Lib = "liblinphone";
    private const CallingConvention Cdecl = CallingConvention.Cdecl;

    // ---------------------------------------------------------------------
    // kernel32 — настройка путей поиска нативных зависимостей liblinphone
    // ---------------------------------------------------------------------
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32")]
    public static extern uint GetACP();

    // Short-path (8.3): для кириллических путей отдаёт ASCII-имя (C:\Users\ROMAN~1\...),
    // которое mediastreamer/soci открывают, а UTF-8 путь с кириллицей — нет.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetShortPathNameW(string lpszLongPath, System.Text.StringBuilder lpszShortPath, uint cchBuffer);

    // user32 — вывод окна на передний план (для «окно поверх при входящем»)
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // Индикация входящего, когда окно неактивно/перекрыто: мигание в панели задач.
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    public const uint FLASHW_STOP = 0;
    public const uint FLASHW_ALL = 3;
    public const uint FLASHW_TIMERNOFG = 12;   // мигать, пока окно не станет активным

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    // --- user32 для оверлея видео (NativeVideoAdopter): окно, которое создаёт MSOGL,
    // приклеивается к области видео экрана звонка. ВАЖНО: передать MSOGL чужой HWND или
    // репарентить его окно нельзя — DXGI-рендер гибнет (проверено экспериментально:
    // child/top-level, свой класс с CS_OWNDC, SetParent со сменой стилей и без — всюду
    // чёрный кадр или крэш в EGL-инициализации). Живым остаётся только его собственное
    // top-level окно — поэтому owner + позиционирование в экранных координатах.
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    // Мгновенный перехват окна видео: хук на его создание/показ. Опрос таймером не успевал —
    // MSOGL показывает окно сразу после создания, и большое окно успевало мигнуть.
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const int OBJID_WINDOW = 0;

    public delegate void WinEventProc(IntPtr hook, uint winEvent, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc cb, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    // ---------------------------------------------------------------------
    // Логирование (для диагностики, в т.ч. нативных падений)
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_logging_service_get();

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_logging_service_set_log_level(IntPtr service, int level);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_logging_service_set_log_file(
        IntPtr service,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        UIntPtr maxSize);

    // ---------------------------------------------------------------------
    // Factory
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_factory_get();

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_factory_create_core_3(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? configPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? factoryConfigPath,
        IntPtr systemContext);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_factory_create_core_cbs(IntPtr factory);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_factory_create_auth_info(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string username,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? userid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? passwd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? ha1,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? realm,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? domain);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_factory_create_address(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string addr);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_factory_set_msplugins_dir(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_factory_set_top_resources_dir(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_factory_set_data_dir(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_factory_set_cache_dir(
        IntPtr factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    // ---------------------------------------------------------------------
    // Core
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_start(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_stop(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_iterate(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_add_callbacks(IntPtr core, IntPtr cbs);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_add_auth_info(IntPtr core, IntPtr authInfo);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_account_params(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_account(IntPtr core, IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_add_account(IntPtr core, IntPtr account);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_default_account(IntPtr core, IntPtr account);

    // Список всех аккаунтов (bctbx_list of LinphoneAccount, const — НЕ освобождать).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_account_list(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_remove_account(IntPtr core, IntPtr account);

    // Состояние регистрации конкретного аккаунта (LinphoneRegistrationState).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_account_get_state(IntPtr account);

    // --- Остановка регистрации при неверных учётных данных (колбэк отдаёт LinphoneProxyConfig) ---
    // Причина последней ошибки регистрации (LinphoneReason: Forbidden/Unauthorized при плохом пароле).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_proxy_config_get_error(IntPtr proxyConfig);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_proxy_config_edit(IntPtr proxyConfig);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_proxy_config_enable_register(IntPtr proxyConfig, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_proxy_config_done(IntPtr proxyConfig);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_proxy_config_get_identity_address(IntPtr proxyConfig);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_remove_auth_info(IntPtr core, IntPtr authInfo);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_invite(
        IntPtr core,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    // Исходящий звонок с параметрами (нужно, чтобы задать record_file ДО старта медиа).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_invite_with_params(
        IntPtr core,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        IntPtr callParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_current_call(IntPtr core);

    // Перевод звонка (blind transfer): нужен LinphoneAddress из interpret_url.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_interpret_url(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_transfer_to(IntPtr call, IntPtr referTo);

    // Сопровождаемый перевод (attended): REFER с Replaces — call переводится на собеседника dest.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_transfer_to_another(IntPtr call, IntPtr dest);

    // Причина завершения/ошибки звонка (LinphoneReason) — для человеческого сообщения в UI.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_get_reason(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_address_unref(IntPtr addr);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_terminate_all_calls(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_version();

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_network_reachable(IntPtr core, int reachable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_default_account(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_account_get_params(IntPtr account);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_account_params_get_identity_address(IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_address_get_username(IntPtr address);

    // --- Редактирование аккаунта (раздел «Аккаунт» в настройках) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_clear_accounts(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_clear_all_auth_info(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_address_set_display_name(IntPtr address, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_address_get_display_name(IntPtr address);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_address_get_domain(IntPtr address);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_address_get_port(IntPtr address);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_params_set_expires(IntPtr accountParams, int expires);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_account_params_get_expires(IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_account_params_get_transport(IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_account_params_get_server_address(IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_find_auth_info(
        IntPtr core,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? realm,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string username,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? sipDomain);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_auth_info_get_password(IntPtr authInfo);

    // Список учётных данных ядра: const bctbx_list_t* — НЕ освобождать (ни список, ни элементы).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_auth_info_list(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_auth_info_get_username(IntPtr authInfo);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_auth_info_get_domain(IntPtr authInfo);

    // ---------------------------------------------------------------------
    // Аудиоустройства (liblinphone) + обход bctbx_list (bctoolbox)
    // ---------------------------------------------------------------------
    private const string BcLib = "bctoolbox";

    [DllImport(BcLib, CallingConvention = Cdecl)]
    public static extern IntPtr bctbx_list_next(IntPtr elem);

    [DllImport(BcLib, CallingConvention = Cdecl)]
    public static extern IntPtr bctbx_list_get_data(IntPtr elem);

    [DllImport(BcLib, CallingConvention = Cdecl)]
    public static extern IntPtr bctbx_list_free(IntPtr list);

    // Освобождение строк/данных, которыми владеет вызывающий (например, из
    // linphone_address_as_string и элементов linphone_core_get_video_devices_list).
    [DllImport(BcLib, CallingConvention = Cdecl)]
    public static extern void bctbx_free(IntPtr ptr);

    // ---------------------------------------------------------------------
    // unref-функции: многие get-списки liblinphone возвращают СВЕЖИЕ объекты
    // с ref=1 — без unref каждый вызов утекает нативную память.
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_chat_message_unref(IntPtr msg);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_chat_room_unref(IntPtr room);

    // Удаляет комнату из core вместе с историей (для «Удалить переписку»).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_delete_chat_room(IntPtr core, IntPtr room);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_payload_type_unref(IntPtr payloadType);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_audio_device_unref(IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_friend_unref(IntPtr friend);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_presence_model_unref(IntPtr model);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_auth_info_unref(IntPtr authInfo);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_unref(IntPtr account);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_unref(IntPtr cbs);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_reload_sound_devices(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_enable_echo_cancellation(IntPtr core, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_echo_cancellation_enabled(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_use_rfc2833_for_dtmf(IntPtr core, int useRfc2833);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_get_use_rfc2833_for_dtmf(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_use_info_for_dtmf(IntPtr core, int useInfo);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_get_use_info_for_dtmf(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_audio_devices(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_extended_audio_devices(IntPtr core);

    // --- Камера (видеоустройства — список строк-имён) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_reload_video_devices(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_video_devices_list(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_set_video_device(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_video_device(IntPtr core);

    // --- Шифрование медиа ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_set_media_encryption(IntPtr core, int encryption);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_get_media_encryption(IntPtr core);

    // --- Кодеки (payload types) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_audio_payload_types(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_video_payload_types(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_payload_type_get_mime_type(IntPtr payloadType);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_payload_type_get_clock_rate(IntPtr payloadType);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_payload_type_enabled(IntPtr payloadType);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_payload_type_enable(IntPtr payloadType, int enabled);

    // Приоритизация кодеков = порядок в списке payload types.
    [DllImport(BcLib, CallingConvention = Cdecl)]
    public static extern IntPtr bctbx_list_append(IntPtr list, IntPtr data);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_audio_payload_types(IntPtr core, IntPtr payloadTypes);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_video_payload_types(IntPtr core, IntPtr payloadTypes);

    // Сохранение настроек на диск (rc-файл).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_config(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_config_sync(IntPtr config);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_config_get_string(IntPtr config,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string section,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? defaultString);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_config_set_string(IntPtr config,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string section,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    // --- Рингтон ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_ring(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string? path);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_ring(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_play_local(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string audiofile);

    // --- Запись с микрофона (LinphoneRecorder) — для проверки микрофона в настройках ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_recorder_params(IntPtr core);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_recorder_params_set_file_format(IntPtr prm, int format); // MediaFileFormat: Wav=1
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_recorder_params_unref(IntPtr prm);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_recorder(IntPtr core, IntPtr parameters);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_recorder_open(IntPtr rec, [MarshalAs(UnmanagedType.LPUTF8Str)] string file);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_recorder_start(IntPtr rec);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_recorder_pause(IntPtr rec);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_recorder_close(IntPtr rec);
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_recorder_unref(IntPtr rec);

    // --- Удалённое конфигурирование (provisioning) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_set_provisioning_uri(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string? uri);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_provisioning_uri(IntPtr core);

    // --- TLS: доверие сертификату сервера ---
    // Путь к файлу CA (PEM). Пусто/несуществующий — liblinphone останется с бандлом SDK.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_root_ca(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string? path);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_root_ca(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_verify_server_certificates(IntPtr core, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_verify_server_cn(IntPtr core, int enable);

    // --- NAT / STUN / ICE ---
    // --- Произвольная подписка SUBSCRIBE/NOTIFY (для «Event: dialog», BLF) ---
    // Возвращает LinphoneEvent с НАШЕЙ ссылкой — обязателен unref после terminate.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_subscribe(
        IntPtr core, IntPtr resource, [MarshalAs(UnmanagedType.LPUTF8Str)] string ev, int expires);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_event_send_subscribe(IntPtr ev, IntPtr body);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_event_terminate(IntPtr ev);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_event_unref(IntPtr ev);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_notify_received(IntPtr cbs, NotifyReceivedCb cb);

    /// <summary>NOTIFY по нашей подписке. body — LinphoneContent (const, НЕ освобождать).</summary>
    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void NotifyReceivedCb(
        IntPtr core, IntPtr ev, [MarshalAs(UnmanagedType.LPUTF8Str)] string notifiedEvent, IntPtr body);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_content_get_utf8_text(IntPtr content);

    // Session timers (RFC 4028): периодический re-INVITE/UPDATE подтверждает, что звонок жив.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_session_expires_value(IntPtr core, int seconds);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_get_session_expires_value(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_session_expires_min_value(IntPtr core, int seconds);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_user_agent(
        IntPtr core,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_nat_policy(IntPtr core);

    // Возвращает НОВЫЙ объект со счётчиком 1 — владеет вызывающий, обязан unref
    // (после set_nat_policy, который берёт собственную ссылку).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_nat_policy(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_nat_policy_unref(IntPtr policy);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_nat_policy(IntPtr core, IntPtr policy);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_nat_policy_set_stun_server(IntPtr policy, [MarshalAs(UnmanagedType.LPUTF8Str)] string? server);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_nat_policy_get_stun_server(IntPtr policy);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_nat_policy_enable_stun(IntPtr policy, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_nat_policy_enable_ice(IntPtr policy, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_nat_policy_ice_enabled(IntPtr policy);

    // --- TURN (релей медиа, когда прямой путь и STUN не работают) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_nat_policy_enable_turn(IntPtr policy, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_nat_policy_turn_enabled(IntPtr policy);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_nat_policy_set_stun_server_username(IntPtr policy, [MarshalAs(UnmanagedType.LPUTF8Str)] string? username);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_nat_policy_get_stun_server_username(IntPtr policy);

    // --- Устройство звонка (рингер) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_ringer_device(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_set_ringer_device(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string? devid);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_default_input_audio_device(IntPtr core, IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_default_output_audio_device(IntPtr core, IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_input_audio_device(IntPtr core, IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_output_audio_device(IntPtr core, IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_default_input_audio_device(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_default_output_audio_device(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_audio_device_get_id(IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_audio_device_get_device_name(IntPtr device);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_audio_device_get_capabilities(IntPtr device);

    // ---------------------------------------------------------------------
    // Журнал звонков (call logs)
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_call_logs(IntPtr core); // const bctbx_list*, НЕ освобождать

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_clear_call_logs(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_log_get_dir(IntPtr callLog);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_log_get_duration(IntPtr callLog);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_call_log_get_remote_address(IntPtr callLog);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern long linphone_call_log_get_start_date(IntPtr callLog); // time_t (64-bit)

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_log_get_status(IntPtr callLog);

    // ---------------------------------------------------------------------
    // Видео
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_native_video_window_id(IntPtr core, IntPtr windowId);

    // Политика видео: чтобы звонки по умолчанию были аудио (видео — только вручную).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_factory_create_video_activation_policy(IntPtr factory);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_video_activation_policy_set_automatically_initiate(IntPtr policy, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_video_activation_policy_set_automatically_accept(IntPtr policy, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_video_activation_policy(IntPtr core, IntPtr policy);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_native_preview_window_id(IntPtr core, IntPtr windowId);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_enable_video_capture(IntPtr core, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_enable_video_display(IntPtr core, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_enable_video_preview(IntPtr core, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_call_params(IntPtr core, IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_params_unref(IntPtr callParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_update(IntPtr call, IntPtr callParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_accept_with_params(IntPtr call, IntPtr callParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_call_get_current_params(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_params_enable_video(IntPtr callParams, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_params_video_enabled(IntPtr callParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_params_get_video_direction(IntPtr callParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_params_set_video_direction(IntPtr callParams, int direction);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_video_activation_policy_set_automatically_accept_direction(IntPtr policy, int direction);

    // ---------------------------------------------------------------------
    // Core callbacks (LinphoneCoreCbs)
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_registration_state_changed(
        IntPtr cbs, RegistrationStateChangedCb cb);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_call_state_changed(
        IntPtr cbs, CallStateChangedCb cb);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_message_received(IntPtr cbs, MessageReceivedCb cb);

    // Результат перевода звонка (REFER): состояние трансфер-звонка — Connected = успех, Error = провал.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_transfer_state_changed(IntPtr cbs, TransferStateChangedCb cb);

    // --- MWI (голосовая почта, NOTIFY message-summary) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_message_waiting_indication_changed(
        IntPtr cbs, MessageWaitingIndicationChangedCb cb);

    // bool_t: есть ли ожидающие сообщения.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_message_waiting_indication_has_message_waiting(IntPtr mwi);

    // Суммарное число новых/старых сообщений по всем summary.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern uint linphone_message_waiting_indication_get_nb_new(IntPtr mwi);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern uint linphone_message_waiting_indication_get_nb_old(IntPtr mwi);

    // --- Чат (SIP MESSAGE) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_chat_room(IntPtr core, IntPtr peerAddr);

    // BASIC chat room по URI: прямой SIP SIMPLE MESSAGE (для Asterisk без conference-сервера).
    // get_chat_room в 5.x по умолчанию создаёт ГРУППОВОЙ (conference) чат → INVITE на conf-factory,
    // из-за чего у абонента появлялся «звонок от anonymous», а обычные MESSAGE не ходили.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_chat_room_from_uri(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);

    // Явное создание BASIC chat room через params — гарантирует прямой SIP MESSAGE
    // НЕЗАВИСИМО от conference-factory аккаунта (иначе get_chat_room* делают group chat).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_default_chat_room_params(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_chat_room_params_set_backend(IntPtr prm, int backend); // Basic=1, FlexisipChat=2

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_chat_room_params_unref(IntPtr prm);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_search_chat_room(IntPtr core, IntPtr prm, IntPtr localAddr, IntPtr remoteAddr, IntPtr participants);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_chat_room_6(IntPtr core, IntPtr prm, IntPtr localAddr, IntPtr participants);

    // Отключение conference-factory: без него все chat rooms создаются basic (страховка).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_conference_factory_uri(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string? uri);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_chat_room_create_message_from_utf8(IntPtr room, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_chat_message_send(IntPtr msg);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_chat_message_get_utf8_text(IntPtr msg);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_chat_message_get_from_address(IntPtr msg);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_chat_message_is_outgoing(IntPtr msg);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_chat_room_get_history(IntPtr room, int nbMessage);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_get_unread_chat_message_count(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_chat_room_mark_as_read(IntPtr room);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_chat_rooms(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_chat_room_get_peer_address(IntPtr room);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_chat_room_get_unread_messages_count(IntPtr room);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_chat_room_get_last_message_in_history(IntPtr room);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern long linphone_chat_room_get_last_update_time(IntPtr room);

    // --- Presence (статус коллег через friend list + SUBSCRIBE/NOTIFY) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_friend_with_address(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string address);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_friend_enable_subscribes(IntPtr friend, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_default_friend_list(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_friend_list_add_friend(IntPtr list, IntPtr friend);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_friend_list_enable_subscriptions(IntPtr list, int enabled);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_friend_list_find_friend_by_address(IntPtr list, IntPtr addr);

    // Список друзей (bctbx_list of LinphoneFriend, const — НЕ освобождать).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_friend_list_get_friends(IntPtr list);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_friend_list_remove_friend(IntPtr list, IntPtr friend);

    // Адрес друга (const — НЕ освобождать).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_friend_get_address(IntPtr friend);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_friend_get_consolidated_presence(IntPtr friend);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_cbs_set_notify_presence_received(IntPtr cbs, NotifyPresenceReceivedCb cb);

    // --- Расширенные статусы (presence model + activity) ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_presence_model_new_with_activity(int activity, [MarshalAs(UnmanagedType.LPUTF8Str)] string? description);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_presence_model(IntPtr core, IntPtr model);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_friend_get_presence_model(IntPtr friend);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_presence_model_get_activity(IntPtr model);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_presence_activity_get_type(IntPtr activity);

    // ---------------------------------------------------------------------
    // AccountParams
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_account_params_set_identity_address(IntPtr accountParams, IntPtr identity);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_account_params_set_server_address(IntPtr accountParams, IntPtr serverAddress);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_params_enable_register(IntPtr accountParams, int enable);

    // Публикация своего присутствия (PUBLISH) — чтобы коллеги видели наш статус.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_account_params_clone(IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_set_params(IntPtr account, IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_params_unref(IntPtr accountParams);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_params_enable_publish(IntPtr accountParams, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_account_params_set_publish_expires(IntPtr accountParams, int expires);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_set_consolidated_presence(IntPtr core, int presence);

    // ---------------------------------------------------------------------
    // Call
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_terminate(IntPtr call);

    // Отклонить звонок с указанной причиной (LinphoneReason: Busy=6).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_decline(IntPtr call, int reason);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_accept(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_get_state(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_call_get_remote_address(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_get_dir(IntPtr call); // LinphoneCallDir

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_get_duration(IntPtr call); // секунды

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_send_dtmf(IntPtr call, byte dtmf);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_play_dtmf(IntPtr core, byte dtmf, int durationMs);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_pause(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_resume(IntPtr call);

    // --- Несколько звонков одновременно ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_get_calls_nb(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_get_calls(IntPtr core); // const bctbx_list, НЕ освобождать

    // --- Локальная конференция (все звонки в одну беседу) ---
    // Устарело с 5.x, оставлено как запасной путь для сборок SDK без conference-API ниже.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_add_all_to_conference(IntPtr core);

    // Современный путь: параметры → конференция → добавление каждого звонка участником.
    // create_* возвращают объекты с НАШЕЙ ссылкой — обязателен unref.
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_conference_params_2(IntPtr core, IntPtr conference);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_core_create_conference_with_params(IntPtr core, IntPtr parms);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_conference_add_participant(IntPtr conference, IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_conference_unref(IntPtr conference);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_conference_params_unref(IntPtr parms);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_is_in_conference(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_terminate_conference(IntPtr core);

    // --- Запись разговора ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_call_get_params(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_params_set_record_file(IntPtr callParams, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_start_recording(IntPtr call);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_stop_recording(IntPtr call);

    // --- Авто-переподключение ---
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_refresh_registers(IntPtr core);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_call_set_microphone_muted(IntPtr call, int muted);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_call_get_microphone_muted(IntPtr call);

    // Микрофон на уровне ядра — для конференции (у неё нет «текущего» звонка).
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern void linphone_core_enable_mic(IntPtr core, int enable);

    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern int linphone_core_mic_enabled(IntPtr core);

    // ---------------------------------------------------------------------
    // Address
    // ---------------------------------------------------------------------
    [DllImport(Lib, CallingConvention = Cdecl)]
    public static extern IntPtr linphone_address_as_string(IntPtr address);

    // ---------------------------------------------------------------------
    // Делегаты колбэков (native -> managed). CallingConvention.Cdecl.
    // Экземпляры делегатов ОБЯЗАТЕЛЬНО держать в полях, чтобы GC их не собрал.
    // ---------------------------------------------------------------------
    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void RegistrationStateChangedCb(
        IntPtr core, IntPtr proxyConfig, int state,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? message);

    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void CallStateChangedCb(
        IntPtr core, IntPtr call, int state,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? message);

    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void MessageReceivedCb(IntPtr core, IntPtr room, IntPtr message);

    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void NotifyPresenceReceivedCb(IntPtr core, IntPtr friend);

    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void TransferStateChangedCb(IntPtr core, IntPtr transfered, int newCallState);

    // MWI: linphoneEvent может быть Zero (unsolicited NOTIFY без подписки — так шлёт Asterisk).
    [UnmanagedFunctionPointer(Cdecl)]
    public delegate void MessageWaitingIndicationChangedCb(IntPtr core, IntPtr linphoneEvent, IntPtr mwi);
}

// ============================= Enum'ы C API =============================
// СВЕРЕНО с документацией liblinphone SDK 5.4.122 (doxygen C API из пакета
// LinphoneSDK.Windows, content/doc/liblinphone-5.4.0/c) — все значения ниже
// совпадают с объявлениями заголовков. При апгрейде SDK сверку повторить.

/// <summary>LinphoneConsolidatedPresence (types.h) — сводный статус присутствия абонента.</summary>
public enum LinphoneConsolidatedPresence
{
    Online = 0,
    Busy = 1,
    DoNotDisturb = 2,
    Offline = 3,
}

/// <summary>LinphonePresenceActivityType (types.h) — нужные нам активности.</summary>
public enum LinphonePresenceActivity
{
    Away = 1,
    Lunch = 8,
    Meeting = 10,
    OnThePhone = 11,
    Vacation = 24,
}

/// <summary>Статус присутствия уровня приложения (для выбора своего и показа чужого).
/// Набор согласован с CustomPresence на Asterisk — АТС принимает только эти четыре;
/// Offline — служебный, для показа отключившихся коллег.</summary>
public enum PresenceStatus
{
    Available,    // 🟢 Доступен (device idle — авто)
    OnThePhone,   // 📞 Разговаривает (device INUSE — авто, во время звонка)
    DoNotDisturb, // 🚫 Не беспокоить (вручную; заворот звонка в голосовую почту делает АТС)
    Away,         // ☕ Отошёл (вручную; только показ — звонки проходят)
    Offline,      // ⚪ Не в сети (только для показа)
}

/// <summary>LinphoneLogLevel (logging.h) — битовые уровни; set_log_level задаёт порог.</summary>
public enum LinphoneLogLevel
{
    Debug = 1 << 0,
    Trace = 1 << 1,
    Message = 1 << 2,
    Warning = 1 << 3,
    Error = 1 << 4,
    Fatal = 1 << 5,
}

/// <summary>LinphoneRegistrationState (types.h).</summary>
public enum LinphoneRegistrationState
{
    None = 0,
    Progress = 1,
    Ok = 2,
    Cleared = 3,
    Failed = 4,
    Refreshing = 5,
}

/// <summary>LinphoneCallState (enums/call-enums.h).</summary>
public enum LinphoneCallState
{
    Idle = 0,
    IncomingReceived = 1,
    PushIncomingReceived = 2,
    OutgoingInit = 3,
    OutgoingProgress = 4,
    OutgoingRinging = 5,
    OutgoingEarlyMedia = 6,
    Connected = 7,
    StreamsRunning = 8,
    Pausing = 9,
    Paused = 10,
    Resuming = 11,
    Referred = 12,
    Error = 13,
    End = 14,
    PausedByRemote = 15,
    UpdatedByRemote = 16,
    IncomingEarlyMedia = 17,
    Updating = 18,
    Released = 19,
    EarlyUpdatedByRemote = 20,
    EarlyUpdating = 21,
}

/// <summary>LinphoneCallDir (types.h).</summary>
public enum LinphoneCallDir
{
    Outgoing = 0,
    Incoming = 1,
}

/// <summary>LinphoneMediaEncryption (types.h).</summary>
public enum LinphoneMediaEncryption
{
    None = 0,
    SRTP = 1,
    ZRTP = 2,
    DTLS = 3,
}

/// <summary>LinphoneTransportType (types.h).</summary>
public enum LinphoneTransportType
{
    Udp = 0,
    Tcp = 1,
    Tls = 2,
    Dtls = 3,
}

/// <summary>LinphoneMediaDirection (types.h).</summary>
public enum LinphoneMediaDirection
{
    Invalid = -1,
    Inactive = 0,
    SendOnly = 1,
    RecvOnly = 2,
    SendRecv = 3,
}

/// <summary>LinphoneReason (types.h) — причина завершения/ошибки звонка.
/// Значения соответствуют официальной C#-обёртке (LinphoneWrapper.cs).</summary>
public enum LinphoneReason
{
    None = 0,
    NoResponse = 1,
    Forbidden = 2,
    Declined = 3,
    NotFound = 4,
    NotAnswered = 5,
    Busy = 6,
    UnsupportedContent = 7,
    BadEvent = 8,
    IOError = 9,
    DoNotDisturb = 10,
    Unauthorized = 11,
    NotAcceptable = 12,
    NoMatch = 13,
    MovedPermanently = 14,
    Gone = 15,
    TemporarilyUnavailable = 16,
    AddressIncomplete = 17,
    NotImplemented = 18,
    BadGateway = 19,
    SessionIntervalTooSmall = 20,
    ServerTimeout = 21,
    Unknown = 22,
    Transferred = 23,
}

/// <summary>LinphoneCallStatus (types.h).</summary>
public enum LinphoneCallStatus
{
    Success = 0,
    Aborted = 1,
    Missed = 2,
    Declined = 3,
    EarlyAborted = 4,
    AcceptedElsewhere = 5,
    DeclinedElsewhere = 6,
}
