using CommunityToolkit.Mvvm.ComponentModel;

namespace LinphoneWinUI.ViewModels;

/// <summary>
/// Наблюдаемое состояние экрана активного звонка: имя собеседника, строка статуса
/// (таймер / «На удержании» / «Соединение…»), набранные за разговор DTMF-точки.
/// Только отображаемое состояние — режимы экрана, доступность кнопок и видимости
/// пока управляет code-behind View (мигрируют отдельными шагами).
/// </summary>
public partial class CallViewModel : ObservableObject
{
    [ObservableProperty] private string _remoteName = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _dtmfText = "";

    /// <summary>Добавить набранную во время разговора цифру к строке DTMF.</summary>
    public void AppendDtmf(char digit) => DtmfText += digit;

    /// <summary>Сбросить набранные DTMF (вход в разговор / его завершение).</summary>
    public void ResetDtmf() => DtmfText = "";
}
