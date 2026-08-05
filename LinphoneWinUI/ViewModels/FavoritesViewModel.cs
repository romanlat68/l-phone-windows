using System.Collections.Generic;
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
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial DialogState Dialog { get; set; } = DialogState.Unknown;

    /// <summary>Что произойдёт по клику — подсказка на плитке. Поведение меняется вместе с
    /// состоянием линии, и без подписи его пришлось бы угадывать.</summary>
    public string ActionHint => IsRinging && CanPickup ? Loc.S("RowPickup.Text") : Loc.S("St_CallAction");

    /// <summary>Цвет лампы. Звонок перебивает presence: пока телефон звонит, оператору важно
    /// именно это — в остальное время показываем обычный статус коллеги. Кисть общая
    /// (см. PresenceUi.Brush): на пульте из сорока плиток создавать её заново на каждое
    /// чтение значило сорок объектов на перерисовку.</summary>
    public Microsoft.UI.Xaml.Media.Brush LampBrush =>
        PresenceUi.Brush(IsRinging ? PresenceUi.RingingColor : PresenceUi.Color(Status));

    /// <summary>Телефон коллеги звонит прямо сейчас — лампа выделяется, перехват доступен.</summary>
    public bool IsRinging => Dialog == DialogState.Early;

    public FavoriteRow(FavoriteEntry e, PresenceStatus status)
    {
        Number = e.Number;
        Display = e.Display;
        Initials = e.Initials;
        SubText = e.SubText;
        Status = status;
        // Перехват (**<ext>) имеет смысл только для внутренних номеров АТС.
        _internal = PhoneNumberUtils.IsLikelyInternal(e.Number);
    }

    private readonly bool _internal;
    public string Number { get; }
    public string Display { get; }
    public string Initials { get; }
    public string SubText { get; }

    /// <summary>Статус коллеги. НАБЛЮДАЕМЫЙ, и это принципиально: presence меняется постоянно
    /// (дважды за каждый звонок каждого коллеги), а раньше ради этого пересоздавался весь
    /// список — GridView выбрасывал и строил заново все плитки. Пользователь при этом терял
    /// прокрутку, подсветку под курсором и начатое перетаскивание звонка на плитку, то есть
    /// ломалась ровно та функция, ради которой пульт и сделан.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusEmojiText))]
    [NotifyPropertyChangedFor(nameof(LampBrush))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial PresenceStatus Status { get; set; }

    public string StatusEmojiText => PresenceUi.Emoji(Status);

    /// <summary>Что читает Narrator на плитке пульта: «Иванов, 375, разговаривает».
    /// Без него скринридер произносил «элемент» — то есть весь пульт был для него пустым
    /// набором одинаковых объектов, а лампа (единственное, что отличает плитки) не звучит
    /// вовсе, потому что это цвет.</summary>
    public string AccessibleName => $"{Display}, {Number}, {PresenceUi.Label(Status)}"
        + (IsRinging ? ", " + Loc.S("St_LineRinging") : "");
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

    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;
    /// <summary>Вид «Избранного»: список или BLF-пульт (сетка плиток). Сохраняется в rc.</summary>
    [ObservableProperty] public partial bool GridView { get; set; }

    public FavoritesViewModel(ISipService sip) => _sip = sip;

    /// <summary>Номера, за состоянием линий которых надо следить (внутренние из избранного).</summary>
    public IReadOnlyList<string> DialogWatched { get; private set; } = System.Array.Empty<string>();

    /// <summary>Прочитать сохранённый вид из настроек. Вызывать ПОСЛЕ старта ядра: VM создаётся
    /// в конструкторе окна раньше, чем ядро запущено, и в ctor флаг ещё недоступен.
    ///
    /// Флаг вместо записи в поле мимо свойства: прежний SetProperty(ref _gridView, …) обходил
    /// OnGridViewChanged, то есть прочитанное значение не улетало обратно в конфиг. Работало
    /// это по совпадению — генератор мог назвать поле иначе, и чтение настройки начало бы её
    /// же перезаписывать.</summary>
    public void InitView()
    {
        _applyingSavedView = true;
        try { GridView = _sip.GetAppFlag("favorites_grid", false); }
        finally { _applyingSavedView = false; }
    }

    private bool _applyingSavedView;

    partial void OnGridViewChanged(bool value)
    {
        if (_applyingSavedView) return;   // это чтение настройки, а не выбор пользователя
        _sip.SetAppFlag("favorites_grid", value);
    }

    public void Load()
    {
        var favs = LinphoneWinUI.Favorites.Load();
        // Подписка на presence — только внутренние номера АТС: SUBSCRIBE на внешние
        // (+7…, городские) бессмыслен и сыплет в лог Asterisk «has no associated hint».
        var watched = favs.Select(f => f.Number).Where(PhoneNumberUtils.IsLikelyInternal).ToList();
        _sip.SyncPresenceFriends(watched);
        // Подписку «Event: dialog» здесь НЕ трогаем: она задаёт полный набор номеров, а
        // слоты парковки следят за собой отдельно — два независимых вызова затирали бы
        // друг друга, и пульт с парковкой не работали бы одновременно. Набор собирает
        // владелец обеих моделей (MainWindow.SyncBlfSubscriptions).
        DialogWatched = watched;
        // Статусы — ОДНИМ обращением к ядру: список перечитывается на каждый presence-NOTIFY,
        // и N отдельных вызовов подтормаживали интерфейс.
        var statuses = _sip.GetStatuses(favs.Select(f => f.Number));

        // Состав избранного меняется РЕДКО (по кнопке «звезда»), а статусы — постоянно.
        // Поэтому пересобираем список только когда изменился состав, иначе обновляем строки
        // на месте: Clear() поднимает Reset, и GridView выбрасывает и строит заново ВСЕ плитки.
        // Заодно исчезает костыль с переносом Dialog в новые строки — строки просто живут.
        if (SameOrder(favs))
        {
            foreach (var row in Favorites)
                row.Status = statuses.TryGetValue(row.Number, out var s) ? s : PresenceStatus.Offline;
        }
        else
        {
            Favorites.Clear();
            foreach (var f in favs)
            {
                var row = new FavoriteRow(f, statuses.TryGetValue(f.Number, out var s) ? s : PresenceStatus.Offline);
                // Состояние линии обязано пережить пересборку: подписка dialog живёт своей
                // жизнью, и новая строка рождалась бы с Unknown — ореол «звонит» гас бы,
                // оставляя красную лампу занятости.
                if (_dialog.TryGetValue(PhoneNumberUtils.SipUser(f.Number), out var d)) row.Dialog = d;
                Favorites.Add(row);
            }
        }
        IsEmpty = Favorites.Count == 0;
    }

    /// <summary>Тот же состав в том же порядке. Сравниваем ПО ПОРЯДКУ, а не как множества:
    /// строки обновляются позиционно, и перестановка без пересборки развела бы список
    /// с избранным (у человека оказалась бы чужая лампа).</summary>
    private bool SameOrder(IReadOnlyList<FavoriteEntry> favs)
    {
        if (Favorites.Count != favs.Count) return false;
        for (int i = 0; i < favs.Count; i++)
            if (!string.Equals(Favorites[i].Number, favs[i].Number, System.StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Favorites[i].Display, favs[i].Display, System.StringComparison.Ordinal))
                return false;
        return true;
    }
}
