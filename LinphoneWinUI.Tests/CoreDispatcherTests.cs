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

    // --- TryQuery: «ядро ответило» против «вернулся fallback» ---
    // Из-за неразличимости этих случаев значение по умолчанию, прочитанное до старта ядра,
    // оседало в кэше настроек навсегда и подменяло собой конфиг (выключенные горячие клавиши
    // регистрировались снова при каждом запуске).

    [Fact]
    public void TryQuery_BeforeStart_ReportsNotAnswered_AndDoesNotRunJob()
    {
        using var d = NewDispatcher();   // Start НЕ вызывали

        bool executed = false;
        bool answered = d.TryQuery(() => { executed = true; return 42; }, fallback: -7, out int value);

        Assert.False(answered);          // ключевое: вызывающий видит, что это подстановка
        Assert.Equal(-7, value);
        Assert.False(executed);
    }

    [Fact]
    public void TryQuery_WhenRunning_ReportsAnswered()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");

        bool answered = d.TryQuery(() => 42, fallback: -7, out int value);

        Assert.True(answered);
        Assert.Equal(42, value);
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TryQuery_AfterStop_ReportsNotAnswered()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        d.Stop(() => { }, TimeSpan.FromSeconds(1));

        Assert.False(d.TryQuery(() => 42, fallback: -7, out int value));
        Assert.Equal(-7, value);
    }

    [Fact]
    public void TryQuery_OnTimeout_ReportsNotAnswered()
    {
        var stuck = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        using var d = NewDispatcher(iterate: () => { entered.Set(); stuck.Wait(TimeSpan.FromSeconds(5)); },
                                    timeout: TimeSpan.FromMilliseconds(150));
        d.Start(() => { }, "test-core");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));   // см. Query_Timeout: без этого тест флаки

        bool answered = d.TryQuery(() => 42, fallback: -7, out int value);

        Assert.False(answered);   // просрочка — тоже «не ответило», кэшировать нельзя
        Assert.Equal(-7, value);
        stuck.Set();
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

    // --- Счётчики здоровья за сеанс ---
    // Отдельные строки о медленных вызовах в логе были и раньше, но ответить по ним на вопрос
    // «часто ли у этого пользователя подмерзает интерфейс» нельзя — их надо искать глазами
    // среди тысяч других.

    [Fact]
    public void Counters_StartAtZero()
    {
        using var d = NewDispatcher();

        Assert.Equal(0, d.SlowCalls);
        Assert.Equal(0, d.Timeouts);
        Assert.Equal(0, d.DroppedCommands);
        Assert.Equal(0, d.MaxCallMs);
    }

    [Fact]
    public void Timeout_IsCounted()
    {
        var stuck = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        using var d = NewDispatcher(iterate: () => { entered.Set(); stuck.Wait(TimeSpan.FromSeconds(5)); },
                                    timeout: TimeSpan.FromMilliseconds(150));
        d.Start(() => { }, "test-core");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        d.Query(() => 42, fallback: -1);

        Assert.Equal(1, d.Timeouts);
        Assert.Contains("просрочек", d.HealthSummary());
        stuck.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DroppedCommand_IsCounted()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");
        d.Stop(() => { }, TimeSpan.FromSeconds(1));

        d.Post(() => { });

        Assert.Equal(1, d.DroppedCommands);
    }

    [Fact]
    public void SlowCall_IsCountedAndBounded()
    {
        using var d = new CoreDispatcher(() => { }, iterateIntervalMs: 5)
        {
            Timeout = TimeSpan.FromSeconds(2),
            SlowCallThreshold = TimeSpan.FromMilliseconds(20),
        };
        d.Start(() => { }, "test-core");

        d.Query(() => { Thread.Sleep(80); return 1; }, 0);

        Assert.Equal(1, d.SlowCalls);
        Assert.True(d.MaxCallMs >= 60, $"самое долгое ожидание не записано: {d.MaxCallMs} мс");
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    // --- Размыкатель цепи ---
    // Когда ядро зависло, КАЖДЫЙ запрос честно ждал свой таймаут. Открытие вкладки устройств —
    // это десяток запросов подряд, то есть под минуту неподвижного интерфейса на ровном месте.

    [Fact]
    public void AfterSeveralTimeouts_QueriesReturnImmediately()
    {
        var stuck = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        using var d = new CoreDispatcher(() => { entered.Set(); stuck.Wait(TimeSpan.FromSeconds(10)); }, 5)
        {
            Timeout = TimeSpan.FromMilliseconds(120),
            DegradeAfterTimeouts = 2,
            DegradedFor = TimeSpan.FromSeconds(30),
        };
        d.Start(() => { }, "test-core");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        d.Query(() => 1, -1);   // просрочка 1
        d.Query(() => 1, -1);   // просрочка 2 — размыкаем
        Assert.True(d.Degraded);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int v = d.Query(() => 1, -1);
        sw.Stop();

        Assert.Equal(-1, v);
        // Ключевое: третий запрос НЕ ждал таймаут, а вернулся сразу.
        Assert.True(sw.ElapsedMilliseconds < 50, $"запрос ждал {sw.ElapsedMilliseconds} мс вместо мгновенного отказа");
        Assert.Contains("не отвечает", d.HealthSummary());

        stuck.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Degraded_ClearsAfterCooldown_AndCoreAnswersAgain()
    {
        var stuck = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        using var d = new CoreDispatcher(() => { entered.Set(); stuck.Wait(TimeSpan.FromMilliseconds(400)); }, 5)
        {
            Timeout = TimeSpan.FromMilliseconds(120),
            DegradeAfterTimeouts = 1,
            DegradedFor = TimeSpan.FromMilliseconds(150),   // короткая пауза — тест не ждёт долго
        };
        d.Start(() => { }, "test-core");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        d.Query(() => 1, -1);
        Assert.True(d.Degraded);

        stuck.Set();                 // ядро ожило
        Thread.Sleep(250);           // пауза размыкания истекла
        Assert.False(d.Degraded);

        Assert.Equal(7, d.Query(() => 7, -1));   // и запросы снова доходят
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void HealthySeries_NeverDegrades()
    {
        using var d = NewDispatcher();
        d.Start(() => { }, "test-core");

        for (int i = 0; i < 20; i++) Assert.Equal(i, d.Query(() => i, -1));

        Assert.False(d.Degraded);
        Assert.Equal(0, d.Timeouts);
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    // ------------------------------------------------------------------
    // Ожидание готовности ядра: опрос ЧЕРЕЗ ЯДРО против опроса ЧЕРЕЗ ДИСПЕТЧЕР
    // ------------------------------------------------------------------
    // Окно, не дождавшееся ядра за StartTimeout, показывается без него и ждёт его в фоне,
    // опрашивая раз в секунду. Вопрос — что именно опрашивать. Тесты ниже ставят оба варианта
    // на один стенд: init занят, поток ядра ЖИВ и занят им же.

    /// <summary>Стенд: инициализация ядра идёт долго (медленный диск, ACL, sqlite, антивирус),
    /// Start сдался по таймауту. Именно в этот момент интерфейс и начинает опрашивать готовность.</summary>
    private static CoreDispatcher StartWithSlowInit(ManualResetEventSlim releaseInit, out bool startedInTime)
    {
        var d = new CoreDispatcher(() => { }, iterateIntervalMs: 5)
        {
            Timeout = TimeSpan.FromMilliseconds(300),        // как боевые 5 с, только короче
            StartTimeout = TimeSpan.FromMilliseconds(100),   // как боевые 20 с, только короче
        };
        startedInTime = d.Start(() => releaseInit.Wait(TimeSpan.FromSeconds(10)), "test-core");
        return d;
    }

    // Замер, ради которого эти тесты писались (до правки, таймаут стенда 300 мс):
    //   вариант А, Query к ядру      — 250–300 мс на ОДИН опрос, +1 к счётчику просрочек;
    //   вариант Б, флаг диспетчера   — <1 мс на ДЕСЯТЬ опросов, просрочек ноль.
    // В боевых числах (таймаут 5 с, опрос раз в секунду) вариант А означал замирание окна на
    // 5 секунд каждый тик. Выбран вариант Б, а само короткое замыкание перенесено внутрь
    // TryQuery — тогда оно защищает не только опрос готовности, но и десяток чтений настроек,
    // которые конструктор окна делает подряд на неподнятом ядре.

    [Fact]
    public void QueryDuringInit_ReturnsImmediately_WithoutBurningTimeout()
    {
        // Поток ядра ЖИВ (он внутри init), поэтому проверка IsRunning вызов не отсекает.
        // Отсечь обязан признак «инициализация не закончена».
        var release = new ManualResetEventSlim(false);
        using var d = StartWithSlowInit(release, out bool startedInTime);
        Assert.False(startedInTime);   // ядро не успело — окно открывается без него

        bool executed = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool value = d.Query(() => { executed = true; return true; }, fallback: false);
        sw.Stop();

        Assert.False(value);
        Assert.False(executed);   // задание даже не ставилось в очередь
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"вызов ждал {sw.ElapsedMilliseconds} мс — интерфейс это чувствует");
        // И, что не менее важно, это НЕ просрочка: иначе три чтения настроек подряд размыкали
        // бы цепь на ровном месте и ядро объявлялось бы больным ещё до своего старта.
        Assert.Equal(0, d.Timeouts);
        Assert.False(d.Degraded);

        release.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void InvokeDuringInit_Throws_InsteadOfWaiting()
    {
        var release = new ManualResetEventSlim(false);
        using var d = StartWithSlowInit(release, out _);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidOperationException>(() => d.Invoke(() => { }));
        sw.Stop();

        Assert.Contains("запускается", ex.Message);   // именно «ещё запускается», а не «остановлено»
        Assert.True(sw.ElapsedMilliseconds < 50, $"Invoke ждал {sw.ElapsedMilliseconds} мс");

        release.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WaitingForCore_ViaDispatcherFlag_ReturnsImmediately()
    {
        // Опрос готовности, как его делает таймер ожидания в окне.
        var release = new ManualResetEventSlim(false);
        using var d = StartWithSlowInit(release, out bool startedInTime);
        Assert.False(startedInTime);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) Assert.False(d.CoreReady);   // десять тиков
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50,
            $"десять опросов заняли {sw.ElapsedMilliseconds} мс — это уже заметно интерфейсу");
        Assert.Equal(0, d.Timeouts);
        Assert.False(d.Degraded);

        release.Set();
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CoreReady_BecomesTrue_WhenInitFinishes()
    {
        var release = new ManualResetEventSlim(false);
        using var d = StartWithSlowInit(release, out _);
        Assert.False(d.CoreReady);
        Assert.False(d.StartCompleted);

        release.Set();

        // Опрос флага (как это делает таймер ожидания в окне) дожидается готовности.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!d.CoreReady && DateTime.UtcNow < deadline) Thread.Sleep(10);

        Assert.True(d.CoreReady);
        Assert.True(d.StartCompleted);
        Assert.False(d.StartFailed);
        d.Stop(() => { }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StartFailed_DistinguishesCrashFromStillStarting()
    {
        // «Ещё поднимается» и «упало при старте» — разные состояния: в первом надо ждать,
        // во втором показать отказ. Прежний IsStarted == false их склеивал.
        using var d = NewDispatcher();
        Assert.False(d.StartCompleted);
        Assert.False(d.StartFailed);

        Assert.Throws<InvalidOperationException>(
            () => d.Start(() => throw new InvalidOperationException("boom"), "test-core"));

        Assert.True(d.StartCompleted);
        Assert.True(d.StartFailed);
        Assert.False(d.CoreReady);
    }

    [Fact]
    public void Dispose_WhileInitStillRunning_DoesNotKillCoreThread()
    {
        // Пользователь закрыл окно, которое ему всё равно не отвечает, пока init не закончился.
        // Раньше Dispose освобождал _started, а поток ядра затем звал Set() из блока finally —
        // ObjectDisposedException уходил наружу и убивал процесс.
        var release = new ManualResetEventSlim(false);
        var d = StartWithSlowInit(release, out _);

        Exception? onCoreThread = null;
        AppDomain.CurrentDomain.UnhandledException += Capture;
        try
        {
            d.Dispose();      // поток ЖИВ и всё ещё в init
            release.Set();    // init заканчивается и идёт в _started.Set()
            Thread.Sleep(200);
            Assert.Null(onCoreThread);
            Assert.True(d.StartCompleted);   // и старт всё-таки зафиксирован
        }
        finally { AppDomain.CurrentDomain.UnhandledException -= Capture; }

        void Capture(object? s, UnhandledExceptionEventArgs e) => onCoreThread = e.ExceptionObject as Exception;
    }
}
