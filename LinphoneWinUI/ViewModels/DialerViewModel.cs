using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>
/// ViewModel набора номера (поле ввода дайлера). Держит текст номера, имя совпавшего
/// контакта и признак «есть цифры»; звонок — через ISipService. Состояние активного
/// звонка (экран разговора, кнопки, DTMF, автодозвон) — отдельная секция, мигрирует позже.
/// </summary>
public partial class DialerViewModel : ObservableObject
{
    private readonly ISipService _sip;
    private readonly Func<string, string?> _matchName;   // имя контакта по номеру (FindContact)

    [ObservableProperty] private string _number = "";
    [ObservableProperty] private string _matchedName = "";
    [ObservableProperty] private bool _hasNumber;
    /// <summary>Последний набранный (полный адрес). Ведёт View: ставит при старте из журнала
    /// и на каждом исходящем — поэтому кнопке повтора не нужен поход в ядро на клик.</summary>
    [ObservableProperty] private string _lastDialed = "";

    /// <summary>Есть что повторять. Нечего — View прячет кнопку (как в Android-клиенте),
    /// иначе клик по ней был бы молчаливым «ничего не произошло».</summary>
    public bool HasLastDialed => LastDialed.Length > 0;

    public DialerViewModel(ISipService sip, Func<string, string?> matchName)
    {
        _sip = sip;
        _matchName = matchName;
    }

    partial void OnNumberChanged(string value)
    {
        HasNumber = value.Length > 0;
        MatchedName = _matchName(value) ?? "";
    }

    partial void OnLastDialedChanged(string value) => OnPropertyChanged(nameof(HasLastDialed));

    public void AppendDigit(string digit) => Number += digit;
    public void Backspace() { if (Number.Length > 0) Number = Number[..^1]; }

    /// <summary>Позвонить на набранный номер. Пусто — ничего не делает (пустоту проверяет View).</summary>
    public void Call()
    {
        string t = Number.Trim();
        if (t.Length > 0) _sip.Call(t);
    }
}
