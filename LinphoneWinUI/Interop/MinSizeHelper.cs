using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Задаёт минимальный размер окна на уровне ОС через WM_GETMINMAXINFO.
/// Так система сама не даёт перетащить край ниже минимума — без дёрганья.
/// Поддерживает несколько окон (минимум хранится для каждого hwnd отдельно).
/// </summary>
internal static class MinSizeHelper
{
    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Делегат держим в статике, чтобы GC его не собрал. Минимум — для каждого окна свой.
    private static SubclassProc? _proc;
    private static readonly Dictionary<IntPtr, (int W, int H)> _mins = new();

    /// <summary>Масштаб окна (DPI/96): 1.0 на 100%, 1.5 на 150% и т.д.</summary>
    public static double GetScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Минимальный размер в ЛОГИЧЕСКИХ пикселях (как в XAML, на 100% масштабе).</summary>
    public static void SetMinSize(IntPtr hwnd, int minWidthLogical, int minHeightLogical)
    {
        _mins[hwnd] = (minWidthLogical, minHeightLogical);
        _proc ??= WndProc;
        SetWindowSubclass(hwnd, _proc, UIntPtr.Zero, UIntPtr.Zero);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_GETMINMAXINFO && _mins.TryGetValue(hWnd, out var m))
        {
            double scale = GetScale(hWnd);
            // MINMAXINFO.ptMinTrackSize: смещение 24 (x) и 28 (y) от начала структуры.
            Marshal.WriteInt32(lParam, 24, (int)(m.W * scale));
            Marshal.WriteInt32(lParam, 28, (int)(m.H * scale));
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
