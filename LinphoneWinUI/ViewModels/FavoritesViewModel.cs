using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>Строка избранного/BLF: данные контакта, presence-статус коллеги и состояние его
/// линии. Presence отвечает «свободен ли», dialog — «звонит ли прямо сейчас»: перехват
/// осмыслен только во втором случае, иначе оператор получает отбойник.</summary>
public sealed partial class FavoriteRow : ObservableObject
{
    /// <summary>Состояние линии из подписки «Event: dialog». Меняется на ходу, поэтому
    /// строка наблюдаемая: лампа и доступность перехвата обязаны реагировать без пересборки списка.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPickup))]
    [NotifyPropertyChangedFor(nameof(IsRinging))]
    [NotifyPropertyChangedFor(nameof(LampBrush))]
    [NotifyPropertyChangedFor(nameof(ActionHint))]
    private DialogState _dialog = DialogState.Unknown;

    /// <summary>Что произойдёт по клику — подсказка на плитке. Поведение меняется вместе с
    /// состоянием линии, и без подписи его пришлось бы угадывать.</summary>
    public string ActionHint => IsRinging && CanPickup ? Loc.S("RowPickup.Text") : Loc.S("St_CallAction");

    /// <summary>Цвет лампы. Звонок перебивает presence: пока телефон звонит, оператору важно
    /// именно это — в остальное время показываем обычный статус коллеги.</summary>
    public Microsoft.UI.Xaml.Media.Brush LampBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(
        IsRinging ? Microsoft.UI.Colors.Orange : PresenceUi.Color(Status));

    /// <summary>Телефон коллеги звонит прямо сейчас — лампа выделяется, перехват доступен.</summary>
    public bool IsRinging => Dialog == DialogState.Early;

    public FavoriteRow(FavoriteEntry e, PresenceStatus status)
    {
        Number = e.Number;
        Display = e.Display;
        Initials = e.Initials;
        SubText = e.SubText;
        Status = status;
        StatusEmojiText = PresenceUi.Emoji(status);
        // Перехват (**<ext>) имеет смысл только для внутренних номеров АТС.
        _internal = PhoneNumberUtils.IsLikelyInternal(e.Number);
    }

    private readonly bool _internal;
    public string Number { get; }
    public string Display { get; }
    public string Initials { get; }
    public string SubText { get; }
    public PresenceStatus Status { get; }
    public string StatusEmojiText { get; }
    /// <summary>Перехват доступен, пока телефон коллеги ЗВОНИТ. Раньше кнопка была доступна
    /// всегда и набирала **ext вслепую — если у коллеги никто не звонил, оператор получал
    /// отбойник. Пока состояние линии неизвестно (АТС не отдаёт dialog, подписка не
    /// поднялась), ведём себя как прежде: лучше лишняя кнопка, чем неработающий пульт.</summary>
    public bool CanPickup => _internal && Dialog is DialogState.Early or DialogState.Unknown;
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

    // Состояние линий держим отдельно от строк: список пересобирается на каждый
    // presence-NOTIFY, а подписка dialog живёт своей жизнью и не должна сбрасываться.
    private readonly System.Collections.Generic.Dictionary<string, DialogState> _dialog =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Линия коллеги сменила состояние (из подписки «Event: dialog»).</summary>
    public void SetDialogState(string extension, DialogState state)
    {
        string ext = PhoneNumberUtils.SipUser(extension);
        if (ext.Length == 0) return;
        _dialog[ext] = state;
        foreach (var row in Favorites)
            if (string.Equals(PhoneNumberUtils.SipUser(row.Number), ext, System.StringComparison.OrdinalIgnoreCase))
                row.Dialog = state;
    }

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
        var watched = favs.Select(f => f.Number).Where(PhoneNumberUtils.IsLikelyInternal).ToList();
        _sip.SyncPresenceFriends(watched);
        // Та же выборка, но вторая подписка: presence не отличает звонящий телефон от
        // разговаривающего, а перехват имеет смысл только пока телефон звонит.
        _sip.SyncDialogSubscriptions(watched);
        // Статусы — ОДНИМ обращением к ядру: список перечитывается на каждый presence-NOTIFY,
        // и N отдельных вызовов подтормаживали интерфейс.
        var statuses = _sip.GetStatuses(favs.Select(f => f.Number));
        Favorites.Clear();
        foreach (var f in favs)
        {
            var row = new FavoriteRow(f, statuses.TryGetValue(f.Number, out var s) ? s : PresenceStatus.Offline);
            // Состояние линии обязано пережить пересборку списка. Список перечитывается на
            // КАЖДЫЙ presence-NOTIFY, а входящий коллеге как раз меняет его presence на
            // «разговаривает» — новые строки рождались с Unknown, и ореол «звонит» гас через
            // долю секунды, оставляя красную лампу занятости. Подписка dialog живёт своей
            // жизнью и от пересборки списка зависеть не должна.
            if (_dialog.TryGetValue(PhoneNumberUtils.SipUser(f.Number), out var d)) row.Dialog = d;
            Favorites.Add(row);
        }
        IsEmpty = Favorites.Count == 0;
    }
}
