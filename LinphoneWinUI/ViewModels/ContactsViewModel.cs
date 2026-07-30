using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>
/// ViewModel вкладки «Контакты» — пилот перехода на MVVM. Держит состояние (список,
/// поиск, статус) и команды (звонок), не касаясь контролов. View (ContactsSection в
/// MainWindow.xaml) биндится к этим свойствам через {x:Bind}. Зависит от ISipService —
/// значит тестируется с fake-сервисом.
/// </summary>
public partial class ContactsViewModel : ObservableObject
{
    private readonly ISipService _sip;
    private readonly Func<string, string> _loc;         // Loc.S
    private readonly Func<string, object[], string> _locF; // Loc.F
    private List<ContactEntry> _all = new();

    /// <summary>Просит View переключиться на вкладку набора (звонок пошёл).</summary>
    public event Action? CallPlaced;

    public ObservableCollection<ContactEntry> Contacts { get; } = new();

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isEmpty;

    public ContactsViewModel(ISipService sip, Func<string, string> loc, Func<string, object[], string> locF)
    {
        _sip = sip;
        _loc = loc;
        _locF = locF;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private bool _busy;   // защита от повторного входа, см. LoadAsync

    /// <summary>Мгновенно из кэша, затем фоновое обновление внешней книги по URL.
    /// Повторный вход запрещён: каждый клик по вкладке запускал новую загрузку, и два быстрых
    /// клика давали две гонки за _all и StatusText — список мог собраться из ответов вперемешку.</summary>
    public async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        try { await LoadCoreAsync(); }
        finally { _busy = false; }
    }

    private async Task LoadCoreAsync()
    {
        _all = Merge(Phonebook.LoadCached());
        ApplyFilter();

        string url = _sip.GetPhonebookUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText = _loc("St_ContactsNoUrl");
            return;
        }

        StatusText = _all.Count > 0 ? _locF("St_ContactsUpdating", new object[] { _all.Count }) : _loc("St_Loading");
        try
        {
            var (user, pass) = _sip.GetPhonebookAuth();   // удалённые книги — опционально под HTTP-авторизацией
            var book = await Phonebook.FetchAsync(url, user, pass);
            _all = Merge(book);
            ApplyFilter();
            StatusText = _locF("St_ContactsCount", new object[] { _all.Count });
        }
        catch (Exception ex)
        {
            StatusText = _all.Count > 0
                ? _locF("St_ContactsCache", new object[] { _all.Count })
                : _locF("St_LoadFailed", new object[] { ex.Message });
        }
    }

    /// <summary>Пересобрать из локальных контактов + кэша книги (после правки/добавления).</summary>
    public void Refresh()
    {
        _all = Merge(Phonebook.LoadCached());
        ApplyFilter();
    }

    private static List<ContactEntry> Merge(IEnumerable<ContactEntry> phonebook) =>
        LocalContacts.Load().Concat(phonebook)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    private void ApplyFilter()
    {
        string q = (SearchQuery ?? "").Trim();
        IEnumerable<ContactEntry> items = _all;
        if (q.Length > 0)
            items = _all.Where(c =>
                c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                c.Number.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Mobile.Contains(q, StringComparison.OrdinalIgnoreCase));

        Contacts.Clear();
        foreach (var c in items) Contacts.Add(c);
        IsEmpty = Contacts.Count == 0;
    }

    /// <summary>Кнопка-трубка / контекстное «Позвонить»: звонок и просьба переключиться на набор.</summary>
    [RelayCommand]
    private void Call(ContactEntry? c)
    {
        if (c is null || c.CallNumber.Length == 0) return;
        _sip.Call(c.CallNumber);
        CallPlaced?.Invoke();
    }
}
