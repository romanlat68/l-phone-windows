using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LinphoneWinUI;

/// <summary>
/// Журнал приложения (%LOCALAPPDATA%\LinphoneWinUI\logs\app.log) — тот, что уходит в саппорт.
///
/// Вынесен из MainWindow: логирование не имеет отношения к окну, но жило внутри него, из-за
/// чего путь к файлу приходилось дублировать в SettingsWindow, а проверять маскировку — через
/// внутренности окна.
///
/// Пишем АСИНХРОННО: синхронный AppendAllText на каждое событие блокировал UI-поток (диск,
/// антивирус), а событий при звонке много. Канал + один фоновый писатель, строки пачками,
/// ротация по 4 МБ (app.log → app.log.1).
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>Куда пишем. Один источник истины: окно настроек читает и чистит этот же файл.</summary>
    public static readonly string FilePath = Path.Combine(AppPaths.Logs, "app.log");

    /// <summary>Каталог журналов — там же crash.log и app.log.1, и БОЛЬШЕ НИЧЕГО: раньше сюда
    /// же складывались тестовые wav и кэш книги, и «папка логов» превращалась в свалку.</summary>
    public static string Directory => AppPaths.Logs;

    private static readonly Channel<string> Queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    private static readonly Task Writer = Task.Run(WriterLoop);

    /// <summary>Записать строку. Не блокирует вызывающего и не бросает.</summary>
    public static void Write(string message) =>
        Queue.Writer.TryWrite($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Mask(message)}");

    /// <summary>Шапка при старте — версия приложения и ОС (для разборов у саппорта).</summary>
    public static void WriteHeader(string version) =>
        Queue.Writer.TryWrite($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === L-Phone {version} · "
                              + $"{Environment.OSVersion.VersionString} · .NET {Environment.Version} ===");

    // Приватность app.log (уходит в саппорт): внешние номера маскируем в середине, оставляя
    // начало/конец для идентификации звонка (+74951234567 → +7495*****67). Короткие внутренние
    // номера (3–6 цифр) — не ПД, не трогаем. Полная SIP-трасса при необходимости есть в
    // liblinphone.log (техлог, лежит отдельно).
    private static readonly Regex LongDigits = new(@"\d{7,}", RegexOptions.Compiled);

    internal static string Mask(string s) => LongDigits.Replace(s, m =>
    {
        string d = m.Value;
        return d.Length <= 6 ? d : d[..4] + new string('*', d.Length - 6) + d[^2..];
    });

    /// <summary>Дописать накопившееся перед завершением процесса.</summary>
    public static void Flush()
    {
        Queue.Writer.TryComplete();
        try { Writer.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    private static async Task WriterLoop()
    {
        var reader = Queue.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = FilePath + ".1";
                    try { File.Delete(old); } catch { }
                    try { File.Move(FilePath, old); } catch { }
                }
                using var sw = new StreamWriter(FilePath, append: true);
                while (reader.TryRead(out string? line)) sw.WriteLine(line);
            }
            catch { /* журнал не должен ронять приложение */ }
        }
    }
}
