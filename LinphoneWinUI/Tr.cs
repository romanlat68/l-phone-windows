using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LinphoneWinUI;

/// <summary>
/// Локализатор интерфейса для UNPACKAGED WinUI (где x:Uid нельзя переключить на лету).
/// На элементе ставится Tr.Key="ИмяКлюча.Свойство" (напр. "BtnSave.Content"),
/// а Apply() обходит визуальное дерево и проставляет локализованный текст через Loc.
/// </summary>
public static class Tr
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Tr), new PropertyMetadata(null));

    public static void SetKey(DependencyObject o, string v) => o.SetValue(KeyProperty, v);
    public static string GetKey(DependencyObject o) => (string)o.GetValue(KeyProperty);

    /// <summary>Проставить локализованные тексты по всему поддереву.</summary>
    public static void Apply(DependencyObject? root)
    {
        if (root == null) return;
        if (root is FrameworkElement fe)
        {
            string k = GetKey(fe);
            if (!string.IsNullOrEmpty(k)) Set(fe, k);
        }
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++) Apply(VisualTreeHelper.GetChild(root, i));
    }

    /// <summary>Локализовать один элемент по ключу "Имя.Свойство" (для пунктов меню вне дерева).</summary>
    public static void Set(DependencyObject el, string fullKey)
    {
        string text = Loc.S(fullKey);
        int dot = fullKey.LastIndexOf('.');
        string prop = dot >= 0 ? fullKey[(dot + 1)..] : "Text";
        PropertyInfo? pi = el.GetType().GetProperty(prop);
        if (pi == null || !pi.CanWrite) return;
        if (pi.PropertyType == typeof(string) || pi.PropertyType == typeof(object))
            pi.SetValue(el, text);
    }
}
