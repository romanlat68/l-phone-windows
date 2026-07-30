using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>Представление presence-статуса для UI: эмодзи и локализованная подпись.
/// Общее для шапки (свой статус) и списка избранного (статус коллеги).</summary>
internal static class PresenceUi
{
    public static string Emoji(PresenceStatus s) => s switch
    {
        PresenceStatus.Available => "\U0001F7E2",     // 🟢
        PresenceStatus.OnThePhone => "\U0001F534",    // 🔴 занят — точка в тон 🟢 (просьба владельца)
        PresenceStatus.DoNotDisturb => "\U0001F6AB",  // 🚫
        PresenceStatus.Away => "☕",                   // ☕
        _ => "⚪",                                     // ⚪ Offline
    };

    public static string Label(PresenceStatus s) => Loc.S("PS_" + s);

    /// <summary>Цвет индикатора статуса: зелёный/красный/жёлтый/серый. Единый источник
    /// для точки в шапке (StatusColor) и плиток BLF-пульта.</summary>
    public static Windows.UI.Color Color(PresenceStatus s) => s switch
    {
        PresenceStatus.Available => Microsoft.UI.Colors.LimeGreen,
        PresenceStatus.DoNotDisturb or PresenceStatus.OnThePhone => Microsoft.UI.Colors.IndianRed,
        PresenceStatus.Offline => Microsoft.UI.Colors.Gray,
        _ => Microsoft.UI.Colors.Goldenrod,   // Away — «отошёл»
    };
}

/// <summary>PresenceStatus → кисть индикатора (для крупного кружка плитки BLF).</summary>
public sealed class PresenceBrushConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, string language) =>
        new Microsoft.UI.Xaml.Media.SolidColorBrush(
            PresenceUi.Color(value is PresenceStatus s ? s : PresenceStatus.Offline));

    public object ConvertBack(object value, System.Type targetType, object parameter, string language) =>
        throw new System.NotSupportedException();
}
