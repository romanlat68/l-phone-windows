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
    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _avatarInitials = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _dndActive;
    [ObservableProperty] private bool _autoAnswerActive;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private bool _voicemailWaiting;      // MWI: конверт в шапке
    [ObservableProperty] private string _voicemailText = "";  // число новых («3»; пусто, если АТС не сообщила)
}
