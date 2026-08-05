using System;
using System.Collections.Generic;
using LinphoneWinUI;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Ручной таймер: тесты «прокручивают» секунды сами, а не ждут их.</summary>
internal sealed class FakeCountdownTimer : ICountdownTimer
{
    private Action? _onTick;
    public bool Running { get; private set; }

    public void Start(TimeSpan interval, Action onTick) { _onTick = onTick; Running = true; }
    public void Stop() => Running = false;

    /// <summary>Проматывает n тиков (тик = секунда). Остановленный таймер не тикает.</summary>
    public void Advance(int ticks)
    {
        for (int i = 0; i < ticks && Running; i++) _onTick?.Invoke();
    }
}

/// <summary>
/// Автодозвон при «занято». До выноса из MainWindow этот автомат не проверялся вовсе:
/// он был перемешан с видимостью панелей, а условий выхода у него четыре — успех, отмена
/// пользователем, звонок на другой номер и входящий звонок. Ошибка здесь означает, что
/// клиент звонит сам по себе, — цена такой ошибки высока.
/// </summary>
public class RedialControllerTests
{
    private readonly FakeSipService _sip = new();
    private readonly FakeCountdownTimer _timer = new();
    private readonly List<string> _log = new();
    private int _shown;          // сколько раз показывали отсчёт
    private int _hidden;         // сколько раз прятали
    private int _lastSeconds, _lastAttempt;

    private RedialController New(bool enabled = true, string delay = "30")
    {
        _sip.Flags["auto_redial"] = enabled;
        _sip.Strings["auto_redial_delay"] = delay;
        return new RedialController(_sip, _timer,
            (sec, att) => { _shown++; _lastSeconds = sec; _lastAttempt = att; },
            () => _hidden++,
            m => _log.Add(m));
    }

    private static CallEvent Busy(string remote) =>
        new(LinphoneCallState.End, null, remote, IsIncoming: false, 0, LinphoneReason.Busy);

    [Fact]
    public void Busy_StartsCountdown_AndDialsWhenItExpires()
    {
        var c = New(delay: "5");

        c.OnCallTerminated(Busy("sip:378@pbx"));

        Assert.True(c.CountingDown);
        Assert.Equal(5, _lastSeconds);
        Assert.Equal(1, _lastAttempt);
        Assert.Empty(_sip.PlacedCalls);

        _timer.Advance(5);

        Assert.False(c.CountingDown);
        Assert.Single(_sip.PlacedCalls);
        Assert.Equal("sip:378@pbx", _sip.PlacedCalls[0]);
    }

    [Fact]
    public void NonBusyReason_DoesNothing()
    {
        var c = New();

        c.OnCallTerminated(new CallEvent(LinphoneCallState.End, null, "sip:378@pbx", false, 0,
                                         LinphoneReason.NotAnswered));

        Assert.False(c.CountingDown);
        Assert.Empty(_sip.PlacedCalls);
    }

    [Fact]
    public void Disabled_DoesNothing()
    {
        var c = New(enabled: false);

        c.OnCallTerminated(Busy("sip:378@pbx"));

        Assert.False(c.CountingDown);
    }

    [Fact]
    public void IncomingCall_IsNeverRedialed()
    {
        var c = New();

        c.OnCallTerminated(new CallEvent(LinphoneCallState.End, null, "sip:378@pbx",
                                         IsIncoming: true, 0, LinphoneReason.Busy));

        Assert.False(c.CountingDown);
    }

    // End и Released приходят друг за другом. Без защиты вторая попытка накручивалась бы
    // мгновенно, и счётчик показывал бы «попытка 2» через долю секунды после первой.
    [Fact]
    public void DuplicateTerminalEvents_DoNotBumpAttempt()
    {
        var c = New(delay: "10");

        c.OnCallTerminated(Busy("sip:378@pbx"));
        c.OnCallTerminated(Busy("sip:378@pbx"));

        Assert.Equal(1, _lastAttempt);
    }

    [Fact]
    public void SecondBusy_AfterCountdownExpired_IsAttemptTwo()
    {
        var c = New(delay: "5");

        c.OnCallTerminated(Busy("sip:378@pbx"));
        _timer.Advance(5);                       // отсчёт вышел, набрали
        c.OnCallTerminated(Busy("sip:378@pbx")); // снова занято

        Assert.Equal(2, _lastAttempt);
    }

    // Пользователь передумал и позвонил другому — цикл обязан прекратиться, иначе клиент
    // через полминуты сам наберёт номер, который человек уже не набирает.
    [Fact]
    public void OutgoingToDifferentNumber_CancelsTheCycle()
    {
        var c = New(delay: "30");
        c.OnCallTerminated(Busy("sip:378@pbx"));

        c.NoteOutgoing("sip:377@pbx");
        _timer.Advance(60);

        Assert.False(c.CountingDown);
        Assert.Empty(_sip.PlacedCalls);
    }

    [Fact]
    public void OutgoingToSameNumber_KeepsTargetForNextBusy()
    {
        var c = New(delay: "5");
        c.OnCallTerminated(Busy("sip:378@pbx"));

        c.NoteOutgoing("sip:378@pbx");            // это и есть наш же автодозвон
        c.OnCallTerminated(Busy("sip:378@pbx"));  // опять занято

        Assert.True(c.CountingDown);
        Assert.Equal(2, _lastAttempt);
    }

    // Номер приходит то как «378», то как «sip:378@host» — в зависимости от источника события.
    [Fact]
    public void TargetComparison_IgnoresAddressForm()
    {
        var c = New(delay: "5");
        c.OnCallTerminated(Busy("sip:378@pbx.example.com"));

        c.NoteOutgoing("378");

        Assert.False(_sip.PlacedCalls.Count > 0);
        c.OnCallTerminated(Busy("378"));
        Assert.Equal(2, _lastAttempt);   // тот же абонент, а не новый цикл
    }

    [Fact]
    public void Cancel_StopsEverything_AndHidesPanel()
    {
        var c = New(delay: "30");
        c.OnCallTerminated(Busy("sip:378@pbx"));
        int hiddenBefore = _hidden;

        c.Cancel();
        _timer.Advance(60);

        Assert.False(c.CountingDown);
        Assert.Empty(_sip.PlacedCalls);
        Assert.True(_hidden > hiddenBefore);
    }

    // Слишком короткая пауза превратила бы автодозвон в шторм на АТС.
    [Theory]
    [InlineData("1", 5)]
    [InlineData("0", 5)]
    [InlineData("мусор", 30)]
    [InlineData("45", 45)]
    public void Delay_IsClampedAndFallsBack(string configured, int expected)
    {
        var c = New(delay: configured);

        c.OnCallTerminated(Busy("sip:378@pbx"));

        Assert.Equal(expected, _lastSeconds);
    }

    [Fact]
    public void EmptyTarget_DoesNotStartCycle()
    {
        var c = New();

        c.OnCallTerminated(new CallEvent(LinphoneCallState.End, null, null, false, 0, LinphoneReason.Busy));

        Assert.False(c.CountingDown);
    }

    // Цель берётся из последнего исходящего, если её нет в событии завершения.
    [Fact]
    public void TargetFallsBackToLastOutgoing()
    {
        var c = New(delay: "5");
        c.NoteOutgoing("sip:378@pbx");

        c.OnCallTerminated(new CallEvent(LinphoneCallState.End, null, null, false, 0, LinphoneReason.Busy));
        _timer.Advance(5);

        Assert.Single(_sip.PlacedCalls);
        Assert.Equal("sip:378@pbx", _sip.PlacedCalls[0]);
    }

    // ------------------------------------------------------------------
    // Потолок попыток
    // ------------------------------------------------------------------
    // Без него цикл не заканчивается никогда: коллега ушёл на обед с трубкой на разговоре —
    // клиент набирает его каждые полминуты до конца рабочего дня. Со стороны АТС это сотни
    // INVITE от одного внутреннего за смену, то есть поведение, неотличимое от атаки.

    /// <summary>Один полный круг «занято → отсчёт → набор».</summary>
    private void Cycle(RedialController c, int delaySeconds, string target = "sip:378@pbx")
    {
        c.OnCallTerminated(Busy(target));
        _timer.Advance(delaySeconds);
    }

    [Fact]
    public void StopsAfterFiveAttempts_ByDefault()
    {
        var c = New(delay: "5");
        bool gaveUp = false;
        c.GaveUp += () => gaveUp = true;

        for (int i = 0; i < 5; i++) Cycle(c, 5);

        Assert.Equal(5, _sip.PlacedCalls.Count);   // ровно пять попыток
        Assert.False(gaveUp);

        // Шестое «занято» — сдаёмся, а не начинаем шестой круг.
        c.OnCallTerminated(Busy("sip:378@pbx"));

        Assert.False(c.CountingDown);
        Assert.True(gaveUp);                       // и человеку об этом говорим
        _timer.Advance(30);
        Assert.Equal(5, _sip.PlacedCalls.Count);   // больше ни одного набора
        Assert.Contains(_log, m => m.Contains("прекращаю"));
    }

    [Fact]
    public void MaxAttempts_IsConfigurable()
    {
        _sip.Strings["auto_redial_max"] = "2";
        var c = New(delay: "5");

        Cycle(c, 5);
        Cycle(c, 5);
        c.OnCallTerminated(Busy("sip:378@pbx"));

        Assert.Equal(2, _sip.PlacedCalls.Count);
        Assert.False(c.CountingDown);
    }

    [Fact]
    public void DelayStaysFixed_BetweenAttempts()
    {
        // Пауза НЕ растёт сознательно: растущая (30→60→120) точнее описывает вероятность
        // освободившейся линии, но пятая попытка приходила бы через восемь минут — а через
        // восемь минут человек уже позвонил сам. Автодозвон рассчитан на ближайшие пару минут.
        var c = New(delay: "30");

        c.OnCallTerminated(Busy("sip:378@pbx"));
        Assert.Equal(30, _lastSeconds);
        _timer.Advance(30);

        c.OnCallTerminated(Busy("sip:378@pbx"));
        Assert.Equal(30, _lastSeconds);   // и на второй попытке те же 30 с
        Assert.Equal(2, _lastAttempt);
    }

    [Fact]
    public void CallingAnotherNumber_ResetsAttemptCounter()
    {
        // Счётчик привязан к НОМЕРУ: набрав другого, пользователь начинает новую историю,
        // и потолок прошлой на неё не переносится.
        var c = New(delay: "5");
        for (int i = 0; i < 5; i++) Cycle(c, 5);

        c.NoteOutgoing("sip:999@pbx");      // передумал, звонит другому
        c.OnCallTerminated(Busy("sip:999@pbx"));

        Assert.True(c.CountingDown);
        Assert.Equal(1, _lastAttempt);
    }
}
