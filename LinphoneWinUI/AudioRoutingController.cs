using System;
using System.Collections.Generic;
using System.Linq;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI;

/// <summary>
/// Куда идёт звук: выбор устройств при подключении и отключении гарнитуры.
///
/// Вынесено из MainWindow первым — по соотношению «польза к риску». Здесь нет ни одного
/// обращения к XAML, зато есть три неочевидных правила, каждое из которых родилось из живой
/// поломки и до сих пор не проверялось ничем:
///
/// 1. Перечитывание списка устройств САМО поднимает событие «список изменился». Ответить на
///    него новым перечитыванием — это бесконечный цикл: приложение съедает процессор и
///    перестаёт отвечать (случилось на рабочей машине). Поэтому своё эхо помечаем по времени.
/// 2. Событие об ОТКЛЮЧЕНИИ приходит не от всякого драйвера — Bluetooth как раз молчит.
///    Поэтому список ещё и опрашивается по таймеру, и расхождение обрабатывается как событие.
/// 3. Псевдоустройство «по умолчанию» из списка не исчезает НИКОГДА, поэтому правило «текущего
///    больше нет» на нём не срабатывает, а повторная установка того же устройства ядро не
///    перепривязывает: в журнале «Динамик переключён», а звук остаётся в снятой гарнитуре.
///    Поток приходится пересоздавать силой — уйти на соседнее устройство и вернуться.
/// </summary>
internal sealed class AudioRoutingController
{
    private readonly ISipService _sip;
    private readonly Action<string> _log;

    /// <summary>Устройства сменились во время разговора — View показывает это человеку.
    /// Вне разговора молчим: подсказка на каждое подключение веб-камеры с микрофоном
    /// раздражала бы, а слышимого эффекта там нет.</summary>
    public event Action? DevicesSwitched;

    public AudioRoutingController(ISipService sip, Action<string> log)
    {
        _sip = sip;
        _log = log;
    }

    // Устройства, которые оператор выбрал сам. Держим отдельно, чтобы вернувшуюся гарнитуру
    // можно было отличить от «просто появилось новое устройство».
    private string? _preferredMic;
    private string? _preferredSpeaker;

    private DateTime _selfReloadAt = DateTime.MinValue;
    private IReadOnlyList<string>? _lastMicIds;

    /// <summary>Запомнить текущий выбор как предпочтительный. Зовётся после настроек:
    /// там могли выбрать другую гарнитуру.</summary>
    public void RememberChoice()
    {
        _preferredMic = _sip.CurrentInputDeviceId;
        _preferredSpeaker = _sip.CurrentOutputDeviceId;
    }

    /// <summary>Событие от ядра: список устройств изменился.
    ///
    /// ЗДЕСЬ НЕЛЬЗЯ ЗВАТЬ ReloadSoundDevices — см. правило 1 в описании класса. Список ядро
    /// к моменту события уже обновило.</summary>
    public void OnDevicesChanged()
    {
        if ((DateTime.UtcNow - _selfReloadAt).TotalSeconds < 2) return;   // наше собственное эхо
        Apply();
    }

    /// <summary>Сверить список самим (правило 2: не всякий драйвер сообщает об отключении).
    /// Вызывается по таймеру примерно раз в две секунды во время разговора.</summary>
    public void CheckDrift()
    {
        _selfReloadAt = DateTime.UtcNow;   // помечаем СВОЁ перечитывание до его начала
        _sip.ReloadSoundDevices();
        var mics = _sip.AudioDeviceIds(input: true);

        if (_lastMicIds != null && AudioDeviceChoice.SetChanged(_lastMicIds, mics))
        {
            _log("Набор аудиоустройств изменился без уведомления ядра");
            Apply();
        }
        _lastMicIds = mics;
    }

    /// <summary>Выбрать устройства заново и, если понадобилось, пересоздать поток.</summary>
    public void Apply()
    {
        var mics = _sip.AudioDeviceIds(input: true);
        var spks = _sip.AudioDeviceIds(input: false);
        // Пишем ИМЕНА: идентификатор ядро отдаёт в системной кодировке, и в журнале он
        // выглядел набором вопросительных знаков.
        _log("устройства: микрофоны " + Describe(_sip.GetInputDevices(), _sip.CurrentInputDeviceId));
        _log("устройства: динамики " + Describe(_sip.GetOutputDevices(), _sip.CurrentOutputDeviceId));

        string? micPick = AudioDeviceChoice.Pick(_preferredMic, _sip.CurrentInputDeviceId, mics);
        string? spkPick = AudioDeviceChoice.Pick(_preferredSpeaker, _sip.CurrentOutputDeviceId, spks);

        if (micPick != null) { _sip.SetInputDevice(micPick); _log("Микрофон переключён: " + micPick); }
        if (spkPick != null) { _sip.SetOutputDevice(spkPick); _log("Динамик переключён: " + spkPick); }

        bool rebound = false;
        if (micPick == null) rebound |= RebindDefault(input: true, mics);
        if (spkPick == null) rebound |= RebindDefault(input: false, spks);

        if (micPick != null || spkPick != null || rebound) DevicesSwitched?.Invoke();
    }

    /// <summary>Пересоздать звуковой поток для устройства «по умолчанию» (правило 3):
    /// ставим соседнее устройство и тут же возвращаем прежнее.</summary>
    private bool RebindDefault(bool input, IReadOnlyList<string> available)
    {
        string? current = input ? _sip.CurrentInputDeviceId : _sip.CurrentOutputDeviceId;
        if (!AudioDeviceChoice.IsSystemDefault(current)) return false;

        string? other = available.FirstOrDefault(a => a != current);
        if (other == null || current == null) return false;

        if (input) { _sip.SetInputDevice(other); _sip.SetInputDevice(current); }
        else { _sip.SetOutputDevice(other); _sip.SetOutputDevice(current); }
        _log($"Звук перепривязан к «по умолчанию» ({(input ? "микрофон" : "динамик")})");
        return true;
    }

    private static string Describe(IReadOnlyList<AudioDeviceInfo> devices, string? currentId) =>
        string.Join(", ", devices.Select(d => d.Id == currentId ? $"[{d.Name}]" : d.Name))
        + "  (в квадратных скобках — текущее)";
}
