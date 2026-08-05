using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;   // PresenceUi — общий кэш кистей индикаторов
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Enumeration;
using Windows.Graphics;
using Windows.Media.Playback;

namespace LinphoneWinUI;

public sealed partial class SettingsWindow : Window
{
    private readonly ISipService _linphone;
    private readonly Action _onAccountChanged;
    private readonly Action<string> _onThemeChanged;
    private readonly Action _onBehaviorChanged;
    private readonly Action? _onCheckUpdate;   // проверку показывает главное окно — диалог нужен там
    private bool _loading;
    private ObservableCollection<CodecInfo>? _audioCodecs;
    private ObservableCollection<CodecInfo>? _videoCodecs;
    private MediaPlayer? _ringPreview; // неблокирующее прослушивание рингтона (играет WAV и MKV)
    private bool _behaviorReady;       // вкладка «Поведение» заполнена (защита от записи на старте)

    // Проверка устройств (вкладка «Звонки и устройства»)
    private bool _micTesting;                 // идёт цикл «запись → воспроизведение»
    private DispatcherTimer? _micTimer;       // отмеряет фазы записи и воспроизведения
    private string? _micWavPath;              // временный WAV с записью микрофона
    private MediaPlayer? _testPlayer;         // воспроизведение проверочных звуков (тон/запись)
    private bool _cameraPreviewOn;            // открыто ли превью камеры (его рисует liblinphone)


    public SettingsWindow(ISipService linphone, Action onAccountChanged, Action<string> onThemeChanged,
                          Action onBehaviorChanged, Action? onCheckUpdate = null)
    {
        _linphone = linphone;
        _onAccountChanged = onAccountChanged;
        _onThemeChanged = onThemeChanged;
        _onBehaviorChanged = onBehaviorChanged;
        _onCheckUpdate = onCheckUpdate;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop(); // тематический фон (иначе окно прозрачное)
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "lphone.ico"));
        AppWindow.Resize(new SizeInt32(760, 680));

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        MinSizeHelper.SetMinSize(hwnd, 700, 560);
        ApplyTheme(_linphone.GetTheme());

        _linphone.RegistrationStateChanged += OnRegistrationStateChanged;
        Closed += (_, _) =>
        {
            _linphone.RegistrationStateChanged -= OnRegistrationStateChanged;
            _ringPreview?.Dispose(); // глушим прослушивание рингтона при закрытии окна
            _ringPreview = null;
            StopTestPlayer();         // глушим проверочный звук
            StopDeviceTests();        // глушим замер микрофона и превью камеры
            Interop.GlobalHotkeys.Resume(); // окно закрыли посреди захвата — вернуть сочетания
        };

        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();

        if (Content is FrameworkElement root) root.Loaded += (_, _) => ApplyLanguage();
    }

    /// <summary>Проставляет локализованные тексты (Tr.Key по дереву + пункты навигации явно).</summary>
    private void ApplyLanguage()
    {
        Tr.Apply(Content);
        Tr.Set(NiAccount, "NavAccount.Content");
        Tr.Set(NiDevices, "NavDevices.Content");
        Tr.Set(NiContacts, "NavContacts.Content");
        Tr.Set(NiCalls, "NavCalls.Content");
        Tr.Set(NiIntegrations, "NavIntegrations.Content");
        Tr.Set(NiNetwork, "NavNetwork.Content");
        Tr.Set(NiApp, "NavApp.Content");
        Tr.Set(NiAbout, "NavAbout.Content");
    }

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

    /// <summary>Открыть нужную группу настроек (account/devices/codecs/about).</summary>
    public void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((item.Tag as string) == tag) { Nav.SelectedItem = item; return; }
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "account";
        StopDeviceTests(); // уходя с вкладки устройств — глушим замер микрофона и превью камеры

        AccountPanel.Visibility = Vis(tag == "account");
        DevicesPanel.Visibility = Vis(tag == "devices");
        CallsPanel.Visibility = Vis(tag == "calls");
        ContactsPanel.Visibility = Vis(tag == "contacts");
        IntegrationsPanel.Visibility = Vis(tag == "integrations");
        NetworkPanel.Visibility = Vis(tag == "network");
        AppPanel.Visibility = Vis(tag == "app");
        AboutPanel.Visibility = Vis(tag == "about");

        switch (tag)
        {
            case "account": PopulateAccount(); break;
            case "devices": PopulateDevices(); break;
            case "calls": PopulateCalls(); break;
            case "contacts": PopulateContacts(); break;
            case "integrations": PopulateIntegrations(); break;
            case "network": PopulateNetwork(); break;
            case "app": PopulateApp(); break;
            case "about": PopulateAbout(); break;
        }
    }

    // ---------------- Аккаунты (мультиаккаунт) ----------------
    /// <summary>Строка списка аккаунтов (точка статуса + имя/sip + пометка «основной»).
    ///
    /// Наблюдаемая, потому что статус регистрации меняется часто и сам собой: одно нажатие
    /// «Сохранить» даёт re-REGISTER, а тот — Progress (жёлтая точка) и Ok (зелёная). Пока
    /// строки были неизменяемыми, каждое такое событие пересобирало весь список, и он
    /// заметно моргал. Теперь меняется только цвет точки, на месте.</summary>
    private sealed class AccountRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private AccountInfo _info;
        private string _title = "";
        private string _subtitle = "";
        private Brush _dot = PresenceUi.Brush(Colors.Gray);
        private Visibility _defaultVisibility;

        /// <summary>Данные аккаунта. Не наблюдаемое: в разметке не участвует, его читает форма.</summary>
        public AccountInfo Info => _info;
        public string Identity => _info.SipAddress;
        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }
        public Brush Dot { get => _dot; private set => SetProperty(ref _dot, value); }
        public Visibility DefaultVisibility
        {
            get => _defaultVisibility;
            private set => SetProperty(ref _defaultVisibility, value);
        }

        /// <summary>Переложить данные аккаунта в строку. Свойства, которые не изменились,
        /// событий не поднимут — за этим следит SetProperty.</summary>
        public void Apply(AccountInfo a)
        {
            _info = a;
            Title = string.IsNullOrEmpty(a.DisplayName) ? a.Login : a.DisplayName;
            Subtitle = a.SipAddress;
            // Кисть из общего кэша: цветов всего четыре, и создавать их заново на каждое
            // событие регистрации незачем (тот же приём, что на плитках пульта).
            Dot = PresenceUi.Brush(StateColor(a.State));
            DefaultVisibility = a.IsDefault ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Строки списка аккаунтов. Коллекция одна на всё время жизни окна — ItemsSource
    /// ей назначается один раз, а меняется её содержимое.</summary>
    private readonly ObservableCollection<AccountRow> _accountRows = new();

    private string? _editingIdentity; // sip аккаунта, открытого в форме; null = новый аккаунт

    private void PopulateAccount()
    {
        RefreshAccountList(_editingIdentity ?? _linphone.GetAccountInfo()?.SipAddress, reloadForm: true);
    }

    /// <summary>Перестраивает список аккаунтов; при reloadForm заполняет форму выбранным.
    /// При reloadForm=false (обновление точек статуса) сохраняет текущее выделение и форму.</summary>
    private void RefreshAccountList(string? selectIdentity, bool reloadForm)
    {
        // Без перезагрузки формы сохраняем текущее выделение (чтобы фоновые re-register не сбивали ввод).
        string? keep = selectIdentity ?? (AccountsList.SelectedItem as AccountRow)?.Identity;
        var accounts = _linphone.GetAccounts();

        _loading = true;
        // Состав аккаунтов меняется только по кнопке, статусы — сами и постоянно. Пересобираем
        // список ТОЛЬКО когда изменился состав: Clear() поднимает Reset, и ListView выбрасывает
        // и создаёт заново все контейнеры — это и есть видимое моргание при «Сохранить».
        // Сравниваем по адресам В ПОРЯДКЕ: множество совпало бы и при перестановке, а строки
        // тогда разъехались бы с аккаунтами.
        bool sameSet = _accountRows.Count == accounts.Count;
        for (int i = 0; sameSet && i < accounts.Count; i++)
            sameSet = _accountRows[i].Identity == accounts[i].SipAddress;

        if (sameSet)
        {
            for (int i = 0; i < accounts.Count; i++) _accountRows[i].Apply(accounts[i]);
        }
        else
        {
            _accountRows.Clear();
            foreach (var a in accounts)
            {
                var row = new AccountRow();
                row.Apply(a);
                _accountRows.Add(row);
            }
        }
        if (AccountsList.ItemsSource is not ObservableCollection<AccountRow>)
            AccountsList.ItemsSource = _accountRows;

        var sel = _accountRows.FirstOrDefault(r => r.Identity == keep)
                  ?? (reloadForm ? _accountRows.FirstOrDefault() : null);
        AccountsList.SelectedItem = sel;
        _loading = false;

        if (reloadForm)
        {
            if (sel != null) LoadForm(sel.Info);
            else ClearForm();
        }
    }

    private static Windows.UI.Color StateColor(LinphoneRegistrationState state) => state switch
    {
        LinphoneRegistrationState.Ok => Colors.LimeGreen,
        LinphoneRegistrationState.Progress or LinphoneRegistrationState.Refreshing => Colors.Goldenrod,
        LinphoneRegistrationState.Failed => Colors.IndianRed,
        _ => Colors.Gray,
    };

    private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (AccountsList.SelectedItem is AccountRow row) LoadForm(row.Info);
    }

    /// <summary>Заполняет форму данными аккаунта (режим редактирования).</summary>
    private void LoadForm(AccountInfo a)
    {
        _editingIdentity = a.SipAddress;
        DisplayNameBox.Text = a.DisplayName;
        LoginBox.Text = a.Login;
        PasswordBox.Password = a.Password;
        ServerBox.Text = a.Server;
        PortBox.Text = a.Port > 0 ? a.Port.ToString() : "";
        ExpiresBox.Text = a.Expires > 0 ? a.Expires.ToString() : "";
        SipAddressText.Text = string.IsNullOrEmpty(a.SipAddress) ? "—" : a.SipAddress;
        SelectComboByTag(TransportCombo, a.Transport);
        ShowStatus(a.State, null);
        MakeDefaultButton.IsEnabled = !a.IsDefault;
        DeleteAccountButton.IsEnabled = true;
    }

    /// <summary>Очищает форму для ввода нового аккаунта.</summary>
    private void ClearForm()
    {
        _editingIdentity = null;
        DisplayNameBox.Text = "";
        LoginBox.Text = "";
        PasswordBox.Password = "";
        ServerBox.Text = "";
        PortBox.Text = "";
        ExpiresBox.Text = "";
        SipAddressText.Text = "—";
        TransportCombo.SelectedIndex = 0;
        ShowStatus(LinphoneRegistrationState.None, null);
        MakeDefaultButton.IsEnabled = false;
        DeleteAccountButton.IsEnabled = false;
    }

    private void ShowStatus(LinphoneRegistrationState state, string? message)
    {
        AccountStatusText.Text = state switch
        {
            LinphoneRegistrationState.Progress or LinphoneRegistrationState.Refreshing => Loc.S("St_Registering"),
            LinphoneRegistrationState.Ok => Loc.S("St_Online"),
            LinphoneRegistrationState.Failed => Loc.S("St_ErrorPrefix") + message,
            LinphoneRegistrationState.Cleared => Loc.S("St_Offline"),
            _ => Loc.S("St_NotConnected"),
        };
        AccountStatusDot.Fill = new SolidColorBrush(StateColor(state));
    }

    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        _loading = true;
        AccountsList.SelectedItem = null;
        _loading = false;
        ClearForm();
        LoginBox.Focus(FocusState.Programmatic);
    }

    private void OnSaveAccountClick(object sender, RoutedEventArgs e)
    {
        string login = LoginBox.Text.Trim();
        string server = ServerBox.Text.Trim();
        string password = PasswordBox.Password;
        string transport = (TransportCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "udp";
        if (server.Length == 0 || login.Length == 0) return;
        int.TryParse(PortBox.Text.Trim(), out int port);
        if (!int.TryParse(ExpiresBox.Text.Trim(), out int expires)) expires = 3600;

        try
        {
            _linphone.AddOrUpdateAccount(DisplayNameBox.Text.Trim(), login, password, server, port, transport, expires, makeDefault: false);
            RefreshAccountList($"sip:{login}@{server}", reloadForm: true);
            _onAccountChanged();
        }
        catch (Exception ex)
        {
            // Молча глотать нельзя: пользователь был уверен, что аккаунт сохранён.
            AccountStatusText.Text = Loc.S("St_ErrorPrefix") + ex.Message;
            AccountStatusDot.Fill = new SolidColorBrush(Colors.IndianRed);
        }
    }

    private void OnMakeDefaultClick(object sender, RoutedEventArgs e)
    {
        if (_editingIdentity == null) return;
        try
        {
            _linphone.SetDefaultAccount(_editingIdentity);
            RefreshAccountList(_editingIdentity, reloadForm: true);
            _onAccountChanged();
        }
        catch (Exception ex) { ShowAccountError(ex); }
    }

    private void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (_editingIdentity == null) return;
        try
        {
            _linphone.RemoveAccount(_editingIdentity);
            _editingIdentity = null;
            RefreshAccountList(null, reloadForm: true);
            _onAccountChanged();
        }
        catch (Exception ex) { ShowAccountError(ex); }
    }

    /// <summary>Операция с аккаунтом не удалась (в т.ч. ядро не ответило) — сказать пользователю.</summary>
    private void ShowAccountError(Exception ex)
    {
        AccountStatusText.Text = Loc.S("St_ErrorPrefix") + ex.Message;
        AccountStatusDot.Fill = new SolidColorBrush(Colors.IndianRed);
    }

    private void OnRegistrationStateChanged(LinphoneRegistrationState state, string? message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Статус в форме — по последнему событию (как раньше), точки в списке — по каждому аккаунту.
            ShowStatus(state, message);
            if (AccountPanel.Visibility == Visibility.Visible)
                RefreshAccountList(_editingIdentity, reloadForm: false);
        });
    }

    // ---------------- Звонки и устройства ----------------
    private void PopulateDevices()
    {
        _loading = true;
        _linphone.ReloadSoundDevices();
        MicCombo.ItemsSource = _linphone.GetInputDevices();
        MicCombo.SelectedValue = _linphone.CurrentInputDeviceId;
        SpeakerCombo.ItemsSource = _linphone.GetOutputDevices();
        SpeakerCombo.SelectedValue = _linphone.CurrentOutputDeviceId;
        RingerCombo.ItemsSource = _linphone.GetOutputDevices();
        RingerCombo.SelectedValue = _linphone.GetRingerDeviceId();
        CameraCombo.ItemsSource = _linphone.GetCameras();
        CameraCombo.SelectedItem = _linphone.CurrentCamera;
        EchoSwitch.IsOn = _linphone.GetEchoCancellation();
        PopulateRingtones();
        ShowRingtonePath(_linphone.GetRingtone());
        ShowRecordingsDir(_linphone.GetRecordingsDir());
        AutoRecordSwitch.IsOn = _linphone.GetAppFlag("auto_record", false);
        _loading = false;
    }

    private void OnAutoRecordToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("auto_record", AutoRecordSwitch.IsOn);
    }

    private sealed record RingtoneItem(string Display, string Path);

    /// <summary>Список рингтонов: «По умолчанию» (пустой путь) + встроенные образцы (+ текущий свой файл).
    /// Выделяет текущий; «По умолчанию» — отдельный пункт, чтобы выбор не был пустым.</summary>
    private void PopulateRingtones()
    {
        bool prev = _loading; _loading = true;   // не дёргать OnRingtoneSelected при программном выборе

        string current = (_linphone.GetRingtone() ?? "").Replace('\\', '/');
        var builtins = _linphone.GetBuiltInRingtones().Select(p => p.Replace('\\', '/')).ToList();

        var items = new List<RingtoneItem> { new(Loc.S("St_RingtoneDefault"), "") };
        items.AddRange(builtins.Select(p => new RingtoneItem(PrettyRingtoneName(p), p)));
        // Текущий свой файл (не из набора и не пустой) — добавим отдельным пунктом.
        bool isBuiltin = builtins.Any(p => p.Equals(current, StringComparison.OrdinalIgnoreCase));
        if (current.Length > 0 && !isBuiltin)
            items.Add(new RingtoneItem(Loc.F("St_RingtoneCustom", PrettyRingtoneName(current)), current));

        RingtoneCombo.ItemsSource = items;
        RingtoneCombo.SelectedValue = current.Length > 0 ? current : "";   // нет рингтона → «По умолчанию»

        _loading = prev;
    }

    private static string PrettyRingtoneName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Length == 0 ? Path.GetFileName(path)
                                : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private void OnRingtoneSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RingtoneCombo.SelectedValue is not string path) return;
        StopRingtonePreview();
        if (path.Length == 0) { _linphone.SetRingtone(null); ShowRingtonePath(null); }   // «По умолчанию»
        else { _linphone.SetRingtone(path); ShowRingtonePath(path); }
    }

    private void ShowRecordingsDir(string dir) =>
        RecordingsDirText.Text = string.IsNullOrEmpty(dir) ? "—" : dir.Replace('/', '\\');

    private async void OnPickRecordingsDirClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*"); // обязателен, иначе PickSingleFolderAsync кидает исключение
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _linphone.SetRecordingsDir(folder.Path);
            ShowRecordingsDir(_linphone.GetRecordingsDir());
        }
    }

    private void OnResetRecordingsDirClick(object sender, RoutedEventArgs e)
    {
        _linphone.SetRecordingsDir(null); // вернуться к каталогу по умолчанию
        ShowRecordingsDir(_linphone.GetRecordingsDir());
    }

    private void OnOpenRecordingsDirClick(object sender, RoutedEventArgs e)
    {
        string dir = _linphone.GetRecordingsDir().Replace('/', '\\');
        try { Directory.CreateDirectory(dir); } catch { }
        try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
    }

    /// <summary>Показывает имя файла рингтона (полный путь — в подсказке), а не «странный» путь целиком.</summary>
    private void ShowRingtonePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            RingtoneText.Text = Loc.S("St_RingtoneDefault");
            ToolTipService.SetToolTip(RingtoneText, null);
        }
        else
        {
            RingtoneText.Text = Path.GetFileName(path);
            ToolTipService.SetToolTip(RingtoneText, path.Replace('/', '\\'));
        }
    }

    private async void OnPickRingtoneClick(object sender, RoutedEventArgs e)
    {
        StopRingtonePreview();
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mkv");
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _linphone.SetRingtone(file.Path);
            ShowRingtonePath(file.Path);
            PopulateRingtones();   // свой файл попадёт в список отдельным пунктом и будет выбран
        }
    }

    private void OnPlayRingtoneClick(object sender, RoutedEventArgs e)
    {
        // Повторный клик (или кнопка в режиме «Остановить») — глушим.
        if (_ringPreview != null) { StopRingtonePreview(); return; }

        string path = (_linphone.GetRingtone() ?? "").Replace('/', '\\');
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path)) { RingtoneText.Text = Loc.S("St_RingtoneNotFound"); return; }

        try
        {
            // MediaPlayer не блокирует UI-поток (в отличие от linphone_core_play_local) и
            // проигрывает не только WAV, но и MKV — дефолтный рингтон linphone.
            _ringPreview = new MediaPlayer { Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path)) };
            _ringPreview.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(StopRingtonePreview);
            _ringPreview.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(StopRingtonePreview);
            _ringPreview.Play();
            PlayRingtoneButton.Content = Loc.S("St_RingtoneStop");
        }
        catch { StopRingtonePreview(); }
    }

    private void StopRingtonePreview()
    {
        _ringPreview?.Dispose();
        _ringPreview = null;
        PlayRingtoneButton.Content = Loc.S("BtnPlayRingtone.Content");
    }

    private void OnResetRingtoneClick(object sender, RoutedEventArgs e)
    {
        StopRingtonePreview();
        _linphone.SetRingtone(null);
        ShowRingtonePath(null);  // вернулись к дефолту — показываем «(по умолчанию)»
        PopulateRingtones();     // выбрать пункт «По умолчанию» в списке (а не оставлять пусто)
    }

    // ---------------- Проверка устройств ----------------

    /// <summary>Глушит замер микрофона и закрывает превью камеры (при уходе с вкладки / закрытии окна).</summary>
    private void StopDeviceTests()
    {
        StopMicTest();
        if (_cameraPreviewOn)
        {
            _linphone.StopCameraPreview();
            _cameraPreviewOn = false;
            CameraTestButton.Content = Loc.S("BtnCheckCamera.Content");
        }
    }

    // --- Динамик: тестовый тон на ВЫБРАННЫЙ динамик (через MediaPlayer — не морозит окно) ---
    private void OnTestSpeakerClick(object sender, RoutedEventArgs e)
    {
        try { PlayCheckSound(EnsureTestToneFile()); } catch { }
    }

    /// <summary>
    /// Воспроизводит WAV через WinRT MediaPlayer на выбранный динамик. В отличие от LinphonePlayer,
    /// не «замораживает» окно настроек (тот же надёжный путь, что и прослушивание рингтона).
    /// </summary>
    private async void PlayCheckSound(string path)
    {
        StopTestPlayer();
        string? spkName = (SpeakerCombo.SelectedItem as AudioDeviceInfo)?.Name;
        DeviceInformation? dev = await MatchRenderByNameAsync(spkName);

        var mp = new MediaPlayer { Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path)) };
        if (dev != null) { try { mp.AudioDevice = dev; } catch { /* останется устройство по умолчанию */ } }
        mp.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(StopTestPlayer);
        mp.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(StopTestPlayer);
        _testPlayer = mp;
        mp.Play();
    }

    private void StopTestPlayer()
    {
        _testPlayer?.Dispose();
        _testPlayer = null;
    }

    /// <summary>Ищет WinRT render-устройство по имени выбранного в linphone динамика.</summary>
    private static async Task<DeviceInformation?> MatchRenderByNameAsync(string? name)
    {
        try
        {
            if (string.IsNullOrEmpty(name)) return null;
            var all = await DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioRenderSelector());
            return all.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                (name.Contains(d.Name, StringComparison.OrdinalIgnoreCase) ||
                 d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }
        catch { return null; }
    }

    /// <summary>
    /// Генерирует (один раз) короткую приятную мелодию для проверки динамика: восходящее
    /// арпеджио до-мажор с «колокольчиковым» затуханием нот и финальным аккордом.
    /// </summary>
    private static string EnsureTestToneFile()
    {
        // В cache\, а не рядом с журналами: файл целиком воспроизводимый, его не жалко потерять,
        // и в «папке логов», которую человек отправляет в поддержку, ему делать нечего.
        Directory.CreateDirectory(AppPaths.Cache);
        string path = Path.Combine(AppPaths.Cache, "speaker-check.wav");
        if (File.Exists(path)) return path;

        const int rate = 44100;
        // Ноты до-мажорного трезвучия: C5, E5, G5, C6 (Гц).
        double[] notes = { 523.25, 659.25, 783.99, 1046.50 };
        const double noteDur = 0.22;     // длительность каждой ноты арпеджио
        const double chordDur = 0.9;     // финальный аккорд (все ноты разом)
        double total = notes.Length * noteDur + chordDur;
        int samples = (int)(rate * total);

        var pcm = new short[samples];
        // Один «колокольчиковый» голос: основной тон + 2-я гармоника, экспоненциальное затухание.
        void AddVoice(int startSample, double freq, double dur, double amp)
        {
            int n = (int)(rate * dur);
            for (int i = 0; i < n && startSample + i < samples; i++)
            {
                double t = (double)i / rate;
                double env = Math.Exp(-3.5 * t / dur) * Math.Min(1.0, t / 0.004); // быстрый старт, плавный спад
                double s = Math.Sin(2 * Math.PI * freq * t) + 0.35 * Math.Sin(2 * Math.PI * 2 * freq * t);
                double v = pcm[startSample + i] + s * env * amp * short.MaxValue;
                pcm[startSample + i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            }
        }

        for (int k = 0; k < notes.Length; k++)
            AddVoice((int)(k * noteDur * rate), notes[k], noteDur + 0.15, 0.42);
        int chordStart = (int)(notes.Length * noteDur * rate);
        foreach (double f in notes)
            AddVoice(chordStart, f, chordDur, 0.30); // аккорд: чуть тише, голоса складываются

        using var bw = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write));
        int dataSize = samples * 2;
        bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt ")); bw.Write(16);
        bw.Write((short)1); bw.Write((short)1);            // PCM, моно
        bw.Write(rate); bw.Write(rate * 2);               // sample rate, byte rate
        bw.Write((short)2); bw.Write((short)16);          // block align, бит/сэмпл
        bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(dataSize);
        foreach (short s in pcm) bw.Write(s);
        return path;
    }

    // --- Камера: локальное превью (окно рисует сам liblinphone) ---
    private void OnTestCameraClick(object sender, RoutedEventArgs e)
    {
        if (_cameraPreviewOn)
        {
            _linphone.StopCameraPreview();
            _cameraPreviewOn = false;
            CameraTestButton.Content = Loc.S("BtnCheckCamera.Content");
            return;
        }
        _linphone.StartCameraPreview();
        _cameraPreviewOn = true;
        CameraTestButton.Content = Loc.S("St_CameraHide");
    }

    // --- Микрофон: запись с микрофона и воспроизведение обратно («слышу себя») ---
    private const int MicRecordSeconds = 4;

    private void OnTestMicClick(object sender, RoutedEventArgs e)
    {
        if (_micTesting) { StopMicTest(); return; }

        // Каталог может быть под кириллическим профилем — mediastreamer не откроет файл.
        // ASCII short-path (8.3) чинит и запись, и последующее воспроизведение.
        string dir = AppPaths.Cache;   // запись проверки микрофона — не журнал
        try { Directory.CreateDirectory(dir); } catch { }
        _micWavPath = Interop.LinphoneCore.AsciiSafePath(Path.Combine(dir, "mic-test.wav"));
        if (!_linphone.StartMicRecording(_micWavPath))
        {
            MicStatusText.Text = Loc.S("St_MicNoAccess");
            return;
        }

        _micTesting = true;
        MicTestButton.Content = Loc.S("St_CheckStop");
        MicStatusText.Text = Loc.S("St_MicRecording");

        // Запись фиксированной длины, затем автоматическое воспроизведение записанного.
        _micTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MicRecordSeconds) };
        _micTimer.Tick += OnMicRecordingDone;
        _micTimer.Start();
    }

    private void OnMicRecordingDone(object? sender, object e)
    {
        StopTimer();
        _linphone.StopMicRecording();

        if (_micWavPath == null || !File.Exists(_micWavPath))
        {
            StopMicTest();
            return;
        }

        MicStatusText.Text = Loc.S("St_MicPlaying");
        try { PlayCheckSound(_micWavPath); } catch { }

        // Воспроизведение чуть дольше записи, затем возвращаем кнопку в исходное.
        _micTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MicRecordSeconds + 1) };
        _micTimer.Tick += OnMicPlaybackDone;
        _micTimer.Start();
    }

    private void OnMicPlaybackDone(object? sender, object e)
    {
        StopTimer();
        StopTestPlayer();
        StopMicTest();
    }

    private void StopTimer()
    {
        _micTimer?.Stop();
        _micTimer = null;
    }

    private void StopMicTest()
    {
        _micTesting = false;
        StopTimer();
        _linphone.StopMicRecording();
        StopTestPlayer();
        MicTestButton.Content = Loc.S("BtnCheckMic.Content");
        MicStatusText.Text = Loc.S("LblCheckMic.Text"); // вернули обычную подсказку
    }

    // ---------------- Поведение (как в Zoiper) ----------------
    /// <summary>Раздел «Звонки»: всё про сам разговор — от DTMF до записи.</summary>
    private void PopulateCalls()
    {
        _loading = true;
        SelectComboByTag(DtmfCombo, _linphone.GetDtmfMethod());
        PopulateCodecs();

        AutoAnswerSwitch.IsOn = _linphone.GetAppFlag("auto_answer", false);
        int.TryParse(_linphone.GetAppString("auto_answer_delay", "0"), out int autoDelay);
        AutoAnswerDelayBox.Value = autoDelay;
        AutoAnswerDelayBox.IsEnabled = AutoAnswerSwitch.IsOn;
        IntercomSwitch.IsOn = _linphone.GetAppFlag("intercom_autoanswer", true);
        AutoRedialSwitch.IsOn = _linphone.GetAppFlag("auto_redial", false);
        int.TryParse(_linphone.GetAppString("auto_redial_delay", "30"), out int redialDelay);
        AutoRedialDelayBox.Value = redialDelay < 5 ? 30 : redialDelay;
        AutoRedialDelayBox.IsEnabled = AutoRedialSwitch.IsOn;
        VoicemailNumberBox.Text = _linphone.GetVoicemailNumber();

        ParkExtBox.Text = _linphone.GetAppString("park_ext", "");
        ParkSlotsBox.Text = _linphone.GetAppString("park_slots", "");
        ShowParkingSummary();

        RecordingsDirText.Text = _linphone.GetRecordingsDir();
        AutoRecordSwitch.IsOn = _linphone.GetAppFlag("auto_record", false);

        _loading = false;
        _behaviorReady = true;
    }

    /// <summary>Раздел «Приложение»: язык, оформление, окно, автозапуск, горячие клавиши.</summary>
    private void PopulateApp()
    {
        _loading = true;
        SelectComboByTag(ThemeCombo, _linphone.GetTheme());
        SelectComboByTag(LangCombo, AppSettings.GetLanguage());

        AutostartSwitch.IsOn = Autostart.IsEnabled();
        TraySwitch.IsOn = _linphone.GetAppFlag("minimize_to_tray", true);
        StartHiddenSwitch.IsOn = _linphone.GetAppFlag("start_hidden", false);
        IncomingFrontSwitch.IsOn = _linphone.GetAppFlag("incoming_to_front", false);
        PublishPresenceSwitch.IsOn = _linphone.GetAppFlag("publish_presence", false);   // Asterisk PUBLISH не принимает
        AwayOnLockSwitch.IsOn = _linphone.GetAppFlag("away_on_lock", true);
        MissedToastSwitch.IsOn = _linphone.GetAppFlag("missed_toast", true);

        HotkeysSwitch.IsOn = _linphone.GetAppFlag("hotkeys", true);
        RefreshHotkeyButtons();
        MiniWindowSwitch.IsOn = _linphone.GetAppFlag("mini_call_window", true);
        HotkeysStatus.Text = "";
        _loading = false;
    }

    /// <summary>Раздел «Интеграции»: CRM, click-to-call, статус в АТС, провизионинг.</summary>
    private void PopulateIntegrations()
    {
        _loading = true;
        ApiSwitch.IsOn = _linphone.GetAppFlag("api_enabled", false);
        ApiPortBox.Text = _linphone.GetAppString("api_port", "");
        ApiTokenBox.Text = _linphone.GetApiToken();   // из DPAPI-хранилища, не из rc
        ShowApiHint();

        CallHookBox.Text = _linphone.GetAppString("call_hook_url", "");
        CallHookBrowserSwitch.IsOn = _linphone.GetAppFlag("call_hook_browser", true);
        CallHookIncomingSwitch.IsOn = _linphone.GetAppFlag("call_hook_incoming_only", true);

        ClickToCallSwitch.IsOn = ClickToCall.IsRegistered();

        PresenceHookUrlBox.Text = _linphone.GetAppString("presence_hook_url", "");
        PresenceHookSecretBox.Password = _linphone.GetPresenceHookSecret();
        ProvisioningUrlBox.Text = _linphone.GetProvisioningUri();
        ProvisioningStatus.Text = "";
        _loading = false;
    }

    /// <summary>Раздел «Сеть»: NAT и доверие TLS. Обе группы применяются кнопкой —
    /// они перезапускают подсистему, и делать это на каждый символ нельзя.</summary>
    private void PopulateNetwork()
    {
        _loading = true;
        var (stun, ice, turn, turnUser) = _linphone.GetNat();
        StunBox.Text = stun;
        IceSwitch.IsOn = ice;
        TurnSwitch.IsOn = turn;
        TurnUserBox.Text = turnUser;
        TurnPassBox.Password = "";   // пароль не читается обратно; пусто = оставить прежний
        TurnRealmBox.Text = _linphone.GetAppString("turn_realm", "");
        ShowTurnPassState();         // …поэтому говорим словами, есть он или нет
        NetStatus.Text = "";

        var (caFile, tlsVerify) = _linphone.GetTls();
        // Бандл, приехавший с SDK, показываем как «пусто» — это состояние по умолчанию,
        // а не выбор пользователя; свой файл виден полным путём.
        CaFileBox.Text = IsBundledCa(caFile) ? "" : caFile;
        TlsVerifySwitch.IsOn = tlsVerify;
        TlsWarnText.Visibility = tlsVerify ? Visibility.Collapsed : Visibility.Visible;
        TlsStatus.Text = "";
        _loading = false;
    }

    /// <summary>Интерком: слушаться ли просьбы АТС ответить автоматически (Alert-Info).
    /// По умолчанию включено — заголовок присылают намеренно, под интерком и оповещения;
    /// выключение нужно тем, кого не устраивает, что телефон отвечает сам.</summary>
    private void OnIntercomToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("intercom_autoanswer", IntercomSwitch.IsOn);
    }

    private void OnClickToCallToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ClickToCallSwitch.IsOn) ClickToCall.Register();
        else ClickToCall.Unregister();
    }

    private void OnAutoRedialToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _linphone.SetAppFlag("auto_redial", AutoRedialSwitch.IsOn);
        AutoRedialDelayBox.IsEnabled = AutoRedialSwitch.IsOn;
    }

    private void OnAutoRedialDelayChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || !_behaviorReady) return;
        int v = double.IsNaN(sender.Value) ? 30 : (int)sender.Value;
        _linphone.SetAppString("auto_redial_delay", v.ToString());
    }

    private void OnVoicemailNumberChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && _behaviorReady) _linphone.SetVoicemailNumber(VoicemailNumberBox.Text);
    }

    private void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) Autostart.Set(AutostartSwitch.IsOn);
    }

    private void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("minimize_to_tray", TraySwitch.IsOn);
    }

    private void OnStartHiddenToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("start_hidden", StartHiddenSwitch.IsOn);
    }

    private void OnIncomingFrontToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("incoming_to_front", IncomingFrontSwitch.IsOn);
    }

    /// <summary>Статус «Отошёл» при блокировке станции: коллеги видят, что человека нет,
    /// без единого его действия. Именно «Отошёл», а не «Не беспокоить» — тот заворачивает
    /// звонки в почту, и включать это самовольно недопустимо.</summary>
    private void OnAwayOnLockToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("away_on_lock", AwayOnLockSwitch.IsOn);
    }

    /// <summary>Уведомление о пропущенном с кнопкой «Перезвонить».</summary>
    private void OnMissedToastToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("missed_toast", MissedToastSwitch.IsOn);
    }

    private void OnPublishPresenceToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _linphone.SetAppFlag("publish_presence", PublishPresenceSwitch.IsOn);
        _linphone.SetPresencePublish(PublishPresenceSwitch.IsOn);   // применить сразу
    }

    private void OnAutoAnswerToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _linphone.SetAppFlag("auto_answer", AutoAnswerSwitch.IsOn);
        AutoAnswerDelayBox.IsEnabled = AutoAnswerSwitch.IsOn;
        _onBehaviorChanged(); // обновить индикатор на главном экране
    }

    private void OnAutoAnswerDelayChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || !_behaviorReady) return;
        int v = double.IsNaN(sender.Value) ? 0 : (int)sender.Value;
        _linphone.SetAppString("auto_answer_delay", v.ToString());
    }

    // ---------------- Дополнительно (язык, тема, сеть, provisioning) ----------------
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string lang = (LangCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        AppSettings.SetLanguage(lang);
        // Рядом с самим выбором: раньше это сообщение уходило в строку раздела «Интеграции»,
        // и человек менял язык, не получая ни отклика, ни объяснения, почему ничего не изменилось.
        LangNote.Text = Loc.S("St_LangNote");
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        _linphone.SetTheme(theme);
        ApplyTheme(theme);
        _onThemeChanged(theme);
    }

    private void OnApplyNatClick(object sender, RoutedEventArgs e)
    {
        string realm = TurnRealmBox.Text.Trim();
        _linphone.SetAppString("turn_realm", realm);
        // В поле могут стоять наши же точки-заполнитель — тогда пароль НЕ меняли, и отправить
        // их значило бы затереть настоящий пароль строкой из восьми точек. Пустая строка
        // означает для SetNat «оставить прежний», это ровно то, что нужно.
        string pass = _turnPassIsPlaceholder ? "" : TurnPassBox.Password;

        bool ok = _linphone.SetNat(StunBox.Text, IceSwitch.IsOn, TurnSwitch.IsOn,
                                   TurnUserBox.Text, pass, realm);
        // Раньше «применено» печаталось безусловно, хотя SetNat умеет молча ничего не сделать.
        NetStatus.Text = Loc.S(ok ? "St_NetApplied" : "St_NetApplyFailed");
        // Новый пароль принят — снова показываем точки вместо введённого.
        if (ok && !_turnPassIsPlaceholder) { TurnPassBox.Password = ""; ShowTurnPassState(); }
    }

    /// <summary>Сменили логин TURN — пароль у нового логина может быть свой (или никакого).</summary>
    private void OnTurnUserChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) ShowTurnPassState();
    }

    /// <summary>Что стоит в поле пароля TURN — заполнитель, а не введённое пользователем.
    /// В отличие от пароля книги и секрета хука, этот обратно НЕ читается: liblinphone держит
    /// его в auth_info, а при заданном realm — только как хеш ha1. Показать нечего, поэтому
    /// рисуем точки сами и следим, чтобы они не уехали на сервер вместо настоящего пароля.</summary>
    private bool _turnPassIsPlaceholder;

    /// <summary>Столько точек рисуем вместо нечитаемого пароля. Длина произвольная и НЕ равна
    /// настоящей: показывать её значило бы подсказывать длину пароля тому, кто заглянул в экран.</summary>
    private const string SecretPlaceholder = "••••••••";

    /// <summary>Пустое поле не отличает «пароля нет» от «пароль сохранён и не читается обратно»,
    /// а решения это разные: в первом случае TURN не заработает вовсе. Показываем точки, если
    /// пароль есть, и поясняем словами под полем.</summary>
    private void ShowTurnPassState()
    {
        string user = TurnUserBox.Text.Trim();
        bool saved = user.Length > 0 && _linphone.HasTurnPassword(user);

        TurnPassState.Text = user.Length == 0
            ? ""   // без логина спрашивать про пароль нечего
            : Loc.S(saved ? "St_TurnPassSaved" : "St_TurnPassMissing");

        // Точки ставим, только если поле не трогали: затирать начатый ввод нельзя.
        if (TurnPassBox.Password.Length > 0 && !_turnPassIsPlaceholder) return;
        _turnPassIsPlaceholder = saved;
        TurnPassBox.Password = saved ? SecretPlaceholder : "";
        // Пока в поле заполнитель, «глазок» показал бы те же точки и только сбил бы с толку.
        TurnPassBox.PasswordRevealMode = saved ? PasswordRevealMode.Hidden : PasswordRevealMode.Peek;
    }

    /// <summary>Щёлкнули поле, чтобы ввести новый пароль — убираем точки-заполнитель,
    /// иначе человек дописывал бы свой пароль к ним.</summary>
    private void OnTurnPassGotFocus(object sender, RoutedEventArgs e)
    {
        if (!_turnPassIsPlaceholder) return;
        _turnPassIsPlaceholder = false;
        TurnPassBox.Password = "";
        TurnPassBox.PasswordRevealMode = PasswordRevealMode.Peek;
    }

    /// <summary>Ушли из поля, ничего не введя — возвращаем точки: пароль-то на месте,
    /// и пустое поле снова выглядело бы как «пароля нет».</summary>
    private void OnTurnPassLostFocus(object sender, RoutedEventArgs e)
    {
        if (TurnPassBox.Password.Length == 0) ShowTurnPassState();
    }

    // ---------------- Работа поверх других окон ----------------
    /// <summary>Горячие клавиши занимаются СИСТЕМОЙ целиком, поэтому включение и выключение
    /// применяется сразу: держать занятой комбинацию, которой пользователь только что
    /// отказался пользоваться, нельзя — она перестанет работать в других программах.</summary>
    private void OnCallHookChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string url = CallHookBox.Text.Trim();
        _linphone.SetAppString("call_hook_url", url);
        _linphone.SetAppFlag("call_hook_browser", CallHookBrowserSwitch.IsOn);
        _linphone.SetAppFlag("call_hook_incoming_only", CallHookIncomingSwitch.IsOn);

        // Ссылку, которую нельзя открыть, лучше отвергнуть сразу: иначе о проблеме узнают
        // на первом же входящем — в самый неподходящий момент.
        ApiHint.Text = url.Length > 0 && CallHookBrowserSwitch.IsOn && !CallHook.IsBrowsable(
                CallHook.Format(url, new CallHookData(CallHookEvent.Incoming, "377", "Тест", 0, "x")))
            ? Loc.S("St_HookBadUrl")
            : ApiHint.Text;
    }

    // ---------------- Локальный API для CRM ----------------
    private void OnApiToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // Токен создаём в момент включения, а не при первом запросе: пользователь должен
        // увидеть его сразу, чтобы вставить в CRM.
        if (ApiSwitch.IsOn && ApiTokenBox.Text.Trim().Length == 0)
        {
            ApiTokenBox.Text = LocalApi.LocalApiServer.NewToken();
            _linphone.SetApiToken(ApiTokenBox.Text);
        }
        _linphone.SetAppFlag("api_enabled", ApiSwitch.IsOn);
        ApplyApi();
    }

    private void OnApiSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string port = ApiPortBox.Text.Trim();
        _linphone.SetAppString("api_port", port);
        ApplyApi();
    }

    /// <summary>Новый токен. Старый перестаёт работать немедленно — это и есть смысл кнопки:
    /// токен утёк, доступ отзываем.</summary>
    private void OnApiNewToken(object sender, RoutedEventArgs e)
    {
        ApiTokenBox.Text = LocalApi.LocalApiServer.NewToken();
        _linphone.SetApiToken(ApiTokenBox.Text);
        ApplyApi();
    }

    private void OnApiCopyExample(object sender, RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ApiExampleUrl());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ApiHint.Text = Loc.S("St_ApiCopied");
    }

    private string ApiExampleUrl()
    {
        string port = ApiPortBox.Text.Trim();
        if (port.Length == 0) port = LocalApi.LocalApiServer.DefaultPort.ToString();
        return $"http://127.0.0.1:{port}/call?number=377&token={ApiTokenBox.Text.Trim()}";
    }

    private void ApplyApi()
    {
        ShowApiHint();
        _onBehaviorChanged();   // главное окно перезапустит сервер с новыми настройками
    }

    private void ShowApiHint()
    {
        ApiHint.Text = ApiSwitch.IsOn ? ApiExampleUrl() : Loc.S("St_ApiOff");
    }

    /// <summary>Сохранить настройки парковки. Пустое поле — вернуться к умолчанию
    /// (500 / 501-520): так на типовой Asterisk-конфигурации ничего настраивать не нужно.</summary>
    private void OnParkingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _linphone.SetAppString("park_ext", ParkExtBox.Text.Trim());
        _linphone.SetAppString("park_slots", ParkSlotsBox.Text.Trim());
        ShowParkingSummary();
        _onBehaviorChanged();   // главное окно перечитает слоты и переподпишется
    }

    /// <summary>Сколько слотов получилось. Строка вроде «501-20» разбору не поддаётся, и
    /// сказать об этом надо сразу — иначе пульт парковки просто молча не появится.</summary>
    private void ShowParkingSummary()
    {
        string spec = ParkSlotsBox.Text.Trim();
        var slots = Interop.ParkingSlots.Parse(spec.Length == 0 ? "501-520" : spec);
        ParkStatus.Text = slots.Count > 0
            ? Loc.F("St_ParkSlotsOk", slots.Count, slots[0], slots[^1])
            : Loc.S("St_ParkSlotsBad");
    }

    private void OnHotkeysToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _linphone.SetAppFlag("hotkeys", HotkeysSwitch.IsOn);
        HotkeyBox.IsEnabled = HotkeysSwitch.IsOn;
        ApplyHotkeys();
    }

    // ---------------- Назначение горячих клавиш ----------------
    // Ввод сочетания текстом («Ctrl+Alt+A») — не дело пользователя, поэтому кнопка переходит
    // в режим захвата и ждёт нажатия. Занятость проверяем СРАЗУ, коротко заняв сочетание:
    // иначе о конфликте с другой программой узнают только после перезапуска, а до тех пор
    // человек жмёт клавишу и считает, что сломался софтфон.

    private Interop.HotkeyAction? _capturing;
    // Сочетания, которые занять не удалось: их держит другая программа. Помечаем прямо на
    // кнопке — списком под сеткой пользователь не поймёт, какая из трёх клавиш мертва.
    private HashSet<string> _hkFailed = new();

    private Button ButtonFor(Interop.HotkeyAction a) => a switch
    {
        Interop.HotkeyAction.Answer => HkAnswerButton,
        Interop.HotkeyAction.Hangup => HkHangupButton,
        _ => HkMuteButton,
    };

    /// <summary>Показать текущие назначения. Пустое значение — «не назначено».</summary>
    private void RefreshHotkeyButtons()
    {
        foreach (var action in Interop.GlobalHotkeys.AllActions)
        {
            var spec = EffectiveHotkey(action);
            string text = spec?.Format() ?? Loc.S("Hk_NotSet");
            var b = ButtonFor(action);
            b.Content = text;
            if (_hkFailed.Contains(text))
            {
                b.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                ToolTipService.SetToolTip(b, Loc.F("Hk_Taken", text));
            }
            else
            {
                b.ClearValue(Control.ForegroundProperty);
                ToolTipService.SetToolTip(b, null);
            }
        }
        // Назначать сочетания при выключенном переключателе бессмысленно и вводит в
        // заблуждение: они всё равно не будут работать.
        HotkeyBox.IsEnabled = HotkeysSwitch.IsOn;
    }

    private Interop.HotkeySpec? EffectiveHotkey(Interop.HotkeyAction action) =>
        Interop.GlobalHotkeys.Effective(
            action, _linphone.GetAppString(Interop.GlobalHotkeys.ConfigKey(action), ""));

    private void OnCaptureHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        if (!Enum.TryParse(tag, out Interop.HotkeyAction action)) return;

        _capturing = action;
        // Пока идёт захват, свои сочетания надо отпустить: занятое системой сочетание
        // приходит главному окну как WM_HOTKEY и до окна настроек не доходит вовсе —
        // назначить Ctrl+Alt+H поверх Ctrl+Alt+H было бы невозможно, а Ctrl+Alt+A
        // вместо захвата ответил бы на звонок.
        Interop.GlobalHotkeys.Suspend();
        b.Content = Loc.S("Hk_PressCombo");
        HotkeysStatus.Text = Loc.S("Hk_CaptureHint");
        b.Focus(FocusState.Programmatic);
        b.KeyDown += OnHotkeyKeyDown;
        b.LostFocus += OnHotkeyCaptureLost;
    }

    private void OnHotkeyCaptureLost(object sender, RoutedEventArgs e)
    {
        // Ушли с кнопки, не нажав сочетание — возвращаем как было, чтобы не остаться
        // с надписью «нажмите сочетание» навсегда.
        if (sender is Button b) { b.KeyDown -= OnHotkeyKeyDown; b.LostFocus -= OnHotkeyCaptureLost; }
        _capturing = null;
        Interop.GlobalHotkeys.Resume();
        RefreshHotkeyButtons();
        HotkeysStatus.Text = "";
    }

    private void OnHotkeyKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_capturing is not { } action || sender is not Button b) return;
        var key = e.Key;
        // Сами модификаторы нажатием не считаем — ждём «настоящую» клавишу.
        if (key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Menu
                or Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.LeftWindows
                or Windows.System.VirtualKey.RightWindows) return;
        e.Handled = true;

        uint mods = 0;
        if (IsDown(Windows.System.VirtualKey.Control)) mods |= Interop.HotkeySpec.ModControl;
        if (IsDown(Windows.System.VirtualKey.Menu)) mods |= Interop.HotkeySpec.ModAlt;
        if (IsDown(Windows.System.VirtualKey.Shift)) mods |= Interop.HotkeySpec.ModShift;

        var spec = new Interop.HotkeySpec(mods, (uint)key);
        b.KeyDown -= OnHotkeyKeyDown;
        b.LostFocus -= OnHotkeyCaptureLost;
        _capturing = null;

        if (!spec.IsValid)
        {
            // Без модификатора сочетание отобрало бы обычную клавишу у всей системы.
            HotkeysStatus.Text = Loc.S("Hk_NeedModifier");
            Interop.GlobalHotkeys.Resume();
            RefreshHotkeyButtons();
            return;
        }
        // Сначала свои же назначения. Сочетание, уже занятое главным окном, проба ниже
        // объявила бы «занято другой программой» — и пользователь искал бы виновника
        // снаружи, хотя это наш собственный софтфон.
        foreach (var other in Interop.GlobalHotkeys.AllActions)
        {
            if (other == action || EffectiveHotkey(other) != spec) continue;
            HotkeysStatus.Text = Loc.F("Hk_Duplicate", spec.Format(), Loc.S("Hk" + other + ".Text"));
            Interop.GlobalHotkeys.Resume();
            RefreshHotkeyButtons();
            return;
        }
        // Своё прежнее сочетание переназначать не надо — и пробовать его нельзя, оно занято нами.
        if (EffectiveHotkey(action) != spec
            && !Interop.GlobalHotkeys.IsAvailable(WinRT.Interop.WindowNative.GetWindowHandle(this), spec))
        {
            HotkeysStatus.Text = Loc.F("Hk_Taken", spec.Format());
            Interop.GlobalHotkeys.Resume();
            RefreshHotkeyButtons();
            return;
        }

        _linphone.SetAppString(Interop.GlobalHotkeys.ConfigKey(action), spec.Format());
        RefreshHotkeyButtons();
        ApplyHotkeys();
    }

    /// <summary>Занять сочетания по текущим настройкам. Делаем сразу, а не «после
    /// перезапуска»: оператор правит клавишу посреди рабочего дня и должен проверить её
    /// тут же.</summary>
    private void ApplyHotkeys()
    {
        if (!HotkeysSwitch.IsOn)
        {
            Interop.GlobalHotkeys.Suspend();
            HotkeysStatus.Text = "";
            return;
        }
        var failed = Interop.GlobalHotkeys.Rebind(Interop.GlobalHotkeys.FromConfig(_linphone));
        _hkFailed = new HashSet<string>(failed);
        RefreshHotkeyButtons();
        HotkeysStatus.Text = failed.Count > 0
            ? Loc.F("Hk_TakenList", string.Join(", ", failed))
            : Loc.S("Hk_Applied");
    }

    /// <summary>Снять назначение. Пишем «-», а не пустую строку: пустая означала бы
    /// «не задано» и вернула бы значение по умолчанию вопреки решению пользователя.</summary>
    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        if (!Enum.TryParse(tag, out Interop.HotkeyAction action)) return;
        _linphone.SetAppString(Interop.GlobalHotkeys.ConfigKey(action), "-");
        RefreshHotkeyButtons();
        ApplyHotkeys();
    }

    private static bool IsDown(Windows.System.VirtualKey k) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnMiniWindowToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("mini_call_window", MiniWindowSwitch.IsOn);
    }

    // ---------------- Обновления ----------------
    private void OnUpdateCheckToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetAppFlag("update_check", UpdateCheckSwitch.IsOn);
    }

    /// <summary>Проверку выполняет главное окно: диалог с предложением обновиться должен
    /// висеть на нём, а не на настройках, которые пользователь тут же закроет.</summary>
    private void OnCheckUpdateClick(object sender, RoutedEventArgs e) => _onCheckUpdate?.Invoke();

    // ---------------- Доверие TLS ----------------
    /// <summary>Файл CA из поставки SDK (share/linphone/rootca.pem) — «как из коробки».</summary>
    private static bool IsBundledCa(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path!.Replace('/', '\\').EndsWith(@"\share\linphone\rootca.pem", StringComparison.OrdinalIgnoreCase);

    private async void OnPickCaClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        foreach (string ext in new[] { ".pem", ".crt", ".cer" }) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        if (file != null) { CaFileBox.Text = file.Path; TlsStatus.Text = ""; }
    }

    private void OnClearCaClick(object sender, RoutedEventArgs e)
    {
        CaFileBox.Text = "";   // применится встроенный набор SDK
        TlsStatus.Text = "";
    }

    private void OnTlsVerifyToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // Предупреждение показываем сразу, не дожидаясь «Применить»: пользователь должен видеть
        // цену выключения проверки в момент, когда он его выключает.
        TlsWarnText.Visibility = TlsVerifySwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnApplyTlsClick(object sender, RoutedEventArgs e)
    {
        string ca = CaFileBox.Text.Trim();
        if (ca.Length > 0 && !File.Exists(ca))
        {
            TlsStatus.Text = Loc.S("St_TlsCaMissing");
            return;
        }
        bool ok = _linphone.SetTls(ca, TlsVerifySwitch.IsOn);
        // Смена доверия действует на новые соединения — молча оставлять старую регистрацию
        // нельзя, иначе пользователь решит, что настройка не сработала.
        if (ok) _linphone.RefreshNetwork();
        TlsStatus.Text = Loc.S(ok ? "St_TlsApplied" : "St_TlsApplyFailed");
    }

    private void OnSaveProvisioningClick(object sender, RoutedEventArgs e)
    {
        string uri = ProvisioningUrlBox.Text.Trim();
        _linphone.SetProvisioningUri(uri);
        _linphone.SetAppString("presence_hook_url", PresenceHookUrlBox.Text.Trim());
        _linphone.SetPresenceHookSecret(PresenceHookSecretBox.Password);
        // Перезапуск нужен только provisioning'у; статус-хук применяется сразу.
        ProvisioningStatus.Text = uri.Length == 0 ? Loc.S("St_UrlCleared") : Loc.S("St_SavedRestart");
    }

    private void OnEchoToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _linphone.SetEchoCancellation(EchoSwitch.IsOn);
    }

    private void OnDtmfChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) _linphone.SetDtmfMethod((DtmfCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "rfc2833");
    }

    private void OnMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (_micTesting) StopMicTest(); // сменили микрофон — сбрасываем замер, проверяем заново уже новый
        if (MicCombo.SelectedValue is string id) _linphone.SetInputDevice(id);
    }

    private void OnSpeakerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && SpeakerCombo.SelectedValue is string id) _linphone.SetOutputDevice(id);
    }

    private void OnRingerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && RingerCombo.SelectedValue is string id) _linphone.SetRingerDevice(id);
    }

    private void OnCameraChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CameraCombo.SelectedItem is not string cam) return;
        bool wasPreview = _cameraPreviewOn;
        if (wasPreview) { _linphone.StopCameraPreview(); _cameraPreviewOn = false; }
        _linphone.SetCamera(cam);
        if (wasPreview) // превью было открыто — перезапускаем уже на новой камере
        {
            _linphone.StartCameraPreview();
            _cameraPreviewOn = true;
        }
    }

    // ---------------- Контакты ----------------
    private void PopulateContacts()
    {
        PhonebookUrlBox.Text = _linphone.GetPhonebookUrl();
        var phonebookAuth = _linphone.GetPhonebookAuth();   // один запрос к ядру на оба поля
        PhonebookUserBox.Text = phonebookAuth.User;
        // Показываем сохранённый пароль — точками, как и положено PasswordBox. Раньше поле
        // оставляли пустым «чтобы не показывать», и человек не мог отличить «пароль есть»
        // от «пароля нет»; пустое поле при этом ещё и означало «не менять», что приходилось
        // объяснять прямо в заголовке. Значение и так лежит под DPAPI и доступно этому же
        // пользователю Windows — скрывать его от него самого смысла нет.
        PhonebookPassBox.Password = phonebookAuth.Password;
        PhonebookStatus.Text = "";
    }

    private async void OnSavePhonebookClick(object sender, RoutedEventArgs e)
    {
        string url = PhonebookUrlBox.Text.Trim();
        _linphone.SetPhonebookUrl(url);
        _linphone.SetPhonebookAuth(PhonebookUserBox.Text, PhonebookPassBox.Password);
        if (url.Length == 0) { PhonebookStatus.Text = Loc.S("St_UrlCleared"); return; }
        PhonebookStatus.Text = Loc.S("St_Checking");
        try
        {
            var (user, pass) = _linphone.GetPhonebookAuth();   // пароль мог остаться прежним
            var list = await Phonebook.FetchAsync(url, user, pass);
            PhonebookStatus.Text = string.Format(Loc.S("St_PhonebookLoaded"), list.Count);
            // Сама книга по http — осознанный риск (подмена номеров), но с логином туда уезжают
            // ещё и учётные данные: Basic-авторизация передаёт их в заголовке открытым текстом.
            // Молча этого не оставляем — пользователь должен знать, что именно он включил.
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && user.Length > 0)
                PhonebookStatus.Text += "\n" + Loc.S("St_PhonebookInsecureAuth");
        }
        catch (Exception ex)
        {
            PhonebookStatus.Text = Loc.S("St_ErrorPrefix") + ex.Message;
        }
    }

    // ---------------- Кодеки и шифрование ----------------
    private void PopulateCodecs()
    {
        _loading = true;
        SelectComboByTag(EncryptionCombo, _linphone.GetMediaEncryption());

        // ObservableCollection + подписка на CollectionChanged: событие приходит уже ПОСЛЕ
        // перемещения (в отличие от DragItemsCompleted, который ловил старый порядок).
        _audioCodecs = new ObservableCollection<CodecInfo>(_linphone.GetAudioCodecs());
        _audioCodecs.CollectionChanged += (_, _) =>
        {
            if (!_loading) _linphone.SetAudioCodecsOrder(_audioCodecs.Select(c => c.Pt));
        };
        AudioCodecsList.ItemsSource = _audioCodecs;

        _videoCodecs = new ObservableCollection<CodecInfo>(_linphone.GetVideoCodecs());
        _videoCodecs.CollectionChanged += (_, _) =>
        {
            if (!_loading) _linphone.SetVideoCodecsOrder(_videoCodecs.Select(c => c.Pt));
        };
        VideoCodecsList.ItemsSource = _videoCodecs;

        _loading = false;
    }

    private void OnEncryptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) _linphone.SetMediaEncryption((EncryptionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "none");
    }

    private void OnCodecToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.DataContext is CodecInfo ci)
        {
            _linphone.SetCodecEnabled(ci.Pt, ts.IsOn);
            ci.Enabled = ts.IsOn;
        }
    }

    // ---------------- О программе ----------------
    private void PopulateAbout()
    {
        UpdateCheckSwitch.IsOn = _linphone.GetAppFlag("update_check", true);
        // Version, а не InformationalVersion: последняя у .NET несёт хвост с git-хэшем
        // («1.2.0+81bdf5f…»), и он оказался бы прямо в окне «О программе».
        AboutVersionText.Text = $"L-Phone {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "?"}";
        AboutSdkText.Text = $"liblinphone {_linphone.CoreVersion}";
        AboutRuntimeText.Text = $".NET {Environment.Version} · WinUI 3";
        _ = ShowLogTailAsync();
    }

    /// <summary>Хвост журнала в окно «О программе».
    ///
    /// Раньше здесь был File.ReadAllText на UI-потоке — а app.log растёт до 4 МБ. То есть
    /// открытие вкладки означало синхронное чтение мегабайтов с диска (через антивирус) и
    /// рендер их же в TextBox: окно замирало, память подскакивала. Читаем в фоне и только
    /// последние строки — при разборе обращения смотрят конец, а не начало.</summary>
    private const int LogTailLines = 500;

    private async Task ShowLogTailAsync()
    {
        LogText.Text = Loc.S("St_Loading");
        string tail = await Task.Run(() => ReadTail(AppLog.FilePath, LogTailLines));
        LogText.Text = tail;
    }

    /// <summary>Последние maxLines строк файла. Читаем потоком, не поднимая файл в память целиком.</summary>
    private static string ReadTail(string path, int maxLines)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var tail = new Queue<string>(maxLines + 1);
            foreach (string line in File.ReadLines(path))
            {
                if (tail.Count == maxLines) tail.Dequeue();
                tail.Enqueue(line);
            }
            return string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex) { return Loc.S("St_ErrorPrefix") + ex.Message; }
    }

    private void OnVerboseLogsToggled(object sender, RoutedEventArgs e)
    {
        _linphone.SetVerboseLogs(VerboseLogsSwitch.IsOn);
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        string dir = AppLog.Directory;
        try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
    }

    private void OnClearLogsClick(object sender, RoutedEventArgs e)
    {
        string localDir = AppLog.Directory;
        // app.log(.1) и crash.log — в LOCALAPPDATA; liblinphone.log — в каталоге профиля (ProgramData\SID).
        foreach (var f in new[] { "app.log", "app.log.1", "crash.log" })
            try { File.Delete(Path.Combine(localDir, f)); } catch { }
        try { File.Delete(Path.Combine(_linphone.ProfileDir, "liblinphone.log")); } catch { }
        LogText.Text = "";
        _ = ShowLogTailAsync();   // журнал пишется дальше — покажем, что осталось после очистки
    }

    /// <summary>Собирает все логи (из ОБОИХ каталогов) + версии в один ZIP на рабочем столе —
    /// пользователю не нужно искать файлы в LOCALAPPDATA и ProgramData\SID вручную.</summary>
    private void OnCollectDiagClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string localDir = AppLog.Directory;
            string zip = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"L-Phone-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var fs = new FileStream(zip, FileMode.Create))
            using (var arc = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                void Add(string path, string entry)
                {
                    try { if (File.Exists(path)) arc.CreateEntryFromFile(path, entry); } catch { }
                }
                Add(Path.Combine(localDir, "app.log"), "app.log");
                Add(Path.Combine(localDir, "app.log.1"), "app.log.1");
                Add(Path.Combine(localDir, "crash.log"), "crash.log");
                Add(Path.Combine(_linphone.ProfileDir, "liblinphone.log"), "liblinphone.log");
                // Версии/окружение — отдельной запиской.
                var info = arc.CreateEntry("info.txt");
                using var w = new StreamWriter(info.Open());
                string ver = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
                w.WriteLine($"L-Phone {ver} · liblinphone {_linphone.CoreVersion}");
                w.WriteLine($"{Environment.OSVersion.VersionString} · .NET {Environment.Version}");
                w.WriteLine($"Профиль: {_linphone.ProfileDir}");
            }
            DiagStatus.Text = zip;
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zip}\""); } catch { }
        }
        catch (Exception ex) { DiagStatus.Text = Loc.S("St_ErrorPrefix") + ex.Message; }
    }

    // ---------------- Утилиты ----------------
    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is ComboBoxItem item && (item.Tag as string) == tag)
            { combo.SelectedIndex = i; return; }
        combo.SelectedIndex = 0;
    }
}
