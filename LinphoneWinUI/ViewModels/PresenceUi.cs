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

    /// <summary>Цвет лампы «телефон коллеги звонит» — перебивает presence.</summary>
    public static Windows.UI.Color RingingColor => Microsoft.UI.Colors.Orange;

    // Кисти КЭШИРУЕМ. Их читают привязки: на пульте из сорока плиток каждая перерисовка
    // создавала сорок объектов, а перерисовки идут пачками — presence коллеги меняется
    // дважды за каждый его звонок. Цветов всего пять, и они неизменны.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint,
        Microsoft.UI.Xaml.Media.SolidColorBrush> BrushCache = new();

    /// <summary>Кисть для цвета (общая, не создаётся заново). Кисти WinUI неизменяемы по
    /// использованию у нас — мы их только читаем.</summary>
    public static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(Windows.UI.Color c)
    {
        uint key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        return BrushCache.GetOrAdd(key, _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(c));
    }

    /// <summary>Кисть индикатора статуса.</summary>
    public static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(PresenceStatus s) => Brush(Color(s));
}

/// <summary>PresenceStatus → кисть индикатора (для крупного кружка плитки BLF).</summary>
public sealed class PresenceBrushConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, string language) =>
        PresenceUi.Brush(value is PresenceStatus s ? s : PresenceStatus.Offline);

    public object ConvertBack(object value, System.Type targetType, object parameter, string language) =>
        throw new System.NotSupportedException();
}
