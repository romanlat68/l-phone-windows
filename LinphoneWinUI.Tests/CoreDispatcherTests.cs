using System;
using System.Threading;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Дисциплина однопоточного диспетчера ядра: таймауты, отказ после остановки,
/// исполнение заданий строго на своём потоке. Без liblinphone — насос подставной.</summary>
public class CoreDispatcherTests
{
    private static CoreDispatcher NewDispatcher(Action? iterate = null, TimeSpan? timeout = null) =>
        new(iterate ?? (() => { }), iterateIntervalMs: 5) { Timeout = timeout ?? TimeSpan.FromSeconds(2) };

    [Fact]
    public void Query_RunsOnDispatcherThread_AndReturnsValue()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        int callerThread = Environment.CurrentManagedThreadId;

        int jobThread = d.Query(() => Environment.CurrentManagedThreadId, -1);

        Assert.NotEqual(-1, jobThread);
        Assert.NotEqual(callerThread, jobThread);   // ядро трогает только свой поток
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Post_DoesNotBlockCaller_AndStillRuns()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        var ran = new ManualResetEventSlim(false);

        d.Post(() => { Thread.Sleep(50); ran.Set(); });

        Assert.True(ran.Wait(TimeSpan.FromSeconds(2)));
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Query_Timeout_ReturnsFallback_AndReportsDiagnostics()
    {
        // Насос «залип» (как liblinphone в DNS/TLS внутри iterate) — раньше UI ждал вечно.
        var stuck = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        using var d = NewDispatcher(iterate: () => { entered.Set(); stuck.Wait(TimeSpan.FromSeconds(5)); },
                                    timeout: TimeSpan.FromMilliseconds(150));
        string? diag = null;
        d.Diagnostics += m => diag ??= m;
        d.Start(() => { }, "test-core");
        // Ждём ФАКТ входа в залипший насос, а не «примерно столько же миллисекунд»: на нагруженном
        // раннере CI поток не успевал туда войти, задание подхватывалось сразу и таймаута не было.
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        int value = d.Query(() => 42, fallback: -7);

        Assert.Equal(-7, value);
        Assert.NotNull(diag);
        Assert.Contains("не ответило", diag);
        stuck.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Invoke_Timeout_Throws()
    {
        var stuck = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        using var d = NewDispatcher(iterate: () => { entered.Set(); stuck.Wait(TimeSpan.FromSeconds(5)); },
                                    timeout: TimeSpan.FromMilliseconds(150));
        d.Start(() => { }, "test-core");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));   // см. Query_Timeout: без этого тест флаки

        Assert.Throws<TimeoutException>(() => d.Invoke(() => { }));
        stuck.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AfterStop_QueryReturnsFallback_AndDoesNotRunOnCallerThread()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        d.Stop(() => { }, TimeSpan.FromSeconds(1));

        bool executed = false;
        int value = d.Query(() => { executed = true; return 5; }, fallback: -1);

        Assert.Equal(-1, value);
        Assert.False(executed);   // ключевое: НЕ исполняем код ядра на чужом потоке
    }

    [Fact]
    public void AfterStop_InvokeThrows_AndPostIsDropped()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        d.Stop(() => { }, TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(() => d.Invoke(() => { }));

        bool ran = false;
        d.Post(() => ran = true);
        Thread.Sleep(30);
        Assert.False(ran);
    }

    [Fact]
    public void Stop_RunsFinalJobOnDispatcherThread()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        int finalThread = -1;

        d.Stop(() => finalThread = Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(2));

        Assert.NotEqual(-1, finalThread);   // финализация (стоп записи, core stop) выполнилась
        Assert.NotEqual(Environment.CurrentManagedThreadId, finalThread);
    }

    [Fact]
    public void Query_PropagatesException()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");

        Assert.Throws<InvalidOperationException>(() =>
            d.Query<int>(() => throw new InvalidOperationException("boom"), 0));

        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ReentrantCall_FromDispatcherThread_RunsInline()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");

        // Событие ядра → подписчик → снова фасад: не должно самоблокироваться.
        int result = d.Query(() => d.Query(() => 11, 0), 0);

        Assert.Equal(11, result);
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StartError_IsPropagated()
    {
        using var d = NewDispatcher();
        var ex = Assert.Throws<InvalidOperationException>(
            () => d.Start(() => throw new InvalidOperationException("core start failed"), "test-core"));
        Assert.Contains("core start failed", ex.Message);
    }
}
