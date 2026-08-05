using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace LinphoneWinUI;

/// <summary>
/// Свой Main вместо сгенерированного XAML (DISABLE_XAML_GENERATED_MAIN в csproj):
/// приложение — ЕДИНСТВЕННЫЙ экземпляр. Два процесса открывали один linphonerc,
/// оба регистрировались на АТС и перетирали конфиг друг друга.
/// Повторный запуск сигналит живому экземпляру «покажи окно» и завершается;
/// с аргументом tel:/sip: (click-to-call) — передаёт ему номер на набор.
/// </summary>
public static class Program
{
    private static Mutex? _singleMutex; // держим всё время жизни процесса

    /// <summary>Сигнал от второго экземпляра «разверни окно» (MainWindow на него подписан).</summary>
    public static EventWaitHandle ShowSignal { get; private set; } = null!;

    /// <summary>Сигнал «набери номер из файла запроса» (click-to-call из второго экземпляра).</summary>
    public static EventWaitHandle DialSignal { get; private set; } = null!;

    /// <summary>tel:/sip:-аргумент ПЕРВОГО запуска — MainWindow наберёт его после старта Core.</summary>
    public static string? PendingDialUri { get; private set; }

    /// <summary>Файл-почтовый ящик для передачи URI живому экземпляру (EventWaitHandle без payload).</summary>
    public static string DialRequestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "dial.req");

    [STAThread]
    private static void Main(string[] args)
    {
        // Кодовые страницы (windows-1251 и пр.) — для XML телефонных книг и имён устройств.
        // Регистрируем до любого кода, который может их запросить (XDocument.Load по декларации).
        try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { }

        string? dialUri = args.FirstOrDefault(a =>
            a.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("callto:", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("sips:", StringComparison.OrdinalIgnoreCase));

        // Local\ — в пределах сессии пользователя: у разных пользователей машины свои экземпляры
        // (как и профили, см. SID-подкаталог в LinphoneService.Start). Сигналы «покажи окно» и
        // «набери номер» ИМЕЮТ смысл только внутри сессии: развернуть окно на чужом рабочем столе
        // всё равно нельзя, поэтому они остаются Local. Namespace Global\ здесь не годится —
        // создание объектов в нём требует SeCreateGlobalPrivilege, которой у обычного
        // пользователя в RDP-сессии нет: первый же запуск падал бы с отказом в доступе.
        _singleMutex = new Mutex(initiallyOwned: true, @"Local\L-Phone-SingleInstance", out bool first);
        ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\L-Phone-ShowWindow");
        DialSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\L-Phone-Dial");
        // А вот профиль (ProgramData\L-Phone\<SID>) у ДВУХ СЕАНСОВ одного пользователя общий:
        // RDP + консоль на терминальном сервере = два ядра над одним linphonerc, двойная
        // регистрация и перетёртый конфиг. Mutex сессии этого не ловит, а эксклюзивная блокировка
        // файла — межсессионная и никаких привилегий не требует. Держим её всё время жизни процесса.
        if (first && !TryLockProfile())
        {
            // Профиль занят ДРУГИМ сеансом того же пользователя (RDP + консоль на терминальном
            // сервере). Просто выйти нельзя: сигнал «покажи окно» уходит в СВОЮ сессию, где
            // слушателя нет по определению — владелец профиля в другой. Пользователь щёлкал по
            // ярлыку и не получал ничего: ни окна, ни сообщения, ни строки в логе (он ещё не поднят).
            ShowSignal.Set();   // вдруг живой экземпляр всё же в этой сессии — пусть развернётся
            ShowStartupError(
                Localized("St_AlreadyRunningTitle", "L-Phone"),
                Localized("St_AlreadyRunningText",
                    "L-Phone уже запущен в другом сеансе Windows этого пользователя.\n\n" +
                    "Два экземпляра работают с одним профилем и мешают друг другу: перетирают " +
                    "настройки и дважды регистрируются на АТС. Завершите L-Phone в том сеансе " +
                    "и запустите снова."));
            return;             // портить чужой конфиг нельзя
        }
        if (!first)
        {
            if (dialUri != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DialRequestPath)!);
                    File.WriteAllText(DialRequestPath, dialUri);
                    DialSignal.Set();   // живой экземпляр прочитает файл и наберёт номер
                }
                catch { ShowSignal.Set(); }
            }
            else
            {
                ShowSignal.Set(); // попросить живой экземпляр развернуться из трея
            }
            return;
        }

        PendingDialUri = dialUri;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            new App();
        });
        GC.KeepAlive(_singleMutex);
        GC.KeepAlive(_profileLock);
    }

    /// <summary>Сообщение об отказе старта — ДО создания XAML-приложения, поэтому окном ОС,
    /// а не ContentDialog. Молчаливый выход недопустим: с точки зрения пользователя ярлык
    /// просто не работает.</summary>
    private static void ShowStartupError(string title, string text)
    {
        try { MessageBoxW(IntPtr.Zero, text, title, MB_OK | MB_ICONWARNING | MB_SETFOREGROUND); }
        catch { }
    }

    private const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_SETFOREGROUND = 0x10000;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Строка из ресурсов; при любой осечке (ресурсы ещё не подняты, ключа нет) —
    /// запасной текст. Loc.S на промахе возвращает САМ КЛЮЧ, и показать пользователю
    /// «St_AlreadyRunningText» было бы хуже, чем показать русский текст.</summary>
    private static string Localized(string key, string fallback)
    {
        try
        {
            string s = Loc.S(key);
            return string.IsNullOrEmpty(s) || s == key ? fallback : s;
        }
        catch { return fallback; }
    }

    private static FileStream? _profileLock;   // держим до конца процесса — освобождает ОС

    /// <summary>Эксклюзивно занять профиль пользователя. false — им уже владеет другой сеанс.</summary>
    private static bool TryLockProfile()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinphoneWinUI", "instance.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _profileLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }          // файл занят другим сеансом
        catch (UnauthorizedAccessException) { return true; }  // не смогли проверить — не мешаем запуску
    }
}
