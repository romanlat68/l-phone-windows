using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Задаёт минимальный размер окна на уровне ОС через WM_GETMINMAXINFO.
/// Так система сама не даёт перетащить край ниже минимума — без дёрганья.
/// Поддерживает несколько окон (минимум хранится для каждого hwnd отдельно).
///
/// ВАЖНО: минимум ОБРЕЗАЕТСЯ рабочей областью монитора, на котором сейчас окно.
/// Иначе желаемый минимум превращается в ловушку: на 1920×1080 при масштабе 150 %
/// рабочая область — 720 логических пикселей, и окно с минимумом 740 не помещалось
/// по высоте, а уменьшить его было НЕЛЬЗЯ: нижний край с кнопкой вызова и вкладками
/// уезжал под панель задач. Пересчёт идёт на каждое сообщение, поэтому перенос окна
/// на монитор с другим разрешением или масштабом обрабатывается сам собой.
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

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONULL = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>Есть ли ПРЯМО СЕЙЧАС монитор под этой точкой экрана.
    ///
    /// Win32, а не DisplayArea.FindAll(): последний в конструкторе окна (до Activate) не
    /// отрабатывает, и восстановление геометрии молча уходило в ветку «по умолчанию» — окно
    /// каждый раз открывалось в стартовом размере, хотя сохранённые координаты были
    /// совершенно нормальные и лежали на единственном мониторе.</summary>
    public static bool IsPointOnAnyMonitor(int x, int y)
    {
        try { return MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONULL) != IntPtr.Zero; }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // Делегат держим в статике, чтобы GC его не собрал. Минимум — для каждого окна свой.
    private static SubclassProc? _proc;
    private static readonly Dictionary<IntPtr, (int W, int H)> _mins = new();

    /// <summary>Масштаб окна (DPI/96): 1.0 на 100%, 1.5 на 150% и т.д.</summary>
    public static double GetScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Рабочая область (без панели задач) монитора, на котором окно, в ФИЗИЧЕСКИХ пикселях.
    /// (0, 0) — определить не удалось.</summary>
    public static (int Width, int Height) GetWorkAreaPhysical(IntPtr hwnd)
    {
        try
        {
            IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return (0, 0);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(mon, ref mi)) return (0, 0);
            return (mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
        }
        catch { return (0, 0); }
    }

    /// <summary>Минимальный размер в ЛОГИЧЕСКИХ пикселях (как в XAML, на 100% масштабе).</summary>
    public static void SetMinSize(IntPtr hwnd, int minWidthLogical, int minHeightLogical)
    {
        _mins[hwnd] = (minWidthLogical, minHeightLogical);
        _proc ??= WndProc;
        SetWindowSubclass(hwnd, _proc, UIntPtr.Zero, UIntPtr.Zero);
    }

    /// <summary>Минимум в ФИЗИЧЕСКИХ пикселях с поправкой на рабочую область монитора.
    /// Вынесено отдельно, чтобы стартовый размер окна считался ровно тем же правилом,
    /// что и ограничение, которое отдаётся системе.</summary>
    public static (int W, int H) EffectiveMinPhysical(IntPtr hwnd, int minWidthLogical, int minHeightLogical)
    {
        double scale = GetScale(hwnd);
        int w = (int)(minWidthLogical * scale);
        int h = (int)(minHeightLogical * scale);
        return ClampToWorkArea(hwnd, w, h);
    }

    /// <summary>Обрезать размер (физические пиксели) рабочей областью монитора.
    /// Запас в 40 px: окно ровно во всю рабочую область невозможно ни подвинуть, ни ухватить.</summary>
    public static (int W, int H) ClampToWorkArea(IntPtr hwnd, int wPhysical, int hPhysical)
    {
        var (workW, workH) = GetWorkAreaPhysical(hwnd);
        if (workW > 0) wPhysical = Math.Min(wPhysical, Math.Max(320, workW - 40));
        if (workH > 0) hPhysical = Math.Min(hPhysical, Math.Max(360, workH - 40));
        return (wPhysical, hPhysical);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_GETMINMAXINFO && _mins.TryGetValue(hWnd, out var m))
        {
            var (w, h) = EffectiveMinPhysical(hWnd, m.W, m.H);
            // MINMAXINFO.ptMinTrackSize: смещение 24 (x) и 28 (y) от начала структуры.
            Marshal.WriteInt32(lParam, 24, w);
            Marshal.WriteInt32(lParam, 28, h);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
