using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>Строка списка «кому перевести»: человек, номер и лампа занятости.</summary>
public sealed class TransferTargetRow
{
    public string Display { get; init; } = "";
    public string Number { get; init; } = "";

    /// <summary>Исходная строка избранного, если человек оттуда. Держим ССЫЛКУ, а не копию
    /// кисти: кисть — объект XAML, её нельзя создавать вне потока интерфейса. Модель, которая
    /// это делала, падала в тестах с COMException — значит и в приложении работала бы лишь
    /// по счастливой случайности.</summary>
    public FavoriteRow? Favorite { get; init; }

    /// <summary>Лампу показываем только для тех, за кем реально следим (избранные): подписка
    /// BLF идёт по ним, и «свободен» для остальных был бы выдумкой, на которую оператор
    /// положится.</summary>
    public bool HasLamp => Favorite != null;
}

/// <summary>
/// Кому можно перевести звонок. Один список на два случая: пока поле поиска пусто —
/// избранные коллеги с лампами занятости, как только начали набирать — совпадения из всей
/// адресной книги.
///
/// Раньше это были две независимые выборки: список показывал избранное, а поле поиска
/// отдельно выпадало своими подсказками по книге. На экране жили два разных набора людей,
/// и было непонятно, какой из них главный.
/// </summary>
public partial class TransferTargetsViewModel : ObservableObject
{
    public ObservableCollection<TransferTargetRow> Rows { get; } = new();

    /// <summary>Сколько строк показываем при поиске. Книга на 74 человека в окне шириной
    /// 400 пикселей — это не список, а простыня; уточнить запрос быстрее, чем прокрутить.</summary>
    public const int MaxRows = 25;

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    /// <summary>Заголовок над списком: что именно сейчас показано.</summary>
    [ObservableProperty] public partial string Caption { get; set; } = "";

    /// <summary>Перестроить список под текущий запрос.</summary>
    public void Update(IEnumerable<FavoriteRow> favorites, IEnumerable<ContactEntry> contacts,
                       string? query, Func<string, string> loc)
    {
        var rows = Build(favorites, contacts, query);
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        IsEmpty = Rows.Count == 0;
        Caption = string.IsNullOrWhiteSpace(query) ? loc("Xfer_Favorites") : loc("Xfer_Found");
    }

    /// <summary>Отбор строк. Статикой и без обращений к ядру — это решает, кого оператор
    /// увидит в момент перевода, и должно проверяться тестами.</summary>
    public static IReadOnlyList<TransferTargetRow> Build(
        IEnumerable<FavoriteRow> favorites, IEnumerable<ContactEntry> contacts, string? query)
    {
        var favs = favorites?.ToList() ?? new List<FavoriteRow>();
        string q = (query ?? "").Trim();

        if (q.Length == 0)
            return favs.Select(FromFavorite).ToList();

        // Ищем по всей книге, но избранных ставим первыми: чаще всего переводят именно им,
        // и их лампы — единственные, которым можно верить.
        var byFav = favs.Where(f => Matches(f.Display, f.Number, q))
                        .Select(FromFavorite)
                        .ToList();

        var seen = new HashSet<string>(byFav.Select(r => r.Number), StringComparer.OrdinalIgnoreCase);

        var byBook = (contacts ?? Enumerable.Empty<ContactEntry>())
            .SelectMany(c => c.AllNumbers.Select(n => (c.Name, n.Number)))
            .Where(x => Matches(x.Name, x.Number, q))
            .Where(x => seen.Add(x.Number))
            .Select(x => new TransferTargetRow { Display = x.Name, Number = x.Number })
            .ToList();

        return byFav.Concat(byBook).Take(MaxRows).ToList();
    }

    private static TransferTargetRow FromFavorite(FavoriteRow f) => new()
    {
        Display = f.Display,
        Number = f.Number,
        Favorite = f,
    };

    private static bool Matches(string? name, string? number, string q)
    {
        name ??= ""; number ??= "";
        if (name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (number.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;

        // Номер набирают как попало: «+7 999», «8 (999)». Сверяем по цифрам.
        string digits = Digits(q);
        return digits.Length > 0 && Digits(number).Contains(digits);
    }

    private static string Digits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
