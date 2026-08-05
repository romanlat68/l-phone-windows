using System;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI;

/// <summary>Отсчёт секунд для автодозвона. Абстракция нужна ровно затем, чтобы автомат
/// можно было проверить тестами: настоящий таймер тикает раз в секунду, и ждать в тестах
/// реальные минуты недопустимо.</summary>
internal interface ICountdownTimer
{
    void Start(TimeSpan interval, Action onTick);
    void Stop();
}

/// <summary>
/// Автодозвон при «занято»: если исходящий сорвался с Busy и режим включён, ведём обратный
/// отсчёт и набираем снова.
///
/// Вынесено из MainWindow: это конечный автомат с четырьмя условиями выхода (успех, отмена
/// пользователем, звонок на другой номер, входящий звонок), и внутри окна он был перемешан с
/// видимостью панелей — то есть не читался отдельно и не проверялся вовсе. Теперь UI получает
/// два колбэка, а логика живёт сама по себе.
/// </summary>
internal sealed class RedialController
{
    private const int MinDelaySeconds = 5;
    private const int DefaultDelaySeconds = 30;

    /// <summary>Сколько раз перезванивать, прежде чем сдаться.
    ///
    /// Потолок обязателен. Без него цикл не заканчивается НИКОГДА: коллега ушёл на обед,
    /// оставив трубку на разговоре, — клиент набирает его каждые полминуты до конца рабочего
    /// дня. За смену это сотни INVITE от одного внутреннего, мусор в CDR и трафик, который
    /// на стороне АТС выглядит как атака.
    ///
    /// Паузу между попытками СОЗНАТЕЛЬНО не растим. Растущая пауза (30 → 60 → 120 …) точнее
    /// описывает вероятность освободившейся линии, но пятая попытка приходит через восемь
    /// минут — а через восемь минут человек уже позвонил сам или забыл, зачем звонил.
    /// Автодозвон нужен, чтобы не следить за линией ближайшие пару минут, и ровно на этот
    /// срок он и рассчитан: пять попыток по 30 секунд — это две с половиной минуты.</summary>
    private const int DefaultMaxAttempts = 5;

    private readonly ISipService _sip;
    private readonly ICountdownTimer _timer;
    private readonly Action<int, int> _showCountdown;   // (осталось секунд, номер попытки)
    private readonly Action _hide;
    private readonly Action<string> _log;

    private string? _target;             // кому дозваниваемся (полный адрес)
    private string? _lastOutgoing;       // адрес последнего исходящего — цель, если её нет в событии
    private int _secondsLeft;
    private int _attempt;

    /// <summary>Идёт обратный отсчёт (панель показана).</summary>
    public bool CountingDown { get; private set; }

    public RedialController(ISipService sip, ICountdownTimer timer,
                            Action<int, int> showCountdown, Action hide, Action<string> log)
    {
        _sip = sip;
        _timer = timer;
        _showCountdown = showCountdown;
        _hide = hide;
        _log = log;
    }

    /// <summary>Пошёл исходящий звонок. Запоминаем цель; звонок на ДРУГОЙ номер означает,
    /// что пользователь передумал — цикл сбрасываем целиком.</summary>
    public void NoteOutgoing(string? remote)
    {
        if (string.IsNullOrEmpty(remote)) return;
        _lastOutgoing = remote;

        _timer.Stop();
        CountingDown = false;
        _hide();

        if (_target != null && !SameTarget(remote, _target)) Cancel();
    }

    /// <summary>Звонок завершился. Решает, начинать ли отсчёт до повторного набора.</summary>
    public void OnCallTerminated(CallEvent e)
    {
        if (e.IsIncoming) return;
        if (e.Reason is not (LinphoneReason.Busy or LinphoneReason.DoNotDisturb)) return;
        if (!_sip.GetAppFlag("auto_redial", false)) return;

        string target = e.Remote ?? _lastOutgoing ?? "";
        if (target.Length == 0) return;

        if (_target != null && SameTarget(_target, target))
        {
            // End и Released приходят друг за другом: пока отсчёт по этому номеру идёт,
            // второй запуск попытку не накручивает.
            if (CountingDown) return;
            _attempt++;
        }
        else
        {
            _target = target;
            _attempt = 1;
        }

        // Попытки кончились — сдаёмся и говорим об этом. Молча прекратить нельзя: человек
        // видел отсчёт и вправе знать, что телефон перестал набирать, а не «забыл».
        int maxAttempts = ReadInt("auto_redial_max", DefaultMaxAttempts, min: 1, max: 50);
        if (_attempt > maxAttempts)
        {
            _log($"Автодозвон: {maxAttempts} попыток подряд — номер занят, прекращаю");
            Cancel();
            GaveUp?.Invoke();
            return;
        }

        int delay = ReadInt("auto_redial_delay", DefaultDelaySeconds, min: MinDelaySeconds, max: 600);

        _secondsLeft = delay;
        CountingDown = true;
        _showCountdown(_secondsLeft, _attempt);
        _timer.Start(TimeSpan.FromSeconds(1), Tick);
        _log($"Автодозвон: занято, попытка {_attempt} из {maxAttempts}, повтор через {delay} с");
    }

    /// <summary>Число из настроек с границами. TryParse при неудаче ОБНУЛЯЕТ out-переменную,
    /// затирая значение по умолчанию, — поэтому дефолт восстанавливаем явно.</summary>
    private int ReadInt(string key, int def, int min, int max)
    {
        if (!int.TryParse(_sip.GetAppString(key, def.ToString()), out int v)) v = def;
        return Math.Clamp(v, min, max);
    }

    /// <summary>Попытки исчерпаны — View показывает это человеку (панель отсчёта уже скрыта).</summary>
    public event Action? GaveUp;

    /// <summary>Прервать цикл: дозвонились, пришёл входящий или пользователь отменил.</summary>
    public void Cancel()
    {
        _timer.Stop();
        _target = null;
        _attempt = 0;
        CountingDown = false;
        _hide();
    }

    private void Tick()
    {
        if (--_secondsLeft > 0)
        {
            _showCountdown(_secondsLeft, _attempt);
            return;
        }
        _timer.Stop();
        CountingDown = false;
        _hide();
        if (_target is { Length: > 0 } target)
        {
            _log($"Автодозвон: набираю {PhoneNumberUtils.Pretty(target)}");
            _sip.Call(target);
        }
    }

    /// <summary>Тот же абонент: сравниваем user-части, а не адреса целиком — один и тот же
    /// номер приходит то как «378», то как «sip:378@host» в зависимости от источника события.</summary>
    private static bool SameTarget(string a, string b) =>
        PhoneNumberUtils.SipUser(a).Equals(PhoneNumberUtils.SipUser(b), StringComparison.OrdinalIgnoreCase);
}
