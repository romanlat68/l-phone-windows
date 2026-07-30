using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>Строка избранного/BLF: данные контакта + presence-статус коллеги.</summary>
public sealed class FavoriteRow
{
    public FavoriteRow(FavoriteEntry e, PresenceStatus status)
    {
        Number = e.Number;
        Display = e.Display;
        Initials = e.Initials;
        SubText = e.SubText;
        Status = status;
        StatusEmojiText = PresenceUi.Emoji(status);
        // Перехват (**<ext>) имеет смысл только для внутренних номеров АТС.
        CanPickup = PhoneNumberUtils.IsLikelyInternal(e.Number);
    }
    public string Number { get; }
    public string Display { get; }
    public string Initials { get; }
    public string SubText { get; }
    public PresenceStatus Status { get; }
    public string StatusEmojiText { get; }
    public bool CanPickup { get; }
}

/// <summary>
/// ViewModel вкладки «Избранное». Держит строки с presence-статусом и empty-состояние;
/// при загрузке подписывается на присутствие избранных через ISipService. Дебаунс NOTIFY
/// и переключение вкладки — в code-behind View (UI-таймер), он дёргает Load().
/// </summary>
public partial class FavoritesViewModel : ObservableObject
{
    private readonly ISipService _sip;

    public ObservableCollection<FavoriteRow> Favorites { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;
    /// <summary>Вид «Избранного»: список или BLF-пульт (сетка плиток). Сохраняется в rc.</summary>
    [ObservableProperty] private bool _gridView;

    public FavoritesViewModel(ISipService sip) => _sip = sip;

    /// <summary>Прочитать сохранённый вид из настроек. Вызывать ПОСЛЕ старта ядра: VM создаётся
    /// в конструкторе окна раньше, чем ядро запущено, и в ctor флаг ещё недоступен.</summary>
    public void InitView() => SetProperty(ref _gridView, _sip.GetAppFlag("favorites_grid", false), nameof(GridView));

    partial void OnGridViewChanged(bool value) => _sip.SetAppFlag("favorites_grid", value);

    public void Load()
    {
        var favs = LinphoneWinUI.Favorites.Load();
        // Подписка на presence — только внутренние номера АТС: SUBSCRIBE на внешние
        // (+7…, городские) бессмыслен и сыплет в лог Asterisk «has no associated hint».
        _sip.SyncPresenceFriends(favs.Select(f => f.Number).Where(PhoneNumberUtils.IsLikelyInternal));
        // Статусы — ОДНИМ обращением к ядру: список перечитывается на каждый presence-NOTIFY,
        // и N отдельных вызовов подтормаживали интерфейс.
        var statuses = _sip.GetStatuses(favs.Select(f => f.Number));
        Favorites.Clear();
        foreach (var f in favs)
            Favorites.Add(new FavoriteRow(f, statuses.TryGetValue(f.Number, out var s) ? s : PresenceStatus.Offline));
        IsEmpty = Favorites.Count == 0;
    }
}
