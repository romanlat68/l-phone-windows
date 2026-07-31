using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LinphoneWinUI.Interop;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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

    // === Минимальный и стартовый размер окна (логические пиксели) ===
    private const int MinWidth = 400;
    private const int MinHeight = 740;

    private readonly LinphoneService _linphone = new();

    // MVVM: секции переезжают на ViewModels по одной.
    public ViewModels.ContactsViewModel ContactsVm { get; }
    public ViewModels.HistoryViewModel HistoryVm { get; }
    public ViewModels.FavoritesViewModel FavoritesVm { get; }
    public ViewModels.ChatViewModel ChatVm { get; }
    public ViewModels.DialerViewModel DialerVm { get; }
    public ViewModels.HeaderViewModel HeaderVm { get; } = new();
    public ViewModels.CallViewModel CallVm { get; } = new();

    private DispatcherQueueTimer? _durationTimer;
    private DispatcherQueueTimer? _autoAnswerTimer;
    private DispatcherQueueTimer? _netDebounce;   // дебаунс смены сети для авто-переподключения
    private bool _inCall;
    private bool _videoOn;
    private SettingsWindow? _settingsWindow;
    private List<ContactEntry> _allContacts = new();
    private bool _exiting;
    private long _missedSeen;   // Unix-секунды последнего просмотра журнала (для бейджа пропущенных)

    private static readonly string LogFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "app.log");

    private readonly SolidColorBrush _activeTab = new(Windows.UI.Color.FromArgb(255, 0, 120, 215));
    private readonly SolidColorBrush _inactiveTab = new(Colors.Gray);

    /// <summary>Функция-биндинг для {x:Bind}: bool → Visibility (Converter={StaticResource}
    /// не работает в x:Bind на Window — нет FrameworkElement для converter-lookup).</summary>
    public Visibility VisIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>Инверсия для {x:Bind} (Converter на Window не резолвится — см. VisIf).</summary>
    public bool NotVal(bool value) => !value;

    public MainWindow()
    {
        // ViewModel до InitializeComponent — {x:Bind} к ContactsVm резолвится при загрузке XAML.
        ContactsVm = new ViewModels.ContactsViewModel(_linphone, Loc.S, (k, a) => Loc.F(k, a));
        ContactsVm.CallPlaced += () => ShowSection(Tab.Dialer);
        HistoryVm = new ViewModels.HistoryViewModel(_linphone);
        FavoritesVm = new ViewModels.FavoritesViewModel(_linphone);
        ChatVm = new ViewModels.ChatViewModel(_linphone, DisplayName);
        ChatVm.ScrollToEnd += ScrollChatToEnd;
        ChatVm.BadgeChanged += UpdateChatBadge;
        DialerVm = new ViewModels.DialerViewModel(_linphone, q => FindContact(q)?.Name);

        InitializeComponent();
        SystemBackdrop = new MicaBackdrop(); // тематический фон (иначе окно прозрачное)
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "lphone.ico"));

        // Минимальный размер на уровне ОС (WM_GETMINMAXINFO) + старт в нём же.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        MinSizeHelper.SetMinSize(hwnd, MinWidth, MinHeight);
        double scale = MinSizeHelper.GetScale(hwnd);
        AppWindow.Resize(new SizeInt32((int)(MinWidth * scale), (int)(MinHeight * scale)));

        WriteLogHeader();
        _linphone.Log += EnqueueLog;
        _linphone.RegistrationStateChanged += OnRegistrationStateChanged;
        _linphone.CallStateChanged += OnCallStateChanged;
        _linphone.ChatMessageReceived += OnChatMessageReceived;
        _linphone.PresenceChanged += OnPresenceChanged;
        _linphone.RegistrationAuthStopped += OnRegistrationAuthStopped;
        _linphone.TransferResult += OnTransferResult;
        _linphone.VoicemailChanged += OnVoicemailChanged;

        // Toast-уведомления о входящем (когда окно свёрнуто в трей): кнопки Принять/Отклонить.
        // Классический путь (Windows.UI.Notifications) через CommunityToolkit — работает в
        // unpackaged self-contained без рантайма уведомлений WinAppSDK. Подписка регистрирует
        // COM-активатор; ошибку не роняем в старт — без уведомлений приложение всё равно работает.
        try { ToastNotificationManagerCompat.OnActivated += OnToastActivated; }
        catch (Exception ex) { EnqueueLog("Уведомления недоступны: " + ex.Message); }

        // Отказ файла локальных контактов раньше уходил в никуда — см. LocalContacts.Log.
        LocalContacts.Log = EnqueueLog;

        // BLF: состояние линии коллеги. Событие приходит с потока ядра — маршалим в свой.
        // Состояние линий меняется часто (каждый звонок коллеги — минимум два NOTIFY),
        // поэтому в лог не пишем: состояние видно на пульте, а журнал бы захлебнулся.
        _linphone.DialogStateChanged += (ext, state) =>
            DispatcherQueue.TryEnqueue(() => FavoritesVm.SetDialogState(ext, state));

        try
        {
            _linphone.Start();
            EnqueueLog($"liblinphone {_linphone.CoreVersion}");
            RefreshHeader();
            RefreshBehaviorIndicators();
            ApplyTheme(_linphone.GetTheme());

            long.TryParse(_linphone.GetAppString("history_seen", "0"), out _missedSeen);
            UpdateMissedBadge();
            // Избранное грузим при старте, а не только при открытии вкладки: от него зависят
            // подписки BLF (presence + dialog). Иначе лампы оживают лишь после того, как
            // пользователь зайдёт на пульт, и первые секунды показывают неправду.
            PopulateFavorites();
            // Единственный за сеанс разбор журнала ради кнопки повтора; дальше номер
            // поддерживает воронка исходящих (CallStateChanged) — на клике ядро не трогаем.
            DialerVm.LastDialed = ViewModels.HistoryViewModel.LastDialed(_linphone.GetCallHistory());
            UpdateChatBadge();
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
            FavoritesVm.InitView();   // вид «Избранного» (список/BLF) — после старта ядра

            // iterate() больше НЕ на UI-потоке: ядро само крутит его на выделенном
            // потоке (см. LinphoneService/LinphoneCore) — DNS/TLS/sqlite не фризят окно.

            // Авто-переподключение при смене сети (Wi-Fi/VPN/Ethernet): дебаунс 2с, затем re-REGISTER.
            _netDebounce = DispatcherQueue.CreateTimer();
            _netDebounce.IsRepeating = false;
            _netDebounce.Interval = TimeSpan.FromSeconds(2);
            _netDebounce.Tick += (t, _) => { t.Stop(); EnqueueLog("Смена сети — переподключение"); _linphone.RefreshNetwork(); };
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // Видео: liblinphone создаёт окна рендера сам; хук перехватывает их в момент
            // создания и встраивает в экран звонка (иначе окно успевает мигнуть).
            _linphone.SetupVideo();
            EnsureVideoWindowHook();
        }
        catch (Exception ex)
        {
            EnqueueLog("ОШИБКА запуска Core: " + ex.Message);
            HeaderVm.StatusText = "Core не запущен";
            RegDot.Fill = new SolidColorBrush(Colors.Red);
        }

        SetMode(Mode.Dialer);
        ShowSection(Tab.Dialer);

        // Контакты из кэша (+ локальные) — чтобы имя показывалось при наборе сразу, не открывая вкладку.
        _allContacts = MergeContacts(Phonebook.LoadCached());

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

        // Проверку обновления откладываем: при старте важнее регистрация и звуковые устройства,
        // а окно должно успеть появиться — иначе ContentDialog не на чем показывать.
        var updateDelay = DispatcherQueue.CreateTimer();
        updateDelay.Interval = TimeSpan.FromSeconds(20);
        updateDelay.IsRepeating = false;
        updateDelay.Tick += (_, _) => CheckForUpdateAsync(manual: false);
        updateDelay.Start();
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
        try
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
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch { return false; }
    }

    private static string NormalizeDialTarget(string raw) => PhoneNumberUtils.NormalizeDialTarget(raw);

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

    // ---------------- Toast-уведомление о входящем ----------------
    private const string IncomingToastTag = "incoming";
    private const string IncomingToastGroup = "calls";

    /// <summary>Показывает toast о входящем с кнопками «Принять»/«Отклонить». Звук приглушён —
    /// рингтон проигрывает сам liblinphone, чтобы не было двойного звонка.</summary>
    private void ShowIncomingToast(string caller, string number)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")   // клик по телу — открыть окно
                .AddText(Loc.S("St_IncomingCall"))
                .AddText(caller);
            if (!string.IsNullOrEmpty(number)) builder.AddText(number);   // номер второй строкой, если есть имя
            var content = builder
                .AddButton(new ToastButton(Loc.S("BtnAccept.Content"), "action=accept"))
                .AddButton(new ToastButton(Loc.S("BtnDecline.Content"), "action=decline"))
                .GetToastContent();
            content.Scenario = ToastScenario.Reminder;        // не пропадает сам, ждёт реакции
            content.Audio = new ToastAudio { Silent = true };  // без своего звука — звонит liblinphone

            var toast = new ToastNotification(content.GetXml())
            {
                Tag = IncomingToastTag,
                Group = IncomingToastGroup,
            };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
        }
        catch (Exception ex) { EnqueueLog("Уведомление о входящем: " + ex.Message); }
    }

    private void ClearIncomingToast()
    {
        try { ToastNotificationManagerCompat.History.Remove(IncomingToastTag, IncomingToastGroup); }
        catch { }
    }

    /// <summary>Мигание кнопкой в панели задач на входящем (FLASHW_TIMERNOFG — до активации окна).</summary>
    private void FlashTaskbar(bool on)
    {
        try
        {
            var fi = new Interop.NativeMethods.FLASHWINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Interop.NativeMethods.FLASHWINFO>(),
                hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this),
                dwFlags = on ? Interop.NativeMethods.FLASHW_ALL | Interop.NativeMethods.FLASHW_TIMERNOFG
                             : Interop.NativeMethods.FLASHW_STOP,
            };
            Interop.NativeMethods.FlashWindowEx(ref fi);
        }
        catch { }
    }

    private const string ChatToastGroup = "chat";

    /// <summary>Toast о новом сообщении чата (когда окно скрыто или открыт другой чат).
    /// Клик открывает окно и нужную переписку. Тег = собеседник (одно уведомление на чат).</summary>
    private void ShowChatToast(string peer, string display, string text)
    {
        try
        {
            var content = new ToastContentBuilder()
                .AddArgument("action", "chat")
                .AddArgument("peer", peer)
                .AddText(display)
                .AddText(text)
                .GetToastContent();

            var toast = new ToastNotification(content.GetXml())
            {
                Tag = ToastTagSafe(peer),
                Group = ChatToastGroup,
            };
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
        }
        catch (Exception ex) { EnqueueLog("Уведомление чата: " + ex.Message); }
    }

    /// <summary>Tag для toast: только безопасные символы, не длиннее лимита WinRT (16 симв.).</summary>
    private static string ToastTagSafe(string s)
    {
        string t = new string((s ?? "").Where(char.IsLetterOrDigit).ToArray());
        return t.Length == 0 ? "peer" : (t.Length > 16 ? t[^16..] : t);
    }

    /// <summary>Нажатие на toast/его кнопки: принять, отклонить или открыть окно.</summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        string action = args.Contains("action") ? args["action"] : "open";
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (action)
            {
                case "accept":
                    _linphone.AcceptIncoming();
                    ShowFromTray();
                    BringToFront();
                    break;
                case "decline":
                    _linphone.DeclineIncoming();
                    break;
                case "chat":
                    ShowFromTray();
                    BringToFront();
                    string peer = args.Contains("peer") ? args["peer"] : "";
                    ShowSection(Tab.Chat);
                    if (peer.Length > 0) ChatVm.OpenConversation(peer);
                    else ChatVm.ShowList();
                    break;
                default: // "open" — показать окно с экраном входящего
                    ShowFromTray();
                    BringToFront();
                    break;
            }
            ClearIncomingToast();
        });
    }

    /// <summary>Автоответ на входящий после настроенной задержки (сек). Таймер отменяется,
    /// как только звонок покидает состояние «входящий» (см. OnCallStateChanged).</summary>
    private void StartAutoAnswer()
    {
        int.TryParse(_linphone.GetAppString("auto_answer_delay", "0"), out int delay);
        _autoAnswerTimer?.Stop();
        _autoAnswerTimer = DispatcherQueue.CreateTimer();
        _autoAnswerTimer.IsRepeating = false;
        _autoAnswerTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(delay, 0, 60));
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
        try
        {
            _durationTimer?.Stop();
            _autoAnswerTimer?.Stop();
            _netDebounce?.Stop();
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _settingsWindow?.Close();
            ClearIncomingToast();
            TrayIcon.Dispose();
            if (_videoWinHook != IntPtr.Zero) Interop.NativeMethods.UnhookWinEvent(_videoWinHook);
            _linphone.Dispose();
        }
        catch { /* при выходе ошибки очистки не важны */ }
        FlushLog();
        Environment.Exit(0);
    }

    /// <summary>Смена сети (часто несколько событий) — рестартуем дебаунс, по истечении переподключаемся.</summary>
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => { _netDebounce?.Stop(); _netDebounce?.Start(); });

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
        int n = _linphone.GetMissedSince(_missedSeen);
        MissedBadgeText.Text = n > 99 ? "99+" : n.ToString();
        MissedBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrayIndicator();
    }

    private bool _trayAlert;   // текущее состояние иконки трея (с точкой/без), чтобы не пересоздавать зря

    /// <summary>Красная точка на иконке в трее + подсказка, когда есть пропущенные или непрочитанные.</summary>
    private void UpdateTrayIndicator()
    {
        int missed = _linphone.GetMissedSince(_missedSeen);
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
        _allContacts = MergeContacts(Phonebook.LoadCached());
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

        ContentDialogResult result = await dialog.ShowAsync();
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
        if (e.ClickedItem is ContactEntry c) PrefillDialer(c.CallNumber);
    }

    // ---------------- Избранное (локальные пометки + статус присутствия) ----------------
    private void PopulateFavorites() => FavoritesVm.Load();

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
    private void ApplyMyStatus(PresenceStatus s)
    {
        if (s != PresenceStatus.DoNotDisturb) _statusBeforeDnd = s;
        _linphone.SetMyStatus(s);
        _linphone.SetAppString("presence_status", ((int)s).ToString());
        _linphone.SetAppFlag("dnd", s == PresenceStatus.DoNotDisturb);
        NotifyPbxPresence(s);   // сообщить АТС (CustomPresence через pbx-admin)
        RefreshBehaviorIndicators();   // бейдж DND в шапке
        UpdateStatusLine();   // статус в шапке = выбранный presence (когда онлайн)
    }

    // ---------------- Статус-хук для АТС ----------------
    // Asterisk не принимает SIP PUBLISH presence («No registered publish handler for event
    // presence» в его логе), поэтому о смене ручного статуса сообщаем HTTP-запросом в
    // pbx-admin (GET /prov/status?ext=N&state=available|away|dnd) — тем же механизмом, что
    // Action URL на настольных Fanvil. Дальше АТС сама: ставит CustomPresence (виден
    // коллегам в BLF/избранном), dnd дополнительно заворачивает звонки в голосовую почту,
    // away — только показ, звонки проходят.
    private static readonly System.Net.Http.HttpClient HookHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    // Запросы хука сериализуем: раньше _lastHookState писался ДО await, и два быстрых
    // переключения статуса гонялись за ним — на АТС мог остаться устаревший. Теперь один
    // «рабочий» цикл шлёт всегда САМЫЙ свежий желаемый статус.
    private readonly System.Threading.SemaphoreSlim _hookGate = new(1, 1);
    private string? _lastHookSent;      // что реально ушло на АТС (для отсева дублей)
    private string? _hookDesired;       // последний желаемый статус (перезаписывается на лету)

    private void NotifyPbxPresence(PresenceStatus s)
    {
        if (PbxHook.HookState(s) is not { } state) return;   // авто-статусы не шлём
        _hookDesired = state;
        _ = PumpPresenceHookAsync();
    }

    /// <summary>Отправляет на АТС последний желаемый статус; параллельные вызовы сливаются
    /// в один цикл (SemaphoreSlim). Дошедший статус запоминаем, дубли не шлём.</summary>
    private async System.Threading.Tasks.Task PumpPresenceHookAsync()
    {
        if (!await _hookGate.WaitAsync(0)) return;   // цикл уже идёт — он подхватит _hookDesired
        try
        {
            string tpl = _linphone.GetAppString("presence_hook_url", "");
            if (tpl.Length == 0) return;
            string ext = _linphone.GetDefaultAccountUsername() ?? "";
            if (ext.Length == 0) return;
            string secret = _linphone.GetPresenceHookSecret();

            // Пока шлём один статус, пользователь мог переключить ещё раз — добираем свежий.
            while (_hookDesired is { } state && state != _lastHookSent)
            {
                _lastHookSent = state;
                string url = PbxHook.BuildUrl(tpl, ext, state, secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                try
                {
                    using var resp = await HookHttp.GetAsync(url);
                    EnqueueLog($"Статус-хук АТС ({state}): HTTP {(int)resp.StatusCode}");
                    if (!resp.IsSuccessStatusCode) _lastHookSent = null;   // 401/403 — повторим при след. смене
                }
                catch (Exception ex)
                {
                    _lastHookSent = null;   // не дошло — при следующей смене попробуем снова
                    EnqueueLog("Статус-хук АТС не сработал: " + ex.Message);
                    break;
                }
            }
        }
        finally { _hookGate.Release(); }
    }

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
    private void OnHistoryFavToggle(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CallLogEntry h && h.Remote.Length > 0)
        {
            Favorites.Toggle(DisplayName(h.Remote), h.Remote);
            PopulateHistory();
        }
    }

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
        UpdateInCallHost();   // вкладка «Набор» показывает дайлер ИЛИ экран разговора
        ContactsSection.Visibility = tab == Tab.Contacts ? Visibility.Visible : Visibility.Collapsed;
        FavoritesSection.Visibility = tab == Tab.Favorites ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = tab == Tab.History ? Visibility.Visible : Visibility.Collapsed;
        ChatSection.Visibility = tab == Tab.Chat ? Visibility.Visible : Visibility.Collapsed;

        NavDial.Foreground = tab == Tab.Dialer ? _activeTab : _inactiveTab;
        NavContacts.Foreground = tab == Tab.Contacts ? _activeTab : _inactiveTab;
        NavFavorites.Foreground = tab == Tab.Favorites ? _activeTab : _inactiveTab;
        NavHistory.Foreground = tab == Tab.History ? _activeTab : _inactiveTab;
        NavChat.Foreground = tab == Tab.Chat ? _activeTab : _inactiveTab;
    }

    private bool _incoming;   // показывается полноэкранный экран входящего (не во время разговора)

    /// <summary>На вкладке «Набор»: дайлер / полноэкранный входящий / полноэкранный разговор.</summary>
    private void UpdateInCallHost()
    {
        bool dialerTab = _currentTab == Tab.Dialer;
        InCallSection.Visibility = dialerTab && _inCall ? Visibility.Visible : Visibility.Collapsed;
        IncomingSection.Visibility = dialerTab && _incoming && !_inCall ? Visibility.Visible : Visibility.Collapsed;
        DialerSection.Visibility = dialerTab && !_inCall && !_incoming ? Visibility.Visible : Visibility.Collapsed;
        UpdateVideoOverlayVisibility();   // ушли с экрана разговора — спрятать оверлеи видео
    }

    /// <summary>Оверлеи видео видимы только вместе с экраном разговора и областью видео.</summary>
    private void UpdateVideoOverlayVisibility()
    {
        bool show = AppWindow.IsVisible && InCallSection.Visibility == Visibility.Visible
                    && VideoArea.Visibility == Visibility.Visible;
        _remoteVideo?.SetVisible(show);
        _previewVideo?.SetVisible(show);
    }

    /// <summary>Окно двигали/меняли размер/прятали — синхронизируем оверлеи видео.</summary>
    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender,
                                    Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange)
        {
            _remoteVideo?.UpdateBounds(force: true);
            _previewVideo?.UpdateBounds(force: true);
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
            (LastDialedButton, "St_LastDialed"),
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
        HeaderVm.DndActive = _linphone.GetAppFlag("dnd", false);
        HeaderVm.AutoAnswerActive = _linphone.GetAppFlag("auto_answer", false);
        ToolTipService.SetToolTip(DndBadge, Loc.S("St_DndActive"));
        ToolTipService.SetToolTip(AutoAnswerBadge, Loc.S("St_AutoAnswerActive"));
        ToolTipService.SetToolTip(LastDialedButton, Loc.S("St_LastDialed"));
    }

    // ---------------- Журнал ----------------
    private void PopulateHistory() => HistoryVm.Load();

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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
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
        if (DialerVm.Number.Trim().Length == 0) { EnqueueLog("Введи номер."); return; }
        DialerVm.Call();
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

    private void OnBackspaceClick(object sender, RoutedEventArgs e) => DialerVm.Backspace();

    // Повтор последнего набранного: номер уже в DialerVm (ставится при старте и на каждом
    // исходящем), поэтому клик не ходит в ядро — иначе на UI-потоке синхронно ждали бы поток
    // ядра и разбирали весь журнал ради одного номера. Кнопки нет, если повторять нечего.
    private void OnLastDialedClick(object sender, RoutedEventArgs e) => PrefillDialer(DialerVm.LastDialed);

    // Нормализация номеров/адресов — в PhoneNumberUtils (единый источник). Локальные псевдонимы
    // оставлены, чтобы не переписывать десятки коротких call sites.
    private static string DigitsOnly(string? s) => PhoneNumberUtils.DigitsOnly(s);
    private static string SipUser(string? address) => PhoneNumberUtils.SipUser(address);

    /// <summary>Ищет контакт по совпадению цифр номера (Telephone или Mobile). Понимает SIP-адрес.</summary>
    private ContactEntry? FindContact(string number)
    {
        string n = DigitsOnly(SipUser(number));
        if (n.Length == 0) return null;
        return _allContacts.FirstOrDefault(c => DigitsOnly(c.Number) == n || DigitsOnly(c.Mobile) == n);
    }

    /// <summary>Имя контакта по адресу; если в книге нет — чистый номер (без sip:/скобок/имени).</summary>
    private string DisplayName(string remote)
    {
        if (FindContact(remote)?.Name is { Length: > 0 } name) return name;
        string user = SipUser(remote);
        return user.Length > 0 ? user : Pretty(remote);
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        bool muted = _linphone.ToggleMute();
        UpdateMuteVisuals(muted);
    }

    /// <summary>Индикация выключенного микрофона: перечёркнутая иконка на кнопке
    /// и красный бейдж «Без звука» в шапке (как у DND) — чтобы не забыть про мьют.</summary>
    private void UpdateMuteVisuals(bool muted)
    {
        MuteLabel.Text = VideoMuteLabel.Text = muted ? Loc.S("St_Unmute") : Loc.S("St_Mic");
        MuteIcon.Glyph = VideoMuteIcon.Glyph = muted ? "" : "";   // микрофон перечёркнутый / обычный
        HeaderVm.Muted = muted;   // бейдж «Без звука» в шапке
    }

    private void OnHoldClick(object sender, RoutedEventArgs e)
    {
        bool held = _linphone.ToggleHold();
        HoldLabel.Text = held ? Loc.S("St_Resume") : Loc.S("St_Hold");
    }

    private void OnSwapClick(object sender, RoutedEventArgs e) => _linphone.SwapCalls();

    private bool _confInvitePending;   // пригласили третьего кнопкой — объединить, как только ответит

    private async void OnConferenceClick(object sender, RoutedEventArgs e)
    {
        // Два звонка уже есть — просто объединяем.
        if (_linphone.CallCount > 1)
        {
            if (_linphone.MergeCallsIntoConference())
            {
                EnqueueLog("Конференция: звонки объединены");
                CallVm.RemoteName = Loc.S("St_Conference");
                CallVm.StatusText = Loc.S("St_InCall");
                UpdateSwapButton();
            }
            else EnqueueLog("Не удалось создать конференцию");
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        string target = input.Text.Trim();
        if (target.Length == 0) return;
        _confInvitePending = true;
        _linphone.HoldCurrent();
        _linphone.Call(target);
        EnqueueLog($"Конференция: приглашаю {target}");
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        // Путь файла задаётся в момент установки звонка (record_file в параметрах вызова),
        // иначе рекордер не попадает в медиаграф и запись не пишется на диск.
        bool recording = _linphone.ToggleRecording();
        UpdateRecordButton(recording);
        if (recording) EnqueueLog("Запись разговора: " + (_linphone.RecordPath ?? ""));
    }

    private void UpdateRecordButton(bool recording)
    {
        RecordDot.Fill = new SolidColorBrush(recording ? Windows.UI.Color.FromArgb(255, 255, 59, 48) : Colors.Gray);
        RecordLabel.Text = Loc.S(recording ? "St_RecordStop" : "St_Record");
    }

    /// <summary>«Переключить» — при двух необъединённых звонках. Кнопка конференции видна всегда,
    /// кроме готовой конференции: при одном звонке это «Пригласить», при двух — «Конференция».</summary>
    private void UpdateSwapButton()
    {
        bool conf = _linphone.IsInConference;
        bool multi = _linphone.CallCount > 1;
        SwapStack.Visibility = multi && !conf ? Visibility.Visible : Visibility.Collapsed;
        ConferenceStack.Visibility = conf ? Visibility.Collapsed : Visibility.Visible;
        ConferenceLabel.Text = Loc.S(multi ? "LblConference.Text" : "LblInvite.Text");
    }

    private async void OnTransferClick(object sender, RoutedEventArgs e)
    {
        // Ввод номера с подсказками из адресной книги (не только с клавиатуры).
        var input = new AutoSuggestBox
        {
            PlaceholderText = Loc.S("St_NumberOrSip"),
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        input.TextChanged += (s, a) =>
        {
            if (a.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            string q = input.Text.Trim();
            string digits = DigitsOnly(q);
            input.ItemsSource = q.Length < 2 ? null : _allContacts.Where(c =>
                    c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                    (digits.Length > 0 && (DigitsOnly(c.Number).Contains(digits) || DigitsOnly(c.Mobile).Contains(digits))))
                .Take(6).Select(c => $"{c.Name} · {c.CallNumber}").ToList();
        };
        var dialog = new ContentDialog
        {
            Title = Loc.S("St_TransferTitle"),
            Content = input,
            PrimaryButtonText = Loc.S("St_TransferDo"),
            CloseButtonText = Loc.S("St_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        // Есть удержанный звонок — предлагаем сопровождаемый перевод (соединить собеседников).
        if (_linphone.CallCount > 1)
            dialog.SecondaryButtonText = Loc.S("St_TransferToHeld");
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            bool okAttended = _linphone.TransferToHeldCall();
            EnqueueLog(okAttended ? "Сопровождаемый перевод: соединяю собеседников"
                                  : "Не удалось выполнить сопровождаемый перевод");
            return;
        }
        if (result != ContentDialogResult.Primary) return;
        // Из подсказки «Имя · номер» берём номер после разделителя.
        string picked = input.Text.Trim();
        int sep = picked.LastIndexOf(" · ", StringComparison.Ordinal);
        string target = sep >= 0 ? picked[(sep + 3)..].Trim() : picked;
        if (target.Length == 0) return;
        _lastTransferTarget = target;   // для перенабора, если перевод сорвётся
        bool ok = _linphone.Transfer(target);
        EnqueueLog(ok ? $"Перевод звонка на {target}" : "Не удалось перевести звонок");
    }

    private string? _lastTransferTarget;   // последняя цель blind-перевода (для перенабора при провале)

    private void OnVideoClick(object sender, RoutedEventArgs e)
    {
        // «Видео» = моя камера. Видео собеседника рендерится в области VideoSurface
        // прямо на экране звонка и при выключении камеры не пропадает.
        _videoOn = _linphone.ToggleVideo();
        UpdateVideoButton();
        UpdateVideoLayout();   // включили камеру — показать область/превью не дожидаясь re-INVITE
    }

    // ---------------- Видео в окне звонка ----------------
    // MSOGL рисует в СВОИ окна (class 'Video'); мы «усыновляем» их внутрь экрана звонка
    // (NativeVideoAdopter): первое найденное — большая область, второе (превью) — угол.
    // XAML рисовать поверх усыновлённых окон не может (airspace).
    private NativeVideoAdopter? _remoteVideo;
    private NativeVideoAdopter? _previewVideo;
    private IntPtr _videoWinHook;
    private Interop.NativeMethods.WinEventProc? _videoWinHookProc;   // держим от GC

    /// <summary>Ловит СОЗДАНИЕ/ПОКАЗ окон рендера MSOGL и мгновенно встраивает их в область
    /// видео. Опрос таймером тут не годится: MSOGL показывает своё окно сразу после создания,
    /// и большое отдельное окно успевало мигнуть на экране.
    /// Хук ставится один раз на процесс и живёт до выхода — окна пересоздаются на каждом
    /// re-INVITE (включение видео с любой стороны).</summary>
    private void EnsureVideoWindowHook()
    {
        _remoteVideo ??= new NativeVideoAdopter(this, VideoSurface);
        _previewVideo ??= new NativeVideoAdopter(this, PreviewSurface, topmostSibling: true);
        if (_videoWinHook != IntPtr.Zero) return;

        _videoWinHookProc = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject != Interop.NativeMethods.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
            if (!_inCall) return;   // вне разговора превью проверки камеры (настройки) — своим окном
            if (!NativeVideoAdopter.IsVideoWindow(hwnd)) return;
            AdoptVideoWindow(hwnd);
        };
        _videoWinHook = Interop.NativeMethods.SetWinEventHook(
            Interop.NativeMethods.EVENT_OBJECT_CREATE, Interop.NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _videoWinHookProc, (uint)Environment.ProcessId, 0,
            Interop.NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Первое окно видео — область собеседника, второе — превью своей камеры.</summary>
    private void AdoptVideoWindow(IntPtr hwnd)
    {
        if (hwnd == _remoteVideo!.Adopted || hwnd == _previewVideo!.Adopted) return;
        if (!_remoteVideo.IsAlive) { _remoteVideo.Adopt(hwnd); EnqueueLog("Видео: окно собеседника встроено"); }
        else if (!_previewVideo.IsAlive) { _previewVideo.Adopt(hwnd); EnqueueLog("Видео: превью камеры встроено"); }
        UpdateVideoOverlayVisibility();
    }

    /// <summary>Подобрать окна, созданные до установки хука (например, до входа в разговор).</summary>
    private void AdoptExistingVideoWindows()
    {
        foreach (IntPtr wnd in NativeVideoAdopter.FindVideoWindows()) AdoptVideoWindow(wnd);
    }

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
        // вся верхняя сетка управления скрыта (видео занимает её место).
        InCallButtons.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        VideoMuteStack.Visibility = VideoCamStack.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active && InCallKeypad.Visibility == Visibility.Visible)
            InCallKeypad.Visibility = Visibility.Collapsed;   // DTMF-клавиатура несовместима с видео-режимом
        if (active) UpdateVideoButton();   // подсветка кнопки видео — на её видео-двойнике

        if (active) AdoptExistingVideoWindows();   // окна ловит хук; подбираем созданные до входа
        else
        {
            _remoteVideo?.Release();
            _previewVideo?.Release();
        }
        UpdateVideoOverlayVisibility();
    }

    private void UpdateVideoButton()
    {
        // Кнопка есть в двух местах: сетка управления (аудио) и ряд у сброса (видео).
        foreach (var btn in new[] { VideoButton, VideoCamButton })
        {
            if (_videoOn)
                btn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 168, 74)); // зелёный = камера вкл
            else
                btn.ClearValue(Button.BackgroundProperty); // вернуть тематический цвет клавиш
        }
    }

    // ---------------- События Core ----------------
    private bool _regOnline;                 // зарегистрированы (Ok) — тогда статус = presence
    private string _regOfflineText = "";      // что показывать, когда НЕ онлайн
    private Windows.UI.Color _regOfflineColor = Colors.Gray;

    private void OnRegistrationStateChanged(LinphoneRegistrationState state, string? message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EnqueueLog($"Регистрация: {state} {(message is null ? "" : "(" + message + ")")}");
            _regOnline = state == LinphoneRegistrationState.Ok;
            (_regOfflineText, _regOfflineColor) = state switch
            {
                LinphoneRegistrationState.Progress => (Loc.S("St_Registering"), Colors.Goldenrod),
                LinphoneRegistrationState.Failed => (Loc.S("St_ErrorPrefix") + message, Colors.IndianRed),
                LinphoneRegistrationState.Cleared => (Loc.S("St_Offline"), Colors.Gray),
                _ => (Loc.S("St_NotConnected"), Colors.Gray),
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
            RegDot.Fill = new SolidColorBrush(StatusColor(s));   // единственная точка = цвет статуса
        }
        else
        {
            HeaderVm.StatusText = _regOfflineText;
            RegDot.Fill = new SolidColorBrush(_regOfflineColor);
        }
    }

    /// <summary>Цвет точки статуса под presence: зелёный/красный/жёлтый/серый.</summary>
    private static Windows.UI.Color StatusColor(PresenceStatus s) => s switch
    {
        PresenceStatus.Available => Colors.LimeGreen,
        PresenceStatus.DoNotDisturb or PresenceStatus.OnThePhone => Colors.IndianRed,
        PresenceStatus.Offline => Colors.Gray,
        _ => Colors.Goldenrod,   // Lunch/Away/Meeting/Offsite — «отошёл»
    };

    /// <summary>Результат перевода звонка (REFER): раньше «Перевод отправлен» показывался всегда,
    /// даже если абонент не дозвонился до цели. Теперь показываем реальный итог.</summary>
    private void OnTransferResult(bool success)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
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
                if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                    OnTransferClick(this, new RoutedEventArgs());   // снова диалог перевода
                return;
            }
            CallEndReasonText.Text = Loc.S(success ? "St_TransferDone" : "St_TransferFailed");
            CallEndReasonText.Visibility = Visibility.Visible;
            _reasonTimer?.Stop();
            _reasonTimer ??= CreateReasonTimer();
            _reasonTimer.Start();
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
            RegDot.Fill = new SolidColorBrush(Colors.IndianRed);
        });
    }

    private void OnCallStateChanged(CallEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EnqueueLog($"Звонок: {e.State} {(e.Remote is null ? "" : "← " + e.Remote)}");
            string remote = Pretty(e.Remote);

            // Автоответ отменяем и убираем toast/баннер/мигание, как только звонок покидает «входящий».
            if (e.State != LinphoneCallState.IncomingReceived)
            {
                _autoAnswerTimer?.Stop();
                ClearIncomingToast();
                FlashTaskbar(false);
                IncomingBanner.Visibility = Visibility.Collapsed;
            }

            switch (e.State)
            {
                case LinphoneCallState.IncomingReceived:
                    CancelRedial();   // пришёл входящий — автодозвон не должен ему мешать
                    // «Не беспокоить» — отклоняем входящий «занято», окно не трогаем.
                    if (_linphone.GetAppFlag("dnd", false))
                    {
                        EnqueueLog("Не беспокоить: входящий отклонён");
                        _linphone.DeclineBusy();
                        break;
                    }
                    string caller = DisplayName(remote);
                    string number = SipUser(e.Remote ?? remote);   // чистый номер абонента
                    bool named = !string.IsNullOrEmpty(number) && number != caller;  // имя нашлось в книге
                    if (_inCall)
                    {
                        // ВТОРОЙ входящий во время разговора: раньше SetMode(Incoming) прятал
                        // экран активного звонка — управлять текущим разговором было нельзя.
                        // Теперь — компактный баннер поверх любой вкладки, звонок остаётся на экране.
                        IncomingBannerText.Text = caller + (named ? " · " + number : "");
                        IncomingBanner.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        IncomingName.Text = caller;
                        IncomingNumber.Text = named ? number : "";
                        IncomingAvatar.Text = Initials(caller);
                        ShowSection(Tab.Dialer);
                        SetMode(Mode.Incoming);   // полноэкранный экран входящего (круглые кнопки внизу)
                    }
                    if (AppWindow.IsVisible)
                    {
                        // Окно есть, но может быть неактивно/перекрыто/свёрнуто — без индикации
                        // звонок замечали только по динамику. Мигаем в панели задач (до возврата
                        // в окно) и дублируем toast'ом с кнопками, если окно не на переднем плане.
                        FlashTaskbar(true);
                        if (Interop.NativeMethods.GetForegroundWindow()
                                != WinRT.Interop.WindowNative.GetWindowHandle(this))
                            ShowIncomingToast(caller, named ? number : "");
                        if (_linphone.GetAppFlag("incoming_to_front", false)) BringToFront();
                    }
                    else
                    {
                        // Окно свёрнуто в трей — показываем только уведомление, окно не разворачиваем.
                        ShowIncomingToast(caller, named ? number : "");
                    }
                    if (_linphone.GetAppFlag("auto_answer", false)) StartAutoAnswer();
                    break;

                case LinphoneCallState.OutgoingInit:
                case LinphoneCallState.OutgoingProgress:
                case LinphoneCallState.OutgoingRinging:
                case LinphoneCallState.OutgoingEarlyMedia:
                case LinphoneCallState.Connected:
                    if (!e.IsIncoming && e.Remote is { Length: > 0 })
                    {
                        _lastOutgoingRemote = e.Remote;   // цель для автодозвона при «занято»
                        DialerVm.LastDialed = e.Remote;   // и он же — «повтор последнего» на дайлере
                    }
                    // Звоним (вручную или автодозвоном) — счётчик прячем; звонок на ДРУГОЙ номер
                    // означает, что пользователь передумал: цикл автодозвона сбрасываем целиком.
                    _redialTimer?.Stop();
                    RedialPanel.Visibility = Visibility.Collapsed;
                    if (!e.IsIncoming && _redialTarget != null && e.Remote != null
                        && !SipUser(e.Remote).Equals(SipUser(_redialTarget), StringComparison.OrdinalIgnoreCase))
                        CancelRedial();
                    ShowCall(remote, Loc.S("St_Calling"));
                    SetCallControlsEnabled(false);
                    break;

                case LinphoneCallState.StreamsRunning:
                    CancelRedial();   // дозвонились — цикл завершён
                    // Приглашённый в конференцию ответил — объединяем звонки автоматически.
                    if (_confInvitePending && _linphone.CallCount > 1)
                    {
                        _confInvitePending = false;
                        EnqueueLog(_linphone.MergeCallsIntoConference()
                            ? "Конференция: звонки объединены"
                            : "Не удалось создать конференцию — звонки остались раздельными");
                    }
                    ShowCall(remote, Loc.S("St_InCall"));
                    if (_linphone.IsInConference) CallVm.RemoteName = Loc.S("St_Conference");
                    SetCallControlsEnabled(true);
                    StartDurationTimer();
                    _videoOn = _linphone.IsCameraOn();
                    UpdateVideoButton();
                    UpdateVideoLayout();   // видео могло включиться/выключиться после re-INVITE
                    // Автозапись: включена в настройках и запись ещё не идёт — стартуем сами.
                    if (_linphone.GetAppFlag("auto_record", false) && !_linphone.IsRecording
                        && _linphone.ToggleRecording())
                        EnqueueLog("Автозапись разговора: " + (_linphone.RecordPath ?? ""));
                    UpdateRecordButton(_linphone.IsRecording);
                    UpdateSwapButton();
                    _linphone.PublishOnThePhone(true);   // для коллег — «Разговаривает»
                    break;

                case LinphoneCallState.Pausing:
                case LinphoneCallState.Paused:
                    CallVm.StatusText = Loc.S("St_OnHold");
                    UpdateSwapButton();
                    break;

                case LinphoneCallState.PausedByRemote:
                    CallVm.StatusText = Loc.S("St_HeldByRemote");
                    break;

                case LinphoneCallState.End:
                case LinphoneCallState.Released:
                case LinphoneCallState.Error:
                    // Любое завершение отменяет незавершённое приглашение в конференцию:
                    // либо сорвался приглашённый (занято), либо умер исходный разговор.
                    _confInvitePending = false;
                    if (e.State == LinphoneCallState.Error)
                        EnqueueLog($"Звонок завершён с ошибкой: {e.Message}");
                    StopDurationTimer();
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
                        TryStartRedial(e);                    // «занято» + включён автодозвон → отсчёт
                    }
                    UpdateMissedBadge();   // мог добавиться пропущенный — обновим бейдж
                    break;
            }
        });
    }

    private void ShowCall(string remote, string status)
    {
        CallVm.RemoteName = DisplayName(remote);
        CallVm.StatusText = status;
        SetMode(Mode.InCall);
        Title = $"{DisplayName(remote)} — L-Phone";   // видно в таскбаре, пока идёт звонок
    }

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
        CallEndReasonText.Text = Loc.S(key);
        CallEndReasonText.Visibility = Visibility.Visible;
        _reasonTimer?.Stop();
        _reasonTimer ??= CreateReasonTimer();
        _reasonTimer.Start();
    }

    /// <summary>Одноразовый таймер, гасящий строку под кнопкой вызова (причина сбоя / итог перевода) через 6 с.</summary>
    private DispatcherQueueTimer CreateReasonTimer()
    {
        var t = DispatcherQueue.CreateTimer();
        t.IsRepeating = false;
        t.Interval = TimeSpan.FromSeconds(6);
        t.Tick += (timer, _) => { timer.Stop(); CallEndReasonText.Visibility = Visibility.Collapsed; };
        return t;
    }

    // ---------------- Автодозвон при «занято» ----------------
    private DispatcherQueueTimer? _redialTimer;   // тикает раз в секунду, ведёт обратный отсчёт
    private string? _redialTarget;                // кому дозваниваемся (полный адрес)
    private string? _lastOutgoingRemote;          // адрес последнего исходящего звонка
    private int _redialSecondsLeft;
    private int _redialAttempt;

    /// <summary>Исходящий не удался с «занято», автодозвон включён — запускаем обратный отсчёт
    /// до повторного набора. Цикл живёт до успеха (StreamsRunning), отмены пользователем,
    /// звонка на другой номер или входящего звонка.</summary>
    private void TryStartRedial(CallEvent e)
    {
        if (e.IsIncoming) return;
        if (e.Reason is not (LinphoneReason.Busy or LinphoneReason.DoNotDisturb)) return;
        if (!_linphone.GetAppFlag("auto_redial", false)) return;
        string target = e.Remote ?? _lastOutgoingRemote ?? "";
        if (target.Length == 0) return;

        // Тот же номер — следующая попытка; новый — цикл заново. (End и Released приходят
        // друг за другом: пока идёт отсчёт по этому номеру, второй запуск не накручиваем.)
        if (_redialTarget != null && SipUser(_redialTarget).Equals(SipUser(target), StringComparison.OrdinalIgnoreCase))
        {
            if (RedialPanel.Visibility == Visibility.Visible) return; // отсчёт уже идёт (дубль End→Released)
            _redialAttempt++;
        }
        else
        {
            _redialTarget = target;
            _redialAttempt = 1;
        }

        int.TryParse(_linphone.GetAppString("auto_redial_delay", "30"), out int delay);
        if (delay < 5) delay = 5;
        _redialSecondsLeft = delay;

        CallEndReasonText.Visibility = Visibility.Collapsed;   // счётчик сам говорит «занято»
        UpdateRedialText();
        RedialPanel.Visibility = Visibility.Visible;
        if (_redialTimer == null)
        {
            _redialTimer = DispatcherQueue.CreateTimer();
            _redialTimer.IsRepeating = true;
            _redialTimer.Interval = TimeSpan.FromSeconds(1);
            _redialTimer.Tick += (_, _) => OnRedialTick();
        }
        _redialTimer.Start();
        EnqueueLog($"Автодозвон: занято, попытка {_redialAttempt}, повтор через {delay} с");
    }

    private void OnRedialTick()
    {
        if (--_redialSecondsLeft > 0)
        {
            UpdateRedialText();
            return;
        }
        _redialTimer!.Stop();
        RedialPanel.Visibility = Visibility.Collapsed;
        if (_redialTarget is { Length: > 0 } target)
        {
            EnqueueLog($"Автодозвон: набираю {Pretty(target)}");
            _linphone.Call(target);
        }
    }

    private void UpdateRedialText() =>
        RedialText.Text = string.Format(Loc.S("St_RedialIn"), _redialSecondsLeft, _redialAttempt);

    private void CancelRedial()
    {
        _redialTimer?.Stop();
        _redialTarget = null;
        _redialAttempt = 0;
        RedialPanel.Visibility = Visibility.Collapsed;
    }

    private void OnRedialCancelClick(object sender, RoutedEventArgs e)
    {
        EnqueueLog("Автодозвон отменён");
        CancelRedial();
    }

    // ---------------- Режимы экрана ----------------
    private void SetMode(Mode mode)
    {
        bool wasInCall = _inCall;
        _inCall = mode == Mode.InCall;
        _incoming = mode == Mode.Incoming;
        // Точки DTMF сбрасываем на входе в разговор и при его завершении (но не на смене статуса внутри).
        if (_inCall != wasInCall) ResetDtmf();

        DialerTop.Visibility = mode == Mode.Dialer ? Visibility.Visible : Visibility.Collapsed;
        DialActions.Visibility = mode == Mode.Dialer ? Visibility.Visible : Visibility.Collapsed;
        Dialpad.Visibility = mode == Mode.Dialer ? Visibility.Visible : Visibility.Collapsed;

        // Свежий экран разговора всегда открывается кнопками, а не DTMF-клавиатурой.
        if (_inCall && !wasInCall)
        {
            InCallKeypad.Visibility = Visibility.Collapsed;
            InCallButtons.Visibility = Visibility.Visible;
        }
        UpdateInCallHost();   // дайлер ↔ полноэкранный звонок на вкладке «Набор»
        UpdateVideoLayout();  // вход/выход из разговора: показать или спрятать область видео
    }

    /// <summary>Кнопка «Клавиши» / «Скрыть»: переключение сетки управления и DTMF-клавиатуры.</summary>
    private void OnKeypadToggleClick(object sender, RoutedEventArgs e)
    {
        bool showKeypad = InCallKeypad.Visibility == Visibility.Collapsed;
        InCallKeypad.Visibility = showKeypad ? Visibility.Visible : Visibility.Collapsed;
        InCallButtons.Visibility = showKeypad ? Visibility.Collapsed : Visibility.Visible;
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
    }

    // ---------------- Таймер длительности ----------------
    private void StartDurationTimer()
    {
        if (_durationTimer == null)
        {
            _durationTimer = DispatcherQueue.CreateTimer();
            _durationTimer.Interval = TimeSpan.FromSeconds(1);
            _durationTimer.Tick += (_, _) =>
            {
                int sec = _linphone.CurrentCallDuration;
                string t = $"{sec / 60:00}:{sec % 60:00}";
                CallVm.StatusText = t;
                Title = $"{t} · {CallVm.RemoteName} — L-Phone";   // таймер в таскбаре
            };
        }
        _durationTimer.Start();
    }

    private void StopDurationTimer() => _durationTimer?.Stop();

    // ---------------- Утилиты ----------------
    private static string Pretty(string? sipAddress) => PhoneNumberUtils.Pretty(sipAddress);

    private static string Initials(string value)
    {
        string s = Pretty(value);
        int at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        return s.Length >= 2 ? s[..2] : s;
    }

    // ---------------- Журнал приложения (app.log) ----------------
    // Пишем асинхронно: синхронный AppendAllText на каждое событие блокировал UI-поток
    // (диск/антивирус), а событий при звонке много. Канал + один фоновый писатель,
    // строки пачками, ротация по 4 МБ (app.log → app.log.1).
    private const long LogMaxBytes = 4 * 1024 * 1024;
    private static readonly System.Threading.Channels.Channel<string> _logChannel =
        System.Threading.Channels.Channel.CreateUnbounded<string>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
    private static readonly System.Threading.Tasks.Task _logWriter =
        System.Threading.Tasks.Task.Run(LogWriterLoop);

    private void EnqueueLog(string message) =>
        _logChannel.Writer.TryWrite($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {MaskNumbers(message)}");

    // Приватность app.log (уходит в саппорт): внешние номера маскируем в середине,
    // оставляя начало/конец для идентификации звонка (+74951234567 → +7495***67).
    // Короткие внутренние номера (3–6 цифр) — не ПД, не трогаем. Полная SIP-трасса
    // при необходимости есть в liblinphone.log (техлог, лежит отдельно).
    private static readonly System.Text.RegularExpressions.Regex LongDigits =
        new(@"\d{7,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static string MaskNumbers(string s) => LongDigits.Replace(s, m =>
    {
        string d = m.Value;
        return d.Length <= 6 ? d : d[..4] + new string('*', d.Length - 6) + d[^2..];
    });

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
        try { if (await ask.ShowAsync() != ContentDialogResult.Primary) return; }
        catch { return; }

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

    private async System.Threading.Tasks.Task ShowInfoDialogAsync(string title, string text)
    {
        try
        {
            await new ContentDialog
            {
                Title = title,
                Content = text,
                CloseButtonText = Loc.S("St_Close"),
                XamlRoot = Content.XamlRoot,
            }.ShowAsync();
        }
        catch { }
    }

    /// <summary>Шапка лога при старте — версия приложения и ОС (для разборов у саппорта).</summary>
    private static void WriteLogHeader()
    {
        string ver = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
        _logChannel.Writer.TryWrite(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === L-Phone {ver} · {Environment.OSVersion.VersionString} · .NET {Environment.Version} ===");
    }

    private static async System.Threading.Tasks.Task LogWriterLoop()
    {
        var reader = _logChannel.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogFilePath)!);
                var fi = new System.IO.FileInfo(LogFilePath);
                if (fi.Exists && fi.Length > LogMaxBytes)
                {
                    string old = LogFilePath + ".1";
                    try { System.IO.File.Delete(old); } catch { }
                    try { System.IO.File.Move(LogFilePath, old); } catch { }
                }
                using var sw = new System.IO.StreamWriter(LogFilePath, append: true);
                while (reader.TryRead(out string? line)) sw.WriteLine(line);
            }
            catch { /* журнал не должен ронять приложение */ }
        }
    }

    /// <summary>Дописать накопившиеся строки перед завершением процесса.</summary>
    private static void FlushLog()
    {
        _logChannel.Writer.TryComplete();
        try { _logWriter.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
