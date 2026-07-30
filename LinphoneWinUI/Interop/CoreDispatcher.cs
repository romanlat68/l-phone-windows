using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Однопоточный диспетчер для нативного ядра: выделенный поток исполняет задания из очереди
/// и между ними качает насос (<c>iterate</c>). Отделён от <see cref="LinphoneService"/>, чтобы
/// его дисциплину (таймауты, отказ после остановки, диагностика) можно было проверять тестами
/// без liblinphone.
///
/// Три вида вызовов, и это главное:
/// <list type="bullet">
/// <item><b>Post</b> — команда без результата. UI НЕ ждёт: возвращает управление немедленно.</item>
/// <item><b>Query</b> — нужен результат. Ждёт не дольше <see cref="Timeout"/>; по просрочке
/// возвращает fallback и сообщает в <see cref="Diagnostics"/> (раньше был бесконечный Wait —
/// поток ядра, застрявший в DNS/TLS внутри iterate, намертво вешал интерфейс).</item>
/// <item><b>Invoke</b> — команда, чей провал важен: по просрочке бросает <see cref="TimeoutException"/>.</item>
/// </list>
///
/// После остановки (или смерти) потока задания НЕ исполняются на вызывающем потоке: параллельный
/// вызов ядра из UI нарушил бы однопоточность liblinphone (прежний фолбэк именно это и делал).
/// </summary>
internal sealed class CoreDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Action _iterate;
    private readonly int _iterateIntervalMs;

    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _stopped;
    private Exception? _startError;

    /// <summary>Сколько ждать результата Query/Invoke, прежде чем сдаться.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Порог, после которого ожидание считается аномальным и попадает в диагностику.</summary>
    public TimeSpan SlowCallThreshold { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Сообщения о просрочках и медленных вызовах — уходят в лог приложения.</summary>
    public event Action<string>? Diagnostics;

    /// <param name="iterate">Насос ядра; вызывается ТОЛЬКО на потоке диспетчера.</param>
    /// <param name="iterateIntervalMs">Максимальная пауза между вызовами насоса.</param>
    public CoreDispatcher(Action iterate, int iterateIntervalMs)
    {
        _iterate = iterate;
        _iterateIntervalMs = iterateIntervalMs;
    }

    /// <summary>Поток жив и принимает задания.</summary>
    public bool IsRunning => _thread is { IsAlive: true } && !_stopRequested;

    /// <summary>Вызывающий код уже исполняется НА потоке ядра (реентерабельный вызов из колбэка).</summary>
    public bool OnDispatcherThread => Thread.CurrentThread == _thread;

    /// <summary>Запускает поток и ждёт готовности. init выполняется первым на потоке ядра;
    /// его исключение пробрасывается вызывающему (ошибка старта Core).</summary>
    public void Start(Action init, string threadName)
    {
        if (_thread != null) return;
        _thread = new Thread(() => Loop(init)) { IsBackground = true, Name = threadName };
        _thread.SetApartmentState(ApartmentState.MTA);   // mswasapi перечисляет устройства стабильнее
        _thread.Start();
        _started.Wait();
        if (_startError != null)
            throw new InvalidOperationException(_startError.Message, _startError);
    }

    private void Loop(Action init)
    {
        try { init(); }
        catch (Exception ex) { _startError = ex; }
        finally { _started.Set(); }
        if (_startError != null) { _stopped = true; return; }

        try
        {
            while (!_stopRequested)
            {
                // Ждём задание не дольше интервала насоса: вызовы из UI исполняются
                // немедленно, а ядро при этом не голодает.
                if (_work.TryTake(out var job, _iterateIntervalMs))
                {
                    Run(job);
                    while (!_stopRequested && _work.TryTake(out var more)) Run(more);
                }
                if (_stopRequested) break;
                _iterate();
            }
        }
        finally { _stopped = true; }

        void Run(Action job)
        {
            // Исключение одного задания не должно ронять поток ядра: Post-задания
            // некому передать ошибку, у Query/Invoke она уже перехвачена внутри.
            // Но и молчать нельзя: Post — основной путь команд (Call, Hangup, SetAppFlag…),
            // и раньше упавшее задание не оставляло ни следа — пользователь жал «Позвонить»,
            // не происходило ничего, в логе пусто.
            try { job(); }
            catch (Exception ex) { Diagnostics?.Invoke($"Задание ядра упало: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>Команда без результата: ставим в очередь и возвращаемся. UI не ждёт ядро.</summary>
    public void Post(Action action)
    {
        if (OnDispatcherThread) { action(); return; }
        if (!TryEnqueue(action)) Diagnostics?.Invoke("Ядро остановлено — команда отброшена");
    }

    /// <summary>Запрос результата. По просрочке возвращает fallback (UI не виснет) и жалуется в лог.</summary>
    public T Query<T>(Func<T> func, T fallback, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (OnDispatcherThread) return func();
        if (!IsRunning) return fallback;   // на чужом потоке ядро НЕ трогаем

        if (TryRun(func, out T result, out ExceptionDispatchInfo? error, caller))
        {
            error?.Throw();
            return result;
        }
        return fallback;
    }

    /// <summary>Команда, чей провал важен (например, сохранение аккаунта): по просрочке бросает.</summary>
    public void Invoke(Action action, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (OnDispatcherThread) { action(); return; }
        if (!IsRunning) throw new InvalidOperationException("Ядро SIP остановлено.");

        if (!TryRun<object?>(() => { action(); return null; }, out _, out ExceptionDispatchInfo? error, caller))
            throw new TimeoutException($"Ядро SIP не ответило за {Timeout.TotalSeconds:0.#} с ({caller}).");
        error?.Throw();
    }

    /// <summary>Ставит задание и ждёт его не дольше Timeout. false — просрочка.</summary>
    private bool TryRun<T>(Func<T> func, out T result, out ExceptionDispatchInfo? error, string caller)
    {
        T local = default!;
        ExceptionDispatchInfo? captured = null;
        // НЕ using: при просрочке задание ещё живёт в очереди и позже вызовет Set().
        // Освобождаем событие только когда точно знаем, что задание отработало.
        var done = new ManualResetEventSlim(false);
        // Просроченное задание НЕ должно исполниться позже: вызывающий уже получил fallback и
        // отрисовал состояние по нему. Раньше ToggleMute по таймауту возвращал false («микрофон
        // включён»), а задание срабатывало через секунду и реально выключало микрофон — UI врал.
        // Для Invoke то же самое ломало контракт «провалилось = не сделано».
        var cancelled = new StrongBox<int>(0);

        if (!TryEnqueue(() =>
            {
                if (Volatile.Read(ref cancelled.Value) != 0) return;
                try { local = func(); }
                catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
                finally { done.Set(); }
            }))
        {
            done.Dispose();
            result = default!;
            error = null;
            return false;
        }

        var sw = Stopwatch.StartNew();
        bool ok = done.Wait(Timeout);
        sw.Stop();

        if (!ok)
        {
            Volatile.Write(ref cancelled.Value, 1);   // ещё не начатое задание не выполнится
            // Задание отработает позже на потоке ядра — оно безвредно; событие не трогаем,
            // его соберёт GC (финализатора нет, дескриптор создаётся лениво только при
            // длинном ожидании — здесь как раз этот случай, поэтому просрочки логируем).
            Diagnostics?.Invoke($"Ядро SIP не ответило за {Timeout.TotalSeconds:0.#} с ({caller}) — вызов пропущен");
            result = default!;
            error = null;
            return false;
        }
        done.Dispose();

        if (sw.Elapsed > SlowCallThreshold)
            Diagnostics?.Invoke($"Медленный вызов ядра: {caller} — {sw.ElapsedMilliseconds} мс");

        result = local;
        error = captured;
        return true;
    }

    private bool TryEnqueue(Action job)
    {
        if (!IsRunning) return false;
        // Очередь могла закрыться (CompleteAdding) или быть освобождена между проверкой и Add.
        try { _work.Add(job); return true; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Останавливает поток: финальное задание исполняется на нём же, затем цикл выходит.
    /// Флаг остановки взводит САМО задание — иначе цикл мог выйти по флагу, не разобрав очередь
    /// (и, например, не финализировав запись разговора).</summary>
    public void Stop(Action final, TimeSpan joinTimeout)
    {
        if (_thread is not { IsAlive: true } t || OnDispatcherThread)
        {
            if (!_stopped) { try { final(); } catch { } }
            _stopRequested = true;
            _stopped = true;
            return;
        }

        _work.Add(() => { try { final(); } catch { } _stopRequested = true; });
        if (!t.Join(joinTimeout))
        {
            // Поток завис в нативном вызове. НЕ исполняем задания на своём потоке (это сломало бы
            // однопоточность ядра) — просто помечаем сервис мёртвым: дальнейшие Query отдадут
            // fallback, Post/Invoke сообщат об остановке.
            _stopRequested = true;
            Diagnostics?.Invoke("Поток ядра SIP не завершился за отведённое время — вызовы отклоняются");
        }
    }

    public void Dispose()
    {
        _stopRequested = true;
        try { _work.CompleteAdding(); } catch { }
        // Если поток ядра завис в нативном вызове и не завершился по Join, он ЖИВ и, вернувшись,
        // пойдёт в _work.TryTake. На освобождённой коллекции это ObjectDisposedException на
        // фоновом потоке — то есть падение процесса на выходе. Утечка одной очереди при закрытии
        // приложения дешевле краха, поэтому Dispose только когда поток точно мёртв.
        if (_thread is not { IsAlive: true }) _work.Dispose();
        _started.Dispose();
    }
}
