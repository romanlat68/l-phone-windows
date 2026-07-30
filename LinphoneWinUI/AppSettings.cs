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

/// <summary>Настройки уровня приложения, нужные ДО старта Core (язык интерфейса).
/// Храним в простом файле — читается в конструкторе App, раньше liblinphone.</summary>
internal static class AppSettings
{
    private static string LangFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "lang.txt");

    /// <summary>Код языка интерфейса ("ru", "en", "zh-Hans") или "" = системный.</summary>
    public static string GetLanguage()
    {
        try { return File.Exists(LangFile) ? File.ReadAllText(LangFile).Trim() : ""; }
        catch { return ""; }
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
