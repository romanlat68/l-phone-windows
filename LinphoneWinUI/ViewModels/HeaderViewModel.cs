using CommunityToolkit.Mvvm.ComponentModel;

namespace LinphoneWinUI.ViewModels;

/// <summary>
/// Состояние шапки для биндинга: имя/инициалы активного аккаунта, единая статус-строка
/// (presence при регистрации, иначе состояние регистрации) и признаки активных режимов
/// (DND / автоответ / микрофон выключен). Заполняется code-behind View; переключатель
/// аккаунтов и меню статуса строятся императивно (динамические Flyout), а цвет точки
/// статуса выставляется в code-behind — их сюда не тянем.
/// </summary>
public partial class HeaderViewModel : ObservableObject
{
    [ObservableProperty] public partial string AccountName { get; set; } = "";
    [ObservableProperty] public partial string AvatarInitials { get; set; } = "—";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool DndActive { get; set; }
    [ObservableProperty] public partial bool AutoAnswerActive { get; set; }
    [ObservableProperty] public partial bool Muted { get; set; }
    [ObservableProperty] public partial bool VoicemailWaiting { get; set; }      // MWI: конверт в шапке
    [ObservableProperty] public partial string VoicemailText { get; set; } = "";  // число новых («3»; пусто, если АТС не сообщила)
}
