using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Data;

namespace LinphoneWinUI;

/// <summary>Запись избранного: имя (если есть) и номер/SIP-адрес для перезвона.</summary>
public sealed record FavoriteEntry(string Name, string Number)
{
    /// <summary>Что показывать крупно: имя, либо сам номер, если имени нет.</summary>
    public string Display => string.IsNullOrWhiteSpace(Name) ? Number : Name;

    public string Initials =>
        string.IsNullOrEmpty(Display) ? "?" :
        (Display.Length >= 2 ? Display[..2].ToUpperInvariant() : Display.ToUpperInvariant());

    /// <summary>Вторая строка: номер (пусто, если имени нет — номер уже в первой строке).</summary>
    public string SubText => string.IsNullOrWhiteSpace(Name) ? "" : Number;
}

/// <summary>
/// Локальное хранилище избранного. Сама адресная книга только для чтения,
/// поэтому пометки храним отдельно в %LOCALAPPDATA%\LinphoneWinUI\favorites.dat
/// (по строке «number\tname»). Ключ — цифры номера (или сам адрес, если цифр нет),
/// чтобы один и тот же абонент из книги и из журнала считался одной записью.
/// </summary>
public static class Favorites
{
    private static readonly object Gate = new();
    private static List<FavoriteEntry>? _cache;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "favorites.dat");

    /// <summary>Нормализованный ключ абонента: цифры номера без «@host», либо сам адрес.</summary>
    public static string Key(string number)
    {
        string raw = number ?? "";
        int at = raw.IndexOf('@');
        if (at >= 0) raw = raw[..at];
        string digits = new(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : (number ?? "").Trim().ToLowerInvariant();
    }

    /// <summary>Текущий список избранного (из кэша; читается с диска при первом обращении).</summary>
    public static List<FavoriteEntry> Load()
    {
        lock (Gate)
        {
            _cache ??= ReadFile();
            return new List<FavoriteEntry>(_cache);
        }
    }

    // Множество ключей избранного — чтобы IsFavorite был O(1). Раньше конвертер звезды
    // дергал Any(...) на каждую строку каждого списка: O(n²) с аллокациями Key() в рендере.
    private static HashSet<string>? _keys;

    private static HashSet<string> Keys()
    {
        _cache ??= ReadFile();
        return _keys ??= new HashSet<string>(_cache.Select(f => Key(f.Number)), StringComparer.Ordinal);
    }

    public static bool IsFavorite(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return false;
        string k = Key(number);
        lock (Gate) return Keys().Contains(k);
    }

    /// <summary>Переключает избранное для номера. Возвращает новое состояние (true = в избранном).</summary>
    public static bool Toggle(string name, string number)
    {
        if (IsFavorite(number)) { Remove(number); return false; }
        Add(name, number);
        return true;
    }

    public static void Add(string name, string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return;
        string k = Key(number);
        lock (Gate)
        {
            _cache ??= ReadFile();
            if (_cache.Any(f => Key(f.Number) == k)) return;
            _cache.Add(new FavoriteEntry((name ?? "").Trim(), number.Trim()));
            WriteFile(_cache);
        }
    }

    public static void Remove(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return;
        string k = Key(number);
        lock (Gate)
        {
            _cache ??= ReadFile();
            if (_cache.RemoveAll(f => Key(f.Number) == k) > 0) WriteFile(_cache);
        }
    }

    private static List<FavoriteEntry> ReadFile()
    {
        var list = new List<FavoriteEntry>();
        try
        {
            if (!File.Exists(FilePath)) return list;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split('\t');
                string number = parts[0].Trim();
                string name = parts.Length > 1 ? parts[1].Trim() : "";
                if (number.Length > 0) list.Add(new FavoriteEntry(name, number));
            }
        }
        catch { }
        return list;
    }

    private static void WriteFile(List<FavoriteEntry> list)
    {
        _keys = null;   // состав избранного изменился — пересоберём множество ключей лениво
        try { SafeFile.WriteLines(FilePath, list.Select(f => $"{SafeFile.Field(f.Number)}\t{SafeFile.Field(f.Name)}")); }
        catch { }
    }
}

/// <summary>Инверсия bool (для взаимно-исключающих переключателей вида).</summary>
public sealed class NotBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not bool b || !b;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not bool b || !b;
}

/// <summary>true → Visible, false → Collapsed. Для показа кнопки правки только у локальных контактов.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (value is bool b && b) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Глиф звезды для строки: заполненная (FavoriteStarFill), если номер в избранном, иначе контур (FavoriteStar).</summary>
public sealed class FavoriteGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => Favorites.IsFavorite(value as string ?? "") ? "" : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
