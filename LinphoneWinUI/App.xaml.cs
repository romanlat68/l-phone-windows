using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace LinphoneWinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // Глобальные обработчики: любое необработанное исключение (в т.ч. из async void
        // обработчиков UI) попадает в crash.log, а не молча роняет процесс без следа.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("XAML", e.Exception);
            e.Handled = true; // софтфону лучше пережить сбой обработчика, чем оборвать звонок
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain", e.ExceptionObject as Exception); // фатально, но след останется
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        // Crash recovery: если процесс упадёт (или его снимет ОС), Windows перезапустит exe.
        // Для софтфона это важно — иначе сотрудник остаётся без связи, пока не заметит. Флаг
        // /restarted помечает такой запуск (можно отличить от обычного). Не восстанавливаем
        // после чистого выхода/обновления/логоффа (по умолчанию RegisterApplicationRestart).
        try { RegisterApplicationRestart("/restarted", 0); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? pwzCommandline, int dwFlags);
}

/// <summary>Крэш-лог для саппорта: %LOCALAPPDATA%\LinphoneWinUI\crash.log.</summary>
internal static class CrashLog
{
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "crash.log");

    public static void Write(string source, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            System.IO.File.AppendAllText(FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* сам крэш-лог падать не должен */ }
    }
}
