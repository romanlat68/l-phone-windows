using System;
using System.IO;

namespace LinphoneWinUI;

/// <summary>
/// Где приложение держит свои файлы. Раньше всё сваливалось в корень
/// %LOCALAPPDATA%\LinphoneWinUI: журналы, данные, настройки, временные wav-файлы проверки
/// звука — вперемешку. Каталог при этом называли «папкой логов» и отдавали в поддержку,
/// то есть человек, которого попросили прислать журнал, видел четырнадцать разных файлов
/// и не знал, какие из них к делу.
///
/// Теперь три роли разведены по подкаталогам:
///   logs\  — журналы (app.log, app.log.1, crash.log): это и есть «папка логов»;
///   cache\ — то, что можно удалить без потерь (тестовые wav, кэш адресной книги);
///   корень — данные и настройки (contacts.dat, favorites.dat, cred.dat, lang.txt, window.txt).
///
/// Существующие файлы переезжают сами при первом запуске новой версии.
/// </summary>
internal static class AppPaths
{
    /// <summary>Корень данных приложения. Данные и настройки остаются здесь.</summary>
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI");

    /// <summary>Журналы — только они. Именно этот каталог показываем как «папку логов».</summary>
    public static readonly string Logs = Path.Combine(Root, "logs");

    /// <summary>Удаляемое без потерь: тестовые записи звука, кэш книги.</summary>
    public static readonly string Cache = Path.Combine(Root, "cache");

    // Статический конструктор, а не ленивая инициализация в каждом обращении: каталоги нужны
    // ДО первой записи в журнал, а первое обращение к любому полю класса как раз её и опережает.
    static AppPaths()
    {
        Create(Logs);
        Create(Cache);
        // Переезд со старой раскладки. Тихий: если файл занят (запущен второй экземпляр) или
        // прав не хватило, приложение обязано работать дальше — потеря старого журнала не повод
        // не запускаться. Новые записи в любом случае пойдут уже в новое место.
        Move("app.log", Logs);
        Move("app.log.1", Logs);
        Move("crash.log", Logs);
        Move("mic-test.wav", Cache);
        Move("speaker-test.wav", Cache);
        Move("speaker-check.wav", Cache);
        Move("phonebook.xml", Cache);
    }

    private static void Create(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { }
    }

    private static void Move(string name, string target) =>
        MoveIfAbsent(Path.Combine(Root, name), Path.Combine(target, name));

    /// <summary>Перенести файл, если он есть и на новом месте его ещё нет.</summary>
    /// <returns>true — перенесли. Отдельным методом с явными путями, чтобы правило
    /// «не затирать то, что уже переехало» проверялось тестами, а не только на живой машине:
    /// ошибка здесь стоит человеку журнала, а поддержке — разбора вслепую.</returns>
    internal static bool MoveIfAbsent(string from, string to)
    {
        try
        {
            // Уже есть на новом месте — старый оставляем как есть, чтобы не затереть свежий
            // журнал прошлогодним. Такой хвост безвреден и виден человеку.
            if (!File.Exists(from) || File.Exists(to)) return false;
            File.Move(from, to);
            return true;
        }
        catch
        {
            // Файл занят вторым экземпляром или прав не хватило. Приложение обязано работать
            // дальше: новые записи пойдут уже в новое место, а старый файл просто останется.
            return false;
        }
    }
}
