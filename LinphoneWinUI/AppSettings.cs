using System;
using System.Collections.Generic;
using System.IO;

namespace LinphoneWinUI;

/// <summary>Атомарная запись пользовательских файлов: пишем во временный и заменяем
/// одним системным вызовом. Смерть процесса в момент записи не оставит обрезанный/пустой
/// файл (иначе терялись бы контакты/избранное).</summary>
internal static class SafeFile
{
    public static void WriteLines(string path, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllLines(tmp, lines);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    /// <summary>Значение поля для TSV-строки: табуляции и переводы строки заменяются
    /// пробелом. Без этого имя контакта с табом/энтером (вставили из буфера) ломало
    /// формат «поле\tполе» при следующем чтении файла.</summary>
    public static string Field(string? value) =>
        (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>Положение и размер окна в ФИЗИЧЕСКИХ пикселях экрана.</summary>
internal readonly record struct WindowGeometry(int X, int Y, int Width, int Height);

/// <summary>Настройки уровня приложения, нужные ДО старта Core (язык интерфейса, геометрия окна).
/// Храним в простом файле — читается в конструкторе App, раньше liblinphone.
///
/// Почему не в linphonerc: и язык, и размер окна нужны РАНЬШЕ, чем поднято ядро. Читать их
/// оттуда невозможно — фасад в этот момент отдаёт значение по умолчанию.</summary>
internal static class AppSettings
{
    private static string LangFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "lang.txt");

    private static string WindowFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "window.txt");

    /// <summary>Сохранённая геометрия главного окна; null — не сохранялась либо файл испорчен.
    /// Проверку «помещается ли это на нынешних мониторах» делает вызывающий: здесь мы не знаем
    /// про экраны, а конфигурация мониторов между запусками меняется (док-станция, RDP).</summary>
    public static WindowGeometry? GetWindowGeometry()
    {
        try { return File.Exists(WindowFile) ? ParseGeometry(File.ReadAllText(WindowFile)) : null; }
        catch { return null; }
    }

    public static void SetWindowGeometry(WindowGeometry g)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WindowFile)!);
            File.WriteAllText(WindowFile, FormatGeometry(g));
        }
        catch { /* не смогли запомнить размер — не повод мешать выходу из приложения */ }
    }

    /// <summary>Разбор строки «x,y,w,h». null — мусор: файл могли править руками или обрезать
    /// на записи, и в этом случае лучше открыться с размером по умолчанию, чем в никуда.
    /// Отрицательные X/Y допустимы — так выглядит монитор слева от основного.</summary>
    internal static WindowGeometry? ParseGeometry(string? text)
    {
        string[] p = (text ?? "").Trim().Split(',');
        if (p.Length != 4) return null;
        if (!int.TryParse(p[0].Trim(), out int x) || !int.TryParse(p[1].Trim(), out int y) ||
            !int.TryParse(p[2].Trim(), out int w) || !int.TryParse(p[3].Trim(), out int h)) return null;
        if (w <= 0 || h <= 0) return null;
        return new WindowGeometry(x, y, w, h);
    }

    internal static string FormatGeometry(WindowGeometry g) => $"{g.X},{g.Y},{g.Width},{g.Height}";

    /// <summary>Код языка интерфейса ("ru", "en", "zh-Hans") или "" = системный.</summary>
    public static string GetLanguage()
    {
        try { return NormalizeLanguage(File.Exists(LangFile) ? File.ReadAllText(LangFile) : ""); }
        catch { return ""; }
    }

    /// <summary>
    /// Приводит код языка к тому виду, в котором строки лежат в PRI.
    ///
    /// makepri схлопывает РЕГИОНАЛЬНЫЙ субтег, когда регион в наборе один: строки из папки
    /// <c>Strings\en-US</c> оказываются под квалификатором <c>en</c>. Запрос по полному тегу
    /// не находит ничего, и ResourceManager молча отдаёт язык по умолчанию — то есть выбор
    /// «English» не включал английский вообще, а выглядело это как «настройка не сохранилась».
    ///
    /// Субтег ПИСЬМЕННОСТИ (<c>zh-Hans</c>) схлопывать нельзя: китайский упрощённый и
    /// традиционный — разные наборы строк, и в PRI они лежат именно так.
    /// </summary>
    internal static string NormalizeLanguage(string? lang)
    {
        string s = (lang ?? "").Trim();
        int dash = s.IndexOf('-');
        if (dash <= 0) return s;
        string tail = s[(dash + 1)..];
        // Письменность записывается четырьмя буквами (Hans, Hant, Cyrl, Latn) — её оставляем.
        bool isScript = tail.Length == 4 && char.IsLetter(tail[0]);
        return isScript ? s : s[..dash];
    }

    public static void SetLanguage(string lang)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LangFile)!);
            File.WriteAllText(LangFile, lang ?? "");
        }
        catch { }
    }
}
