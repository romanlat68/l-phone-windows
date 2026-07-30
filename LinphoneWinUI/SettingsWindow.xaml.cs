using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinphoneWinUI.Interop;
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
    private readonly LinphoneService _linphone;
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

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "app.log");

    public SettingsWindow(LinphoneService linphone, Action onAccountChanged, Action<string> onThemeChanged,
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
        Tr.Set(NiCodecs, "NavCodecs.Content");
        Tr.Set(NiBehavior, "NavBehavior.Content");
        Tr.Set(NiAdvanced, "NavAdvanced.Content");
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

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "account";
        StopDeviceTests(); // уходя с вкладки устройств — глушим замер микрофона и превью камеры
        AccountPanel.Visibility = tag == "account" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = tag == "devices" ? Visibility.Visible : Visibility.Collapsed;
        ContactsPanel.Visibility = tag == "contacts" ? Visibility.Visible : Visibility.Collapsed;
        CodecsPanel.Visibility = tag == "codecs" ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPanel.Visibility = tag == "behavior" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPanel.Visibility = tag == "advanced" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;

        switch (tag)
        {
            case "account": PopulateAccount(); break;
            case "devices": PopulateDevices(); break;
            case "contacts": PopulateContacts(); break;
            case "codecs": PopulateCodecs(); break;
            case "behavior": PopulateBehavior(); break;
            case "advanced": PopulateAdvanced(); break;
            case "about": PopulateAbout(); break;
        }
    }

    // ---------------- Аккаунты (мультиаккаунт) ----------------
    /// <summary>Строка списка аккаунтов (точка статуса + имя/sip + пометка «основной»).</summary>
    private sealed class AccountRow
    {
        public AccountInfo Info { get; init; }
        public string Identity => Info.SipAddress;
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public Brush Dot { get; init; } = new SolidColorBrush(Colors.Gray);
        public Visibility DefaultVisibility { get; init; }
    }

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
        var rows = _linphone.GetAccounts().Select(ToRow).ToList();
        _loading = true;
        AccountsList.ItemsSource = rows;
        var sel = rows.FirstOrDefault(r => r.Identity == keep) ?? (reloadForm ? rows.FirstOrDefault() : null);
        AccountsList.SelectedItem = sel;
        _loading = false;

        if (reloadForm)
        {
            if (sel != null) LoadForm(sel.Info);
            else ClearForm();
        }
    }

    private static AccountRow ToRow(AccountInfo a) => new()
    {
        Info = a,
        Title = string.IsNullOrEmpty(a.DisplayName) ? a.Login : a.DisplayName,
        Subtitle = a.SipAddress,
        Dot = new SolidColorBrush(StateColor(a.State)),
        DefaultVisibility = a.IsDefault ? Visibility.Visible : Visibility.Collapsed,
    };

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
        SelectComboByTag(DtmfCombo, _linphone.GetDtmfMethod());
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
            items.Add(new RingtoneItem(PrettyRingtoneName(current) + " (свой)", current));

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
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "speaker-check.wav");
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
        string dir = Path.GetDirectoryName(LogFilePath) ?? Path.GetTempPath();
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
    private void PopulateBehavior()
    {
        _loading = true;
        AutostartSwitch.IsOn = Autostart.IsEnabled();
        TraySwitch.IsOn = _linphone.GetAppFlag("minimize_to_tray", true);
        StartHiddenSwitch.IsOn = _linphone.GetAppFlag("start_hidden", false);
        IncomingFrontSwitch.IsOn = _linphone.GetAppFlag("incoming_to_front", false);
        PublishPresenceSwitch.IsOn = _linphone.GetAppFlag("publish_presence", false);   // Asterisk PUBLISH не принимает
        AutoAnswerSwitch.IsOn = _linphone.GetAppFlag("auto_answer", false);
        int.TryParse(_linphone.GetAppString("auto_answer_delay", "0"), out int autoDelay);
        AutoAnswerDelayBox.Value = autoDelay;
        AutoAnswerDelayBox.IsEnabled = AutoAnswerSwitch.IsOn;
        AutoRedialSwitch.IsOn = _linphone.GetAppFlag("auto_redial", false);
        int.TryParse(_linphone.GetAppString("auto_redial_delay", "30"), out int redialDelay);
        AutoRedialDelayBox.Value = redialDelay < 5 ? 30 : redialDelay;
        AutoRedialDelayBox.IsEnabled = AutoRedialSwitch.IsOn;
        VoicemailNumberBox.Text = _linphone.GetVoicemailNumber();
        ClickToCallSwitch.IsOn = ClickToCall.IsRegistered();
        _loading = false;
        _behaviorReady = true;
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
    private void PopulateAdvanced()
    {
        _loading = true;
        SelectComboByTag(ThemeCombo, _linphone.GetTheme());
        SelectComboByTag(LangCombo, AppSettings.GetLanguage());
        var (stun, ice, turn, turnUser) = _linphone.GetNat();
        StunBox.Text = stun;
        IceSwitch.IsOn = ice;
        TurnSwitch.IsOn = turn;
        TurnUserBox.Text = turnUser;
        TurnPassBox.Password = "";   // пароль не читается обратно; пусто = оставить прежний
        TurnRealmBox.Text = _linphone.GetAppString("turn_realm", "");   // ключ тот же, что в Android-клиенте
        UpdateCheckSwitch.IsOn = _linphone.GetAppFlag("update_check", true);
        var (caFile, tlsVerify) = _linphone.GetTls();
        // Бандл, приехавший с SDK, показываем как «пусто» — это состояние по умолчанию,
        // а не выбор пользователя; свой файл виден полным путём.
        CaFileBox.Text = IsBundledCa(caFile) ? "" : caFile;
        TlsVerifySwitch.IsOn = tlsVerify;
        TlsWarnText.Visibility = tlsVerify ? Visibility.Collapsed : Visibility.Visible;
        TlsStatus.Text = "";
        ProvisioningUrlBox.Text = _linphone.GetProvisioningUri();
        PresenceHookUrlBox.Text = _linphone.GetAppString("presence_hook_url", "");
        PresenceHookSecretBox.Password = _linphone.GetPresenceHookSecret();
        ProvisioningStatus.Text = "";
        _loading = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string lang = (LangCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        AppSettings.SetLanguage(lang);
        ProvisioningStatus.Text = Loc.S("St_LangNote");
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
        bool ok = _linphone.SetNat(StunBox.Text, IceSwitch.IsOn, TurnSwitch.IsOn,
                                   TurnUserBox.Text, TurnPassBox.Password, realm);
        // Раньше «применено» печаталось безусловно, хотя SetNat умеет молча ничего не сделать.
        ProvisioningStatus.Text = Loc.S(ok ? "St_NetApplied" : "St_NetApplyFailed");
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
        PhonebookUserBox.Text = _linphone.GetPhonebookAuth().User;
        PhonebookPassBox.Password = "";   // пароль обратно не показываем; пусто = не менять
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
        AboutSdkText.Text = $"liblinphone {_linphone.CoreVersion}";
        AboutRuntimeText.Text = $".NET {Environment.Version} · WinUI 3";
        try { LogText.Text = File.Exists(LogFilePath) ? File.ReadAllText(LogFilePath) : ""; } catch { }
    }

    private void OnVerboseLogsToggled(object sender, RoutedEventArgs e)
    {
        _linphone.SetVerboseLogs(VerboseLogsSwitch.IsOn);
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        string dir = Path.GetDirectoryName(LogFilePath) ?? "";
        try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
    }

    private void OnClearLogsClick(object sender, RoutedEventArgs e)
    {
        string localDir = Path.GetDirectoryName(LogFilePath) ?? "";
        // app.log(.1) и crash.log — в LOCALAPPDATA; liblinphone.log — в каталоге профиля (ProgramData\SID).
        foreach (var f in new[] { "app.log", "app.log.1", "crash.log" })
            try { File.Delete(Path.Combine(localDir, f)); } catch { }
        try { File.Delete(Path.Combine(_linphone.ProfileDir, "liblinphone.log")); } catch { }
        LogText.Text = "";
    }

    /// <summary>Собирает все логи (из ОБОИХ каталогов) + версии в один ZIP на рабочем столе —
    /// пользователю не нужно искать файлы в LOCALAPPDATA и ProgramData\SID вручную.</summary>
    private void OnCollectDiagClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string localDir = Path.GetDirectoryName(LogFilePath) ?? "";
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
