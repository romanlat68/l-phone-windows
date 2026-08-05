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

    [ObservableProperty] public partial string SearchQuery { get; set; } = "";

    /// <summary>Группы из книги плюс пункт «Все группы» первым. Пустой список означает,
    /// что группы в книге не заполнены — тогда и выбирать нечего.</summary>
    public ObservableCollection<string> Groups { get; } = new();

    /// <summary>Выбранная группа; пусто или «все» — фильтра нет.</summary>
    [ObservableProperty] public partial string SelectedGroup { get; set; } = "";

    /// <summary>Показывать ли выбор группы. Один пункт «Все группы» — это не выбор,
    /// а лишний элемент в узком окне.</summary>
    [ObservableProperty] public partial bool HasGroups { get; set; }

    partial void OnSelectedGroupChanged(string value) => ApplyFilter();
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; set; }

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
        RefreshGroups();
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
            RefreshGroups();
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
        RefreshGroups();
        ApplyFilter();
    }

    private static List<ContactEntry> Merge(IEnumerable<ContactEntry> phonebook) =>
        LocalContacts.Load().Concat(phonebook)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    private void ApplyFilter()
    {
        string q = (SearchQuery ?? "").Trim();
        IEnumerable<ContactEntry> items = Filter(_all, q, SelectedGroup, AllGroupsLabel);

        Contacts.Clear();
        foreach (var c in items) Contacts.Add(c);
        IsEmpty = Contacts.Count == 0;
    }

    /// <summary>Подпись пункта «все группы». Держим в поле, чтобы сравнивать с выбранным
    /// значением, не полагаясь на язык интерфейса.</summary>
    public string AllGroupsLabel => _loc("Grp_All");

    /// <summary>Отбор контактов: поиск по имени и всем номерам плюс фильтр по группе.
    /// Статикой и без обращений к ядру — это решает, что человек увидит в списке,
    /// и должно проверяться тестами.</summary>
    public static IEnumerable<ContactEntry> Filter(
        IEnumerable<ContactEntry> all, string? query, string? group, string allGroupsLabel)
    {
        IEnumerable<ContactEntry> items = all;

        if (!string.IsNullOrWhiteSpace(group) && group != allGroupsLabel)
            items = items.Where(c => string.Equals(c.Group, group, StringComparison.CurrentCultureIgnoreCase));

        string q = (query ?? "").Trim();
        if (q.Length == 0) return items;

        // Ищем по ВСЕМ номерам, а не только по основному и мобильному: контакт с тремя
        // номерами не должен теряться из-за того, что искали по третьему.
        return items.Where(c =>
            c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            c.AllNumbers.Any(n => n.Number.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Перечитать список групп из загруженной книги.</summary>
    private void RefreshGroups()
    {
        var names = _all.Select(c => c.Group)
                        .Where(g => !string.IsNullOrWhiteSpace(g))
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

        string keep = SelectedGroup;
        Groups.Clear();
        Groups.Add(AllGroupsLabel);
        foreach (var g in names) Groups.Add(g);
        HasGroups = names.Count > 0;

        // Выбранная группа могла исчезнуть из книги — тогда возвращаемся ко «всем»,
        // иначе список молча опустеет и человек решит, что контакты пропали.
        SelectedGroup = Groups.Contains(keep) ? keep : AllGroupsLabel;
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
