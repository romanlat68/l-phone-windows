using System;
using LinphoneWinUI.Interop;
using Microsoft.UI.Xaml;

namespace LinphoneWinUI;

/// <summary>
/// Видео собеседника и превью своей камеры внутри экрана звонка.
///
/// Механика непривычная и потому вынесена отдельно: MSOGL рисует в СВОИ top-level окна
/// (класс 'Video'), а мы их «усыновляем» — снимаем рамку, назначаем владельцем главное окно
/// и совмещаем с плейсхолдерами (см. NativeVideoAdopter). Передать MSOGL чужой HWND или
/// репарентить его окно нельзя: DXGI-рендер при этом гибнет.
///
/// Окна ловятся хуком СОЗДАНИЯ, а не опросом по таймеру: MSOGL показывает своё окно сразу
/// после создания, и при опросе большое отдельное окно успевало мигнуть на экране.
///
/// ВАЖНО (airspace): XAML не может рисовать поверх усыновлённых окон.
/// </summary>
internal sealed class VideoOverlayController : IDisposable
{
    private readonly Window _owner;
    private readonly Func<bool> _inCall;          // вне разговора превью из настроек — своим окном
    private readonly Action<string> _log;

    private readonly NativeVideoAdopter _remote;
    private readonly NativeVideoAdopter _preview;

    private IntPtr _hook;
    private NativeMethods.WinEventProc? _hookProc;   // держим ссылку: иначе GC съест под нативным колбэком

    public VideoOverlayController(Window owner, FrameworkElement remoteSurface, FrameworkElement previewSurface,
                                  Func<bool> inCall, Action<string> log)
    {
        _owner = owner;
        _inCall = inCall;
        _log = log;
        _remote = new NativeVideoAdopter(owner, remoteSurface);
        _preview = new NativeVideoAdopter(owner, previewSurface, topmostSibling: true);
    }

    /// <summary>Поставить хук. Один раз на процесс: окна пересоздаются на каждом re-INVITE
    /// (включение видео любой из сторон), и переустанавливать хук на каждый звонок незачем.</summary>
    public void StartWatching()
    {
        if (_hook != IntPtr.Zero) return;
        _hookProc = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject != NativeMethods.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
            if (!_inCall()) return;
            if (!NativeVideoAdopter.IsVideoWindow(hwnd)) return;
            Adopt(hwnd);
        };
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _hookProc, (uint)Environment.ProcessId, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Подобрать окна, созданные ДО установки хука или до входа в разговор.</summary>
    public void AdoptExisting()
    {
        foreach (IntPtr wnd in NativeVideoAdopter.FindVideoWindows()) Adopt(wnd);
    }

    /// <summary>Первое окно — область собеседника, второе — превью своей камеры.</summary>
    private void Adopt(IntPtr hwnd)
    {
        if (hwnd == _remote.Adopted || hwnd == _preview.Adopted) return;
        if (!_remote.IsAlive) { _remote.Adopt(hwnd); _log("Видео: окно собеседника встроено"); }
        else if (!_preview.IsAlive) { _preview.Adopt(hwnd); _log("Видео: превью камеры встроено"); }
        VisibilityChanged?.Invoke();
    }

    /// <summary>Усыновлено новое окно — вызывающему стоит пересчитать видимость оверлеев.</summary>
    public event Action? VisibilityChanged;

    /// <summary>Показывать ли оверлеи (экран звонка на виду и область видео открыта).</summary>
    public void SetVisible(bool visible)
    {
        _remote.SetVisible(visible);
        _preview.SetVisible(visible);
    }

    /// <summary>Окно двигали или меняли размер — оверлеи живут в экранных координатах.</summary>
    public void UpdateBounds()
    {
        _remote.UpdateBounds(force: true);
        _preview.UpdateBounds(force: true);
    }

    /// <summary>Видео кончилось: прячем и забываем окна (уничтожает их сам MSOGL).</summary>
    public void Release()
    {
        _remote.Release();
        _preview.Release();
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        try { NativeMethods.UnhookWinEvent(_hook); } catch { }
        _hook = IntPtr.Zero;
    }
}
