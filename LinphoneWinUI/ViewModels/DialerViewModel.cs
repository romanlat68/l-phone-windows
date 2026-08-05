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

    [ObservableProperty] public partial string Number { get; set; } = "";
    [ObservableProperty] public partial string MatchedName { get; set; } = "";
    [ObservableProperty] public partial bool HasNumber { get; set; }
    /// <summary>Последний набранный (полный адрес). Ведёт View: ставит при старте из журнала
    /// и на каждом исходящем — поэтому кнопке повтора не нужен поход в ядро на клик.</summary>
    [ObservableProperty] public partial string LastDialed { get; set; } = "";

    /// <summary>Есть что повторять. Нечего — View прячет строку, иначе клик по ней был бы
    /// молчаливым «ничего не произошло».</summary>
    public bool HasLastDialed => LastDialed.Length > 0;

    /// <summary>Последний набранный — как показать человеку. Имя из адресной книги, иначе
    /// чистый номер: полный SIP-адрес («"Roman" &lt;sip:378@sip.example.com&gt;») в строке над
    /// кнопкой вызова не помещается и ничего не сообщает.</summary>
    public string LastDialedDisplay
    {
        get
        {
            if (LastDialed.Length == 0) return "";
            if (_matchName(LastDialed) is { Length: > 0 } name) return name;
            string user = PhoneNumberUtils.SipUser(LastDialed);
            return user.Length > 0 ? user : PhoneNumberUtils.Pretty(LastDialed);
        }
    }

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

    partial void OnLastDialedChanged(string value)
    {
        OnPropertyChanged(nameof(HasLastDialed));
        OnPropertyChanged(nameof(LastDialedDisplay));
    }

    public void AppendDigit(string digit) => Number += digit;
    public void Backspace() { if (Number.Length > 0) Number = Number[..^1]; }

    /// <summary>Позвонить на набранный номер. Пусто — ничего не делает (пустоту проверяет View).</summary>
    public void Call()
    {
        string t = Number.Trim();
        if (t.Length > 0) _sip.Call(t);
    }
}
