using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using static LinphoneWinUI.Interop.NativeMethods;

namespace LinphoneWinUI;

/// <summary>
/// «Приклеивает» окно видео, которое создаёт сам liblinphone/MSOGL (class 'Video'),
/// к области экрана звонка: окно остаётся TOP-LEVEL (репарентинг и передача чужого HWND
/// глушат DXGI-рендер — проверено), но мы снимаем ему рамку, делаем его owned-окном
/// главного (держится над ним, сворачивается вместе, нет в панели задач) и синхронизируем
/// позицию с XAML-плейсхолдером в экранных координатах.
/// ВАЖНО (airspace): XAML не может рисовать ПОВЕРХ оверлея. Все методы — с UI-потока.
/// </summary>
internal sealed class NativeVideoAdopter
{
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
    private const uint WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    private const uint WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x00040000;
    private const int SW_HIDE = 0, SW_SHOWNA = 8;
    private const uint SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_NOZORDER = 0x0004;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    private readonly Window _window;
    private readonly FrameworkElement _placeholder;
    private readonly bool _topmostSibling;   // держать над соседним оверлеем (превью в углу видео)
    private IntPtr _adopted;
    private bool _shouldShow;
    private (int X, int Y, int W, int H) _lastBounds = (-1, -1, -1, -1);

    public NativeVideoAdopter(Window window, FrameworkElement placeholder, bool topmostSibling = false)
    {
        _window = window;
        _placeholder = placeholder;
        _topmostSibling = topmostSibling;
        _placeholder.LayoutUpdated += (_, _) => UpdateBounds();
    }

    /// <summary>Окно живо (MSOGL уничтожает его сам при остановке потока).</summary>
    public bool IsAlive => _adopted != IntPtr.Zero && IsWindow(_adopted);

    public IntPtr Adopted => _adopted;

    /// <summary>Найти окна видео liblinphone (class 'Video') текущего процесса — в т.ч. ещё
    /// НЕ показанные: рамку/владельца/позицию применяем до того, как MSOGL сам покажет окно,
    /// иначе на долю секунды вспыхивает большое отдельное окно с заголовком.</summary>
    public static List<IntPtr> FindVideoWindows()
    {
        var found = new List<IntPtr>();
        uint myPid = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == myPid)
            {
                var cls = new System.Text.StringBuilder(16);
                GetClassNameW(h, cls, 16);
                if (cls.ToString() == "Video") found.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Класс окна — 'Video' (окно рендера MSOGL).</summary>
    public static bool IsVideoWindow(IntPtr h)
    {
        var cls = new System.Text.StringBuilder(16);
        GetClassNameW(h, cls, 16);
        return cls.ToString() == "Video";
    }

    /// <summary>Приклеить окно видео: снять рамку/заголовок, увести из панели задач,
    /// назначить главное окно владельцем и совместить с плейсхолдером.</summary>
    public void Adopt(IntPtr videoWnd)
    {
        if (videoWnd == IntPtr.Zero) return;
        _adopted = videoWnd;
        _lastBounds = (-1, -1, -1, -1);
        _shouldShow = true;

        uint style = (uint)GetWindowLongPtr(videoWnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLongPtr(videoWnd, GWL_STYLE, (IntPtr)style);

        uint ex = (uint)GetWindowLongPtr(videoWnd, GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(videoWnd, GWL_EXSTYLE, (IntPtr)ex);

        // Owner (не parent!): оверлей держится над главным окном и сворачивается вместе с ним.
        SetWindowLongPtr(videoWnd, GWLP_HWNDPARENT, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        UpdateBounds(force: true);
    }

    /// <summary>Показывать ли оверлей (экран звонка на виду). Окно живёт своей жизнью —
    /// прячем его, когда вкладка/область видео скрыты.</summary>
    public void SetVisible(bool visible)
    {
        _shouldShow = visible;
        if (!IsAlive) return;
        if (visible) { UpdateBounds(force: true); ShowWindow(_adopted, SW_SHOWNA); }
        else ShowWindow(_adopted, SW_HIDE);
    }

    /// <summary>Спрятать и забыть окно (конец видео/звонка). Само окно уничтожает MSOGL.</summary>
    public void Release()
    {
        if (IsAlive) ShowWindow(_adopted, SW_HIDE);
        _adopted = IntPtr.Zero;
        _shouldShow = false;
    }

    /// <summary>Совместить оверлей с плейсхолдером (экранные координаты, физпиксели).</summary>
    public void UpdateBounds(bool force = false)
    {
        if (!IsAlive || !_shouldShow || _window.Content is not FrameworkElement root) return;
        try
        {
            var pos = _placeholder.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
            double scale = root.XamlRoot?.RasterizationScale ?? 1.0;
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(_window), ref origin);
            var b = (origin.X + (int)(pos.X * scale), origin.Y + (int)(pos.Y * scale),
                     Math.Max(1, (int)(_placeholder.ActualWidth * scale)),
                     Math.Max(1, (int)(_placeholder.ActualHeight * scale)));
            if (!force && b == _lastBounds) return;   // LayoutUpdated сыплется часто
            _lastBounds = b;
            SetWindowPos(_adopted, HWND_TOP, b.Item1, b.Item2, b.Item3, b.Item4,
                SWP_NOACTIVATE | SWP_FRAMECHANGED | (_topmostSibling ? 0 : SWP_NOZORDER));
        }
        catch { /* элемент ещё не в дереве — обновимся со следующим LayoutUpdated */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
