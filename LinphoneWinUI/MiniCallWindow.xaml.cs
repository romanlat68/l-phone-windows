using System;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI;

/// <summary>
/// Мини-окно активного звонка поверх всех окон.
///
/// Зачем: оператор работает в CRM, а софтфон под ней. Чтобы выключить микрофон или сбросить,
/// приходилось искать окно через Alt-Tab — в разговоре это заметная возня. Здесь только то,
/// что нужно прямо сейчас: кто на линии, сколько идёт, микрофон, удержание, сброс.
///
/// Окном владеет MainWindow: оно показывает мини-окно на время разговора и закрывает после.
/// </summary>
public sealed partial class MiniCallWindow : Window
{
    private readonly ISipService _linphone;
    private readonly Action _openMain;

    private readonly int _slot;

    /// <param name="slot">Этаж стопки в правом нижнем углу: 0 — активный разговор,
    /// 1 — второй входящий НАД ним. Второй звонок не имеет права появиться на месте кнопок
    /// первого: пользователь уже целится мышью, и всплывшее под курсором окно поймает
    /// чужое нажатие — можно сбросить разговор вместо того, что собирался нажать.</param>
    public MiniCallWindow(ISipService linphone, Action openMain, int slot = 0)
    {
        _linphone = linphone;
        _openMain = openMain;
        _slot = slot;
        InitializeComponent();

        Title = "L-Phone";
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            // Поверх всех окон — иначе смысл теряется: окно должно быть видно ПОВЕРХ CRM.
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.SetBorderAndTitleBar(true, false);   // рамка без заголовка: место дороже
        }
        AppWindow.Resize(new SizeInt32(300, 130));
        RestorePosition();

        // Куда человек передвинул окно — там оно и должно появляться в следующий раз.
        // Раньше позиция вычислялась заново на каждый звонок, и окно упрямо возвращалось
        // в угол, даже если оно там перекрывало нужное.
        AppWindow.Changed += (s, e) => { if (e.DidPositionChange) SavePosition(); };
    }

    /// <summary>Ключ настройки для этажа стопки: у второго входящего своё место.</summary>
    private string PosKey => _slot == 0 ? "mini_window_pos" : $"mini_window_pos{_slot}";

    /// <summary>Вернуть окно туда, где его оставили — но так, чтобы его было видно.
    /// Позиции нет, она испорчена или экран, на котором окно стояло, исчез — правый нижний
    /// угол. Окно, вылезшее за край, вдвигаем обратно.</summary>
    private void RestorePosition()
    {
        string saved = _linphone.GetAppString(PosKey, "");
        if (!MiniWindowPlacement.TryParse(saved, out int x, out int y))
        {
            MoveToBottomRight();
            return;
        }

        try
        {
            int w = AppWindow.Size.Width, h = AppWindow.Size.Height;

            // Проверяем все четыре угла, а не одну точку: окно, уведённое за край,
            // формально ещё «на мониторе» верхним левым углом, хотя на экране от него
            // осталась полоска. Углы же честно показывают, видно ли окно целиком —
            // и заодно разрешают законное положение на стыке двух мониторов,
            // где зажатие в один экран отбрасывало бы окно от края к середине.
            bool whollyVisible = MiniWindowPlacement.Corners(x, y, w, h)
                .All(p => MinSizeHelper.IsPointOnAnyMonitor(p.X, p.Y));

            if (!whollyVisible)
            {
                // Вдвигаем в рабочую область ближайшего монитора. Если окно стояло на
                // экране, которого больше нет, ближайший — это тот, что остался.
                var a = DisplayArea.GetFromPoint(new PointInt32(x + w / 2, y + h / 2),
                                                 DisplayAreaFallback.Nearest)?.WorkArea;
                if (a is not { } area) { MoveToBottomRight(); return; }

                (x, y) = MiniWindowPlacement.Clamp(x, y, w, h,
                                                   area.X, area.Y, area.Width, area.Height);
            }

            AppWindow.Move(new PointInt32(x, y));
        }
        catch { MoveToBottomRight(); }
    }

    private void SavePosition()
    {
        try
        {
            var pos = AppWindow.Position;
            _linphone.SetAppString(PosKey, MiniWindowPlacement.Format(pos.X, pos.Y));
        }
        catch { }
    }

    /// <summary>Правый нижний угол рабочей области — там, где пользователь привык видеть
    /// всплывающие окна, и где меньше всего шансов перекрыть то, с чем он работает.</summary>
    private void MoveToBottomRight()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            // Каждый следующий этаж — выше предыдущего на высоту окна плюс зазор.
            int lift = _slot * (AppWindow.Size.Height + 12);
            AppWindow.Move(new PointInt32(
                area.X + area.Width - AppWindow.Size.Width - 24,
                area.Y + area.Height - AppWindow.Size.Height - 24 - lift));
        }
        catch { /* нестандартная конфигурация мониторов — оставим позицию по умолчанию */ }
    }

    /// <summary>Обновить показ. Вызывает MainWindow из своего же обработчика состояния звонка,
    /// чтобы источник правды был один и данные не разъезжались между окнами.</summary>
    public void Update(string name, string initials, string status, bool muted, bool onHold,
                       bool encrypted)
    {
        MiniName.Text = name;
        MiniInitials.Text = initials;
        MiniStatus.Text = status;
        MiniLock.Visibility = encrypted ? Visibility.Visible : Visibility.Collapsed;
        MiniMuteIcon.Glyph = MuteGlyph(muted);
        MiniHoldIcon.Glyph = HoldGlyph(onHold);
        IncomingButtons.Visibility = Visibility.Collapsed;
        InCallButtons.Visibility = Visibility.Visible;
    }

    /// <summary>Показать входящий: имя звонящего и кнопки принять/отклонить. Это окно заменяет
    /// уведомление Windows — оно всегда поверх, не уезжает в центр уведомлений и позволяет
    /// ответить, не трогая главное окно.</summary>
    public void ShowIncoming(string name, string initials, string status)
    {
        MiniName.Text = name;
        MiniInitials.Text = initials;
        MiniStatus.Text = status;
        // У входящего медиа ещё не согласовано — показывать замок нечему.
        MiniLock.Visibility = Visibility.Collapsed;
        IncomingButtons.Visibility = Visibility.Visible;
        InCallButtons.Visibility = Visibility.Collapsed;
    }

    private void OnAccept(object sender, RoutedEventArgs e) => _linphone.AcceptIncoming();
    private void OnDecline(object sender, RoutedEventArgs e) => _linphone.DeclineIncoming();

    // Глифы Segoe Fluent Icons задаём КОДАМИ, а не символами в исходнике: в редакторах и
    // терминалах они не отображаются, и пустая строка неотличима от настоящего глифа.
    private static string MuteGlyph(bool muted) => muted ? "\uE74F" : "\uE720";   // Mute / Microphone
    private static string HoldGlyph(bool onHold) => onHold ? "\uE768" : "\uE769"; // Play / Pause

    // Кнопки только отправляют команду: значки перерисует Update, который главное окно
    // вызывает по событию от ядра. Рисовать по возврату команды нельзя — ядро отвечает
    // не мгновенно, и «микрофон выключен» на кнопке при живом микрофоне хуже, чем задержка.
    private void OnMute(object sender, RoutedEventArgs e) => _linphone.ToggleMute();

    private void OnHold(object sender, RoutedEventArgs e) => _linphone.ToggleHold();

    private void OnHangup(object sender, RoutedEventArgs e) => _linphone.Hangup();

    private void OnOpenMain(object sender, RoutedEventArgs e) => _openMain();
}
