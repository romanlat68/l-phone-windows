using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LinphoneWinUI.Interop;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CommunityToolkit.WinUI.Notifications;
using Windows.Graphics;
using Windows.UI.Notifications;

namespace LinphoneWinUI;

public sealed partial class MainWindow : Window
{
    private enum Mode { Dialer, Incoming, InCall }
    private enum Tab { Dialer, Contacts, Favorites, History, Chat }

    // === Размеры окна (логические пиксели) ===
    // Минимум и стартовый размер РАЗДЕЛЕНЫ. Раньше это была одна константа 740, и она же была
    // ограничением ОС: на 1920×1080 при масштабе 150 % рабочая область — 720 логических пикселей,
    // окно не помещалось по высоте и уменьшить его было нельзя. Теперь минимум — та высота, при
    // которой окном ещё можно пользоваться (дайлер доезжает прокруткой), а 740 — просто приятный
    // стартовый размер, который дополнительно обрезается рабочей областью монитора.
    private const int MinWidth = 400;
    private const int MinHeight = 560;
    private const int DefaultWidth = 400;
    private const int DefaultHeight = 740;

    // Ядро приходит извне (из AppHost) и по ИНТЕРФЕЙСУ: раньше окно создавало его само,
    // из-за чего подменить SIP-стек было нечем и окно не поддавалось проверке.
    private readonly ISipService _linphone;

    /// <summary>Сообщает АТС о смене ручного статуса (Asterisk не принимает SIP PUBLISH).</summary>
    private readonly PresenceHookClient _presenceHook;
    private LocalApi.LocalApiServer? _api;   // локальный HTTP-API для CRM (по умолчанию выключен)
    private CallHookClient? _callHook;       // уведомления о звонках в CRM (карточка клиента)

    /// <summary>Есть непринятый входящий. Отдельное поле, а не чтение Visibility у элементов:
    /// локальный API работает на потоке пула, а обращение к XAML вне потока интерфейса
    /// бросает COM-исключение — /status падал именно на этом.</summary>
    private volatile bool _incomingActive;

    // MVVM: секции переезжают на ViewModels по одной.
    public ViewModels.ContactsViewModel ContactsVm { get; }
    public ViewModels.HistoryViewModel HistoryVm { get; }
    public ViewModels.FavoritesViewModel FavoritesVm { get; }
    public ViewModels.ParkingViewModel ParkingVm { get; }
    public ViewModels.TransferTargetsViewModel TransferVm { get; } = new();
    public ViewModels.ChatViewModel ChatVm { get; }
    public ViewModels.DialerViewModel DialerVm { get; }
    public ViewModels.HeaderViewModel HeaderVm { get; }
    public ViewModels.CallViewModel CallVm { get; }

    private DispatcherQueueTimer? _durationTimer;
    private DispatcherQueueTimer? _autoAnswerTimer;
    private DispatcherQueueTimer? _netDebounce;   // дебаунс смены сети для авто-переподключения
    private bool _inCall;
    private bool _videoOn;
    private SettingsWindow? _settingsWindow;
    private List<ContactEntry> _allContacts = new();
    private bool _exiting;
    private long _missedSeen;   // Unix-секунды последнего просмотра журнала (для бейджа пропущенных)

    // Из палитры, а не числами: «свой» синий и серый жили здесь копиями, и подобрать
    // их к остальному интерфейсу можно было только правкой кода (см. App.xaml).
    private Brush ActiveTab => Palette("LpTabActiveBrush");
    private Brush InactiveTab => Palette("LpNeutralBrush");

    /// <summary>Функция-биндинг для {x:Bind}: bool → Visibility (Converter={StaticResource}
    /// не работает в x:Bind на Window — нет FrameworkElement для converter-lookup).</summary>
    public Visibility VisIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>Инверсия для {x:Bind} (Converter на Window не резолвится — см. VisIf).</summary>
    public bool NotVal(bool value) => !value;

    /// <summary>Цвет палочки индикатора качества: горит она или нет, и тревожный ли уровень.
    /// Погашенная палочка не исчезает, а тускнеет — иначе индикатор менял бы ширину и
    /// дёргал соседний текст при каждом обновлении.</summary>
    /// <summary>Кисть из палитры приложения (App.xaml). Держать оттенки числами в коде —
    /// верный способ развести их с разметкой: так в приложении уже жили два разных красных.</summary>
    public static Brush Palette(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Цвет полоски уровня микрофона.</summary>
    public Brush MicBarBrush(bool muted) => Palette(muted ? "LpDangerBrush" : "LpGreenBrush");

    /// <summary>Цвет замка: обычный — когда защите можно верить, предупреждающий — когда
    /// ключ SRTP ехал по незащищённой сигнализации. Замок при этом остаётся на месте:
    /// шифрование действительно согласовано, вопрос в том, сколько оно стоит.</summary>
    public Brush LockBrush(bool weak) =>
        weak ? Palette("LpDangerBrush") : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    /// <summary>Цвет полоски с учётом «микрофона нет»: и выключенный, и отсутствующий
    /// микрофон одинаково значат «вас не слышат», и оба должны быть красными.</summary>
    public Brush MicBarBrush2(bool muted, bool missing) => MicBarBrush(muted || missing);

    public Brush QualityBrush(bool lit, bool poor)
    {
        // Погашенная палочка не исчезает, а тускнеет — иначе индикатор менял бы ширину
        // и дёргал соседний текст при каждом обновлении.
        if (!lit) return Palette("LpQualityDimBrush");
        return Palette(poor ? "LpDangerBrush" : "LpGreenBrush");
    }

    public MainWindow(ISipService sip, ViewModels.HeaderViewModel headerVm, ViewModels.CallViewModel callVm)
    {
        _linphone = sip;
        HeaderVm = headerVm;
        CallVm = callVm;
        _presenceHook = new PresenceHookClient(sip, EnqueueLog);
        _audio = new AudioRoutingController(sip, EnqueueLog);
        // Сообщаем о смене устройств только во время разговора: вне его переключение никого
        // не беспокоит, а подсказка на каждое подключение веб-камеры раздражала бы.
        _audio.DevicesSwitched += () => { if (_inCall) ShowDialerHint(Loc.S("St_AudioDeviceSwitched")); };
        _toasts = new ToastNotifier(EnqueueLog);
        _mini = new MiniWindowController(sip, () => { ShowFromTray(); BringToFront(); }, Initials);
        _redial = new RedialController(sip, new DispatcherCountdownTimer(DispatcherQueue),
            ShowRedialCountdown, () => RedialPanel.Visibility = Visibility.Collapsed, EnqueueLog);
        // Попытки кончились — говорим об этом там же, где показывали отсчёт. Молча прекратить
        // нельзя: человек видел «повтор через 30 с» и должен узнать, что телефон сдался.
        _redial.GaveUp += () => ShowDialerHint(Loc.S("St_RedialGaveUp"));

        // ViewModel до InitializeComponent — {x:Bind} к ContactsVm резолвится при загрузке XAML.
        _callHook = new CallHookClient(
            template: () => _linphone.GetAppString("call_hook_url", ""),
            openInBrowser: () => _linphone.GetAppFlag("call_hook_browser", true),
            incomingOnly: () => _linphone.GetAppFlag("call_hook_incoming_only", true),
            log: EnqueueLog);

        ContactsVm = new ViewModels.ContactsViewModel(_linphone, Loc.S, (k, a) => Loc.F(k, a));
        ContactsVm.CallPlaced += () => ShowSection(Tab.Dialer);
        HistoryVm = new ViewModels.HistoryViewModel(_linphone);
        FavoritesVm = new ViewModels.FavoritesViewModel(_linphone);
        ParkingVm = new ViewModels.ParkingViewModel(_linphone);
        ChatVm = new ViewModels.ChatViewModel(_linphone, DisplayName);
        ChatVm.ScrollToEnd += ScrollChatToEnd;
        ChatVm.BadgeChanged += UpdateChatBadge;
        DialerVm = new ViewModels.DialerViewModel(_linphone, q => FindContact(q)?.Name);
        // Строку над кнопкой вызова рисуем в code-behind: она показывает то последний номер,
        // то временное сообщение, и {x:Bind} на Content тут только мешал бы.
        DialerVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(ViewModels.DialerViewModel.LastDialedDisplay)) return;
            _dialerPlaceholderDismissed = false;   // новый звонок — новый номер, подсказку возвращаем
            UpdateDialerLine();
        };

        InitializeComponent();
        _msToXaml = MsSinceProcessStart();
        // ПОСЛЕ InitializeComponent: контроллеру нужны плейсхолдеры видео из разметки.
        _video = new VideoOverlayController(this, VideoSurface, PreviewSurface, () => _inCall, EnqueueLog);
        _video.VisibilityChanged += UpdateVideoOverlayVisibility;
        SystemBackdrop = new MicaBackdrop(); // тематический фон (иначе окно прозрачное)
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "lphone.ico"));

        // Минимальный размер на уровне ОС (WM_GETMINMAXINFO) + восстановление прошлой геометрии.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        MinSizeHelper.SetMinSize(hwnd, MinWidth, MinHeight);
        RestoreWindowGeometry(hwnd);

        AppLog.WriteHeader(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?");
        _linphone.Log += EnqueueLog;
        _linphone.RegistrationStateChanged += OnRegistrationStateChanged;
        _linphone.CallStateChanged += OnCallStateChanged;
        _linphone.ChatMessageReceived += OnChatMessageReceived;
        _linphone.PresenceChanged += OnPresenceChanged;
        _linphone.RegistrationAuthStopped += OnRegistrationAuthStopped;
        _linphone.TransferResult += OnTransferResult;
        _linphone.VoicemailChanged += OnVoicemailChanged;
        // Микрофон рисуем по факту, а не по возврату команды (см. OnMuteClick).
        // Событие приходит с потока ядра — маршалим в свой.
        _linphone.MicMuteChanged += muted => DispatcherQueue.TryEnqueue(() => UpdateMuteVisuals(muted));
        // Запись и камера — по той же схеме, что микрофон: команда уходит, состояние приходит.
        _linphone.RecordingChanged += on => DispatcherQueue.TryEnqueue(() => OnRecordingChanged(on));
        _linphone.CameraStateChanged += on => DispatcherQueue.TryEnqueue(() =>
        {
            _videoOn = on;
            UpdateVideoButton();
            UpdateVideoLayout();
        });

        // Уведомления Windows о входящем и о новых сообщениях (см. ToastNotifier).
        _toasts.Activated += OnToastAction;
        _toasts.Subscribe();

        // Отказ файла локальных контактов раньше уходил в никуда — см. LocalContacts.Log.
        LocalContacts.Log = EnqueueLog;

        // BLF: состояние линии коллеги. Событие приходит с потока ядра — маршалим в свой.
        // Состояние линий меняется часто (каждый звонок коллеги — минимум два NOTIFY),
        // поэтому в лог не пишем: состояние видно на пульте, а журнал бы захлебнулся.
        // Гарнитуру втыкают и выдёргивают прямо посреди разговора. liblinphone сам ничего
        // не переключает: звук продолжает идти в исчезнувшее устройство, то есть в никуда,
        // и собеседник слышит тишину.
        _linphone.AudioDevicesChanged += () => DispatcherQueue.TryEnqueue(_audio.OnDevicesChanged);

        _linphone.DialogStateChanged += (ext, state, caller) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                FavoritesVm.SetDialogState(ext, state);
                // Слоты парковки приходят той же подпиской: на АТС это обычные hint'ы.
                ParkingVm.SetDialogState(ext, state);
                ParkingVm.SetCaller(ext, caller);
            });

        try
        {
            var coreStart = System.Diagnostics.Stopwatch.StartNew();
            bool coreReady = _linphone.Start();
            coreStart.Stop();

            if (coreReady)
            {
                EnqueueLog($"liblinphone {_linphone.CoreVersion}; ядро поднялось за {coreStart.ElapsedMilliseconds} мс");
                ApplyStartupSettings();
            }
            else
            {
                // Ядро не успело подняться (медленный диск, доменная машина, антивирус на
                // каталоге профиля). Окно всё равно показываем: прежде ожидание было
                // бесконечным, и пользователь смотрел в пустоту, не имея возможности даже
                // закрыть окно. Настройки применим, как только ядро оживёт.
                EnqueueLog($"Ядро не ответило за {coreStart.ElapsedMilliseconds} мс — окно открывается без него");
                HeaderVm.StatusText = Loc.S("St_CoreStarting");
                RegDot.Fill = Palette("LpAmberBrush");
                WaitForCoreThenApplySettings();
            }

            // iterate() больше НЕ на UI-потоке: ядро само крутит его на выделенном
            // потоке (см. LinphoneService/LinphoneCore) — DNS/TLS/sqlite не фризят окно.

            // Авто-переподключение при смене сети (Wi-Fi/VPN/Ethernet): дебаунс 2с, затем re-REGISTER.
            _netDebounce = DispatcherQueue.CreateTimer();
            _netDebounce.IsRepeating = false;
            _netDebounce.Interval = TimeSpan.FromSeconds(2);
            _netDebounce.Tick += (t, _) => { t.Stop(); EnqueueLog("Смена сети — переподключение"); _linphone.RefreshNetwork(); };
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // Сон, пробуждение и блокировка станции. Пробуждение НЕ равнозначно смене адреса:
            // в той же сети NetworkAddressChanged не поднимется, а регистрация к этому моменту
            // уже протухла (см. PowerEvents).
            _power = new PowerEvents(OnPowerEvent);
            _power.Subscribe();

            // Всё, что не нужно В МОМЕНТ появления окна, — после первой отрисовки. Замер
            // показал: рантайм и XAML занимают ~500 мс, ядро ~100 мс, а ещё ~200 мс уходило
            // на разбор журнала, пересборку избранного, подписки BLF и бейджи — то есть на
            // то, чего пользователь в первую секунду не видит.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, InitializeDeferred);
        }
        catch (Exception ex)
        {
            EnqueueLog("ОШИБКА запуска Core: " + ex.Message);
            HeaderVm.StatusText = Loc.S("St_CoreFailed");   // был захардкожен по-русски
            RegDot.Fill = Palette("LpDangerBrush");
        }

        _msToCore = MsSinceProcessStart();

        SetMode(Mode.Dialer);
        ShowSection(Tab.Dialer);
        // При СТАРТЕ поле должно быть чистым. Номер из журнала прошлого сеанса, показанный
        // подсказкой сразу после запуска, выглядит как забытый чужой ввод: пользователь видит
        // в поле число, которого не набирал. Подсказка появится после первого исходящего
        // звонка в этом сеансе — тогда она к месту.
        _dialerPlaceholderDismissed = true;
        UpdateDialerLine();

        // Контакты из кэша (+ локальные) — чтобы имя показывалось при наборе сразу, не открывая вкладку.
        SetContacts(MergeContacts(Phonebook.LoadCached()));

        // Локализация: проставляем тексты из ресурсов (x:Uid в unpackaged не переключается).
        if (Content is FrameworkElement root) root.Loaded += (_, _) => ApplyLanguage();

        // Горячие клавиши: Esc = сброс/отклонить, Ctrl+M = микрофон, цифры = DTMF в разговоре.
        if (Content is UIElement rootEl) rootEl.KeyDown += OnRootKeyDown;

        // Трей: левый клик — открыть окно; закрытие сворачивает в трей (если включено в настройках).
        TrayIcon.LeftClickCommand = new RelayCommand(ShowFromTray);
        // Пункты контекстного меню трея — через Command: у H.NotifyIcon (WinUI) событие Click
        // в ContextFlyout не доходит до обработчика, а ICommand отрабатывает надёжно.
        MiTrayOpen.Command = new RelayCommand(ShowFromTray);
        MiTrayExit.Command = new RelayCommand(ExitApp);
        TrayIcon.ForceCreate();
        AppWindow.Closing += OnAppWindowClosing;
        // Оверлеи видео живут в экранных координатах — двигаем их вместе с окном.
        AppWindow.Changed += OnAppWindowChanged;

        // Закрытие окна штатным путём (например, при выключенном «сворачивать в трей»).
        Closed += (_, _) => ExitApp();

        // «Запускать свёрнутым в трей»: прячем окно сразу после первой активации.
        if (_linphone.GetAppFlag("start_hidden", false))
            Activated += OnStartHiddenActivated;

        // Single instance: повторный запуск exe сигналит «покажи окно» (см. Program.cs).
        System.Threading.ThreadPool.RegisterWaitForSingleObject(Program.ShowSignal,
            (_, _) => DispatcherQueue.TryEnqueue(() => { ShowFromTray(); BringToFront(); }),
            null, System.Threading.Timeout.Infinite, executeOnlyOnce: false);

        // Click-to-call: второй экземпляр положил tel:/sip: в файл и просигналил — набираем.
        System.Threading.ThreadPool.RegisterWaitForSingleObject(Program.DialSignal,
            (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    string uri = System.IO.File.ReadAllText(Program.DialRequestPath).Trim();
                    try { System.IO.File.Delete(Program.DialRequestPath); } catch { }
                    HandleDialRequest(uri);
                }
                catch { }
            }),
            null, System.Threading.Timeout.Infinite, executeOnlyOnce: false);

        // tel:/sip:-аргумент самого первого запуска (иконки ещё не было — набираем после старта Core).
        if (Program.PendingDialUri is { Length: > 0 } pendingUri)
            DispatcherQueue.TryEnqueue(() => HandleDialRequest(pendingUri));

        // Сколько прошло от запуска процесса до готового окна. Метрика, которой не было:
        // на жалобу «долго открывается» отвечать было нечем, а на глаз это не измеряют.
        LogStartupTime();

        // Проверку обновления откладываем: при старте важнее регистрация и звуковые устройства,
        // а окно должно успеть появиться — иначе ContentDialog не на чем показывать.
        var updateDelay = DispatcherQueue.CreateTimer();
        updateDelay.Interval = TimeSpan.FromSeconds(20);
        updateDelay.IsRepeating = false;
        updateDelay.Tick += (_, _) => CheckForUpdateAsync(manual: false);
        updateDelay.Start();
    }

    /// <summary>
    /// Настройки, которые нужно применить сразу после подъёма ядра: горячие клавиши, локальный
    /// API, тема, свой статус, подписки presence.
    ///
    /// Вынесено из конструктора отдельным методом ровно потому, что читать это можно ТОЛЬКО у
    /// живого ядра. Фасад на неотвечающее ядро отдаёт значения по умолчанию, и применить их —
    /// значит зарегистрировать выключенные пользователем горячие клавиши, сбросить тему и
    /// объявить коллегам статус «Доступен» вместо «Не беспокоить».
    /// </summary>
    private void ApplyStartupSettings()
    {
        // Глобальные горячие клавиши: оператор живёт в CRM, а не в софтфоне, и тянуться мышью
        // к чужому окну — это те секунды, за которые звонящий кладёт трубку.
        RegisterHotkeys(_linphone.GetAppFlag("hotkeys", true));
        StartLocalApi();

        RefreshHeader();
        RefreshBehaviorIndicators();
        ApplyTheme(_linphone.GetTheme());

        long.TryParse(_linphone.GetAppString("history_seen", "0"), out _missedSeen);
        int.TryParse(_linphone.GetAppString("presence_status", "0"), out int savedStatus);
        var myStatus = (PresenceStatus)savedStatus;
        // Сохранённое значение из старых версий (обед/совещание/не на месте) — «Доступен».
        if (Array.IndexOf(SelectableStatuses, myStatus) < 0) myStatus = PresenceStatus.Available;
        // Статус 🚫 и флаг «Не беспокоить» — единое состояние (см. ApplyMyStatus).
        // Согласуем после обновления со старой версии, где они жили раздельно: флаг главнее
        // (у него видимый эффект — отклонение входящих).
        if (_linphone.GetAppFlag("dnd", false)) myStatus = PresenceStatus.DoNotDisturb;
        else if (myStatus == PresenceStatus.DoNotDisturb) _linphone.SetAppFlag("dnd", true);
        if (myStatus != PresenceStatus.DoNotDisturb) _statusBeforeDnd = myStatus;
        _linphone.SetMyStatus(myStatus);   // запомнить выбранный статус до публикации
        RefreshBehaviorIndicators();       // бейдж DND — после согласования
        // Публикация статуса по SIP (PUBLISH) по умолчанию ВЫКЛЮЧЕНА: Asterisk её не
        // принимает (WARNING «no registered publish handler» на каждый PUBLISH), статус
        // уходит HTTP-хуком (NotifyPbxPresence). Кому нужен PUBLISH (flexisip) — включит.
        _linphone.SetPresencePublish(_linphone.GetAppFlag("publish_presence", false));
        // Подписка на статус избранного — только внутренние номера (см. FavoritesViewModel).
        _linphone.SyncPresenceFriends(Favorites.Load().Select(f => f.Number)
            .Where(PhoneNumberUtils.IsLikelyInternal));
        NotifyPbxPresence(myStatus);   // синхронизировать статус на АТС при старте
        BuildStatusMenu();
        UpdateMyStatusEmoji();
    }

    private DispatcherQueueTimer? _coreWaitTimer;

    /// <summary>Ядро не поднялось за отведённое время — ждём его в фоне и применяем настройки,
    /// когда оживёт. Ограничение по числу попыток обязательно: если ядро не встало за минуту,
    /// оно не встанет вовсе, а тикающий вечно таймер прячет отказ вместо того, чтобы его
    /// показать.</summary>
    private void WaitForCoreThenApplySettings()
    {
        const int maxAttempts = 60;
        int attempts = 0;
        _coreWaitTimer = DispatcherQueue.CreateTimer();
        _coreWaitTimer.Interval = TimeSpan.FromSeconds(1);
        _coreWaitTimer.Tick += (t, _) =>
        {
            // Спрашиваем ДИСПЕТЧЕР, а не ядро. IsStarted — это Query: пока идёт инициализация,
            // поток ядра жив (он внутри init) и очередь не разбирает, поэтому каждый такой
            // опрос простаивал на потоке интерфейса ВЕСЬ таймаут — по 5 секунд на тик, ровно
            // на тех медленных машинах, ради которых это ожидание и заведено. CoreReady читает
            // три поля диспетчера и возвращается мгновенно (замерено: CoreDispatcherTests,
            // WaitingForCore_Via*).
            if (_linphone.CoreReady)
            {
                t.Stop();
                EnqueueLog($"Ядро поднялось на {++attempts}-й секунде — применяю настройки");
                ApplyStartupSettings();
                return;
            }
            // Ядро упало при старте — ждать больше нечего, отказ надо показать сразу, а не
            // держать человека минуту перед неработающим телефоном.
            if (_linphone.CoreFailed)
            {
                t.Stop();
                EnqueueLog("Ядро SIP не запустилось — телефон работать не будет. См. liblinphone.log");
                HeaderVm.StatusText = Loc.S("St_CoreFailed");
                RegDot.Fill = Palette("LpDangerBrush");
                return;
            }
            if (++attempts < maxAttempts) return;
            t.Stop();
            EnqueueLog("Ядро SIP так и не запустилось — телефон работать не будет. См. liblinphone.log");
            HeaderVm.StatusText = Loc.S("St_CoreFailed");
            RegDot.Fill = Palette("LpDangerBrush");
        };
        _coreWaitTimer.Start();
    }

    /// <summary>Время от запуска процесса до готового окна — включая рантайм .NET и WinUI,
    /// то есть ровно то, что чувствует пользователь. Отдельно отмечаем аварийный перезапуск
    /// (флаг /restarted ставит RegisterApplicationRestart): такие старты надо уметь отличать
    /// в логе, иначе непонятно, почему приложение вдруг стартовало посреди дня.</summary>
    /// <summary>Работа, без которой окно может показаться: разбор журнала, избранное и его
    /// BLF-подписки, бейджи, настройка видео. Выполняется сразу после первой отрисовки —
    /// пользователь в этот момент ещё читает шапку, а не жмёт кнопки.</summary>
    private void InitializeDeferred()
    {
        // КАЖДЫЙ шаг отдельно. Раньше пять независимых операций стояли под одним try, и отказ
        // первой отменял четыре остальные: не загрузилось избранное — не настроилось видео,
        // причём в журнале оставалась одна строка без указания, где именно сломалось.
        Step("бейджи", () => { UpdateMissedBadge(); UpdateChatBadge(); });
        Step("избранное и BLF", () =>
        {
            FavoritesVm.InitView();   // вид «Избранного» (список/BLF)
            ParkingVm.Reload();       // слоты парковки — до PopulateFavorites: он их подписывает
            // Избранное грузим при старте, а не только при открытии вкладки: от него зависят
            // подписки BLF (presence + dialog). Иначе лампы оживают лишь после того, как
            // пользователь зайдёт на пульт, и первые секунды показывают неправду.
            PopulateFavorites();
        });
        Step("последний набранный", () =>
        {
            // Единственный за сеанс разбор журнала ради кнопки повтора; дальше номер
            // поддерживает воронка исходящих (CallStateChanged) — на клике ядро не трогаем.
            DialerVm.LastDialed = ViewModels.HistoryViewModel.LastDialed(_linphone.GetCallHistory());
            // Присвоение LastDialed выше поднимает событие, а его обработчик показывает
            // подсказку — здесь гасим её обратно: при старте поле обязано быть чистым.
            _dialerPlaceholderDismissed = true;
            UpdateDialerLine();
        });
        Step("видео", () =>
        {
            // liblinphone создаёт окна рендера сам; хук перехватывает их в момент создания
            // и встраивает в экран звонка (иначе окно успевает мигнуть).
            _linphone.SetupVideo();
            EnsureVideoWindowHook();
        });
        EnqueueLog($"Отложенная инициализация завершена на {MsSinceProcessStart()} мс");

        void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { EnqueueLog($"Отложенная инициализация ({what}): {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>Отметки времени внутри конструктора — чтобы понимать, что именно занимает
    /// старт: рантайм и XAML или наше ядро. Без разбивки «долго открывается» не диагностируется.</summary>
    private static int _msToXaml, _msToCore;

    private static int MsSinceProcessStart()
    {
        try { return (int)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
        catch { return 0; }
    }

    private void LogStartupTime()
    {
        try
        {
            var started = System.Diagnostics.Process.GetCurrentProcess().StartTime;
            int ms = (int)(DateTime.Now - started).TotalMilliseconds;
            EnqueueLog($"Разбивка старта: рантайм и XAML {_msToXaml} мс, ядро и подписки {_msToCore - _msToXaml} мс, "
                       + $"остальное окно {ms - _msToCore} мс");
            bool afterCrash = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "/restarted", StringComparison.OrdinalIgnoreCase));
            EnqueueLog($"Окно готово через {ms} мс после запуска процесса"
                       + (afterCrash ? " (перезапуск после аварийного завершения)" : ""));
        }
        catch { }
    }

    /// <summary>Click-to-call: нормализует tel:/callto:/sip: URI и набирает номер.
    /// Инициатор здесь ВНЕШНИЙ — ссылка на веб-странице или файл-запрос второго экземпляра, —
    /// поэтому звонок за пределы своих аккаунтов делается только с подтверждения: иначе любая
    /// страница могла бы увести вызов на чужой SIP-сервер (раскрытие адреса клиента, отдача
    /// медиапотока, платные направления). Свои номера и свой домен набираются как раньше.</summary>
    private async void HandleDialRequest(string uri)
    {
        string target = NormalizeDialTarget(uri);
        if (target.Length == 0 || !_linphone.IsStarted) return;
        ShowFromTray();
        BringToFront();
        ShowSection(Tab.Dialer);
        DialerVm.Number = target;

        if (!PhoneNumberUtils.IsTrustedDialTarget(target, _linphone.GetAccounts().Select(a => a.Server)))
        {
            if (!await ConfirmExternalDialAsync(target))
            {
                EnqueueLog($"Click-to-call отклонён: {target}");
                return;
            }
        }
        EnqueueLog($"Click-to-call: {target}");
        _linphone.Call(target);
    }

    /// <summary>Спросить перед звонком на чужой сервер. Диалог не открылся (уже открыт другой,
    /// окна нет) — считаем отказом: молча не звоним.</summary>
    private async System.Threading.Tasks.Task<bool> ConfirmExternalDialAsync(string target)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.S("St_ExtDialTitle"),
            Content = Loc.F("St_ExtDialText", target),
            PrimaryButtonText = Loc.S("St_CallAction"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        return await Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    private static string NormalizeDialTarget(string raw) => PhoneNumberUtils.NormalizeDialTarget(raw);

    // ---------------- Геометрия окна ----------------
    // Размер и позицию окна приложение обязано помнить: раньше каждый запуск принудительно
    // сжимал окно до минимума и ставил его в место по умолчанию, то есть растянуть окно «на
    // совсем» было невозможно. Помнит это любой софтфон, включая MicroSIP.

    private DispatcherQueueTimer? _geometrySaveDebounce;

    /// <summary>Вернуть окно туда, где пользователь его оставил. Сохранённая геометрия
    /// принимается ТОЛЬКО если она попадает на существующий сейчас монитор: конфигурация
    /// экранов между запусками меняется (отключили док-станцию, зашли по RDP), и окно,
    /// восстановленное на исчезнувший монитор, оказалось бы за пределами видимой области.</summary>
    private void RestoreWindowGeometry(IntPtr hwnd)
    {
        var saved = AppSettings.GetWindowGeometry();
        if (saved is { } g && IsOnSomeDisplay(g))
        {
            var (w, h) = MinSizeHelper.ClampToWorkArea(hwnd, g.Width, g.Height);
            AppWindow.MoveAndResize(new RectInt32(g.X, g.Y, w, h));
            return;
        }
        // Молчать нельзя: «окно опять открылось не там» — жалоба, которую иначе не разобрать.
        EnqueueLog(saved is null
            ? "Геометрия окна не сохранена — открываем в размере по умолчанию"
            : $"Сохранённая геометрия окна ({saved.Value.X},{saved.Value.Y} {saved.Value.Width}×{saved.Value.Height}) "
              + "не попадает ни на один монитор — открываем в размере по умолчанию");
        double scale = MinSizeHelper.GetScale(hwnd);
        var (dw, dh) = MinSizeHelper.ClampToWorkArea(hwnd, (int)(DefaultWidth * scale), (int)(DefaultHeight * scale));
        AppWindow.Resize(new SizeInt32(dw, dh));
    }

    /// <summary>Осталась ли доступной мышью верхняя полоса окна: если заголовок ушёл за пределы
    /// всех экранов, окно не подвинуть и не закрыть, поэтому такую геометрию не принимаем.
    ///
    /// Проверяем ТОЧКАМИ через Win32, а не DisplayArea.FindAll(): последний в конструкторе окна
    /// (до Activate) не отрабатывает, из-за чего восстановление молча уходило в ветку «по
    /// умолчанию» — окно каждый раз открывалось в стартовом размере при совершенно нормальных
    /// сохранённых координатах.</summary>
    private static bool IsOnSomeDisplay(WindowGeometry g)
    {
        int y = g.Y + 12;   // полоса заголовка
        foreach (int x in new[] { g.X + 40, g.X + g.Width / 2, g.X + g.Width - 40 })
            if (MinSizeHelper.IsPointOnAnyMonitor(x, y)) return true;
        return false;
    }

    /// <summary>Запомнить геометрию. Свёрнутое/скрытое окно не сохраняем: у свёрнутого позиция
    /// равна -32000, и такое «сохранение» потеряло бы настоящий размер.</summary>
    private void SaveWindowGeometry()
    {
        try
        {
            if (!AppWindow.IsVisible) return;
            var p = AppWindow.Position;
            var s = AppWindow.Size;
            if (p.X <= -30000 || p.Y <= -30000 || s.Width <= 0 || s.Height <= 0) return;
            AppSettings.SetWindowGeometry(new WindowGeometry(p.X, p.Y, s.Width, s.Height));
        }
        catch { }
    }

    /// <summary>Окно двигают/тянут — сохраняем не на каждое событие, а через паузу:
    /// перетаскивание порождает их десятками в секунду, а это запись файла.</summary>
    private void ScheduleGeometrySave()
    {
        if (_geometrySaveDebounce == null)
        {
            _geometrySaveDebounce = DispatcherQueue.CreateTimer();
            _geometrySaveDebounce.IsRepeating = false;
            _geometrySaveDebounce.Interval = TimeSpan.FromMilliseconds(800);
            _geometrySaveDebounce.Tick += (t, _) => { t.Stop(); SaveWindowGeometry(); };
        }
        _geometrySaveDebounce.Stop();
        _geometrySaveDebounce.Start();
    }

    private void OnStartHiddenActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnStartHiddenActivated; // одноразово — только при старте
        AppWindow.Hide();
    }

    // ---------------- Трей / сворачивание ----------------
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Если включено «сворачивать в трей» — прячем окно вместо закрытия.
        // Явный выход идёт через ExitApp(), а не Close(), поэтому сюда не попадает.
        if (_linphone.GetAppFlag("minimize_to_tray", true))
        {
            args.Cancel = true;
            SaveWindowGeometry();   // прячемся в трей — размер запоминаем, пока окно ещё видно
            AppWindow.Hide();
        }
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    /// <summary>Принудительно вывести окно на передний план и сфокусировать (для входящего).</summary>
    private void BringToFront()
    {
        try
        {
            AppWindow.MoveInZOrderAtTop();
            NativeMethods.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch { }
    }

    // ---------------- Уведомления Windows ----------------
    // Сборка уведомлений и разбор аргументов активации — в ToastNotifier: к окну они
    // отношения не имеют. Здесь остались только решения «что показать» и «что делать».
    private readonly ToastNotifier _toasts;

    private void ShowIncomingToast(string caller, string number) => _toasts.ShowIncoming(caller, number);
    private void ClearIncomingToast() => _toasts.ClearIncoming();
    private void ShowChatToast(string peer, string display, string text) => _toasts.ShowChat(peer, display, text);

    /// <summary>Реакция на нажатие в уведомлении. Приходит с чужого потока — маршалим в свой.</summary>
    private void OnToastAction(ToastAction action, string peer)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (action)
            {
                case ToastAction.Accept:
                    // Окно НЕ выдёргиваем: пользователь ответил из уведомления именно затем,
                    // чтобы не переключаться. Кто на линии — видно в мини-окне.
                    _linphone.AcceptIncoming();
                    break;
                case ToastAction.Decline:
                    _linphone.DeclineIncoming();
                    break;
                case ToastAction.Chat:
                    ShowFromTray();
                    BringToFront();
                    ShowSection(Tab.Chat);
                    if (peer.Length > 0) ChatVm.OpenConversation(peer);
                    else ChatVm.ShowList();
                    break;
                case ToastAction.CallBack:
                    // Клик по телу уведомления о пропущенном приходит сюда же, но БЕЗ звонка:
                    // открываем окно и подставляем номер. Звонит только кнопка «Перезвонить»,
                    // и решает это наличие номера в аргументе кнопки.
                    ShowFromTray();
                    BringToFront();
                    ShowSection(Tab.Dialer);
                    if (peer.Length > 0 && !_inCall && _linphone.CallCount == 0)
                    {
                        EnqueueLog($"Перезвон из уведомления: {peer}");
                        DialerVm.Number = peer;
                        _linphone.Call(peer);
                    }
                    break;
                default:
                    ShowFromTray();
                    BringToFront();
                    break;
            }
            ClearIncomingToast();
        });
    }

    /// <summary>Размер структуры неизменен — считаем один раз, а не на каждое мигание.</summary>
    private static readonly uint FlashInfoSize =
        (uint)System.Runtime.InteropServices.Marshal.SizeOf<Interop.NativeMethods.FLASHWINFO>();

    /// <summary>Мигание кнопкой в панели задач на входящем (FLASHW_TIMERNOFG — до активации окна).</summary>
    private void FlashTaskbar(bool on)
    {
        try
        {
            var fi = new Interop.NativeMethods.FLASHWINFO
            {
                cbSize = FlashInfoSize,
                hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this),
                dwFlags = on ? Interop.NativeMethods.FLASHW_ALL | Interop.NativeMethods.FLASHW_TIMERNOFG
                             : Interop.NativeMethods.FLASHW_STOP,
            };
            Interop.NativeMethods.FlashWindowEx(ref fi);
        }
        catch { }
    }


    /// <summary>Автоответ на входящий после настроенной задержки (сек). Таймер отменяется,
    /// как только звонок покидает состояние «входящий» (см. OnCallStateChanged).</summary>
    /// <param name="delaySeconds">Задержка; null — взять из настроек пользователя.
    /// У интеркома задержку задаёт САМА АТС в заголовке, и она обычно нулевая.</param>
    private void StartAutoAnswer(int? delaySeconds = null)
    {
        int delay = delaySeconds ?? (int.TryParse(_linphone.GetAppString("auto_answer_delay", "0"), out int d) ? d : 0);
        _autoAnswerTimer?.Stop();
        _autoAnswerTimer = DispatcherQueue.CreateTimer();
        _autoAnswerTimer.IsRepeating = false;
        // Нулевой интервал у DispatcherQueueTimer срабатывает неопределённо — берём минимум,
        // при котором таймер гарантированно тикает.
        _autoAnswerTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, Math.Clamp(delay, 0, 60) * 1000));
        _autoAnswerTimer.Tick += (t, _) => { t.Stop(); _linphone.AcceptIncoming(); };
        _autoAnswerTimer.Start();
    }

    /// <summary>
    /// Полный выход из приложения. Не полагаемся на Close()/событие Closed: при свёрнутом
    /// в трей окне (AppWindow.Hide) Close() не доводит до Closed, и процесс остаётся жить.
    /// Поэтому освобождаем ресурсы и завершаем процесс напрямую.
    /// </summary>
    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        SaveWindowGeometry();   // до любой очистки: дальше окно может быть уже не в том состоянии
        try
        {
            _geometrySaveDebounce?.Stop();
            _durationTimer?.Stop();
            _autoAnswerTimer?.Stop();
            _netDebounce?.Stop();
            _coreWaitTimer?.Stop();
            // Комбинации заняты СИСТЕМОЙ: не освободив их, мы оставим их занятыми до
            // перезагрузки, и следующий запуск сам себе их зарегистрировать не сможет.
            Interop.GlobalHotkeys.Unregister();
            CloseIncomingMini();   // оба окна поверх всех — иначе переживут выход приложения
            CloseMiniWindow();
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            // SystemEvents — статическое событие: без снятия оно держит обработчик, а с ним окно.
            _power?.Dispose();
            _settingsWindow?.Close();
            ClearIncomingToast();
            TrayIcon.Dispose();
            _video.Dispose();   // снять хук окон видео
            // Ядро освобождает контейнер, который его и создал: одно место вместо ручной
            // уборки в обработчике выхода окна.
            AppHost.Shutdown();
        }
        catch { /* при выходе ошибки очистки не важны */ }
        // Сводка здоровья ПОСЛЕ очистки, но до сброса лога: одна строка в хвосте app.log,
        // по которой видно, подмерзал ли интерфейс за сеанс, — вместо поиска отдельных
        // сообщений о медленных вызовах среди тысяч других строк.
        try { EnqueueLog("Итог сеанса: " + _linphone.HealthSummary()); } catch { }
        AppLog.Flush();
        Environment.Exit(0);
    }

    /// <summary>Смена сети (часто несколько событий) — рестартуем дебаунс, по истечении переподключаемся.</summary>
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => { _netDebounce?.Stop(); _netDebounce?.Start(); });

    // ---------------- Питание и сеанс Windows ----------------
    private PowerEvents? _power;
    private PresenceStatus? _statusBeforeLock;   // куда вернуть статус после разблокировки

    /// <summary>Реакция на сон/пробуждение/блокировку. Приходит с чужого потока — маршалим в свой.</summary>
    private void OnPowerEvent(PowerEvent e) => DispatcherQueue.TryEnqueue(() =>
    {
        switch (e)
        {
            case PowerEvent.Suspend:
                // Пока сеть ещё есть — снимаем регистрацию. Иначе АТС час будет считать контакт
                // живым и слать в него INVITE: звонящий слышит гудки, сотрудник — ничего.
                EnqueueLog("Система засыпает — снимаю регистрацию");
                _linphone.SetNetworkReachable(false);
                break;

            case PowerEvent.Resume:
                // Немедленный REGISTER уйдёт в никуда: сетевой стек после пробуждения ещё не
                // готов. Пользуемся тем же дебаунсом на 2 с, что и при смене адреса.
                EnqueueLog("Система проснулась — перерегистрация");
                _linphone.SetNetworkReachable(true);
                _netDebounce?.Stop();
                _netDebounce?.Start();
                break;

            case PowerEvent.SessionLock:
                // Человека нет за машиной — коллегам это стоит знать. Ставим ИМЕННО «Отошёл»,
                // а не «Не беспокоить»: последний заворачивает звонки в голосовую почту, и
                // делать это без ведома человека нельзя. Ручной статус не трогаем — он выбран
                // осознанно и переживает блокировку.
                if (_linphone.GetAppFlag("away_on_lock", true) && _linphone.MyStatus == PresenceStatus.Available)
                {
                    _statusBeforeLock = PresenceStatus.Available;
                    EnqueueLog("Станция заблокирована — статус «Отошёл»");
                    ApplyMyStatus(PresenceStatus.Away, remember: false);
                }
                break;

            case PowerEvent.SessionUnlock:
                // Возвращаем только СВОЙ автоматический статус: если человек за время
                // блокировки был переведён в DND (например, с другого устройства), затирать
                // это нельзя.
                if (_statusBeforeLock is { } back && _linphone.MyStatus == PresenceStatus.Away)
                {
                    EnqueueLog("Станция разблокирована — возвращаю статус");
                    ApplyMyStatus(back, remember: false);
                }
                _statusBeforeLock = null;
                break;
        }
    });

    // ---------------- Навигация по вкладкам ----------------
    private void OnNavDialClick(object sender, RoutedEventArgs e) => ShowSection(Tab.Dialer);
    private void OnNavContactsClick(object sender, RoutedEventArgs e)
    {
        ShowSection(Tab.Contacts);
        _ = ContactsVm.LoadAsync();
    }
    private void OnNavFavoritesClick(object sender, RoutedEventArgs e)
    {
        ShowSection(Tab.Favorites);
        PopulateFavorites();
    }
    private void OnNavHistoryClick(object sender, RoutedEventArgs e)
    {
        ShowSection(Tab.History);
        PopulateHistory();
        // Журнал просмотрен — запоминаем момент и гасим бейдж пропущенных.
        _missedSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _linphone.SetAppString("history_seen", _missedSeen.ToString());
        UpdateMissedBadge();
    }

    /// <summary>Обновляет бейдж непросмотренных пропущенных на вкладке «Журнал».</summary>
    private void UpdateMissedBadge()
    {
        // Число пропущенных считаем ОДИН раз и передаём дальше: и бейдж, и индикатор трея
        // спрашивали его каждый сам, а это полный обход журнала звонков на каждое обновление.
        int n = _linphone.GetMissedSince(_missedSeen);
        MissedBadgeText.Text = n > 99 ? "99+" : n.ToString();
        MissedBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrayIndicator(n);
    }

    private bool _trayAlert;   // текущее состояние иконки трея (с точкой/без), чтобы не пересоздавать зря

    /// <summary>Красная точка на иконке в трее + подсказка, когда есть пропущенные или непрочитанные.</summary>
    /// <param name="missedCount">Уже посчитанное число пропущенных; null — посчитать самим.</param>
    private void UpdateTrayIndicator(int? missedCount = null)
    {
        int missed = missedCount ?? _linphone.GetMissedSince(_missedSeen);
        int unread = _linphone.GetUnreadChatCount();
        bool alert = missed + unread > 0;

        if (alert != _trayAlert)
        {
            _trayAlert = alert;
            TrayIcon.IconSource = new BitmapImage(
                new Uri(alert ? "ms-appx:///Assets/lphone-alert.ico" : "ms-appx:///Assets/lphone.ico"));
        }

        var parts = new List<string>();
        if (missed > 0) parts.Add(Loc.F("Tray_Missed", missed));
        if (unread > 0) parts.Add(Loc.F("Tray_Unread", unread));
        TrayIcon.ToolTipText = parts.Count == 0 ? "L-Phone" : "L-Phone — " + string.Join(" · ", parts);
    }

    // ---------------- Контакты (локальные + внешняя адресная книга) ----------------
    /// <summary>Локальные контакты + внешняя книга, отсортированные по имени.</summary>
    private static List<ContactEntry> MergeContacts(IEnumerable<ContactEntry> phonebook) =>
        LocalContacts.Load().Concat(phonebook)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Пересобрать контакты. Вкладка «Контакты» — через ViewModel; _allContacts
    /// держим для дайлера/перевода (FindContact, автодополнение) — их миграция позже.</summary>
    private void RefreshContacts()
    {
        SetContacts(MergeContacts(Phonebook.LoadCached()));
        ContactsVm.Refresh();
    }

    private void OnAddContactClick(object sender, RoutedEventArgs e) => ShowContactDialog(null);

    private void OnEditContactClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ContactEntry c && c.IsLocal)
            ShowContactDialog(c);
    }

    /// <summary>Диалог добавления/изменения локального контакта (с кнопкой удаления при правке).</summary>
    private async void ShowContactDialog(ContactEntry? existing)
    {
        var name = new TextBox { Header = Loc.S("Ct_Name"), Text = existing?.Name ?? "" };
        var number = new TextBox { Header = Loc.S("Ct_Number"), Text = existing?.Number ?? "", Margin = new Thickness(0, 8, 0, 0) };
        var mobile = new TextBox { Header = Loc.S("Ct_Mobile"), Text = existing?.Mobile ?? "", Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(name);
        panel.Children.Add(number);
        panel.Children.Add(mobile);

        var dialog = new ContentDialog
        {
            Title = Loc.S(existing == null ? "Ct_Add" : "Ct_Edit"),
            Content = panel,
            PrimaryButtonText = Loc.S("BtnSave.Content"),
            SecondaryButtonText = existing == null ? "" : Loc.S("Ct_Delete"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        ContentDialogResult result = await Dialogs.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            string nm = name.Text.Trim(), num = number.Text.Trim(), mob = mobile.Text.Trim();
            if (nm.Length == 0 && num.Length == 0 && mob.Length == 0) return;
            LocalContacts.AddOrUpdate(existing?.Id, nm, num, mob);
            RefreshContacts();
        }
        else if (result == ContentDialogResult.Secondary && existing != null)
        {
            LocalContacts.Remove(existing.Id);
            RefreshContacts();
        }
    }

    private void OnContactsSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ContactsVm.SearchQuery = sender.Text ?? "";
    }

    /// <summary>Клик по строке списка НЕ звонит (защита от misclick — раньше один промах
    /// набирал клиента): подставляет номер в дайлер. Звонок — по кнопке-трубке в строке.</summary>
    private void PrefillDialer(string number)
    {
        if (number.Length == 0 || _inCall) return;   // в разговоре набора нет — есть «Перевод»
        ShowSection(Tab.Dialer);
        DialerVm.Number = SipUser(number) is { Length: > 0 } user ? user : number;
    }

    /// <summary>Кнопка-трубка в строке контакта/избранного/журнала — немедленный звонок.</summary>
    private void OnRowCallClick(object sender, RoutedEventArgs e)
    {
        string number = RowNumber((sender as FrameworkElement)?.DataContext);
        if (number.Length == 0) return;
        ShowSection(Tab.Dialer);
        _linphone.Call(number);
    }

    // ---------------- Контекстное меню строки (позвонить/написать/копировать/избранное) ----------------
    private static string RowNumber(object? dc) => dc switch
    {
        ContactEntry c => c.CallNumber,
        ViewModels.FavoriteRow f => f.Number,
        CallLogEntry h => h.Remote,
        _ => "",
    };

    private string RowName(object? dc) => dc switch
    {
        ContactEntry c => c.Name,
        ViewModels.FavoriteRow f => f.Display,
        CallLogEntry h => DisplayName(h.Remote),
        _ => "",
    };

    private void OnRowMenuCall(object sender, RoutedEventArgs e)
    {
        string n = RowNumber((sender as FrameworkElement)?.DataContext);
        if (n.Length == 0) return;
        ShowSection(Tab.Dialer);
        _linphone.Call(n);
    }

    private void OnRowMenuChat(object sender, RoutedEventArgs e)
    {
        string n = RowNumber((sender as FrameworkElement)?.DataContext);
        if (n.Length == 0) return;
        ShowSection(Tab.Chat);
        ChatVm.OpenConversation(SipUser(n) is { Length: > 0 } u ? u : n);
    }

    private void OnRowMenuCopy(object sender, RoutedEventArgs e)
    {
        string n = RowNumber((sender as FrameworkElement)?.DataContext);
        if (n.Length == 0) return;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(SipUser(n) is { Length: > 0 } u ? u : n);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private void OnRowMenuFav(object sender, RoutedEventArgs e)
    {
        object? dc = (sender as FrameworkElement)?.DataContext;
        string n = RowNumber(dc);
        if (n.Length == 0) return;
        Favorites.Toggle(RowName(dc), n);
        // Обновляем тот список, из которого пришли (звёзды/состав избранного).
        if (FavoritesSection.Visibility == Visibility.Visible) PopulateFavorites();
        else if (HistorySection.Visibility == Visibility.Visible) PopulateHistory();
        else ContactsVm.Refresh();
    }

    private void OnContactItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ContactEntry c) return;
        // Один номер — подставляем сразу. Несколько — спрашиваем: у корпоративного
        // контакта рабочий и мобильный это разные люди на том конце в разное время суток,
        // и «звоним на первый попавшийся» здесь означает «звоним не туда».
        if (!c.HasChoice) { PrefillDialer(c.CallNumber); return; }
        ShowNumberChoice(c, ContactsList.ContainerFromItem(c) as FrameworkElement, PrefillDialer);
    }

    /// <summary>Меню номеров контакта. onPick получает выбранный номер — так одно меню
    /// служит и подстановке в набор, и прямому звонку.</summary>
    private void ShowNumberChoice(ContactEntry c, FrameworkElement? target, Action<string> onPick)
    {
        var menu = new MenuFlyout();
        foreach (var n in c.AllNumbers)
        {
            var item = new MenuFlyoutItem { Text = $"{n.Label}: {n.Number}" };
            string number = n.Number;
            item.Click += (_, _) => onPick(number);
            menu.Items.Add(item);
        }
        menu.ShowAt(target ?? ContactsList);
    }

    // ---------------- Избранное (локальные пометки + статус присутствия) ----------------
    private void PopulateFavorites()
    {
        FavoritesVm.Load();
        SyncBlfSubscriptions();
    }

    /// <summary>Полный набор номеров для подписки «Event: dialog»: избранные коллеги плюс
    /// слоты парковки. Собирается в одном месте намеренно — SyncDialogSubscriptions задаёт
    /// НАБОР ЦЕЛИКОМ, и два вызова из разных моделей гасили бы подписки друг друга.</summary>
    private void SyncBlfSubscriptions() =>
        _linphone.SyncDialogSubscriptions(FavoritesVm.DialogWatched.Concat(ParkingVm.Watched));

    private DispatcherQueueTimer? _presenceDebounce;

    /// <summary>Прилетел NOTIFY о статусе друга. На живой АТС их шторм (по одному на каждого
    /// коллегу при переподключении) — не пересобираем список на каждый, а копим 300 мс и
    /// обновляем один раз. Обновляем только если открыто избранное.</summary>
    private void OnPresenceChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FavoritesSection.Visibility != Visibility.Visible) return;
            if (_presenceDebounce == null)
            {
                _presenceDebounce = DispatcherQueue.CreateTimer();
                _presenceDebounce.IsRepeating = false;
                _presenceDebounce.Interval = TimeSpan.FromMilliseconds(300);
                _presenceDebounce.Tick += (t, _) =>
                {
                    t.Stop();
                    if (FavoritesSection.Visibility == Visibility.Visible) PopulateFavorites();
                };
            }
            _presenceDebounce.Stop();
            _presenceDebounce.Start();
        });
    }

    // ---------------- Мой статус присутствия ----------------
    // Выбираемые вручную. Набор согласован с CustomPresence на Asterisk (АТС принимает
    // только их): 🚫 «Не беспокоить» — заворот звонка в голосовую почту делает АТС,
    // ☕ «Отошёл» — только показ, звонки проходят. 📞 «Разговаривает» ставится
    // автоматически во время звонка (device INUSE).
    private static readonly PresenceStatus[] SelectableStatuses =
    {
        PresenceStatus.Available, PresenceStatus.DoNotDisturb, PresenceStatus.Away,
    };

    private void BuildStatusMenu()
    {
        StatusFlyout.Items.Clear();
        foreach (var s in SelectableStatuses)
        {
            var item = new MenuFlyoutItem { Text = StatusEmoji(s) + "  " + StatusLabel(s), Tag = s };
            item.Click += OnStatusMenuClick;
            StatusFlyout.Items.Add(item);
        }
    }

    private void OnStatusMenuClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is PresenceStatus s) ApplyMyStatus(s);
    }

    private PresenceStatus _statusBeforeDnd = PresenceStatus.Available;   // куда вернуться при снятии DND

    /// <summary>Единая точка смены своего статуса. Статус 🚫 и флаг «Не беспокоить»
    /// (отклонение входящих «занято» + бейдж в шапке) — ОДНО состояние: выбор статуса
    /// синхронизирует флаг, тумблер в меню ходит через этот же метод.</summary>
    /// <param name="remember">Сохранять ли выбор в конфиг. false — статус выставлен АВТОМАТОМ
    /// (блокировка станции), и запоминать его нельзя: если приложение закроется или упадёт,
    /// пока компьютер заблокирован, «Отошёл» станет постоянным выбором пользователя, и утром
    /// человек начнёт рабочий день с чужим статусом, которого он не выбирал.</param>
    private void ApplyMyStatus(PresenceStatus s, bool remember = true)
    {
        if (s != PresenceStatus.DoNotDisturb) _statusBeforeDnd = s;
        _linphone.SetMyStatus(s);
        if (remember)
        {
            _linphone.SetAppString("presence_status", ((int)s).ToString());
            _linphone.SetAppFlag("dnd", s == PresenceStatus.DoNotDisturb);
        }
        NotifyPbxPresence(s);   // сообщить АТС (CustomPresence через pbx-admin)
        RefreshBehaviorIndicators();   // бейдж DND в шапке
        UpdateStatusLine();   // статус в шапке = выбранный presence (когда онлайн)
    }

    // Статус-хук АТС вынесен в PresenceHookClient: сетевой код с гонками внутри окна
    // невозможно было ни прочитать отдельно, ни проверить.
    private void NotifyPbxPresence(PresenceStatus s) => _presenceHook.Notify(s);


    private void UpdateMyStatusEmoji() => UpdateStatusLine();

    private void OnFavoriteItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ViewModels.FavoriteRow r) return;
        if (!FavoritesVm.GridView) { PrefillDialer(r.Number); return; }   // список — подставить в дайлер

        // Пульт ведёт себя как аппаратный BLF (Fanvil, Yealink): нажатие на МИГАЮЩУЮ клавишу
        // перехватывает звонок, на спокойную — звонит коллеге. Раньше клик всегда звонил, и
        // чтобы перехватить, приходилось лезть в контекстное меню — рефлекс оператора
        // («вижу, что звонит — жму») приводил к исходящему вызову вместо перехвата.
        if (r.IsRinging && r.CanPickup) { PickupCall(r.Number); return; }
        ShowSection(Tab.Dialer);
        _linphone.Call(r.Number);
    }

    /// <summary>Перехват звонка коллеги: feature-код Asterisk **&lt;ext&gt;.</summary>
    private void PickupCall(string number)
    {
        string ext = SipUser(number);
        if (ext.Length == 0) return;
        ShowSection(Tab.Dialer);
        _linphone.Call("**" + ext);
        EnqueueLog($"Перехват звонка: **{ext}");
    }

    private void OnFavViewListClick(object sender, RoutedEventArgs e) => FavoritesVm.GridView = false;
    private void OnFavViewGridClick(object sender, RoutedEventArgs e) => FavoritesVm.GridView = true;

    /// <summary>Перехват звонка коллеги (BLF): набирает **&lt;ext&gt; — feature-код Asterisk.</summary>
    private void OnBlfPickup(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.FavoriteRow r || !r.CanPickup) return;
        PickupCall(r.Number);
    }

    // Эмодзи/подпись presence — в ViewModels.PresenceUi (общее с избранным);
    // строка избранного — ViewModels.FavoriteRow.
    private static string StatusEmoji(PresenceStatus s) => ViewModels.PresenceUi.Emoji(s);
    private static string StatusLabel(PresenceStatus s) => ViewModels.PresenceUi.Label(s);

    /// <summary>Звезда в строке контакта: добавить/убрать из избранного.</summary>
    private void OnContactFavToggle(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ContactEntry c && c.CallNumber.Length > 0)
        {
            Favorites.Toggle(c.Name, c.CallNumber);
            ContactsVm.Refresh();   // пересобирает список → конвертер перечитает звёзды
        }
    }

    /// <summary>Звезда в строке журнала: добавить/убрать любой номер из избранного.</summary>
    /// <summary>Звезда в строке избранного: убрать из избранного.</summary>
    private void OnFavoriteRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.FavoriteRow r)
        {
            Favorites.Remove(r.Number);
            PopulateFavorites();
        }
    }

    private Tab _currentTab = Tab.Dialer;

    private void ShowSection(Tab tab)
    {
        _currentTab = tab;
        ApplyIncomingPresentation();   // звонящий телефон переезжает между полным экраном и баннером
        UpdateInCallHost();   // вкладка «Набор» показывает дайлер ИЛИ экран разговора
        ContactsSection.Visibility = tab == Tab.Contacts ? Visibility.Visible : Visibility.Collapsed;
        FavoritesSection.Visibility = tab == Tab.Favorites ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = tab == Tab.History ? Visibility.Visible : Visibility.Collapsed;
        ChatSection.Visibility = tab == Tab.Chat ? Visibility.Visible : Visibility.Collapsed;


        NavDial.Foreground = tab == Tab.Dialer ? ActiveTab : InactiveTab;
        NavContacts.Foreground = tab == Tab.Contacts ? ActiveTab : InactiveTab;
        NavFavorites.Foreground = tab == Tab.Favorites ? ActiveTab : InactiveTab;
        NavHistory.Foreground = tab == Tab.History ? ActiveTab : InactiveTab;
        NavChat.Foreground = tab == Tab.Chat ? ActiveTab : InactiveTab;
    }

    private bool _incoming;   // показывается полноэкранный экран входящего (не во время разговора)

    /// <summary>Есть входящий, на который ещё не ответили и не отклонили. Живёт дольше, чем
    /// <see cref="_incoming"/>: тот говорит лишь о полноэкранном экране, а звонок продолжает
    /// звонить и когда человек ушёл на другую вкладку и видит один баннер.</summary>
    private bool _incomingPending;

    /// <summary>Показать непринятый входящий там, где он сейчас уместен: полноэкранным экраном
    /// на вкладке «Набор» или баннером поверх любой другой. Зовётся при приходе звонка и при
    /// каждом переключении вкладок — раньше входящий насильно перебрасывал человека на «Набор»,
    /// и посмотреть карточку звонящего в контактах, не потеряв кнопки ответа, было нельзя.</summary>
    private void ApplyIncomingPresentation()
    {
        if (!_incomingPending) return;

        bool full = IncomingCallPolicy.FullScreenFits(_inCall, _currentTab == Tab.Dialer);
        IncomingBanner.Visibility = full ? Visibility.Collapsed : Visibility.Visible;

        // SetMode сам зовёт UpdateInCallHost — трогаем его только когда режим действительно
        // меняется, иначе на каждом переключении вкладки шла бы лишняя перерисовка.
        if (full != _incoming) SetMode(full ? Mode.Incoming : Mode.Dialer);
        else RefreshIncomingFlag();
    }

    /// <summary>На вкладке «Набор»: дайлер / полноэкранный входящий / полноэкранный разговор.</summary>
    private void UpdateInCallHost()
    {
        bool dialerTab = _currentTab == Tab.Dialer;
        InCallSection.Visibility = dialerTab && _inCall ? Visibility.Visible : Visibility.Collapsed;
        IncomingSection.Visibility = dialerTab && _incoming && !_inCall ? Visibility.Visible : Visibility.Collapsed;
        RefreshIncomingFlag();
        DialerSection.Visibility = dialerTab && !_inCall && !_incoming ? Visibility.Visible : Visibility.Collapsed;
        UpdateVideoOverlayVisibility();   // ушли с экрана разговора — спрятать оверлеи видео
    }

    /// <summary>Оверлеи видео видимы только вместе с экраном разговора и областью видео.</summary>
    private void UpdateVideoOverlayVisibility()
    {
        bool show = AppWindow.IsVisible && InCallSection.Visibility == Visibility.Visible
                    && VideoArea.Visibility == Visibility.Visible;
        _video.SetVisible(show);
    }

    /// <summary>Окно двигали/меняли размер/прятали — синхронизируем оверлеи видео.</summary>
    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender,
                                    Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange)
        {
            _video.UpdateBounds();
            ScheduleGeometrySave();   // окно двигали/тянули — запомним, где его оставили
        }
        if (args.DidVisibilityChange) UpdateVideoOverlayVisibility();
    }

    // ---------------- Меню шестерёнки → окно настроек ----------------
    private void OnAccountClick(object sender, RoutedEventArgs e) => OpenSettings("account");
    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings("devices");
    private void OnAboutClick(object sender, RoutedEventArgs e) => OpenSettings("about");
    private void OnExitClick(object sender, RoutedEventArgs e) => ExitApp();

    /// <summary>Перед открытием меню синхронизирует галочку «Не беспокоить» с текущим флагом.</summary>
    private void OnMainMenuOpening(object sender, object e)
    {
        MiDnd.IsChecked = _linphone.GetAppFlag("dnd", false);
    }

    /// <summary>Контекстные меню локализуем перед показом: они живут либо в ресурсах, либо
    /// внутри DataTemplate, и в визуальное дерево, которое обходит Tr.Apply, не попадают —
    /// поэтому при английском и китайском интерфейсе оставались русскими.</summary>
    private void OnLocalizeFlyout(object sender, object e) => Tr.ApplyToFlyout(sender);

    /// <summary>Тумблер «Не беспокоить» в меню = presence-статус 🚫 (одно состояние):
    /// включение ставит статус DND, выключение возвращает последний выбранный вручную.</summary>
    private void OnDndMenuToggled(object sender, RoutedEventArgs e) =>
        ApplyMyStatus(MiDnd.IsChecked ? PresenceStatus.DoNotDisturb : _statusBeforeDnd);

    private void OpenSettings(string tag)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_linphone, RefreshHeader, ApplyTheme, RefreshBehaviorIndicators,
                                                 () => CheckForUpdateAsync(manual: true));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.NavigateTo(tag);
        _settingsWindow.Activate();
    }

    /// <summary>Проставляет локализованные тексты (Tr.Key по дереву + пункты меню явно).</summary>
    private void ApplyLanguage()
    {
        Tr.Apply(Content);
        Tr.Set(MiAccount, "MenuMyAccount.Text");
        Tr.Set(MiDnd, "MenuDnd.Text");     // пункт меню оставался русским: его просто забыли
        Tr.Set(MiSettings, "MenuSettings.Text");
        Tr.Set(MiAbout, "MenuAbout.Text");
        Tr.Set(MiExit, "MenuExit.Text");
        Tr.Set(MiTrayOpen, "TrayOpen.Text");
        Tr.Set(MiTrayExit, "TrayExit.Text");
        HeaderVm.StatusText = Loc.S("HdrOpenSettings.Text");
        ApplyAutomationNames();
        RefreshHeader();
    }

    /// <summary>Имена для screen reader на кнопках-иконках (без них Narrator читает «кнопка»).</summary>
    private void ApplyAutomationNames()
    {
        var auto = new (UIElement El, string Key)[]
        {
            (CallButton, "St_CallAction"),
            (BackspaceButton, "St_Backspace"),
            (VideoButton, "LblVideo.Text"),
            (MuteButton, "LblMic.Text"),
            (HoldButton, "LblHold.Text"),
            (SwapButton, "LblSwap.Text"),
            (RecordButton, "LblRecord.Text"),
            (ConferenceButton, "LblConference.Text"),
            (KeypadButton, "LblKeypad.Text"),
            (TransferButton, "LblTransfer.Text"),
            (HangupButton, "LblHangup.Text"),
            (ChatSendButton, "St_SendMessage"),
        };
        foreach (var (el, key) in auto)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(el, Loc.S(key));
    }

    /// <summary>Применить тему оформления (system/light/dark) к окну.</summary>
    private void ApplyTheme(string theme)
    {
        if (Content is FrameworkElement fe)
            fe.RequestedTheme = theme switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
    }

    /// <summary>Обновляет шапку (имя и аватар) по текущему аккаунту и наполняет переключатель аккаунтов.</summary>
    private void RefreshHeader()
    {
        var info = _linphone.GetAccountInfo();
        if (info.HasValue && !string.IsNullOrEmpty(info.Value.Login))
        {
            var a = info.Value;
            HeaderVm.AccountName = string.IsNullOrEmpty(a.DisplayName) ? a.Login : a.DisplayName;
            HeaderVm.AvatarInitials = Initials(a.Login);
        }
        else
        {
            HeaderVm.AccountName = Loc.S("St_NotConnected");
            HeaderVm.AvatarInitials = "—";
        }
        BuildAccountSwitcher();
    }

    /// <summary>Наполняет выпадающий список переключения активного (основного) аккаунта.
    /// Если аккаунтов меньше двух — переключать нечего: только пункт «Мой аккаунт».</summary>
    private void BuildAccountSwitcher()
    {
        var accounts = _linphone.GetAccounts();
        AccountFlyout.Items.Clear();
        AccountChevron.Visibility = accounts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var a in accounts)
        {
            // Точка статуса перед именем — цвет по состоянию регистрации; галочка слева = основной.
            string dot = a.State == LinphoneRegistrationState.Ok ? "\U0001F7E2"
                       : a.State is LinphoneRegistrationState.Progress or LinphoneRegistrationState.Refreshing ? "\U0001F7E1"
                       : a.State == LinphoneRegistrationState.Failed ? "\U0001F534" : "⚪";
            string name = string.IsNullOrEmpty(a.DisplayName) ? a.SipAddress : $"{a.DisplayName} · {a.SipAddress}";
            var item = new ToggleMenuFlyoutItem { Text = $"{dot}  {name}", IsChecked = a.IsDefault };
            string id = a.SipAddress;
            item.Click += (_, _) => { _linphone.SetDefaultAccount(id); RefreshHeader(); };
            AccountFlyout.Items.Add(item);
        }

        if (accounts.Count > 0) AccountFlyout.Items.Add(new MenuFlyoutSeparator());
        var open = new MenuFlyoutItem { Text = Loc.S("MenuMyAccount.Text"), Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => OpenSettings("account");
        AccountFlyout.Items.Add(open);
    }

    /// <summary>Показывает в шапке значки активных режимов: «Не беспокоить» и «Автоответ».</summary>
    private void RefreshBehaviorIndicators()
    {
        // Парковку могли перенастроить в этом же окне настроек — перечитываем слоты и
        // переподписываемся, иначе пульт остался бы на старом диапазоне до перезапуска.
        ParkingVm.Reload();
        SyncBlfSubscriptions();
        StartLocalApi();   // API могли включить, выключить или сменить ему порт/токен
        _audio.RememberChoice();  // в настройках могли выбрать другую гарнитуру

        HeaderVm.DndActive = _linphone.GetAppFlag("dnd", false);
        HeaderVm.AutoAnswerActive = _linphone.GetAppFlag("auto_answer", false);
        ToolTipService.SetToolTip(DndBadge, Loc.S("St_DndActive"));
        ToolTipService.SetToolTip(AutoAnswerBadge, Loc.S("St_AutoAnswerActive"));
        // Подсказка у поля набора объясняет бледный номер: это последний набранный,
        // и кнопка вызова при пустом поле подставит именно его.
        ToolTipService.SetToolTip(NumberBox, Loc.S("St_LastDialed"));
    }

    // ---------------- Журнал ----------------
    private void PopulateHistory() => HistoryVm.Load();

    /// <summary>Ctrl+F — журнал и курсор в поиске. Оператор, которого просят «найди, кто звонил
    /// с этого номера», не должен искать мышью сначала вкладку, потом поле.</summary>
    private void OnFindAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
                                   Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (HistorySection.Visibility != Visibility.Visible) OnNavHistoryClick(sender, new RoutedEventArgs());
        // Через очередь: секцию только что показали, и до следующего прохода компоновки
        // поле ввода фокус не принимает — курсор молча оставался бы в наборе номера.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => HistorySearch.Focus(FocusState.Programmatic));
    }

    /// <summary>Ctrl+1..5 — вкладки в том же порядке, в каком они нарисованы внизу.
    /// Во время разговора переключение НЕ трогает экран звонка: он живёт на вкладке набора,
    /// и уводить с него человека посреди разговора нажатием Ctrl+1 было бы сюрпризом.</summary>
    private void OnTabAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
                                  Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var e = new RoutedEventArgs();
        switch (sender.Key)
        {
            case Windows.System.VirtualKey.Number1: OnNavDialClick(this, e); break;
            case Windows.System.VirtualKey.Number2: OnNavContactsClick(this, e); break;
            case Windows.System.VirtualKey.Number3: OnNavFavoritesClick(this, e); break;
            case Windows.System.VirtualKey.Number4: OnNavHistoryClick(this, e); break;
            case Windows.System.VirtualKey.Number5: OnNavChatClick(this, e); break;
        }
    }

    private void OnHistorySearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Реагируем на ЛЮБОЕ изменение текста, а не только на Reason == UserInput: нажатие
        // крестика очистки таким «вводом» не считается, и список остался бы отфильтрованным
        // при пустом поле поиска. Лишней работы нет — сеттер Query сравнивает значения сам.
        HistoryVm.Query = sender.Text;
    }

    private void OnHistoryFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag
            && Enum.TryParse(tag, out ViewModels.HistoryFilter filter))
            HistoryVm.Filter = filter;
    }

    private void OnHistoryItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CallLogEntry entry) PrefillDialer(entry.Remote);
    }

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        // Необратимое действие одним кликом — обязательно спрашиваем.
        var dialog = new ContentDialog
        {
            Title = Loc.S("St_ClearHistoryTitle"),
            Content = Loc.S("St_ClearHistoryText"),
            PrimaryButtonText = Loc.S("BtnClearHistory.Content"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        HistoryVm.Clear();
        DialerVm.LastDialed = "";   // журнала нет — повторять нечего, кнопка уходит
        UpdateMissedBadge();
    }

    // ---------------- Чат (список переписок + переписка) ----------------
    private void OnNavChatClick(object sender, RoutedEventArgs e)
    {
        ShowSection(Tab.Chat);
        if (ChatVm.HasOpenConversation) { ChatVm.PopulateConversations(); ChatVm.MarkCurrentRead(); }
        else ChatVm.ShowList();
    }

    private void OnConversationClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ViewModels.ConversationRow r) ChatVm.OpenConversation(r.Peer);
    }

    private void OnChatBackClick(object sender, RoutedEventArgs e) => ChatVm.ShowList();

    /// <summary>Контекстное меню переписки: удалить целиком. Необратимо (история сотрётся) —
    /// спрашиваем. Заодно это способ убрать дубль-комнату со «застрявшими» непрочитанными.</summary>
    private async void OnConversationDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.ConversationRow r) return;
        var dialog = new ContentDialog
        {
            Title = Loc.S("St_DeleteChatTitle"),
            Content = Loc.F("St_DeleteChatText", r.Title),
            PrimaryButtonText = Loc.S("Ct_Delete"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        ChatVm.DeleteConversation(r.Peer);
    }

    private void OnStartNewChatClick(object sender, RoutedEventArgs e)
    {
        string peer = NewChatPeerBox.Text.Trim();
        if (peer.Length == 0) return;
        NewChatPeerBox.Text = "";
        ChatVm.OpenConversation(peer);
    }

    /// <summary>Обновляет бейдж непрочитанных на вкладке «Чат» (данные — из ViewModel) и трей.</summary>
    private void UpdateChatBadge()
    {
        int n = ChatVm.Unread;
        ChatBadgeText.Text = n > 99 ? "99+" : n.ToString();
        ChatBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrayIndicator();
    }

    private void OnChatSendClick(object sender, RoutedEventArgs e)
    {
        if (ChatVm.Send(ChatInput.Text)) ChatInput.Text = "";
        else if (ChatInput.Text.Trim().Length > 0) EnqueueLog("Не удалось отправить сообщение.");
    }

    private void OnChatMessageReceived(string fromPeer, string text)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            bool onScreen = AppWindow.IsVisible && ChatSection.Visibility == Visibility.Visible;
            bool shown = ChatVm.HandleIncoming(fromPeer, text, onScreen);
            if (!shown)
            {
                EnqueueLog($"Чат: сообщение от {fromPeer}"); // текст переписки в лог не пишем
                ShowChatToast(fromPeer, DisplayName(fromPeer), text);
            }
        });
    }

    private void ScrollChatToEnd()
    {
        if (ChatVm.Messages.Count > 0) ChatList.ScrollIntoView(ChatVm.Messages[^1]);
    }

    // ---------------- Звонок ----------------
    private void OnCallClick(object sender, RoutedEventArgs e)
    {
        if (DialerVm.Number.Trim().Length == 0)
        {
            // Пустое поле, но есть последний набранный — подставляем его, как на телефоне:
            // первое нажатие показывает номер, второе звонит. Так повтор делается кнопкой
            // вызова, а не отдельной клавишей, и промах не приводит к неожиданному звонку.
            if (DialerVm.LastDialed.Length > 0)
            {
                PrefillDialer(DialerVm.LastDialed);
                return;
            }
            // Звонить нечему и повторять нечего: раньше единственной реакцией была строка
            // в app.log — то есть для пользователя кнопка просто не работала. Лог не интерфейс.
            ShowDialerHint(Loc.S("St_EnterNumber"));
            NumberBox.Focus(FocusState.Programmatic);
            return;
        }
        DialerVm.Call();
    }

    // Строка над зелёной кнопкой одна на два назначения: обычно там последний набранный
    // номер (клик по нему = повтор), а на несколько секунд её перебивает сообщение —
    // причина неудавшегося звонка или подсказка про пустое поле.
    private string? _dialerHint;

    /// <summary>Показать временное сообщение над кнопкой вызова. Пустая строка — снять
    /// сообщение и вернуть последний набранный номер.</summary>
    private void ShowDialerHint(string text)
    {
        _dialerHint = text.Length == 0 ? null : text;
        _reasonTimer?.Stop();
        if (_dialerHint != null)
        {
            _reasonTimer ??= CreateReasonTimer();
            _reasonTimer.Start();
        }
        UpdateDialerLine();
    }

    /// <summary>Перерисовать подсказку в поле набора: сообщение важнее номера.
    /// Подсказка видна только при пустом поле — ровно тогда, когда она и нужна,
    /// а место под неё есть всегда, поэтому ничего на экране не двигается.</summary>
    private void UpdateDialerLine()
    {
        bool isHint = _dialerHint is { Length: > 0 };
        NumberBox.PlaceholderText = isHint ? _dialerHint!
            : _dialerPlaceholderDismissed ? "" : DialerVm.LastDialedDisplay;
        NumberBox.PlaceholderForeground = (Brush)Application.Current.Resources[
            isHint ? "SystemFillColorCriticalBrush" : "TextFillColorTertiaryBrush"];
    }

    /// <summary>Клик по пустому месту экрана набора снимает фокус с поля номера.
    /// Иначе нажатие мимо всего оставляло в поле мигающий курсор — вид, будто туда сейчас
    /// будут печатать, хотя пользователь просто щёлкнул по фону. Клик по самому полю
    /// (и по чему угодно внутри него) обрабатывается как обычно.</summary>
    private void OnDialerBackgroundPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsInsideNumberBox(src)) return;
        DialerSection.Focus(FocusState.Pointer);
    }

    /// <summary>Принадлежит ли элемент полю ввода номера (сам TextBox или часть его шаблона).</summary>
    private bool IsInsideNumberBox(DependencyObject? node)
    {
        for (int depth = 0; node != null && depth < 32; depth++)
        {
            if (ReferenceEquals(node, NumberBox)) return true;
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>Enter в поле номера — позвонить (как кнопка вызова).</summary>
    private void OnNumberBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        OnCallClick(sender, e);
    }

    /// <summary>Enter в поле сообщения — отправить (как кнопка отправки).</summary>
    private void OnChatInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        OnChatSendClick(sender, e);
    }

    // ---------------- Глобальные горячие клавиши ----------------
    // Дедупликация событий хука: StreamsRunning приходит и после удержания, End и Released —
    // парой. CRM должна получить по одному событию на звонок, а не журнал состояний.
    private bool _hookAnswered;
    private bool _hookLive;

    private void NotifyCallHook(CallHookEvent kind, Interop.CallEvent e)
    {
        if (kind is CallHookEvent.Incoming or CallHookEvent.Outgoing) _hookLive = true;
        // Именно SipUser, а не Pretty: тот оставляет домен («*43@sip.example.com»), и CRM,
        // которая ищет клиента по номеру, не найдёт ничего. Проверено на живом звонке.
        string number = PhoneNumberUtils.SipUser(e.Remote);
        _callHook?.Notify(new CallHookData(kind, number, DisplayName(e.Remote ?? ""),
                                           e.DurationSeconds, e.CallId ?? ""));
    }

    // Аудиомаршрутизация (выбор устройств при подключении/отключении гарнитуры) вынесена
    // в AudioRoutingController: три её нетривиальных правила — эхо собственного перечитывания,
    // молчащий Bluetooth и неисчезающее устройство «по умолчанию» — не проверялись ничем,
    // потому что жили внутри окна.
    private readonly AudioRoutingController _audio;

    private int _levelTicks;


    /// <summary>Пересчитать признак «есть входящий». Зовётся только из потока интерфейса —
    /// там, где меняется видимость входящей секции или баннера.</summary>
    private void RefreshIncomingFlag() =>
        _incomingActive = IncomingSection.Visibility == Visibility.Visible
                       || IncomingBanner.Visibility == Visibility.Visible;

    /// <summary>Поднять локальный API, если он включён в настройках. Порт и токен — оттуда же;
    /// без токена не запускаем вовсе (см. LocalApiServer).</summary>
    private string _apiConfig = "";   // последние применённые настройки API (флаг+порт+токен)

    private void StartLocalApi()
    {
        bool enabled = _linphone.GetAppFlag("api_enabled", false);
        string token = _linphone.GetApiToken();   // из DPAPI-хранилища (см. LinphoneCore.GetApiToken)
        string config = $"{enabled}|{_linphone.GetAppString("api_port", "")}|{token}";
        // Перезапуск сервера рвёт открытые соединения, а RefreshBehaviorIndicators зовут
        // и при старте, и после каждого закрытия настроек: без этой проверки API поднимался
        // трижды подряд на ровном месте.
        if (config == _apiConfig && (_api != null) == enabled) return;
        _apiConfig = config;

        _api?.Dispose();
        _api = null;
        if (!enabled) return;

        if (!int.TryParse(_linphone.GetAppString("api_port", ""), out int port) || port is < 1 or > 65535)
            port = LocalApi.LocalApiServer.DefaultPort;
        if (token.Length == 0)
        {
            // Генерировать можно ТОЛЬКО у живого ядра. Иначе так: флаг «API включён» пришёл из
            // кэша (его читали, когда ядро было живо), а токен вернулся пустым, потому что
            // ядро уже не отвечает, — и мы бы выдали новый токен взамен рабочего. Интеграция
            // после этого перестаёт работать молча: в CRM прописан старый токен.
            if (!_linphone.CoreReady)
            {
                EnqueueLog("Локальный API: ядро не отвечает — токен не читается, API не поднимаю");
                _apiConfig = "";   // не запоминаем это состояние: при живом ядре надо попробовать снова
                return;
            }
            // Первый запуск с включённым API: генерируем токен сами, иначе пользователю
            // пришлось бы придумывать его руками, а придуманные руками секреты — это «1234».
            token = LocalApi.LocalApiServer.NewToken();
            _linphone.SetApiToken(token);
        }

        var commands = new LocalApi.SipCallCommands(
            calls: _linphone, account: _linphone,
            hasIncoming: () => _incomingActive,
            isRegistered: () => _regOnline,
            log: EnqueueLog);
        _api = new LocalApi.LocalApiServer(commands, EnqueueLog);
        _api.Start(port, token);
    }

    /// <summary>Подписаться на WM_HOTKEY и, если клавиши включены, занять сочетания.
    /// Подписка нужна и при выключенных: тогда включить их можно будет прямо из настроек,
    /// без перезапуска.</summary>
    private void RegisterHotkeys(bool enabled)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Interop.GlobalHotkeys.Attach(hwnd);
        Interop.GlobalHotkeys.Pressed += OnHotkey;
        if (!enabled) return;

        var failed = Interop.GlobalHotkeys.Rebind(Interop.GlobalHotkeys.FromConfig(_linphone));
        // Занятую другой программой комбинацию нельзя оставлять без объяснения: пользователь
        // будет жать её и считать, что сломался софтфон.
        if (failed.Count > 0) EnqueueLog("Горячие клавиши заняты другой программой: " + string.Join(", ", failed));
    }

    /// <summary>Действие горячей клавиши. Смысл зависит от состояния: «Ответить» на входящем
    /// принимает звонок, а вне звонка не делает ничего — глобальная клавиша не должна
    /// совершать неожиданных действий, когда пользователь про неё забыл.</summary>
    private void OnHotkey(Interop.HotkeyAction action)
    {
        bool incoming = IncomingSection.Visibility == Visibility.Visible
                     || IncomingBanner.Visibility == Visibility.Visible;
        switch (action)
        {
            case Interop.HotkeyAction.Answer when incoming:
                // Окно НЕ выдёргиваем: горячей клавишей отвечают как раз чтобы не отрываться
                // от своей работы. Кто на линии — видно в мини-окне.
                _linphone.AcceptIncoming();
                break;
            case Interop.HotkeyAction.Hangup when incoming:
                _linphone.DeclineIncoming();
                break;
            case Interop.HotkeyAction.Hangup when _inCall:
                _linphone.Hangup();
                break;
            case Interop.HotkeyAction.Mute when _inCall:
                _linphone.ToggleMute();   // индикация — по событию от ядра
                break;
        }
    }

    private void OnHangupClick(object sender, RoutedEventArgs e) => _linphone.Hangup();
    private void OnAcceptClick(object sender, RoutedEventArgs e) => _linphone.AcceptIncoming();
    private void OnDeclineClick(object sender, RoutedEventArgs e) => _linphone.DeclineIncoming();

    private void OnDigitClick(object sender, RoutedEventArgs e)
    {
        string digit = (string)((Button)sender).Tag;
        if (_inCall)
            SendDtmfDigit(digit[0]);
        else
            DialerVm.AppendDigit(digit);
    }

    private void SendDtmfDigit(char digit)
    {
        _linphone.SendDtmf(digit);
        _linphone.PlayLocalDtmf(digit);   // слышимый «бип» (если есть в SDK)
        // Обратная связь: показываем набранные цифры (видно, что и сколько нажал).
        CallVm.AppendDtmf(digit);
    }

    /// <summary>Горячие клавиши окна: Esc = отклонить входящий / сбросить разговор,
    /// Ctrl+M = микрофон, цифры и * = DTMF во время разговора.
    /// Не срабатывают, когда фокус в текстовом поле (номер, чат, поиск).</summary>
    private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or PasswordBox)
            return;

        var key = e.Key;
        if (key == Windows.System.VirtualKey.Escape)
        {
            // Сначала звонящий (полный экран или баннер второго входящего), потом активный разговор.
            if (IncomingSection.Visibility == Visibility.Visible || IncomingBanner.Visibility == Visibility.Visible)
                { _linphone.DeclineIncoming(); e.Handled = true; }
            else if (_inCall) { _linphone.Hangup(); e.Handled = true; }
            return;
        }

        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && key == Windows.System.VirtualKey.M && _inCall)
        {
            OnMuteClick(sender, e);   // тот же путь, что кнопка — обновляет подпись
            e.Handled = true;
            return;
        }
        if (ctrl && key == Windows.System.VirtualKey.V && !_inCall)
        {
            e.Handled = true;
            PasteNumberFromClipboard();   // вставить номер из буфера в дайлер
            return;
        }

        if (!_inCall || ctrl) return;
        char digit = key switch
        {
            >= Windows.System.VirtualKey.Number0 and <= Windows.System.VirtualKey.Number9
                => (char)('0' + (key - Windows.System.VirtualKey.Number0)),
            >= Windows.System.VirtualKey.NumberPad0 and <= Windows.System.VirtualKey.NumberPad9
                => (char)('0' + (key - Windows.System.VirtualKey.NumberPad0)),
            Windows.System.VirtualKey.Multiply => '*',
            _ => '\0',
        };
        if (digit != '\0') { SendDtmfDigit(digit); e.Handled = true; }
    }

    /// <summary>Ctrl+V вне текстовых полей: номер из буфера обмена — в поле набора
    /// (очищенный от скобок/дефисов/пробелов; sip:-адреса как есть).</summary>
    private async void PasteNumberFromClipboard()
    {
        try
        {
            var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            string target = NormalizeDialTarget(await data.GetTextAsync());
            if (target.Length == 0) return;
            ShowSection(Tab.Dialer);
            DialerVm.Number = target;
        }
        catch { /* буфер занят другим приложением — не критично */ }
    }

    private void ResetDtmf() => CallVm.ResetDtmf();

    private void OnBackspaceClick(object sender, RoutedEventArgs e)
    {
        DialerVm.Backspace();
        // Поле опустошили СТИРАНИЕМ — значит от номера избавились намеренно. Подсказка с
        // последним набранным после этого не возвращается: иначе стёртый номер тут же
        // всплывал бы снова, и стереть его «до конца» было бы невозможно.
        if (DialerVm.Number.Length == 0) { _dialerPlaceholderDismissed = true; UpdateDialerLine(); }
    }

    // Пользователь сам очистил поле — не подсказываем последний номер до следующего звонка.
    private bool _dialerPlaceholderDismissed;

    // Повтор последнего набранного делает кнопка вызова при пустом поле (см. OnCallClick):
    // номер уже лежит в DialerVm — он ставится при старте и на каждом исходящем, поэтому
    // повтор не ходит в ядро и не разбирает журнал ради одного номера.

    // Нормализация номеров/адресов — в PhoneNumberUtils (единый источник). Локальные псевдонимы
    // оставлены, чтобы не переписывать десятки коротких call sites.
    private static string DigitsOnly(string? s) => PhoneNumberUtils.DigitsOnly(s);
    private static string SipUser(string? address) => PhoneNumberUtils.SipUser(address);

    // Поиск контакта по номеру идёт ПО ИНДЕКСУ, а не перебором.
    //
    // Раньше здесь был FirstOrDefault с DigitsOnly на каждом контакте: одна новая строка на
    // каждый номер каждого контакта при КАЖДОМ поиске. А ищем мы часто — имя собеседника
    // нужно на каждое событие звонка, для CRM-хука и для каждой строки списка переписок.
    // На книге в две тысячи человек открытие вкладки «Чат» с полусотней переписок стоило
    // сотен тысяч сравнений и ровно стольких же выделений памяти.
    private Dictionary<string, ContactEntry> _contactsByDigits = new();

    /// <summary>Заменить контакты и перестроить индекс. Единственное место, где меняется
    /// <see cref="_allContacts"/>, — иначе индекс и список неминуемо разъедутся.</summary>
    private void SetContacts(List<ContactEntry> contacts)
    {
        _allContacts = contacts;
        var index = new Dictionary<string, ContactEntry>(contacts.Count * 2);
        foreach (var c in contacts)
        {
            // Берём поля напрямую, а не через AllNumbers: тот на каждый номер спрашивает
            // подпись у ресурсов, и на построении индекса это тысячи лишних обращений.
            Add(c.Number);
            Add(c.Mobile);
            foreach (var x in c.Extra) Add(x.Number);

            void Add(string? number)
            {
                string key = DigitsOnly(number);
                // Один номер у двух людей (общий кабинетный) — оставляем первого: список
                // отсортирован по имени, значит выбор стабилен от запуска к запуску.
                if (key.Length > 0) index.TryAdd(key, c);
            }
        }
        _contactsByDigits = index;
    }

    /// <summary>Ищет контакт по совпадению цифр ЛЮБОГО его номера. Понимает SIP-адрес.
    /// Раньше сверялись только основной и мобильный, и человек с тремя номерами не находился
    /// по третьему — при том что поиск во вкладке «Контакты» его находил.</summary>
    private ContactEntry? FindContact(string number)
    {
        string n = DigitsOnly(SipUser(number));
        return n.Length > 0 && _contactsByDigits.TryGetValue(n, out var c) ? c : null;
    }

    /// <summary>Имя контакта по адресу; если в книге нет — чистый номер (без sip:/скобок/имени).</summary>
    private string DisplayName(string remote)
    {
        if (FindContact(remote)?.Name is { Length: > 0 } name) return name;
        string user = SipUser(remote);
        return user.Length > 0 ? user : Pretty(remote);
    }

    /// <summary>Кнопка «Микрофон» только ОТПРАВЛЯЕТ команду. Состояние рисует обработчик
    /// события от ядра (подписка на MicMuteChanged в конструкторе): ждать ответа ядра на
    /// клике нельзя, а рисовать «как должно было получиться» — значит однажды соврать
    /// в единственном вопросе, где врать нельзя: слышно меня или нет.</summary>
    private void OnMuteClick(object sender, RoutedEventArgs e) => _linphone.ToggleMute();

    /// <summary>Индикация выключенного микрофона: перечёркнутая иконка на кнопке
    /// и красный бейдж «Без звука» в шапке (как у DND) — чтобы не забыть про мьют.</summary>
    private void UpdateMuteVisuals(bool muted)
    {
        _muted = muted;   // мини-окно берёт состояние отсюда
        MuteLabel.Text = VideoMuteLabel.Text = muted ? Loc.S("St_Unmute") : Loc.S("St_Mic");
        MuteIcon.Glyph = VideoMuteIcon.Glyph = muted ? "" : "";   // микрофон перечёркнутый / обычный
        HeaderVm.Muted = muted;   // бейдж «Без звука» в шапке
        CallVm.MicMuted = muted;  // полоска уровня: выключенный микрофон красный, а не «тихий»
        RefreshMiniWindow();   // и то же самое в мини-окне
    }

    private void OnHoldClick(object sender, RoutedEventArgs e) => _linphone.ToggleHold();

    /// <summary>Подпись кнопки удержания — по РЕАЛЬНОМУ состоянию звонка, пришедшему из ядра.
    /// Сервер вправе отказать в удержании (488 на re-INVITE), и тогда разговор продолжается;
    /// раньше кнопка в этом случае показывала «Возобновить», то есть обещала, что собеседник
    /// не слышит, — а он слышал всё.</summary>
    private void SetHoldVisuals(bool onHold)
    {
        if (_onHold == onHold) return;
        _onHold = onHold;   // мини-окно берёт состояние отсюда
        HoldLabel.Text = onHold ? Loc.S("St_Resume") : Loc.S("St_Hold");
        RefreshMiniWindow();
    }

    private void OnSwapClick(object sender, RoutedEventArgs e) => _linphone.SwapCalls();

    private bool _confInvitePending;   // пригласили третьего кнопкой — объединить, как только ответит

    private async void OnConferenceClick(object sender, RoutedEventArgs e)
    {
        // Два звонка уже есть — просто объединяем.
        if (_linphone.CallCount > 1)
        {
            if (_linphone.MergeCallsIntoConference() == CommandOutcome.Done)
            {
                EnqueueLog("Конференция: звонки объединены");
                CallVm.RemoteName = Loc.S("St_Conference");
                CallVm.StatusText = Loc.S("St_InCall");
                UpdateSwapButton();
            }
            else
            {
                EnqueueLog("Не удалось создать конференцию");
                ShowDialerHint(Loc.S("St_ConferenceFailed"));
            }
            return;
        }

        // Один разговор — приглашаем третьего: набираем его (текущий встанет на удержание),
        // после ответа звонки объединяются автоматически (см. StreamsRunning).
        var input = new TextBox { PlaceholderText = Loc.S("St_NumberOrSip") };
        var dialog = new ContentDialog
        {
            Title = Loc.S("St_InviteTitle"),
            Content = input,
            PrimaryButtonText = Loc.S("St_CallAction"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        string target = input.Text.Trim();
        if (target.Length == 0) return;
        _confInvitePending = true;
        _linphone.HoldCurrent();
        _linphone.Call(target);
        EnqueueLog($"Конференция: приглашаю {target}");
    }

    /// <summary>Кнопка «Запись» только ОТПРАВЛЯЕТ команду. Состояние рисует обработчик события
    /// от ядра (подписка на RecordingChanged в конструкторе) — по той же причине, что у
    /// микрофона: по просрочке кнопка гасла бы при идущей записи, а для продукта, который
    /// пишет разговоры, значение имеет файл, а не кнопка.</summary>
    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        // Писать нечего — говорим сразу, не дожидаясь ядра: молча не сработавшая кнопка
        // не даёт понять, пишется разговор или нет.
        if (!_linphone.IsRecording && _linphone.CallCount == 0)
        {
            ShowDialerHint(Loc.S("St_RecordFailed"));
            return;
        }
        _linphone.ToggleRecording();
    }

    /// <summary>Состояние записи пришло от ядра — рисуем по факту, а не по намерению.</summary>
    private void OnRecordingChanged(bool recording)
    {
        UpdateRecordButton(recording);
        if (recording) EnqueueLog("Запись разговора: " + (_linphone.RecordPath ?? ""));
        RefreshMiniWindow();
    }

    private void UpdateRecordButton(bool recording)
    {
        RecordDot.Fill = Palette(recording ? "LpDangerBrush" : "LpNeutralBrush");
        RecordLabel.Text = Loc.S(recording ? "St_RecordStop" : "St_Record");
    }

    /// <summary>«Переключить» — при двух необъединённых звонках. Кнопка конференции видна всегда,
    /// кроме готовой конференции: при одном звонке это «Пригласить», при двух — «Конференция».</summary>
    private void UpdateSwapButton()
    {
        bool conf = _linphone.IsInConference;
        bool multi = _linphone.CallCount > 1;
        if (!multi) _attendedPending = false;   // перевод с сопровождением кончился
        UpdateAttendedHint();
        SwapStack.Visibility = multi && !conf ? Visibility.Visible : Visibility.Collapsed;
        ConnectStack.Visibility = multi && !conf ? Visibility.Visible : Visibility.Collapsed;
        // «Парковка» и «Соединить» делят одну ячейку: при двух звонках важнее соединить
        // собеседников, при одном — есть что парковать.
        ParkStack.Visibility = ParkingVm.Enabled && !(multi && !conf)
            ? Visibility.Visible : Visibility.Collapsed;
        ConferenceStack.Visibility = conf ? Visibility.Collapsed : Visibility.Visible;
        ConferenceLabel.Text = Loc.S(multi ? "LblConference.Text" : "LblInvite.Text");
    }

    // ---------------- Перевод звонка ----------------
    // Панель на экране звонка вместо модального окна: разговор остаётся перед глазами,
    // коллеги видны со своими лампами занятости, а перевод с сопровождением стал таким же
    // равноправным действием, как прямой, — раньше он прятался вторичной кнопкой диалога.

    private void OnTransferClick(object sender, RoutedEventArgs e) => ShowTransferPanel(true);

    private void OnTransferCancelClick(object sender, RoutedEventArgs e) => ShowTransferPanel(false);

    private void ShowTransferPanel(bool show)
    {
        _screen = show ? InCallScreen.Transfer : InCallScreen.Buttons;
        ApplyInCallScreen();
        if (!show) return;
        TransferManualButtons.Visibility = Visibility.Collapsed;
        TransferInput.Text = "";
        TransferInput.ItemsSource = null;
        TransferHeader.Text = Loc.F("St_TransferWhom", CallVm.RemoteName);
        FavoritesVm.Load();                                // лампы могли измениться с прошлого раза
        RefreshTransferTargets("");
        // Через очередь: панель только что показали, до следующей компоновки фокус не берётся.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => TransferInput.Focus(FocusState.Programmatic));
    }

    private void OnTransferInputChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        string q = sender.Text.Trim();
        // Кнопки внизу — для набранного вручную. Пока поле пусто, действия живут в строках
        // списка, и дублировать их снизу незачем.
        TransferManualButtons.Visibility = q.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshTransferTargets(q);
    }

    /// <summary>Один список на два случая: пусто в поле — избранные с лампами, есть ввод —
    /// совпадения из всей книги. Раньше это были две независимые выборки (список показывал
    /// избранное, а поле выпадало своими подсказками), и на экране жили два разных набора
    /// людей без понятного главного.</summary>
    private void RefreshTransferTargets(string query) =>
        TransferVm.Update(FavoritesVm.Favorites, _allContacts, query, Loc.S);

    /// <summary>Enter в поле — перевести. НО выбор строки из выпадающих подсказок переводом
    /// не считается: AutoSuggestBox поднимает QuerySubmitted и в этом случае тоже, и клик по
    /// найденному контакту молча отправлял собеседника — притом что человек всего лишь
    /// выбирал, кому звонить. Подсказку подставляем в поле и ждём явного действия.</summary>
    private void OnTransferInputSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion != null)
        {
            sender.Text = SuggestionNumber(args.ChosenSuggestion);
            sender.ItemsSource = null;      // список подсказок больше не нужен
            return;
        }
        DoTransfer(TransferTarget());
    }

    /// <summary>Из подсказки «Имя · номер» берём номер — чтобы человек видел в поле, что
    /// именно уйдёт.</summary>
    private static string SuggestionNumber(object suggestion) =>
        NumberFromLabel(suggestion as string ?? suggestion?.ToString() ?? "");

    /// <summary>Разбор строки «Имя · номер». ОДНА реализация на два места: их было две
    /// (здесь и в TransferTarget), и разъехаться им мешала только удача.</summary>
    internal static string NumberFromLabel(string text)
    {
        int sep = text.LastIndexOf(" · ", StringComparison.Ordinal);
        return (sep >= 0 ? text[(sep + 3)..] : text).Trim();
    }

    /// <summary>Клик по коллеге — подставить в поле. Сам перевод отдельной кнопкой: промах
    /// мышью не должен молча отправить собеседника неизвестно куда.</summary>
    /// <summary>Клик по строке подставляет номер в поле — на случай, если человек хочет
    /// сперва посмотреть на него. Сами действия — в двух значках справа.</summary>
    private void OnTransferTargetClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ViewModels.TransferTargetRow row) return;
        TransferInput.Text = row.Number;
        TransferManualButtons.Visibility = Visibility.Visible;
    }

    /// <summary>Второй значок в строке — сначала поговорить с коллегой. Как и стрелка,
    /// без подтверждения: ткнуть в конкретного человека — уже решение.</summary>
    private void OnTransferAskRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.TransferTargetRow row)
            StartAttendedTransfer(row.Number);
    }

    /// <summary>Стрелка в строке коллеги — перевести сразу, без подтверждения.</summary>
    private void OnTransferNowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.TransferTargetRow row) DoTransfer(row.Number);
    }

    private void OnTransferDoClick(object sender, RoutedEventArgs e) => DoTransfer(TransferTarget());

    private void OnTransferAskClick(object sender, RoutedEventArgs e) => StartAttendedTransfer(TransferTarget());

    /// <summary>«Спросить»: звоним коллеге, оставив собеседника на удержании. Соединит их
    /// потом кнопка «Соединить» — она появится, как только звонков станет два.</summary>
    private void StartAttendedTransfer(string target)
    {
        if (target.Length == 0) return;
        ShowTransferPanel(false);
        _lastTransferTarget = target;
        _attendedPending = true;      // ждём ответа коллеги, чтобы подсказать про «Соединить»
        _linphone.HoldCurrent();
        _linphone.Call(target);
        EnqueueLog($"Перевод с сопровождением: звоню {target}");
    }

    /// <summary>Что показано в середине экрана разговора. До появления этого поля видимость
    /// панелей выставлялась из пяти разных мест, и они спорили друг с другом: при переводе
    /// с консультацией сетка кнопок успевала мигнуть, а потом вылезала поверх двух нужных.
    /// Теперь состояние одно, и решает его единственная функция.</summary>
    private enum InCallScreen { Buttons, Keypad, Transfer }

    private InCallScreen _screen = InCallScreen.Buttons;

    /// <summary>Видеорежим: сетку кнопок прячет видео, а не состояние экрана.</summary>
    private bool _videoMode;

    /// <summary>Показать то, что соответствует состоянию. Консультация перебивает всё:
    /// пока идёт разговор с коллегой, решений всего два, и остальное только мешает.</summary>
    private void ApplyInCallScreen()
    {
        bool attended = _attendedPending && _linphone.CallCount > 1;

        AttendedButtons.Visibility = attended ? Visibility.Visible : Visibility.Collapsed;
        AttendedHint.Visibility = attended ? Visibility.Visible : Visibility.Collapsed;

        InCallButtons.Visibility = !attended && !_videoMode && _screen == InCallScreen.Buttons
            ? Visibility.Visible : Visibility.Collapsed;
        InCallKeypad.Visibility = !attended && _screen == InCallScreen.Keypad
            ? Visibility.Visible : Visibility.Collapsed;
        TransferPanel.Visibility = !attended && _screen == InCallScreen.Transfer
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Идёт перевод с сопровождением: собеседник на удержании, звоним коллеге.
    /// Пока флаг стоит, на экране висит подсказка про «Соединить» — без неё человек
    /// доходил до конца разговора с коллегой и клал трубку, а звонок возвращался обратно,
    /// потому что положить трубку в этом месте означает «передумал», а не «переведи».</summary>
    private bool _attendedPending;

    private void UpdateAttendedHint()
    {
        ApplyInCallScreen();

        // Подсвечиваем ЗНАЧОК, а не подменяем стиль кнопки. Подмена стиля стоила падения:
        // CallRoundBtn объявлен в ресурсах ОКНА, а не приложения, и обращение к нему через
        // Application.Current.Resources бросало исключение прямо в потоке интерфейса —
        // приложение падало на первом же ответе собеседника.
        bool show = _attendedPending && _linphone.CallCount > 1;
        if (ConnectIcon != null)
            ConnectIcon.Foreground = show ? Palette("LpGreenBrush")
                                          : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    /// <summary>Припарковать текущий звонок: слепой перевод на parkext. АТС кладёт звонок
    /// в первый свободный слот и объявляет его номер — дальше забрать может любой.</summary>
    private void OnParkClick(object sender, RoutedEventArgs e)
    {
        string ext = ParkingVm.ParkExtension;
        if (ext.Length == 0) return;
        // Кого паркуем — знаем только мы: АТС в NOTIFY о слоте присылает лишь «занят».
        ParkingVm.NoteParking(CallVm.RemoteName);
        _lastTransferWasPark = true;
        var outcome = _linphone.Transfer(ext);
        if (outcome == CommandOutcome.Done) { EnqueueLog($"Парковка: перевожу звонок на {ext}"); return; }
        // REFER не ушёл — итога от ядра не будет, флаг парковки надо снять здесь, иначе
        // его съест следующий (чужой) перевод.
        _lastTransferWasPark = false;
        EnqueueLog(outcome == CommandOutcome.Rejected
            ? "Не удалось припарковать звонок"
            : "Парковка: ядро не ответило — состояние неизвестно");
        ShowDialerHint(Loc.S(outcome == CommandOutcome.Rejected ? "St_ParkFailed" : "St_CoreBusy"));
    }

    /// <summary>Забрать звонок со стоянки — обычный вызов номера слота.</summary>
    private void OnParkedPickupClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string slot || slot.Length == 0) return;
        EnqueueLog($"Парковка: забираю звонок из слота {slot}");
        ShowSection(Tab.Dialer);
        _linphone.Call(slot);
    }

    // ---------------- Перевод перетаскиванием на коллегу ----------------
    // Пульт BLF показывает, кто свободен, а кто говорит. Перевод перетаскиванием превращает
    // это знание в одно движение: видишь зелёную лампу — тянешь туда звонок. Всё то же
    // делается и через панель перевода; это способ для тех, кто живёт на пульте.

    /// <summary>Метка в буфере перетаскивания. Проверяем её при сбросе, чтобы не принять за
    /// перевод случайно перетащенный откуда-то текст.</summary>
    private const string DragCallMarker = "lphone:call";

    private void OnCallChipDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        EnqueueLog($"drag: DragStarting (звонок={_inCall})");
        if (!_inCall) { args.Cancel = true; return; }
        args.Data.SetText(DragCallMarker);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    // CanDrag="True" сам по себе мышью не сработал — проверено на живой машине: плашка
    // просто не бралась. Поэтому запускаем перетаскивание руками: ловим нажатие, ждём
    // осмысленного сдвига и зовём StartDragAsync. Порог обязателен, иначе обычный клик
    // по плашке превращался бы в неудавшееся перетаскивание.
    private Windows.Foundation.Point _chipDownAt;
    private bool _chipPressed;
    private bool _chipDragging;
    private const double DragThreshold = 6;

    private void OnCallChipPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        EnqueueLog($"drag: нажатие на плашке (звонок={_inCall})");
        if (!_inCall || sender is not UIElement el) return;
        _chipDownAt = e.GetCurrentPoint(this.Content).Position;
        _chipPressed = true;
        _chipDragging = false;
        el.CapturePointer(e.Pointer);
    }

    private async void OnCallChipPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_chipPressed || _chipDragging || sender is not UIElement el) return;
        var p = e.GetCurrentPoint(this.Content).Position;
        if (Math.Abs(p.X - _chipDownAt.X) < DragThreshold && Math.Abs(p.Y - _chipDownAt.Y) < DragThreshold) return;

        _chipDragging = true;
        el.ReleasePointerCapture(e.Pointer);   // иначе система не отдаст указатель перетаскиванию
        EnqueueLog("drag: порог пройден, запускаю StartDragAsync");
        try
        {
            var result = await el.StartDragAsync(e.GetCurrentPoint(el));
            EnqueueLog($"drag: завершено, результат {result}");
        }
        catch (Exception ex) { EnqueueLog($"drag: {ex.GetType().Name} {ex.Message}"); }
        finally { _chipPressed = false; _chipDragging = false; }
    }

    private void OnCallChipPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement el) el.ReleasePointerCapture(e.Pointer);
        _chipPressed = false;
    }

    private void OnBlfDragOver(object sender, DragEventArgs e)
    {
        // Принимаем, только пока идёт разговор: перетаскивать нечего, если переводить нечего.
        if (!_inCall) { e.AcceptedOperation = DataPackageOperation.None; return; }
        e.AcceptedOperation = DataPackageOperation.Move;
        if (e.DragUIOverride is { } ui)
        {
            // Подпись под курсором объясняет, что произойдёт, ДО отпускания кнопки:
            // Ctrl меняет смысл жеста, и узнавать об этом постфактум поздно.
            bool attended = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control);
            string who = (sender as FrameworkElement)?.DataContext is ViewModels.FavoriteRow r ? r.Display : "";
            ui.Caption = Loc.F(attended ? "St_DragAsk" : "St_DragTransfer", who);
            ui.IsGlyphVisible = false;
        }
    }

    private async void OnBlfDrop(object sender, DragEventArgs e)
    {
        EnqueueLog("drag: сброс на плитку");
        if (!_inCall) return;
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.FavoriteRow row) return;
        if (row.Number.Length == 0) return;

        // Забираем модификатор ДО await: после него состояние клавиш уже не то.
        bool attended = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control);
        var deferral = e.GetDeferral();
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.Text)) return;
            if (await e.DataView.GetTextAsync() != DragCallMarker) return;
        }
        catch (Exception ex) { EnqueueLog("Перетаскивание: " + ex.Message); return; }
        finally { deferral.Complete(); }

        ShowSection(Tab.Dialer);   // возвращаем экран звонка: дальше всё происходит там
        // Подтверждения здесь нет намеренно: человек прицелился в конкретную плитку и
        // отпустил кнопку — сам жест и есть подтверждение.
        if (attended) StartAttendedTransfer(row.Number);
        else DoTransfer(row.Number);
    }

    private void OnConnectCallsClick(object sender, RoutedEventArgs e)
    {
        _attendedPending = false;
        UpdateAttendedHint();
        switch (_linphone.TransferToHeldCall())
        {
            case CommandOutcome.Done:
                EnqueueLog("Сопровождаемый перевод: соединяю собеседников");
                break;
            case CommandOutcome.Rejected:
                EnqueueLog("Не удалось выполнить сопровождаемый перевод: удержанного звонка нет");
                ShowDialerHint(Loc.S("St_TransferFailed"));
                break;
            case CommandOutcome.CoreUnavailable:
                EnqueueLog("Сопровождаемый перевод: ядро не ответило — состояние неизвестно");
                ShowDialerHint(Loc.S("St_CoreBusy"));
                break;
        }
    }

    /// <summary>Номер из поля. Из подсказки «Имя · номер» берём часть после разделителя.</summary>
    private string TransferTarget() => NumberFromLabel(TransferInput.Text.Trim());

    private void DoTransfer(string target)
    {
        if (target.Length == 0) return;
        ShowTransferPanel(false);
        _lastTransferTarget = target;   // для перенабора, если перевод сорвётся
        switch (_linphone.Transfer(target))
        {
            case CommandOutcome.Done:
                EnqueueLog($"Перевод звонка на {target}");   // итог придёт событием TransferResult
                break;
            case CommandOutcome.Rejected:
                EnqueueLog("Не удалось перевести звонок: адрес не разобран или звонка нет");
                ShowDialerHint(Loc.S("St_TransferFailed"));
                break;
            // Ядро промолчало — что стало с командой, неизвестно. Рапортовать «не удалось»
            // нельзя: REFER мог уйти, и человек, поверив сообщению, переведёт звонок дважды.
            case CommandOutcome.CoreUnavailable:
                EnqueueLog("Перевод: ядро не ответило — состояние неизвестно");
                ShowDialerHint(Loc.S("St_CoreBusy"));
                break;
        }
    }

    private string? _lastTransferTarget;   // последняя цель blind-перевода (для перенабора при провале)
    private bool _lastTransferWasPark;     // последний REFER был парковкой, а не переводом

    /// <summary>«Видео» = моя камера. Видео собеседника рендерится в области VideoSurface прямо
    /// на экране звонка и при выключении камеры не пропадает. Кнопка только отправляет команду:
    /// состояние приходит событием CameraStateChanged (см. подписку в конструкторе).</summary>
    private void OnVideoClick(object sender, RoutedEventArgs e) => _linphone.ToggleVideo();

    // ---------------- Видео в окне звонка ----------------
    // Механика усыновления окон MSOGL — в VideoOverlayController.
    private VideoOverlayController _video = null!;

    private void EnsureVideoWindowHook() => _video.StartWatching();
    private void AdoptExistingVideoWindows() => _video.AdoptExisting();


    /// <summary>Переключает экран звонка между аудио- (тексты по центру) и видео-раскладкой
    /// (видео собеседника на весь свободный верх). Зовётся из SetMode, состояний звонка
    /// и кнопки «Видео».</summary>
    private void UpdateVideoLayout()
    {
        bool active = _inCall && _linphone.IsVideoActive();
        // Видео-режим: видео занимает всё от шапки до компактного ряда кнопок
        // (микрофон · видео · пригласить) над красной; имя/таймер видны в заголовке окна —
        // рисовать их ПОВЕРХ видео нельзя (airspace нативного оверлея).
        VideoArea.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CallTexts.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        CallTopArea.VerticalAlignment = active ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        InCallSection.Padding = active ? new Thickness(0, 0, 0, 0) : new Thickness(12, 12, 12, 12);
        VideoArea.Margin = active ? new Thickness(0) : new Thickness(0, 10, 0, 0);
        InCallSection.RowDefinitions[2].Height = active ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        // Кнопки: в видео-режиме — только микрофон и видео по бокам от красной кнопки,
        // вся верхняя сетка управления скрыта (видео занимает её место). Клавиатура DTMF
        // с видео тоже несовместима.
        _videoMode = active;
        if (active && _screen == InCallScreen.Keypad) _screen = InCallScreen.Buttons;
        ApplyInCallScreen();
        VideoMuteStack.Visibility = VideoCamStack.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active) UpdateVideoButton();   // подсветка кнопки видео — на её видео-двойнике

        if (active) AdoptExistingVideoWindows();   // окна ловит хук; подбираем созданные до входа
        else
        {
            _video.Release();
        }
        UpdateVideoOverlayVisibility();
    }

    private void UpdateVideoButton()
    {
        // Кнопка есть в двух местах: сетка управления (аудио) и ряд у сброса (видео).
        foreach (var btn in new[] { VideoButton, VideoCamButton })
        {
            if (_videoOn)
                btn.Background = Palette("LpGreenBrush");   // зелёный = камера включена
            else
                btn.ClearValue(Button.BackgroundProperty); // вернуть тематический цвет клавиш
        }
    }

    // ---------------- События Core ----------------
    private bool _regOnline;                 // зарегистрированы (Ok) — тогда статус = presence
    private string _regOfflineText = "";      // что показывать, когда НЕ онлайн
    private string _regOfflineBrushKey = "LpNeutralBrush";   // ключ палитры, а не цвет числом

    private void OnRegistrationStateChanged(LinphoneRegistrationState state, string? message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EnqueueLog($"Регистрация: {state} {(message is null ? "" : "(" + message + ")")}");
            _regOnline = state == LinphoneRegistrationState.Ok;
            (_regOfflineText, _regOfflineBrushKey) = state switch
            {
                LinphoneRegistrationState.Progress => (Loc.S("St_Registering"), "LpAmberBrush"),
                LinphoneRegistrationState.Failed => (Loc.S("St_ErrorPrefix") + message, "LpDangerBrush"),
                LinphoneRegistrationState.Cleared => (Loc.S("St_Offline"), "LpNeutralBrush"),
                _ => (Loc.S("St_NotConnected"), "LpNeutralBrush"),
            };
            UpdateStatusLine();
            BuildAccountSwitcher(); // обновить точки статуса в переключателе аккаунтов
        });
    }

    /// <summary>Единый статус в шапке: онлайн → выбранный presence-статус (Доступен/Не беспокоить/…)
    /// с цветной точкой и эмодзи; не онлайн → состояние регистрации (Не в сети/Регистрация…/ошибка),
    /// presence-эмодзи скрыт (статусы не имеют смысла без регистрации).</summary>
    private void UpdateStatusLine()
    {
        if (_regOnline)
        {
            var s = _linphone.MyStatus;
            HeaderVm.StatusText = StatusLabel(s);
            RegDot.Fill = ViewModels.PresenceUi.Brush(s);   // единственная точка = цвет статуса
        }
        else
        {
            HeaderVm.StatusText = _regOfflineText;
            RegDot.Fill = Palette(_regOfflineBrushKey);
        }
    }

    /// <summary>Цвет точки статуса под presence. Берём ЕДИНЫЙ источник (PresenceUi): здесь
    /// лежала его копия, и точка в шапке могла разойтись с лампами пульта при первой же
    /// правке одного из двух списков.</summary>
    private static Windows.UI.Color StatusColor(PresenceStatus s) => ViewModels.PresenceUi.Color(s);

    /// <summary>Результат перевода звонка (REFER): раньше «Перевод отправлен» показывался всегда,
    /// даже если абонент не дозвонился до цели. Теперь показываем реальный итог.</summary>
    private void OnTransferResult(bool success)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            // Парковка выполняется тем же REFER, но это ДРУГОЕ действие: предлагать
            // «перевести снова» и открывать панель перевода тому, кто нажал «Парковку», —
            // значит отвечать не на то, что он делал.
            bool parking = _lastTransferWasPark;
            _lastTransferWasPark = false;
            if (parking)
            {
                EnqueueLog(success ? "Парковка: звонок припаркован" : "Парковка не удалась");
                ShowDialerHint(Loc.S(success ? "St_ParkDone" : "St_ParkFailed"));
                return;
            }

            EnqueueLog(success ? "Перевод выполнен" : "Перевод не удался");
            // Перевод сорвался (ошибся номером), но исходный разговор ещё жив — предлагаем
            // перенабрать, а не терять связь молча. Так ошибочный номер не рвёт звонок.
            if (!success && _inCall)
            {
                var dlg = new ContentDialog
                {
                    Title = Loc.S("St_TransferFailed"),
                    Content = _lastTransferTarget is { Length: > 0 } t
                        ? string.Format(Loc.S("St_TransferRetryText"), t) : Loc.S("St_TransferFailed"),
                    PrimaryButtonText = Loc.S("St_TransferRetry"),
                    CloseButtonText = Loc.S("St_Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot,
                };
                if (await Dialogs.ShowAsync(dlg) == ContentDialogResult.Primary)
                    ShowTransferPanel(true);   // снова панель перевода
                return;
            }
            ShowDialerHint(Loc.S(success ? "St_TransferDone" : "St_TransferFailed"));
        });
    }

    /// <summary>MWI (NOTIFY message-summary) от АТС: конверт с числом новых сообщений голосовой
    /// почты в шапке. Ноль сообщений — конверт скрыт.</summary>
    private void OnVoicemailChanged(bool waiting, int nbNew)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EnqueueLog(waiting ? $"Голосовая почта: новых {nbNew}" : "Голосовая почта: новых нет");
            HeaderVm.VoicemailWaiting = waiting;
            HeaderVm.VoicemailText = nbNew > 0 ? (nbNew > 99 ? "99+" : nbNew.ToString()) : "";
            ToolTipService.SetToolTip(VoicemailBadge, Loc.F("St_VoicemailTip", nbNew));
        });
    }

    /// <summary>Клик по конверту — набираем номер прослушивания голосовой почты (настройки → Поведение).</summary>
    private void OnVoicemailBadgeClick(object sender, RoutedEventArgs e)
    {
        string n = _linphone.GetVoicemailNumber();
        if (n.Length == 0 || _inCall) return;
        ShowSection(Tab.Dialer);
        _linphone.Call(n);
    }

    /// <summary>Сервер стабильно отвергает пароль — регистрация аккаунта остановлена (см. LinphoneService).
    /// Показываем внятную причину вместо вечного «Ошибка: Forbidden».</summary>
    private void OnRegistrationAuthStopped(string identity)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EnqueueLog($"Регистрация остановлена (неверные учётные данные): {identity}");
            HeaderVm.StatusText = Loc.S("St_AuthStopped");
            RegDot.Fill = Palette("LpDangerBrush");
        });
    }

    private void OnCallStateChanged(CallEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Call-ID в каждой строке: при двух линиях подряд идущие «End» и «StreamsRunning»
            // иначе неразличимы, и восстановить, что с каким звонком происходило, нельзя.
            EnqueueLog($"Звонок{(e.CallId is null ? "" : " " + e.CallId)}: {e.State}"
                       + (e.Remote is null ? "" : " ← " + e.Remote));
            string remote = Pretty(e.Remote);

            // Автоответ отменяем и убираем toast/баннер/мигание, как только звонок покидает «входящий».
            if (e.State != LinphoneCallState.IncomingReceived)
            {
                _autoAnswerTimer?.Stop();
                ClearIncomingToast();
                FlashTaskbar(false);
                _incomingPending = false;   // приняли, отклонили или абонент положил трубку
                IncomingBanner.Visibility = Visibility.Collapsed;
                RefreshIncomingFlag();
            }

            switch (e.State)
            {
                case LinphoneCallState.IncomingReceived:
                    _redial.Cancel();   // пришёл входящий — автодозвон не должен ему мешать
                    _hookMissed = true; // пока не принят — его можно пропустить
                    ShowIncomingCall(e, remote);
                    NotifyCallHook(CallHookEvent.Incoming, e);
                    break;

                case LinphoneCallState.OutgoingInit:
                case LinphoneCallState.OutgoingProgress:
                case LinphoneCallState.OutgoingRinging:
                case LinphoneCallState.OutgoingEarlyMedia:
                case LinphoneCallState.Connected:
                    if (!e.IsIncoming && e.Remote is { Length: > 0 })
                    {
                        DialerVm.LastDialed = e.Remote;   // «повтор последнего» на дайлере
                        // Звонок в этом сеансе состоялся — подсказку с номером снова показываем
                        // (в том числе если набрали тот же номер, что был в журнале при старте).
                        _dialerPlaceholderDismissed = false;
                        UpdateDialerLine();
                    }
                    // Звоним (вручную или автодозвоном): счётчик прячем, а звонок на ДРУГОЙ
                    // номер означает, что пользователь передумал — цикл сбрасывается целиком.
                    if (!e.IsIncoming) _redial.NoteOutgoing(e.Remote);
                    // Только на первом состоянии исходящего: Progress/Ringing/Connected идут
                    // следом за Init, и без этого CRM получила бы четыре события на один звонок.
                    if (!e.IsIncoming && e.State == LinphoneCallState.OutgoingInit)
                        NotifyCallHook(CallHookEvent.Outgoing, e);
                    ShowCall(remote, Loc.S("St_Calling"));
                    SetCallControlsEnabled(false);
                    break;

                case LinphoneCallState.StreamsRunning:
                    _redial.Cancel();   // дозвонились — цикл завершён
                    // Разговор идёт: удержания нет. Сюда же приходим, когда сервер отказал
                    // в удержании, — кнопка обязана вернуться в «Удержать».
                    SetHoldVisuals(false);
                    _hookMissed = false;   // поговорили — это не пропущенный
                    if (!_hookAnswered) { _hookAnswered = true; NotifyCallHook(CallHookEvent.Answered, e); }
                    // Приглашённый в конференцию ответил — объединяем звонки автоматически.
                    if (_confInvitePending && _linphone.CallCount > 1)
                    {
                        _confInvitePending = false;
                        EnqueueLog(_linphone.MergeCallsIntoConference() == CommandOutcome.Done
                            ? "Конференция: звонки объединены"
                            : "Не удалось создать конференцию — звонки остались раздельными");
                    }
                    ShowCall(remote, Loc.S("St_InCall"));
                    if (_linphone.IsInConference) CallVm.RemoteName = Loc.S("St_Conference");
                    // Шифрование — по факту договорённости сторон, а не по настройке клиента.
                    // Вместе с ним берём транспорт сигнализации: SDES-ключ едет в SDP, и по
                    // UDP/TCP «зашифрованный» разговор читается любым, кто видит трафик.
                    // Пишем и в лог: при разборе обращения «слышно/не слышно» первым делом
                    // спрашивают, шёл ли разговор через SRTP.
                    var security = _linphone.CurrentCallSecurity();
                    CallVm.SetSecurity(security);
                    EnqueueLog($"Медиа: шифрование {security.Encryption}"
                               + (security.IsWeak ? " (ключ передан по НЕзащищённой сигнализации — включите TLS)" : ""));
                    SetCallControlsEnabled(true);
                    StartDurationTimer();
                    _videoOn = _linphone.IsCameraOn();
                    UpdateVideoButton();
                    UpdateVideoLayout();   // видео могло включиться/выключиться после re-INVITE
                    // Автозапись: включена в настройках и запись ещё не идёт — стартуем сами.
                    // Итог придёт событием RecordingChanged, как и у ручного нажатия.
                    if (_linphone.GetAppFlag("auto_record", false) && !_linphone.IsRecording)
                    {
                        EnqueueLog("Автозапись разговора включена настройками");
                        _linphone.ToggleRecording();
                    }
                    UpdateRecordButton(_linphone.IsRecording);
                    UpdateSwapButton();
                    _linphone.PublishOnThePhone(true);   // для коллег — «Разговаривает»
                    break;

                case LinphoneCallState.Pausing:
                case LinphoneCallState.Paused:
                    CallVm.StatusText = Loc.S("St_OnHold");
                    SetHoldVisuals(true);   // удержание СОСТОЯЛОСЬ — только теперь меняем кнопку
                    UpdateSwapButton();
                    break;

                case LinphoneCallState.PausedByRemote:
                    CallVm.StatusText = Loc.S("St_HeldByRemote");
                    // Нас удержала другая сторона: это НЕ наше удержание, и кнопка не должна
                    // предлагать «Возобновить» — resume тут не сработает.
                    SetHoldVisuals(false);
                    break;

                case LinphoneCallState.End:
                case LinphoneCallState.Released:
                case LinphoneCallState.Error:
                    // Любое завершение отменяет незавершённое приглашение в конференцию:
                    // либо сорвался приглашённый (занято), либо умер исходный разговор.
                    _confInvitePending = false;
                    // End и Released приходят парой на один звонок — уведомляем однократно.
                    if (e.State != LinphoneCallState.Released && _hookLive)
                    {
                        _hookLive = false;
                        NotifyCallHook(CallHookEvent.Ended, e);
                    }
                    _hookAnswered = false;
                    if (e.State == LinphoneCallState.Error)
                        EnqueueLog($"Звонок завершён с ошибкой: {e.Message}");
                    StopDurationTimer();
                    _muted = _onHold = false;   // следующий звонок начинается с чистого состояния
                    CloseIncomingMini();        // входящий разрешился — верхнее окно ни к чему
                    if (_linphone.CallCount == 0) CloseMiniWindow();
                    if (_linphone.CallCount > 0 && _linphone.DisplayCallSnapshot() is { } snap)
                    {
                        // Остался второй звонок — показываем его, а не уходим в набор.
                        string r = Pretty(snap.Remote);
                        bool held = snap.State is LinphoneCallState.Paused or LinphoneCallState.Pausing
                                                or LinphoneCallState.PausedByRemote;
                        ShowCall(r, held ? Loc.S("St_OnHold") : Loc.S("St_InCall"));
                        SetCallControlsEnabled(true);
                        UpdateRecordButton(_linphone.IsRecording);
                        UpdateSwapButton();
                        StartDurationTimer();
                    }
                    else
                    {
                        ResetCallControls();
                        _videoOn = false;
                        SetMode(Mode.Dialer);
                        Title = "L-Phone";                    // звонок кончился — обычный заголовок
                        _linphone.PublishOnThePhone(false);   // звонков нет — вернуть выбранный статус
                        ShowCallEndReason(e.Reason);          // Занято/Нет ответа/… — человеку, не в лог
                        _redial.OnCallTerminated(e);          // «занято» + включён автодозвон → отсчёт
                    }
                    NotifyMissedIfNeeded(e);
                    UpdateMissedBadge();   // мог добавиться пропущенный — обновим бейдж
                    break;
            }
        });
    }

    /// <summary>
    /// Уведомление о пропущенном звонке — с кнопкой «Перезвонить».
    ///
    /// Бейдж на вкладке и точка в трее отвечают «что-то было», но не отвечают КТО и не дают
    /// перезвонить одним движением — а именно это человек и делает, вернувшись к столу.
    /// Показываем только когда мы этот звонок ДЕЙСТВИТЕЛЬНО пропустили: сами отклонили или
    /// поговорили — это не пропущенный, и напоминать о нём незачем.
    /// </summary>
    private void NotifyMissedIfNeeded(CallEvent e)
    {
        if (!e.IsIncoming || e.State == LinphoneCallState.Released) return;   // End и Released — пара
        if (!_hookMissed) return;            // звонок был принят — не пропущенный
        _hookMissed = false;
        // Мы сами нажали «Отклонить» либо режим «Не беспокоить» — это осознанный отказ.
        if (e.Reason is LinphoneReason.Declined or LinphoneReason.Busy or LinphoneReason.DoNotDisturb) return;
        if (!_linphone.GetAppFlag("missed_toast", true)) return;

        // Скрытый номер приходит НЕ пустым («Anonymous» <sip:anonymous@anonymous.invalid>,
        // RFC 3323) — проверено живым звонком. Кнопка «Перезвонить» на такой адрес заведомо
        // не сработает, поэтому номер для неё отдаём только когда он настоящий.
        bool callable = PhoneNumberUtils.IsCallable(e.Remote);
        string number = callable ? SipUser(e.Remote ?? "") : "";
        string caller = callable ? DisplayName(e.Remote ?? "") : Loc.S("St_UnknownCaller");
        EnqueueLog($"Пропущенный звонок: {caller}");
        _toasts.ShowMissed(caller, number);
    }

    /// <summary>Входящий звонок идёт и ещё не принят — значит его можно пропустить.</summary>
    private bool _hookMissed;

    /// <summary>Показать входящий звонок. РЕШЕНИЕ, что именно показывать, принимает
    /// IncomingCallPolicy — чистая функция с семью условиями, покрытая тестами. Здесь
    /// остаётся только исполнение плана.</summary>
    private void ShowIncomingCall(CallEvent e, string remote)
    {
        var plan = IncomingCallPolicy.Decide(new IncomingCallContext(
            DoNotDisturb: _linphone.GetAppFlag("dnd", false),
            AlreadyInCall: _inCall,
            WindowVisible: AppWindow.IsVisible,
            WindowIsForeground: Interop.NativeMethods.GetForegroundWindow()
                                == WinRT.Interop.WindowNative.GetWindowHandle(this),
            MiniWindowEnabled: _linphone.GetAppFlag("mini_call_window", true),
            BringToFrontEnabled: _linphone.GetAppFlag("incoming_to_front", false),
            AutoAnswerEnabled: _linphone.GetAppFlag("auto_answer", false),
            IntercomAnswerAfter: e.IntercomAnswerAfter,
            IntercomEnabled: _linphone.GetAppFlag("intercom_autoanswer", true),
            DialerTabOpen: _currentTab == Tab.Dialer));

        if (plan.RejectBusy)
        {
            EnqueueLog("Не беспокоить: входящий отклонён");
            _linphone.DeclineBusy();
            return;
        }

        string caller = DisplayName(remote);
        string number = SipUser(e.Remote ?? remote);   // чистый номер абонента
        bool named = !string.IsNullOrEmpty(number) && number != caller;   // имя нашлось в книге
        string shownNumber = named ? number : "";

        // Заполняем ОБА представления независимо от того, какое показываем сейчас: пока телефон
        // звонит, человек волен переключать вкладки, и второе должно быть готово к показу.
        IncomingBannerText.Text = caller + (named ? " · " + number : "");
        IncomingName.Text = caller;
        IncomingNumber.Text = shownNumber;
        IncomingAvatar.Text = Initials(caller);

        _incomingPending = true;   // звонок не принят и не отклонён — его надо где-то показывать
        if (plan.ShowBanner)
        {
            IncomingBanner.Visibility = Visibility.Visible;
            RefreshIncomingFlag();
        }
        if (plan.ShowFullScreen) SetMode(Mode.Incoming);   // полноэкранный экран входящего
        if (plan.ShowMiniWindow) ShowIncomingMini(caller, shownNumber);
        if (plan.ShowToast) ShowIncomingToast(caller, shownNumber);
        if (plan.FlashTaskbar) FlashTaskbar(true);
        if (plan.BringToFront) BringToFront();
        if (plan.StartAutoAnswer)
        {
            if (plan.AutoAnswerIsIntercom)
            {
                // Пишем обязательно: «телефон ответил сам» без объяснения выглядит как дефект,
                // а разбираться потом придётся по трассам.
                EnqueueLog($"Интерком: АТС просит ответить автоматически "
                           + $"(через {plan.AutoAnswerAfterSeconds} с) — {caller}");
                StartAutoAnswer(plan.AutoAnswerAfterSeconds);
            }
            else StartAutoAnswer();
        }
    }

    private void ShowCall(string remote, string status)
    {
        // Разговор ТОЛЬКО НАЧИНАЕТСЯ — показываем вкладку, где живёт его экран. Все исходящие
        // пути делают это сами перед набором; входящий больше не перебрасывает человека на
        // «Набор» заранее, поэтому ответить он мог с любой вкладки — и остался бы в разговоре,
        // не видя ни собеседника, ни кнопки «Положить трубку».
        //
        // Условие обязательно: ShowCall зовётся на каждой смене статуса звонка, и без него
        // снятие с удержания выдёргивало бы человека из контактов посреди разговора.
        if (!_inCall) ShowSection(Tab.Dialer);

        CallVm.RemoteName = DisplayName(remote);
        CallVm.StatusText = status;
        SetMode(Mode.InCall);
        Title = $"{DisplayName(remote)} — L-Phone";   // видно в таскбаре, пока идёт звонок
        CloseIncomingMini();   // дошли до разговора — предложение принять больше не нужно
        UpdateMiniWindow(status);
    }

    // ---------------- Мини-окно звонка ----------------
    /// <summary>Мини-окна звонка поверх всех окон (см. MiniWindowController).</summary>
    private readonly MiniWindowController _mini;

    // Состояние микрофона и удержания ядро наружу не отдаёт (есть только Toggle*), поэтому
    // ведём его здесь — там же, где рисуем индикацию, чтобы источник был один.
    private bool _muted;
    private bool _onHold;

    private void UpdateMiniWindow(string status) =>
        _mini.UpdateCall(CallVm.RemoteName, status, _muted, _onHold, CallVm.IsEncrypted);

    /// <summary>Обновить мини-окно, если оно открыто. Именно так, а не через UpdateMiniWindow:
    /// микрофон и удержание сбрасываются в том числе при завершении звонка, и создавать
    /// ради этого окно заново — значит показать его уже после того, как разговор кончился.</summary>
    private void RefreshMiniWindow() =>
        _mini.RefreshCall(CallVm.RemoteName, CallVm.StatusText, _muted, _onHold, CallVm.IsEncrypted);

    private bool ShowIncomingMini(string caller, string number) =>
        _mini.ShowIncoming(caller, number, duringCall: _inCall);

    private void CloseIncomingMini() => _mini.CloseIncoming();
    private void CloseMiniWindow() => _mini.CloseCall();


    private DispatcherQueueTimer? _reasonTimer;

    /// <summary>Показывает под кнопкой вызова причину неудавшегося звонка; гаснет через 6 с.</summary>
    private void ShowCallEndReason(LinphoneReason reason)
    {
        string? key = reason switch
        {
            LinphoneReason.Busy or LinphoneReason.DoNotDisturb => "St_ReasonBusy",
            LinphoneReason.Declined => "St_ReasonDeclined",
            LinphoneReason.NotFound or LinphoneReason.AddressIncomplete or LinphoneReason.NoMatch => "St_ReasonNotFound",
            LinphoneReason.NotAnswered or LinphoneReason.NoResponse => "St_ReasonNoAnswer",
            LinphoneReason.TemporarilyUnavailable or LinphoneReason.Gone or LinphoneReason.IOError
                or LinphoneReason.ServerTimeout or LinphoneReason.BadGateway => "St_ReasonUnavailable",
            LinphoneReason.Forbidden or LinphoneReason.Unauthorized => "St_ReasonForbidden",
            _ => null,   // штатное завершение и экзотика — ничего не показываем
        };
        if (key == null) return;
        ShowDialerHint(Loc.S(key));
    }

    /// <summary>Одноразовый таймер, гасящий строку под кнопкой вызова (причина сбоя / итог перевода) через 6 с.</summary>
    private DispatcherQueueTimer CreateReasonTimer()
    {
        var t = DispatcherQueue.CreateTimer();
        t.IsRepeating = false;
        t.Interval = TimeSpan.FromSeconds(6);
        t.Tick += (timer, _) => { timer.Stop(); _dialerHint = null; UpdateDialerLine(); };
        return t;
    }

    // ---------------- Автодозвон при «занято» ----------------
    // Сам автомат — в RedialController: внутри окна он был перемешан с видимостью панелей,
    // из-за чего не читался отдельно и не проверялся вовсе. Здесь остался только показ.
    private RedialController _redial = null!;

    /// <summary>Таймер отсчёта на очереди UI — реализация ICountdownTimer для контроллера.
    ///
    /// ВАЖНО: таймер один на всё время жизни, и подписка на Tick тоже РОВНО ОДНА — она
    /// делается при создании и больше не трогается, а меняется только поле с колбэком.
    /// Раньше подписка добавлялась в каждом Start поверх прежних: второй цикл автодозвона
    /// получал два обработчика на один тик и отсчитывал 30 секунд за 15, третий — за 10.
    /// К пятому циклу набор уходил почти сразу после отбоя, то есть автодозвон превращался
    /// в долбёжку занятого номера — со стороны АТС это выглядит как атака.</summary>
    private sealed class DispatcherCountdownTimer : ICountdownTimer
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _queue;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
        private Action? _onTick;   // актуальный колбэк; подписка на Tick его только читает

        public DispatcherCountdownTimer(Microsoft.UI.Dispatching.DispatcherQueue queue) => _queue = queue;

        public void Start(TimeSpan interval, Action onTick)
        {
            _onTick = onTick;
            if (_timer == null)
            {
                _timer = _queue.CreateTimer();
                _timer.Tick += (_, _) => _onTick?.Invoke();   // единственная за всё время подписка
            }
            _timer.Stop();
            _timer.IsRepeating = true;
            _timer.Interval = interval;
            _timer.Start();
        }

        public void Stop() => _timer?.Stop();
    }

    private void ShowRedialCountdown(int secondsLeft, int attempt)
    {
        RedialText.Text = string.Format(Loc.S("St_RedialIn"), secondsLeft, attempt);
        RedialPanel.Visibility = Visibility.Visible;
    }

    private void OnRedialCancelClick(object sender, RoutedEventArgs e)
    {
        EnqueueLog("Автодозвон отменён");
        _redial.Cancel();
    }


    // ---------------- Режимы экрана ----------------
    private void SetMode(Mode mode)
    {
        bool wasInCall = _inCall;
        _inCall = mode == Mode.InCall;
        _incoming = mode == Mode.Incoming;
        CallVm.Active = _inCall;   // от этого зависит плашка перетаскивания на пульте
        // Точки DTMF сбрасываем на входе в разговор и при его завершении (но не на смене статуса внутри).
        if (_inCall != wasInCall) ResetDtmf();

        DialerTop.Visibility = mode == Mode.Dialer ? Visibility.Visible : Visibility.Collapsed;
        DialActions.Visibility = mode == Mode.Dialer ? Visibility.Visible : Visibility.Collapsed;
        Dialpad.Visibility = mode == Mode.Dialer ? Visibility.Visible : Visibility.Collapsed;

        // Свежий экран разговора всегда открывается кнопками — ни DTMF-клавиатурой, ни
        // панелью перевода, оставшейся открытой от прошлого звонка. НО во время консультации
        // сетку показывать нельзя: второй звонок приходит сюда же, и она вылезала поверх
        // двух кнопок «Соединить / Вернуть себе», заслоняя пол-экрана.
        if (_inCall && !wasInCall) _screen = InCallScreen.Buttons;
        ApplyInCallScreen();
        UpdateInCallHost();   // дайлер ↔ полноэкранный звонок на вкладке «Набор»
        UpdateVideoLayout();  // вход/выход из разговора: показать или спрятать область видео
    }

    /// <summary>Кнопка «Клавиши» / «Скрыть»: переключение сетки управления и DTMF-клавиатуры.</summary>
    private void OnKeypadToggleClick(object sender, RoutedEventArgs e)
    {
        _screen = _screen == InCallScreen.Keypad ? InCallScreen.Buttons : InCallScreen.Keypad;
        ApplyInCallScreen();
    }

    private void SetCallControlsEnabled(bool enabled)
    {
        MuteButton.IsEnabled = enabled;
        HoldButton.IsEnabled = enabled;
        VideoButton.IsEnabled = enabled;
        TransferButton.IsEnabled = enabled;
        RecordButton.IsEnabled = enabled;
        ConferenceButton.IsEnabled = enabled;   // приглашать можно только из активного разговора
    }

    private void ResetCallControls()
    {
        SetCallControlsEnabled(false);
        UpdateMuteVisuals(false);   // мьют в liblinphone сбрасывается вместе со звонком
        HoldLabel.Text = Loc.S("St_Hold");
        _videoOn = false;
        UpdateVideoButton();
        UpdateRecordButton(false);
        SwapStack.Visibility = Visibility.Collapsed;
        ConferenceStack.Visibility = Visibility.Collapsed;
        CallVm.StatusText = "";
        _screen = InCallScreen.Buttons;
        _attendedPending = false;
        CallVm.SetSecurity(Interop.CallSecurity.None);   // замок не должен пережить звонок, к которому относился
        CallVm.Quality = Interop.CallQuality.None;   // как и палочки качества
    }

    // ---------------- Таймер длительности ----------------
    // Длительность считаем САМИ, а не спрашиваем у ядра. Прежний CurrentCallDuration — это
    // Query, то есть синхронный переход на поток ядра раз в секунду с потока интерфейса: пока
    // ядро свободно, это микросекунды, а как только оно уходит в DNS или TLS — секунды
    // неподвижного окна, ровно тогда, когда человек смотрит на экран разговора внимательнее
    // всего. Момент начала разговора мы и так знаем: он приходит вместе с StreamsRunning.
    private DateTime _callStartedAt;

    /// <summary>Отметить начало отсчёта длительности. Ядро даёт длительность целыми секундами
    /// от своего момента, поэтому при переустановке медиа (удержание, перевод, видео) таймер
    /// раньше перескакивал; здесь точка отсчёта одна на разговор.</summary>
    private void NoteCallStarted()
    {
        if (_callStartedAt == default) _callStartedAt = DateTime.UtcNow;
    }

    private void StartDurationTimer()
    {
        NoteCallStarted();
        if (_durationTimer == null)
        {
            _durationTimer = DispatcherQueue.CreateTimer();
            _durationTimer.Interval = TimeSpan.FromSeconds(1);
            _durationTimer.Tick += (_, _) =>
            {
                int sec = Math.Max(0, (int)(DateTime.UtcNow - _callStartedAt).TotalSeconds);
                string t = $"{sec / 60:00}:{sec % 60:00}";
                CallVm.StatusText = t;
                // Заголовок окна имеет смысл, только пока окно есть на панели задач. Свёрнутому
                // в трей мы раз в секунду переписывали строку, которую никто не видит.
                if (AppWindow.IsVisible) Title = $"{t} · {CallVm.RemoteName} — L-Phone";
                // Качество спрашиваем реже, чем время: RTCP приходит раз в несколько секунд,
                // и опрос каждую секунду дал бы те же числа ценой лишних обращений к ядру.
                // И только когда экран разговора виден — иначе палочки некому смотреть.
                if (sec % 3 == 0 && AppWindow.IsVisible && InCallSection.Visibility == Visibility.Visible)
                    CallVm.Quality = _linphone.CurrentQuality();
            };
        }
        _durationTimer.Start();
        StartLevelTimer();
    }

    private void StopDurationTimer()
    {
        _durationTimer?.Stop();
        _levelTimer?.Stop();
        CallVm.MicLevel = 0;
        _callStartedAt = default;   // следующий разговор считает своё время с нуля
    }

    // Уровень микрофона обновляем в 8 раз чаще таймера длительности: раз в секунду
    // полоска показывала бы случайный слог, а не то, что человека слышно.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _levelTimer;

    private void StartLevelTimer()
    {
        if (_levelTimer == null)
        {
            _levelTimer = DispatcherQueue.CreateTimer();
            _levelTimer.Interval = TimeSpan.FromMilliseconds(125);
            _levelTimer.Tick += (_, _) =>
            {
                // Экран звонка не виден — считать уровень незачем: полоски на экране нет,
                // а ядро мы дёргаем восемь раз в секунду.
                if (InCallSection.Visibility != Visibility.Visible) return;
                // Раз в ~2 с сверяем набор устройств сами: событие об ОТКЛЮЧЕНИИ приходит
                // не от всякого драйвера (Bluetooth — как раз такой случай), а молча
                // говорить в исчезнувшую гарнитуру хуже, чем лишний раз перечитать список.
                if (++_levelTicks % 40 == 0) _audio.CheckDrift();
                // Оба значения — ГОТОВЫЕ СНИМКИ с потока ядра, без перехода на него.
                // Раньше здесь было два синхронных вызова восемь раз в секунду, и второй
                // из них перечислял аудиоустройства: шестнадцать межпоточных ожиданий в
                // секунду ради одной полоски, каждое из которых могло упереться в ядро,
                // застрявшее в DNS или TLS.
                var (mic, _) = _linphone.CurrentAudioLevels();
                // Микрофон мог исчезнуть вместе с гарнитурой. Тогда ядро отдаёт последнее
                // известное значение, и полоска ЗАМИРАЕТ на середине — человек видит
                // «меня слышно», хотя его не слышно вовсе.
                bool haveMic = _linphone.HasRealMicrophone;
                CallVm.MicMissing = !haveMic;
                CallVm.MicLevel = haveMic
                    ? Interop.AudioLevel.Smooth((float)CallVm.MicLevel, mic)
                    : 0;
            };
        }
        _levelTimer.Start();
    }

    // ---------------- Утилиты ----------------
    private static string Pretty(string? sipAddress) => PhoneNumberUtils.Pretty(sipAddress);

    private static string Initials(string value)
    {
        string s = Pretty(value);
        int at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        return s.Length >= 2 ? s[..2] : s;
    }

    // ---------------- Обновления ----------------
    /// <summary>Фоновая проверка обновления после старта. Молчит, если обновления нет, проверка
    /// выключена или сеть недоступна: это не действие пользователя, и жаловаться тут не на что.
    /// Ставим только с согласия — оператор не должен получить перезапуск клиента посреди дня.</summary>
    private async void CheckForUpdateAsync(bool manual)
    {
        if (!manual && !_linphone.GetAppFlag("update_check", true)) return;

        var info = await Updater.CheckAsync(_linphone.GetAppString("update_url", Updater.DefaultFeedUrl));
        if (info == null)
        {
            if (manual) await ShowInfoDialogAsync(Loc.S("St_UpdateNone"), "");
            return;
        }
        EnqueueLog($"Доступно обновление: {info.Version}");
        if (_inCall) return;   // не мешаем разговору; предложим при следующем запуске

        var ask = new ContentDialog
        {
            Title = Loc.F("St_UpdateTitle", info.Version),
            Content = info.Notes.Length > 0 ? info.Notes : Loc.S("St_UpdateText"),
            PrimaryButtonText = Loc.S("St_UpdateInstall"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await Dialogs.ShowAsync(ask) != ContentDialogResult.Primary) return;

        string? file = await Updater.DownloadAsync(info);
        // Проверка подписи — единственная настоящая защита канала: файл ставится с правами
        // администратора, и подменённый на пути установщик получил бы их же.
        if (file == null || !Updater.IsTrusted(file))
        {
            EnqueueLog(file == null ? "Обновление: скачать не удалось"
                                    : "Обновление ОТВЕРГНУТО: подпись не наша или повреждена");
            await ShowInfoDialogAsync(Loc.S("St_UpdateFailed"), file == null ? "" : Loc.S("St_UpdateBadSignature"));
            if (file != null) { try { System.IO.File.Delete(file); } catch { } }
            return;
        }

        EnqueueLog("Обновление: подпись проверена, запускаю установщик");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            ExitApp();   // установщик не сможет заменить файлы, пока мы держим их открытыми
        }
        catch (Exception ex) { EnqueueLog("Обновление: не удалось запустить установщик — " + ex.Message); }
    }

    private async System.Threading.Tasks.Task ShowInfoDialogAsync(string title, string text) =>
        await Dialogs.ShowAsync(new ContentDialog
        {
            Title = title,
            Content = text,
            CloseButtonText = Loc.S("St_Close"),
            XamlRoot = Content.XamlRoot,
        });

    // ---------------- Журнал приложения ----------------
    // Сам журнал — в AppLog: к окну он отношения не имеет, а жил здесь, из-за чего путь
    // к файлу дублировался в SettingsWindow, а маскировку ПД приходилось проверять
    // через внутренности окна.
    private static void EnqueueLog(string message) => AppLog.Write(message);
}
