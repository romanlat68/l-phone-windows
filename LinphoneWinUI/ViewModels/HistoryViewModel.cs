using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>Что показывать в журнале. Значения пишутся в rc — не менять.</summary>
public enum HistoryFilter { All = 0, Missed = 1, Incoming = 2, Outgoing = 3 }

/// <summary>
/// ViewModel вкладки «Журнал». Держит полный список записей и показывает отфильтрованный:
/// у оператора за день набегают сотни строк, и «кто мне звонил, пока меня не было» —
/// самый частый вопрос к журналу, а не пролистывание всего подряд.
/// Клики по строкам (набор/звонок/избранное) и диалог подтверждения очистки пока
/// в code-behind View (мигрируют позже).
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly ISipService _sip;

    /// <summary>Что показано сейчас — после фильтра и поиска.</summary>
    public ObservableCollection<CallLogEntry> Entries { get; } = new();

    private readonly List<CallLogEntry> _all = new();

    /// <summary>Журнал пуст сам по себе (а не «фильтр ничего не нашёл»). Разделять важно:
    /// «Журнал пуст» при заданном поиске выглядел бы как потеря данных.</summary>
    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;

    /// <summary>Записи есть, но под условия не подошла ни одна.</summary>
    [ObservableProperty] public partial bool NothingFound { get; set; }

    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial HistoryFilter Filter { get; set; } = HistoryFilter.All;

    partial void OnQueryChanged(string value) => Apply();
    partial void OnFilterChanged(HistoryFilter value) => Apply();

    public HistoryViewModel(ISipService sip) => _sip = sip;

    /// <summary>Перечитать журнал из ядра. Фильтр и строка поиска сохраняются: перечитывание
    /// происходит и после звонка, сбрасывать поиск под руками пользователя нельзя.</summary>
    public void Load()
    {
        _all.Clear();
        _all.AddRange(_sip.GetCallHistory());
        Apply();
    }

    /// <summary>Очистить журнал (подтверждение показывает View перед вызовом).</summary>
    public void Clear()
    {
        _sip.ClearCallHistory();
        Load();
    }

    private void Apply()
    {
        Entries.Clear();
        foreach (var e in Match(_all, Filter, Query)) Entries.Add(e);
        IsEmpty = _all.Count == 0;
        NothingFound = !IsEmpty && Entries.Count == 0;
    }

    /// <summary>Отбор записей. Вынесен статикой и без обращений к ядру — это единственное
    /// место, где решается, что увидит пользователь, и оно должно проверяться тестами.</summary>
    public static IEnumerable<CallLogEntry> Match(IEnumerable<CallLogEntry> log, HistoryFilter filter, string? query)
    {
        IEnumerable<CallLogEntry> result = log.Where(e => filter switch
        {
            HistoryFilter.Missed => e.Missed,
            // Пропущенный — тоже входящий: «Входящие» не должны скрывать неотвеченные,
            // иначе список входящих врёт о том, кто звонил.
            HistoryFilter.Incoming => e.Incoming,
            HistoryFilter.Outgoing => !e.Incoming,
            _ => true,
        });

        string q = (query ?? "").Trim();
        if (q.Length == 0) return result;

        string digits = Digits(q);
        // Ищут почти всегда номер, а набирают его как попало: «377», «+7 (999) 377».
        // Поэтому цифровой запрос сверяем с цифрами номера, игнорируя разделители,
        // домен и «sip:». Текстовый запрос — по имени и по номеру как есть.
        return digits.Length == q.Length && digits.Length > 0
            ? result.Where(e => Digits(UserPart(e.Remote)).Contains(digits)
                             || Digits(e.Title).Contains(digits))
            : result.Where(e => Contains(e.Title, q) || Contains(UserPart(e.Remote), q));
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Часть адреса до «@» без схемы: из «sip:377@pbx.example.com» — «377».
    /// Искать по полному адресу нельзя: запрос «ru» находил бы всех разом по домену.</summary>
    public static string UserPart(string remote)
    {
        if (string.IsNullOrEmpty(remote)) return "";
        int start = remote.IndexOf(':') + 1;              // «sip:» / «sips:»; -1+1 = 0, если схемы нет
        int at = remote.IndexOf('@', start);
        return at < 0 ? remote[start..] : remote[start..at];
    }

    private static string Digits(string s)
    {
        Span<char> buf = s.Length <= 64 ? stackalloc char[s.Length] : new char[s.Length];
        int n = 0;
        foreach (char c in s) if (char.IsDigit(c)) buf[n++] = c;
        return new string(buf[..n]);
    }

    /// <summary>Последний набранный номер (самый свежий ИСХОДЯЩИЙ). Пусто — исходящих нет.
    /// Выбор явно по времени начала, а не «первый в списке»: порядок, в котором liblinphone
    /// отдаёт журнал, нигде не зафиксирован. Android-клиент выбирает так же
    /// (LinphoneManager.lastDialedNumber: maxByOrNull { it.startDate }).</summary>
    public static string LastDialed(IEnumerable<CallLogEntry> log)
    {
        CallLogEntry? best = null;
        foreach (var e in log)
            if (!e.Incoming && e.Remote.Length > 0 && (best is null || e.StartUnix > best.StartUnix))
                best = e;
        return best?.Remote ?? "";
    }
}
