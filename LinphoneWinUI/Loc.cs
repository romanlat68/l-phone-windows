using Microsoft.Windows.ApplicationModel.Resources;

namespace LinphoneWinUI;

/// <summary>Локализованные строки. В unpackaged WinUI системные API смены языка не работают,
/// поэтому резолвим через ResourceManager с ЯВНЫМ языковым контекстом из настройки.</summary>
internal static class Loc
{
    private static readonly ResourceManager _rm = new();
    private static readonly ResourceMap _map = _rm.MainResourceMap;
    private static readonly ResourceContext _ctx = BuildContext();

    /// <summary>Текущий выбранный язык ("" = системный).</summary>
    public static string Language { get; } = AppSettings.GetLanguage();

    private static ResourceContext BuildContext()
    {
        var c = _rm.CreateResourceContext();
        string lang = AppSettings.GetLanguage();
        if (!string.IsNullOrEmpty(lang))
            c.QualifierValues["Language"] = lang;
        return c;
    }

    public static string S(string key)
    {
        try
        {
            // Точечные ключи x:Uid ("Menu.Text") в PRI хранятся как путь "Resources/Menu/Text".
            var cand = _map.TryGetValue("Resources/" + key.Replace('.', '/'), _ctx);
            return cand != null ? cand.ValueAsString : key;
        }
        catch { return key; }
    }

    public static string F(string key, params object[] args)
    {
        try { return string.Format(S(key), args); }
        catch { return key; }
    }
}
