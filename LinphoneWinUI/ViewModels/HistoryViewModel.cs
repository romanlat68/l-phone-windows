using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>
/// ViewModel вкладки «Журнал». Держит список записей и empty-состояние; загрузка/очистка —
/// через ISipService. Клики по строкам (набор/звонок/избранное) и диалог подтверждения
/// очистки пока в code-behind View (мигрируют позже).
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly ISipService _sip;

    public ObservableCollection<CallLogEntry> Entries { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;

    public HistoryViewModel(ISipService sip) => _sip = sip;

    /// <summary>Перечитать журнал из ядра.</summary>
    public void Load()
    {
        Entries.Clear();
        foreach (var e in _sip.GetCallHistory()) Entries.Add(e);
        IsEmpty = Entries.Count == 0;
    }

    /// <summary>Очистить журнал (подтверждение показывает View перед вызовом).</summary>
    public void Clear()
    {
        _sip.ClearCallHistory();
        Load();
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
