using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LinphoneWinUI;

/// <summary>
/// Локализатор интерфейса для UNPACKAGED WinUI (где x:Uid нельзя переключить на лету).
/// На элементе ставится Tr.Key="ИмяКлюча.Свойство" (напр. "BtnSave.Content").
///
/// ГЛАВНОЕ: текст проставляется КОЛБЭКОМ в момент установки самого Tr.Key — то есть при
/// разборе XAML, для каждого элемента, где бы он ни находился. Обход визуального дерева
/// для этого не годится принципиально, и это выяснилось дорогой ценой:
///
/// — содержимое NavigationView вне дерева, пока он не развернул шаблон (позже Loaded окна);
/// — содержимое ScrollViewer вне дерева, пока не реализован его ContentPresenter;
/// — свёрнутый элемент (Visibility="Collapsed") не проходит measure/arrange, и его дети
///   не созданы;
/// — содержимое MenuFlyout из ресурсов и элементы внутри DataTemplate в дерево не попадают
///   вовсе либо появляются много позже.
///
/// В сумме это давало интерфейс «наполовину переведённый»: пункты навигации английские,
/// форма под ними русская. Колбэк снимает весь класс проблем разом и не зависит ни от
/// порядка загрузки, ни от видимости.
///
/// <see cref="Apply"/> оставлен для элементов, которым ключ назначают из кода.
/// </summary>
public static class Tr
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Tr),
            new PropertyMetadata(null, OnKeyChanged));

    public static void SetKey(DependencyObject o, string v) => o.SetValue(KeyProperty, v);
    public static string GetKey(DependencyObject o) => (string)o.GetValue(KeyProperty);

    /// <summary>Ключ назначен — ставим текст. Работает при разборе XAML, при создании
    /// элемента из DataTemplate и при создании ресурса.
    ///
    /// ОТКЛАДЫВАЕМ на очередь диспетчера, и это обязательно. XAML присваивает атрибуты
    /// по порядку, а Tr.Key в разметке стоит ПЕРЕД самим текстом:
    /// <c>&lt;TextBox Tr.Key="BoxLogin.Header" Header="Логин" /&gt;</c>. Установка «на месте»
    /// тут же затиралась бы литералом из следующего атрибута — ровно поэтому форма настроек
    /// оставалась русской, хотя ключ был проставлен и перевод существовал.
    /// Очередь отрабатывает после разбора элемента целиком, и последнее слово остаётся за нами.</summary>
    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string key || key.Length == 0) return;
        Defer(() => Set(d, key));
    }

    /// <summary>Локализовать подписи «Вкл./Выкл.» у ToggleSwitch.
    ///
    /// Отдельное свойство, потому что подписей ДВЕ, а Tr.Key задаёт одну. Без этого они
    /// брались из языка WINDOWS, а не приложения: английский интерфейс на русской системе
    /// показывал «Вкл.» рядом с «Echo canceller». Ставится один раз неявным стилем на все
    /// переключатели; где подписи не нужны (узкая колонка кодеков) — локально False.</summary>
    public static readonly DependencyProperty OnOffProperty =
        DependencyProperty.RegisterAttached("OnOff", typeof(bool), typeof(Tr),
            new PropertyMetadata(false, OnOnOffChanged));

    public static void SetOnOff(DependencyObject o, bool v) => o.SetValue(OnOffProperty, v);
    public static bool GetOnOff(DependencyObject o) => (bool)o.GetValue(OnOffProperty);

    private static void OnOnOffChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleSwitch ts || e.NewValue is not true) return;
        Defer(() => { ts.OnContent = Loc.S("St_On"); ts.OffContent = Loc.S("St_Off"); });
    }

    /// <summary>Отложить до конца разбора элемента — см. пояснение в <see cref="OnKeyChanged"/>.</summary>
    private static void Defer(Action apply)
    {
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (queue == null || !queue.TryEnqueue(() => apply())) apply();
    }

    /// <summary>Проставить локализованные тексты по всему поддереву. Подстраховка для
    /// элементов, созданных в коде; основную работу делает колбэк <see cref="OnKeyChanged"/>.</summary>
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

    /// <summary>
    /// Локализовать пункты всплывающего меню. Нужен отдельный вход, потому что
    /// <see cref="Apply"/> обходит ВИЗУАЛЬНОЕ дерево, а меню в него не попадают:
    /// объявленные в ресурсах (<c>Grid.Resources</c>) там не появляются вовсе, а
    /// объявленные внутри <c>DataTemplate</c> создаются позже — когда список уже
    /// прокручен к нужной строке. Из-за этого контекстные меню строк («Позвонить»,
    /// «Написать», «Копировать номер», «В избранное») оставались русскими при любом
    /// выбранном языке интерфейса.
    ///
    /// Зовётся из обработчика <c>Opening</c>: меню локализуется ровно перед показом,
    /// то есть независимо от того, где и когда его создали.
    /// </summary>
    public static void ApplyToFlyout(object? flyout)
    {
        if (flyout is MenuFlyout mf) ApplyToItems(mf.Items);
    }

    private static void ApplyToItems(IList<MenuFlyoutItemBase> items)
    {
        foreach (MenuFlyoutItemBase item in items)
        {
            string k = GetKey(item);
            if (!string.IsNullOrEmpty(k)) Set(item, k);
            if (item is MenuFlyoutSubItem sub) ApplyToItems(sub.Items);
        }
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
