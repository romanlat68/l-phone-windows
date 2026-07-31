using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LinphoneWinUI;

/// <summary>
/// Локальная (редактируемая) адресная книга — поверх read-only внешней книги (Phonebook).
/// Хранится в %LOCALAPPDATA%\LinphoneWinUI\contacts.dat, по строке «id\tname\tnumber\tmobile».
/// У каждой записи стабильный Id, чтобы редактировать/удалять независимо от смены номера.
/// </summary>
public static class LocalContacts
{
    private static readonly object Gate = new();
    private static List<ContactEntry>? _cache;

    /// <summary>Куда жаловаться на отказ файла. Ставит MainWindow при старте.
    /// Без этого канала провал записи был НЕВИДИМ: пользователь сохранял контакт, диалог
    /// закрывался, книга в памяти обновлялась — а на диск ничего не легло, и после перезапуска
    /// контакт исчезал. Молчать о потере пользовательских данных нельзя.</summary>
    public static Action<string>? Log;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "contacts.dat");

    public static List<ContactEntry> Load()
    {
        lock (Gate)
        {
            _cache ??= ReadFile();
            return new List<ContactEntry>(_cache);
        }
    }

    /// <summary>Создаёт (если id пустой) или обновляет локальный контакт. Возвращает его Id.</summary>
    public static string AddOrUpdate(string? id, string name, string number, string mobile)
    {
        lock (Gate)
        {
            _cache ??= ReadFile();
            string realId = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
            var entry = new ContactEntry(name.Trim(), number.Trim(), mobile.Trim(), "", IsLocal: true, Id: realId);
            int i = _cache.FindIndex(c => c.Id == realId);
            if (i >= 0) _cache[i] = entry; else _cache.Add(entry);
            WriteFile(_cache);
            return realId;
        }
    }

    public static void Remove(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (Gate)
        {
            _cache ??= ReadFile();
            if (_cache.RemoveAll(c => c.Id == id) > 0) WriteFile(_cache);
        }
    }

    /// <summary>Разбор строки файла: «id\tname\tnumber\tmobile». null — строку пропускаем.
    /// Битая строка не должна ронять загрузку всей книги: файл правится и руками, и
    /// прошлыми версиями, а потерять из-за одной строки все контакты недопустимо.</summary>
    internal static ContactEntry? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string[] p = line!.Split('\t');
        if (p.Length < 4) return null;
        string id = p[0].Trim();
        if (id.Length == 0) return null;   // без стабильного Id запись нельзя ни править, ни удалять
        return new ContactEntry(p[1], p[2], p[3], "", IsLocal: true, Id: id);
    }

    /// <summary>Сериализация записи. Поля прогоняются через SafeFile.Field: таб или перевод
    /// строки в имени иначе сдвинули бы колонки и превратили запись в мусор при чтении.</summary>
    internal static string FormatLine(ContactEntry c) =>
        $"{c.Id}\t{SafeFile.Field(c.Name)}\t{SafeFile.Field(c.Number)}\t{SafeFile.Field(c.Mobile)}";

    private static List<ContactEntry> ReadFile()
    {
        var list = new List<ContactEntry>();
        try
        {
            if (!File.Exists(FilePath)) return list;
            foreach (string line in File.ReadAllLines(FilePath))
                if (ParseLine(line) is { } entry) list.Add(entry);
        }
        catch (Exception ex)
        {
            // Книгу не прочитали — пользователь увидит пустой список вместо своих контактов.
            // Пустой список сам по себе не отличить от «контактов нет», поэтому пишем причину.
            Log?.Invoke($"Локальные контакты: не удалось прочитать {FilePath} — {ex.Message}");
        }
        return list;
    }

    private static void WriteFile(List<ContactEntry> list)
    {
        try { SafeFile.WriteLines(FilePath, list.Select(FormatLine)); }
        catch (Exception ex)
        {
            Log?.Invoke($"Локальные контакты: НЕ СОХРАНЕНЫ в {FilePath} — {ex.Message}");
        }
    }
}
