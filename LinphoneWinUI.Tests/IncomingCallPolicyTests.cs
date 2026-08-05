using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Как показывать входящий звонок. Семь условий, переплетённых неочевидно, и именно здесь
/// жил баг, из-за которого автоответ принимал ВТОРОЙ входящий и терял живой разговор —
/// в двухсотстрочном обработчике заметить это было нечем.
/// </summary>
public class IncomingCallPolicyTests
{
    /// <summary>Обычная обстановка: свободны, окно видно и активно, мини-окна включены.</summary>
    private static IncomingCallContext Ctx(
        bool dnd = false, bool inCall = false, bool visible = true, bool foreground = true,
        bool mini = true, bool toFront = false, bool autoAnswer = false,
        int? intercom = null, bool intercomEnabled = true, bool dialerTab = true) =>
        new(dnd, inCall, visible, foreground, mini, toFront, autoAnswer, intercom, intercomEnabled,
            dialerTab);

    // --- «Не беспокоить» перекрывает всё ---

    [Fact]
    public void DoNotDisturb_RejectsBusy_AndShowsNothing()
    {
        var p = IncomingCallPolicy.Decide(Ctx(dnd: true, autoAnswer: true, toFront: true));

        Assert.True(p.RejectBusy);
        Assert.False(p.ShowBanner);
        Assert.False(p.ShowFullScreen);
        Assert.False(p.ShowMiniWindow);
        Assert.False(p.ShowToast);
        Assert.False(p.FlashTaskbar);
        Assert.False(p.BringToFront);
        // Даже при включённом автоответе: «не беспокоить» означает не беспокоить.
        Assert.False(p.StartAutoAnswer);
    }

    // --- Экран входящего ---

    [Fact]
    public void WhenFree_ShowsFullScreen()
    {
        var p = IncomingCallPolicy.Decide(Ctx());

        Assert.True(p.ShowFullScreen);
        Assert.False(p.ShowBanner);
    }

    // ------------------------------------------------------------------
    // Входящий не забирает навигацию (U-4)
    // ------------------------------------------------------------------
    // Полноэкранный экран входящего — секция вкладки «Набор». Раньше звонок насильно
    // переключал туда, и человек, искавший контакт или смотревший журнал, терял своё место;
    // уйти обратно он мог, но вместе с вкладкой терял и кнопки «Принять/Отклонить».

    [Fact]
    public void OnOtherTab_ShowsBannerInsteadOfFullScreen()
    {
        var p = IncomingCallPolicy.Decide(Ctx(dialerTab: false));

        Assert.True(p.ShowBanner);
        Assert.False(p.ShowFullScreen);
    }

    [Fact]
    public void OnDialerTab_StillShowsFullScreen()
    {
        Assert.True(IncomingCallPolicy.Decide(Ctx(dialerTab: true)).ShowFullScreen);
    }

    // Во время разговора вкладка не важна: полный экран спрятал бы активный звонок в любом случае.
    [Fact]
    public void DuringCall_TabDoesNotMatter()
    {
        Assert.True(IncomingCallPolicy.Decide(Ctx(inCall: true, dialerTab: true)).ShowBanner);
        Assert.True(IncomingCallPolicy.Decide(Ctx(inCall: true, dialerTab: false)).ShowBanner);
    }

    // Тот же ответ, что у Decide, но без всего плана: им пользуется окно при переключении
    // вкладок, пока телефон звонит. Разойдись эти два места — экран входящего и баннер
    // оказались бы видны одновременно.
    [Theory]
    [InlineData(false, true, true)]    // свободны, вкладка «Набор» — полный экран
    [InlineData(false, false, false)]  // свободны, другая вкладка — баннер
    [InlineData(true, true, false)]    // разговор — баннер даже на «Наборе»
    [InlineData(true, false, false)]
    public void FullScreenFits_MatchesDecide(bool inCall, bool dialerTab, bool expected)
    {
        Assert.Equal(expected, IncomingCallPolicy.FullScreenFits(inCall, dialerTab));
        Assert.Equal(expected, IncomingCallPolicy.Decide(Ctx(inCall: inCall, dialerTab: dialerTab)).ShowFullScreen);
    }

    // Полноэкранный экран во время разговора спрятал бы активный звонок, и управлять им
    // стало бы нельзя — поэтому второй входящий показывается компактным баннером.
    [Fact]
    public void DuringCall_ShowsBannerInstead()
    {
        var p = IncomingCallPolicy.Decide(Ctx(inCall: true));

        Assert.True(p.ShowBanner);
        Assert.False(p.ShowFullScreen);
    }

    // --- Уведомление Windows как ЗАПАСНОЙ путь ---

    [Fact]
    public void MiniWindow_ReplacesToast()
    {
        var p = IncomingCallPolicy.Decide(Ctx(mini: true, visible: false));

        Assert.True(p.ShowMiniWindow);
        Assert.False(p.ShowToast);
    }

    [Fact]
    public void WithoutMiniWindow_HiddenWindow_GetsToast()
    {
        var p = IncomingCallPolicy.Decide(Ctx(mini: false, visible: false));

        Assert.True(p.ShowToast);
    }

    // Пользователь и так смотрит на наше окно — уведомление было бы шумом.
    [Fact]
    public void WithoutMiniWindow_ForegroundWindow_GetsNoToast()
    {
        var p = IncomingCallPolicy.Decide(Ctx(mini: false, visible: true, foreground: true));

        Assert.False(p.ShowToast);
    }

    // Окно открыто, но перекрыто чужим — звонок иначе заметили бы только по динамику.
    [Fact]
    public void WithoutMiniWindow_BackgroundWindow_GetsToast()
    {
        var p = IncomingCallPolicy.Decide(Ctx(mini: false, visible: true, foreground: false));

        Assert.True(p.ShowToast);
    }

    // --- Мигание и вывод на передний план ---

    [Fact]
    public void HiddenWindow_DoesNotFlashTaskbar()
    {
        Assert.False(IncomingCallPolicy.Decide(Ctx(visible: false)).FlashTaskbar);
        Assert.True(IncomingCallPolicy.Decide(Ctx(visible: true)).FlashTaskbar);
    }

    // Свёрнутое в трей окно на передний план не выдёргиваем: пользователь спрятал его сам.
    [Fact]
    public void BringToFront_OnlyForVisibleWindow()
    {
        Assert.False(IncomingCallPolicy.Decide(Ctx(visible: false, toFront: true)).BringToFront);
        Assert.True(IncomingCallPolicy.Decide(Ctx(visible: true, toFront: true)).BringToFront);
        Assert.False(IncomingCallPolicy.Decide(Ctx(visible: true, toFront: false)).BringToFront);
    }

    // --- Автоответ ---

    [Fact]
    public void AutoAnswer_WhenFree_IsAllowed()
    {
        Assert.True(IncomingCallPolicy.Decide(Ctx(autoAnswer: true)).StartAutoAnswer);
    }

    // Регрессия: второй входящий принимался сам собой, ставя текущий разговор на удержание —
    // пользователь терял живой звонок без единого своего действия.
    [Fact]
    public void AutoAnswer_DuringCall_IsForbidden()
    {
        Assert.False(IncomingCallPolicy.Decide(Ctx(autoAnswer: true, inCall: true)).StartAutoAnswer);
    }

    [Fact]
    public void AutoAnswer_Disabled_StaysOff()
    {
        Assert.False(IncomingCallPolicy.Decide(Ctx(autoAnswer: false)).StartAutoAnswer);
    }

    // --- Целостность плана на всей таблице истинности ---

    [Fact]
    public void Plan_IsAlwaysSelfConsistent()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            var ctx = new IncomingCallContext(
                DoNotDisturb: (mask & 1) != 0,
                AlreadyInCall: (mask & 2) != 0,
                WindowVisible: (mask & 4) != 0,
                WindowIsForeground: (mask & 8) != 0,
                MiniWindowEnabled: (mask & 16) != 0,
                BringToFrontEnabled: (mask & 32) != 0,
                AutoAnswerEnabled: (mask & 64) != 0,
                DialerTabOpen: (mask & 128) != 0);

            var p = IncomingCallPolicy.Decide(ctx);

            // Баннер и полный экран — взаимоисключающие способы показать один и тот же звонок.
            Assert.False(p.ShowBanner && p.ShowFullScreen);
            // Мини-окно и уведомление одновременно — двойное оповещение об одном звонке.
            Assert.False(p.ShowMiniWindow && p.ShowToast);
            // Отклоняем «занято» — значит не показываем ничего вообще.
            if (p.RejectBusy)
                Assert.False(p.ShowBanner || p.ShowFullScreen || p.ShowMiniWindow
                             || p.ShowToast || p.FlashTaskbar || p.BringToFront || p.StartAutoAnswer);
            else
                // Звонок обязан быть показан хоть как-то, иначе он «беззвучный».
                Assert.True(p.ShowBanner || p.ShowFullScreen);
        }
    }

    // --- Интерком: АТС просит ответить сама ---
    // Так работают интерком-коды, групповые оповещения и домофоны на Asterisk/FreePBX.

    [Fact]
    public void Intercom_AnswersAutomatically_EvenIfAutoAnswerIsOff()
    {
        var p = IncomingCallPolicy.Decide(Ctx(autoAnswer: false, intercom: 0));

        Assert.True(p.StartAutoAnswer);
        Assert.True(p.AutoAnswerIsIntercom);
        Assert.Equal(0, p.AutoAnswerAfterSeconds);
    }

    [Fact]
    public void Intercom_HonorsDelayFromHeader()
    {
        var p = IncomingCallPolicy.Decide(Ctx(intercom: 5));

        Assert.True(p.StartAutoAnswer);
        Assert.Equal(5, p.AutoAnswerAfterSeconds);
    }

    // Оповещение не должно обрывать живой разговор — ни один аппаратный телефон так не делает.
    [Fact]
    public void Intercom_DuringCall_DoesNotAnswer()
    {
        var p = IncomingCallPolicy.Decide(Ctx(inCall: true, intercom: 0));

        Assert.False(p.StartAutoAnswer);
        // Но показать звонок всё равно надо — баннером поверх разговора.
        Assert.True(p.ShowBanner);
    }

    // «Не беспокоить» сильнее просьбы АТС: телефон, отвечающий сам вопреки ему, —
    // худшее, что может случиться на совещании.
    [Fact]
    public void Intercom_DoesNotOverrideDoNotDisturb()
    {
        var p = IncomingCallPolicy.Decide(Ctx(dnd: true, intercom: 0));

        Assert.True(p.RejectBusy);
        Assert.False(p.StartAutoAnswer);
    }

    [Fact]
    public void Intercom_CanBeDisabledByUser()
    {
        var p = IncomingCallPolicy.Decide(Ctx(intercom: 0, intercomEnabled: false, autoAnswer: false));

        Assert.False(p.StartAutoAnswer);
    }

    // Обычный звонок с включённым автоответом — это НЕ интерком, и в журнале он не должен
    // помечаться просьбой АТС.
    [Fact]
    public void OrdinaryAutoAnswer_IsNotMarkedAsIntercom()
    {
        var p = IncomingCallPolicy.Decide(Ctx(autoAnswer: true, intercom: null));

        Assert.True(p.StartAutoAnswer);
        Assert.False(p.AutoAnswerIsIntercom);
    }

    // ------------------------------------------------------------------
    // Мини-окно не дублирует полноэкранный входящий
    // ------------------------------------------------------------------
    // Раньше mini = MiniWindowEnabled без единого условия, тогда как toast честно проверял
    // «не смотрит ли человек и так на наше окно». Получалось два одинаковых предложения
    // ответить друг на друге, причём верхнее перехватывало клик, нацеленный в нижнее.

    [Fact]
    public void ForegroundWindow_ShowsOnlyFullScreen_NoMiniNoToast()
    {
        var p = IncomingCallPolicy.Decide(new IncomingCallContext(
            DoNotDisturb: false, AlreadyInCall: false,
            WindowVisible: true, WindowIsForeground: true,
            MiniWindowEnabled: true, BringToFrontEnabled: false, AutoAnswerEnabled: false));

        Assert.True(p.ShowFullScreen);
        Assert.False(p.ShowMiniWindow);   // ключевое: не дублируем то, что уже на экране
        Assert.False(p.ShowToast);
    }

    [Fact]
    public void DuringCall_ForegroundWindow_StillGetsMiniWindow()
    {
        // Во время разговора показывается компактный баннер, а не полный экран, — мини-окно
        // второго входящего остаётся единственным местом с кнопками «Принять/Отклонить».
        var p = IncomingCallPolicy.Decide(new IncomingCallContext(
            DoNotDisturb: false, AlreadyInCall: true,
            WindowVisible: true, WindowIsForeground: true,
            MiniWindowEnabled: true, BringToFrontEnabled: false, AutoAnswerEnabled: false));

        Assert.True(p.ShowBanner);
        Assert.False(p.ShowFullScreen);
        Assert.True(p.ShowMiniWindow);
    }

    [Fact]
    public void BackgroundWindow_StillGetsMiniWindow()
    {
        var p = IncomingCallPolicy.Decide(new IncomingCallContext(
            DoNotDisturb: false, AlreadyInCall: false,
            WindowVisible: true, WindowIsForeground: false,
            MiniWindowEnabled: true, BringToFrontEnabled: false, AutoAnswerEnabled: false));

        Assert.True(p.ShowMiniWindow);   // окно перекрыто чужим — предложить ответить надо
        Assert.False(p.ShowToast);
    }

    [Fact]
    public void EveryIncoming_AlwaysOffersSomeWayToAnswer()
    {
        // Инвариант поверх всей таблицы: кроме «Не беспокоить», у человека ВСЕГДА есть чем
        // ответить — полный экран, баннер, мини-окно или уведомление. Иначе телефон звонит,
        // а принять звонок негде.
        for (int mask = 0; mask < 256; mask++)
        {
            var ctx = new IncomingCallContext(
                DoNotDisturb: (mask & 1) != 0,
                AlreadyInCall: (mask & 2) != 0,
                WindowVisible: (mask & 4) != 0,
                WindowIsForeground: (mask & 8) != 0,
                MiniWindowEnabled: (mask & 16) != 0,
                BringToFrontEnabled: (mask & 32) != 0,
                AutoAnswerEnabled: (mask & 64) != 0,
                DialerTabOpen: (mask & 128) != 0);
            var p = IncomingCallPolicy.Decide(ctx);
            if (p.RejectBusy) continue;
            Assert.True(p.ShowFullScreen || p.ShowBanner || p.ShowMiniWindow || p.ShowToast,
                $"маска {mask}: входящий не показан нигде");
        }
    }
}
