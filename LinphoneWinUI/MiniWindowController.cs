using System;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI;

/// <summary>
/// Мини-окна звонка поверх всех окон: нижнее — активный разговор, верхнее — второй входящий.
///
/// Вынесено из MainWindow: создание, отслеживание закрытия и уничтожение двух окон было
/// размазано по четырём методам, а поле обнулялось то вручную, то из обработчика Closed —
/// такой код легко ломается при следующей правке.
///
/// Почему второй входящий показывается ОТДЕЛЬНЫМ окном этажом выше, а не подменяет
/// содержимое активного: пользователь мог уже целиться мышью в его кнопки, и всплывшее под
/// курсором окно поймает чужое нажатие — вместо «микрофон» человек сбросит разговор.
/// Кнопки активного звонка не двигаются.
/// </summary>
internal sealed class MiniWindowController
{
    private readonly ISipService _sip;
    private readonly Action _openMain;
    private readonly Func<string, string> _initials;

    private MiniCallWindow? _call;       // активный разговор, нижний этаж
    private MiniCallWindow? _incoming;   // второй входящий, этажом выше

    public MiniWindowController(ISipService sip, Action openMain, Func<string, string> initials)
    {
        _sip = sip;
        _openMain = openMain;
        _initials = initials;
    }

    /// <summary>Мини-окна включены в настройках.</summary>
    private bool Enabled => _sip.GetAppFlag("mini_call_window", true);

    /// <summary>Показать/обновить окно активного разговора. Данные приходят снаружи, чтобы
    /// окна не разъезжались: мини-окно ничего не запрашивает у ядра само.</summary>
    public void UpdateCall(string name, string status, bool muted, bool onHold, bool encrypted)
    {
        if (!Enabled) return;
        Ensure(ref _call, slot: 0);
        _call!.Update(name, _initials(name), status, muted, onHold, encrypted);
    }

    /// <summary>Обновить УЖЕ ОТКРЫТОЕ окно разговора; если его нет — не создавать.
    ///
    /// Нужно там, где меняется деталь состояния (микрофон, удержание), а не сам факт разговора.
    /// Через UpdateCall это привело бы к тому, что мини-окно всплывает заново уже после
    /// завершения звонка: сброс индикации в конце разговора — тоже смена состояния.</summary>
    public void RefreshCall(string name, string status, bool muted, bool onHold, bool encrypted)
    {
        if (_call == null) return;
        _call.Update(name, _initials(name), status, muted, onHold, encrypted);
    }

    /// <summary>Показать входящий. false — мини-окна выключены, и вызывающий должен
    /// показать уведомление Windows вместо них.</summary>
    /// <param name="duringCall">Идёт разговор — значит это ВТОРОЙ входящий, ему своё окно.</param>
    public bool ShowIncoming(string caller, string number, bool duringCall)
    {
        if (!Enabled) return false;
        string status = number.Length > 0 ? number : Loc.S("St_IncomingCall");

        if (duringCall)
        {
            Ensure(ref _incoming, slot: 1);
            _incoming!.ShowIncoming(caller, _initials(caller), status);
            return true;
        }

        Ensure(ref _call, slot: 0);
        _call!.ShowIncoming(caller, _initials(caller), status);
        return true;
    }

    /// <summary>Окно второго входящего: закрываем, как только он разрешился (принят, отклонён,
    /// оборвался) — висящее окно предлагало бы принять звонок, которого уже нет.</summary>
    public void CloseIncoming() => Close(ref _incoming);

    /// <summary>Окно активного разговора.</summary>
    public void CloseCall() => Close(ref _call);

    /// <summary>Оба окна: они поверх всех, и без явного закрытия переживут выход приложения.</summary>
    public void CloseAll()
    {
        CloseIncoming();
        CloseCall();
    }

    private void Ensure(ref MiniCallWindow? window, int slot)
    {
        if (window != null) return;
        var w = new MiniCallWindow(_sip, _openMain, slot);
        window = w;
        // Окно могут закрыть крестиком — тогда поле обязано обнулиться, иначе следующий
        // вызов будет работать с уничтоженным окном.
        w.Closed += (_, _) => { if (ReferenceEquals(_call, w)) _call = null; else if (ReferenceEquals(_incoming, w)) _incoming = null; };
        w.Activate();
    }

    /// <summary>Поле обнуляем ДО Close(): обработчик Closed иначе сделает это повторно.</summary>
    private static void Close(ref MiniCallWindow? window)
    {
        var w = window;
        window = null;
        try { w?.Close(); } catch { }
    }
}
