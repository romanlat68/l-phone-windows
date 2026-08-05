using System.IO;
using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Переезд файлов на новую раскладку каталогов (журналы в logs\, временное в cache\).
///
/// Проверяется живьём ровно один раз — при первом запуске новой версии у каждого пользователя,
/// и если правило ошибётся, человек лишится журнала молча: приложение продолжит работать,
/// а поддержка будет разбирать обращение без единой строки истории.
/// </summary>
public class AppPathsTests
{
    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "lphone-paths-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void MovesFileToNewPlace()
    {
        string dir = TempDir();
        string from = Path.Combine(dir, "app.log");
        string to = Path.Combine(dir, "logs", "app.log");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.WriteAllText(from, "старый журнал");

        Assert.True(AppPaths.MoveIfAbsent(from, to));

        Assert.False(File.Exists(from));
        Assert.Equal("старый журнал", File.ReadAllText(to));

        Directory.Delete(dir, true);
    }

    // Второй запуск не должен ничего трогать: файл уже на новом месте и с тех пор пополнялся.
    [Fact]
    public void DoesNotOverwriteWhatAlreadyMoved()
    {
        string dir = TempDir();
        string from = Path.Combine(dir, "app.log");
        string to = Path.Combine(dir, "logs", "app.log");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.WriteAllText(from, "прошлогодний хвост");
        File.WriteAllText(to, "свежий журнал");

        Assert.False(AppPaths.MoveIfAbsent(from, to));

        // Свежий журнал цел, старый остался на месте — потерять его молча нельзя.
        Assert.Equal("свежий журнал", File.ReadAllText(to));
        Assert.True(File.Exists(from));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void MissingSourceIsNotAnError()
    {
        string dir = TempDir();

        Assert.False(AppPaths.MoveIfAbsent(Path.Combine(dir, "нет-такого.log"),
                                           Path.Combine(dir, "logs", "нет-такого.log")));

        Directory.Delete(dir, true);
    }

    // Каталога назначения нет (первый запуск оборвался между созданием и переносом) — приложение
    // обязано запуститься, а не упасть на статическом конструкторе путей.
    [Fact]
    public void MissingTargetDirectoryDoesNotThrow()
    {
        string dir = TempDir();
        string from = Path.Combine(dir, "crash.log");
        File.WriteAllText(from, "x");

        Assert.False(AppPaths.MoveIfAbsent(from, Path.Combine(dir, "нет-каталога", "crash.log")));
        Assert.True(File.Exists(from));   // остался там, где был

        Directory.Delete(dir, true);
    }

    // Раскладка: журналы отдельно от всего остального — ради этого всё и затевалось.
    [Fact]
    public void LogsAndCacheAreSeparateSubdirectories()
    {
        Assert.Equal(Path.Combine(AppPaths.Root, "logs"), AppPaths.Logs);
        Assert.Equal(Path.Combine(AppPaths.Root, "cache"), AppPaths.Cache);
        Assert.NotEqual(AppPaths.Logs, AppPaths.Cache);
        // Журнал приложения лежит именно в папке журналов — её показывают по кнопке
        // «Открыть папку логов» и из неё собирают диагностический архив.
        Assert.Equal(AppPaths.Logs, AppLog.Directory);
        Assert.Equal(Path.Combine(AppPaths.Logs, "app.log"), AppLog.FilePath);
    }
}
