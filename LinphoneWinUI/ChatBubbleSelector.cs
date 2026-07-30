using LinphoneWinUI.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinphoneWinUI;

/// <summary>Выбирает шаблон пузыря чата: исходящие справа, входящие слева.</summary>
public sealed class ChatBubbleSelector : DataTemplateSelector
{
    public DataTemplate? Mine { get; set; }
    public DataTemplate? Their { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => (item as ChatLine)?.Mine == true ? Mine : Their;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
