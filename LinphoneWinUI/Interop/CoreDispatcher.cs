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
    private volatile bool _startCompleted;
    private Exception? _startError;

    /// <summary>Сколько ждать результата Query/Invoke, прежде чем сдаться.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Порог, после которого ожидание считается аномальным и попадает в диагностику.</summary>
    public TimeSpan SlowCallThreshold { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Сколько просрочек подряд означает «ядро больное».</summary>
    public int DegradeAfterTimeouts { get; init; } = 3;

    /// <summary>Как долго после этого не ждать ядро вовсе, а сразу отдавать fallback.</summary>
    public TimeSpan DegradedFor { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Сообщения о просрочках и медленных вызовах — уходят в лог приложения.</summary>
    public event Action<string>? Diagnostics;

    // --- Счётчики здоровья за сеанс ---
    // Отдельные строки о медленных вызовах в логе есть, но по ним нельзя ответить на вопрос
    // «часто ли у этого пользователя подмерзает интерфейс» — их надо считать глазами среди
    // тысяч других. Сводка за сеанс отвечает на него одной строкой.
    private int _slowCalls, _timeouts, _dropped, _maxCallMs;

    // --- Размыкатель цепи ---
    // Когда ядро зависло, КАЖДЫЙ запрос честно ждал свои 5 секунд. Одно открытие вкладки
    // «Звонки и устройства» — это десяток запросов подряд, то есть под минуту неподвижного
    // интерфейса на ровном месте. После нескольких просрочек подряд перестаём ждать вовсе и
    // сразу отдаём значение по умолчанию; через DegradedFor снова пробуем.
    private int _consecutiveTimeouts;
    private long _degradedUntilTicks;
    private int _degradedAnnounced;

    /// <summary>Ядро признано неотвечающим: запросы возвращают fallback немедленно.</summary>
    public bool Degraded => Volatile.Read(ref _degradedUntilTicks) > DateTime.UtcNow.Ticks;

    /// <summary>Сколько вызовов ядра превысили <see cref="SlowCallThreshold"/> за сеанс.</summary>
    public int SlowCalls => Volatile.Read(ref _slowCalls);
    /// <summary>Сколько вызовов не дождались ответа за <see cref="Timeout"/>.</summary>
    public int Timeouts => Volatile.Read(ref _timeouts);
    /// <summary>Сколько команд отброшено из-за остановленного ядра.</summary>
    public int DroppedCommands => Volatile.Read(ref _dropped);
    /// <summary>Самое долгое ожидание ядра за сеанс, мс.</summary>
    public int MaxCallMs => Volatile.Read(ref _maxCallMs);

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

    /// <summary>
    /// init() отработал — успешно или с ошибкой. Читается БЕЗ обращения к ядру и без ожидания.
    ///
    /// Ради этого свойство и существует. Спросить о готовности у самого ядра
    /// (<c>Query(() =&gt; core.IsStarted)</c>) нельзя: пока идёт инициализация, поток ЖИВ — он внутри
    /// init(), — поэтому проверка <see cref="IsRunning"/> вызов не отсекает, задание уходит в
    /// очередь, разобрать которую некому, и вызывающий честно ждёт свой <see cref="Timeout"/>.
    /// Для интерфейса, который опрашивает готовность раз в секунду, это означало замирание
    /// на 5 секунд каждый тик — ровно на тех медленных машинах, ради которых вводился
    /// <see cref="StartTimeout"/>.
    /// </summary>
    public bool StartCompleted => _startCompleted;

    /// <summary>Инициализация ядра провалилась. Отличать это от «ещё поднимается» обязательно:
    /// в первом случае надо показать отказ, во втором — продолжать ждать.</summary>
    public bool StartFailed => _startCompleted && _startError != null;

    /// <summary>Ядро поднято, живо и принимает задания. Чтение трёх полей, без ожидания.</summary>
    public bool CoreReady => _startCompleted && _startError == null && IsRunning;

    /// <summary>Сколько ждать готовности ядра при старте, прежде чем отпустить интерфейс.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Запускает поток и ждёт готовности. init выполняется первым на потоке ядра;
    /// его исключение пробрасывается вызывающему (ошибка старта Core).
    ///
    /// Ожидание ОГРАНИЧЕНО по времени, и это важно: инициализация ядра лезет в файловую
    /// систему (создание каталога профиля, ACL, миграция, sqlite-базы), а на доменной машине
    /// с медленной службой безопасности или на подтормаживающем диске каждый из этих шагов
    /// может занять секунды. Бесконечное ожидание здесь означало окно, которое никогда не
    /// появится и которое нельзя даже закрыть — оно ещё не качает сообщения. Лучше показать
    /// интерфейс без ядра: команды дождутся своей очереди, запросы вернут значения
    /// по умолчанию, а пользователь увидит, что происходит.
    /// </summary>
    /// <returns>false — ядро не успело подняться за <see cref="StartTimeout"/>.</returns>
    public bool Start(Action init, string threadName)
    {
        if (_thread != null) return true;
        _thread = new Thread(() => Loop(init)) { IsBackground = true, Name = threadName };
        _thread.SetApartmentState(ApartmentState.MTA);   // mswasapi перечисляет устройства стабильнее
        _thread.Start();
        bool ready = _started.Wait(StartTimeout);
        if (!ready)
        {
            Diagnostics?.Invoke($"Ядро SIP не поднялось за {StartTimeout.TotalSeconds:0} с — "
                                + "интерфейс открывается без него, регистрация появится позже");
            return false;
        }
        if (_startError != null)
            throw new InvalidOperationException(_startError.Message, _startError);
        return true;
    }

    private void Loop(Action init)
    {
        try { init(); }
        catch (Exception ex) { _startError = ex; }
        // Флаг взводим ДО события: тот, кто проснулся по _started, обязан увидеть согласованное
        // состояние, а тот, кто опрашивает StartCompleted без ожидания, — не пропустить момент.
        finally
        {
            _startCompleted = true;
            // Событие могли освободить, пока мы сидели в init (выход из приложения при медленном
            // старте). Ронять на этом поток ядра нельзя: исключение из finally уходит наружу и
            // убивает процесс — см. Dispose.
            try { _started.Set(); } catch (ObjectDisposedException) { }
        }
        if (_startError != null)
        {
            _stopped = true;
            // Start мог уже перестать ждать (см. StartTimeout) — тогда пробросить исключение
            // некому, и без этой строки отказ ядра не оставил бы в журнале ни следа.
            Diagnostics?.Invoke("Ядро SIP не запустилось: " + _startError.Message);
            return;
        }

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
        if (!TryEnqueue(action))
        {
            Interlocked.Increment(ref _dropped);
            Diagnostics?.Invoke("Ядро остановлено — команда отброшена");
        }
    }

    /// <summary>Запрос результата. По просрочке возвращает fallback (UI не виснет) и жалуется в лог.</summary>
    public T Query<T>(Func<T> func, T fallback, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        TryQuery(func, fallback, out T value, caller);
        return value;
    }

    /// <summary>Как <see cref="Query{T}"/>, но РАЗЛИЧАЕТ «ядро ответило» и «вернулся fallback».
    ///
    /// Разница не косметическая. Вызывающий, который кэширует ответ (см. кэш настроек в
    /// <c>LinphoneService</c>), обязан знать, настоящее это значение или подстановка: иначе
    /// значение по умолчанию, полученное до старта ядра, оседает в кэше НАВСЕГДА и подменяет
    /// собой то, что реально записано в конфиге. Так выключенные пользователем глобальные
    /// горячие клавиши регистрировались снова при каждом запуске, а тумблер в настройках
    /// показывал «включено» — читая тот же отравленный кэш.</summary>
    /// <returns>false — ядро не поднято, остановлено или не ответило за <see cref="Timeout"/>.</returns>
    public bool TryQuery<T>(Func<T> func, T fallback, out T value,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (OnDispatcherThread) { value = func(); return true; }
        if (!IsRunning) { value = fallback; return false; }   // на чужом потоке ядро НЕ трогаем
        // Ядро ещё инициализируется ЛИБО не поднялось вовсе. Проверка IsRunning ни того, ни
        // другого не ловит: пока идёт init, поток жив и именно поэтому не разбирает очередь;
        // а сразу после провала init поток ещё несколько мгновений жив, доходя до выхода из
        // цикла. В обоих случаях ждать нечего — вызывающий (поток интерфейса) простоял бы
        // весь Timeout и получил тот же fallback. Конструктор окна читает так десяток
        // настроек подряд: это были десятки секунд неподвижного интерфейса.
        if (!_startCompleted || _startError != null) { value = fallback; return false; }
        // Ядро уже признано неотвечающим — не ждём его снова, интерфейс важнее.
        if (Degraded) { value = fallback; return false; }

        if (TryRun(func, out T result, out ExceptionDispatchInfo? error, caller))
        {
            error?.Throw();
            value = result;
            return true;
        }
        value = fallback;
        return false;
    }

    /// <summary>Команда, чей провал важен (например, сохранение аккаунта): по просрочке бросает.</summary>
    public void Invoke(Action action, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (OnDispatcherThread) { action(); return; }
        if (!IsRunning) throw new InvalidOperationException("Ядро SIP остановлено.");
        // См. TryQuery: ждать неинициализированное ядро — это весь таймаут впустую. Провал
        // критичной команды вызывающий обязан увидеть, поэтому здесь исключение, а не тишина.
        if (_startError != null) throw new InvalidOperationException("Ядро SIP не запустилось.");
        if (!_startCompleted) throw new InvalidOperationException("Ядро SIP ещё запускается.");

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
            Interlocked.Increment(ref _timeouts);
            NoteTimeout();
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
        NoteSuccess();

        int elapsedMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue);
        InterlockedMax(ref _maxCallMs, elapsedMs);
        if (sw.Elapsed > SlowCallThreshold)
        {
            Interlocked.Increment(ref _slowCalls);
            Diagnostics?.Invoke($"Медленный вызов ядра: {caller} — {elapsedMs} мс");
        }

        result = local;
        error = captured;
        return true;
    }

    /// <summary>Просрочка. Несколько подряд — размыкаем цепь на <see cref="DegradedFor"/>.</summary>
    private void NoteTimeout()
    {
        if (Interlocked.Increment(ref _consecutiveTimeouts) < DegradeAfterTimeouts) return;
        Volatile.Write(ref _degradedUntilTicks, DateTime.UtcNow.Add(DegradedFor).Ticks);
        // Сообщаем ОДИН раз на период: иначе жалоба сама превратится в шторм в журнале.
        if (Interlocked.Exchange(ref _degradedAnnounced, 1) == 0)
            Diagnostics?.Invoke($"Ядро SIP не отвечает — запросы возвращают значения по умолчанию "
                                + $"следующие {DegradedFor.TotalSeconds:0} с");
    }

    /// <summary>Ядро ответило — цепь замкнута обратно.</summary>
    private void NoteSuccess()
    {
        if (Interlocked.Exchange(ref _consecutiveTimeouts, 0) == 0 && !Degraded) return;
        Volatile.Write(ref _degradedUntilTicks, 0);
        if (Interlocked.Exchange(ref _degradedAnnounced, 0) == 1)
            Diagnostics?.Invoke("Ядро SIP снова отвечает");
    }

    /// <summary>Атомарный максимум: счётчики обновляются с UI-потока и с потока ядра.</summary>
    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
    }

    /// <summary>Здоровье ядра за сеанс одной строкой — для хвоста app.log при выходе.
    /// «Ноль медленных вызовов» это ответ на жалобу «подвисает», которого раньше не было.</summary>
    public string HealthSummary() =>
        $"вызовов медленнее {SlowCallThreshold.TotalMilliseconds:0} мс: {SlowCalls}; " +
        $"просрочек ({Timeout.TotalSeconds:0.#} с): {Timeouts}; " +
        $"отброшено команд: {DroppedCommands}; " +
        $"самое долгое ожидание: {MaxCallMs} мс" +
        (Degraded ? "; ядро СЕЙЧАС не отвечает" : "");

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
        // пойдёт в _work.TryTake, а закончив init — в _started.Set(). На освобождённых объектах
        // это ObjectDisposedException на фоновом потоке, причём для _started — из блока finally,
        // откуда исключение уходит наружу и убивает процесс. Сценарий не выдуманный: Start сдался
        // по StartTimeout, пользователь закрыл окно, которое ему всё равно не отвечает, а init
        // доработал секундой позже.
        // Утечка двух дескрипторов при закрытии приложения дешевле краха, поэтому освобождаем
        // ОБА объекта только когда поток точно мёртв.
        if (_thread is not { IsAlive: true })
        {
            _work.Dispose();
            _started.Dispose();
        }
    }
}
