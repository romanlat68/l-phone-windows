using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Чем кончилась команда, чей провал видит пользователь.
///
/// Двух значений мало. <c>false</c> означал сразу и «ядро отказало» (номер не разобран, звонка
/// нет), и «ядро не ответило за таймаут» — а сообщения человеку нужны разные: в первом случае
/// виноват набранный номер, во втором телефон, и советовать «проверьте номер» бессмысленно.
/// </summary>
public enum CommandOutcome
{
    /// <summary>Ядро приняло команду.</summary>
    Done,
    /// <summary>Ядро отказало: нет подходящего звонка, адрес не разобран, сервер против.</summary>
    Rejected,
    /// <summary>Ядро не ответило (не поднято, остановлено, просрочка). Что случилось с
    /// командой — неизвестно, и рапортовать «не удалось» нельзя.</summary>
    CoreUnavailable,
}

/// <summary>
/// Всё про звонок: набор, ответ, удержание, перевод, конференция, запись, журнал.
///
/// Выделено из <see cref="ISipService"/>, потому что у того появились потребители, которым
/// нужны три метода из девяноста, — локальный API для CRM умеет ровно «позвонить, ответить,
/// положить трубку». Узкий интерфейс здесь не про чистоту ради чистоты: он перечисляет,
/// что именно внешняя интеграция может сделать с телефоном, и не даёт этому списку
/// расползтись незаметно.
/// </summary>
public interface ICallService
{
    /// <summary>Состояние звонка изменилось. Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<CallEvent>? CallStateChanged;

    /// <summary>Итог перевода (REFER): удался или нет.</summary>
    event Action<bool>? TransferResult;

    void Call(string target);
    void Hangup();
    void AcceptIncoming();
    void DeclineIncoming();
    void DeclineBusy();
    int CallCount { get; }
    void SwapCalls();
    void SendDtmf(char digit);
    void PlayLocalDtmf(char digit);
    /// <summary>Слепой перевод. Итог различает отказ ядра и его молчание — сообщения
    /// пользователю в этих случаях разные (см. <see cref="CommandOutcome"/>).</summary>
    CommandOutcome Transfer(string target);

    /// <summary>Сопровождаемый перевод: соединить удержанного с текущим собеседником.</summary>
    CommandOutcome TransferToHeldCall();
    /// <summary>Переключить микрофон. Новое состояние приходит событием <see cref="MicMuteChanged"/>:
    /// команда уходит на поток ядра, и ждать её результата синхронно нельзя — по таймауту
    /// интерфейс нарисовал бы состояние, обратное настоящему.</summary>
    void ToggleMute();

    /// <summary>Микрофон заглушён (снимок ядра).</summary>
    bool MicMuted { get; }

    /// <summary>Микрофон включили или выключили. Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<bool>? MicMuteChanged;

    /// <summary>Поставить на удержание или снять. Итог виден по смене состояния звонка
    /// (Pausing → Paused либо оставшийся StreamsRunning, если сервер отказал).</summary>
    void ToggleHold();
    int CurrentCallDuration { get; }
    void RefreshNetwork();

    /// <summary>Объявить сеть доступной/недоступной. Недоступность = корректный un-REGISTER:
    /// перед засыпанием машины это единственный способ не оставить на АТС мёртвый контакт,
    /// в который она будет слать INVITE ещё час.</summary>
    void SetNetworkReachable(bool reachable);

    /// <summary>Чем зашифрован ТЕКУЩИЙ разговор ("none"/"srtp"/"zrtp"/"dtls") — по факту
    /// договорённости сторон, а не по настройке клиента.
    ///
    /// Для ПОКАЗА пользователю берите <see cref="CurrentCallSecurity"/>: одно только имя
    /// механизма не отвечает на вопрос, чего эта защита стоит.</summary>
    string CurrentCallEncryption();

    /// <summary>Защита разговора целиком: шифрование медиа плюс признак «сигнализация по TLS».
    /// Второе решает, чего стоит первое (см. <see cref="CallSecurity"/>).</summary>
    CallSecurity CurrentCallSecurity();

    /// <summary>Качество текущего разговора (потери, задержка, джиттер, оценка).</summary>
    CallQuality CurrentQuality();

    /// <summary>Громкость микрофона и динамика в разговоре, 0..1.</summary>
    (float Mic, float Speaker) CurrentAudioLevels();

    CallEvent? CurrentCallSnapshot();
    CallEvent? DisplayCallSnapshot();

    // Конференция
    bool IsInConference { get; }
    void HoldCurrent();
    CommandOutcome MergeCallsIntoConference();
    void TerminateConference();

    // Запись разговора
    /// <summary>Идёт ли запись (снимок ядра).</summary>
    bool IsRecording { get; }
    string? RecordPath { get; }

    /// <summary>Переключить запись. Итог приходит событием <see cref="RecordingChanged"/> —
    /// по той же причине, что у микрофона: ждать ответа ядра на клике нельзя, а по просрочке
    /// кнопка гасла бы при идущей записи.</summary>
    void ToggleRecording();

    /// <summary>Запись началась или закончилась. Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<bool>? RecordingChanged;

    string GetRecordingsDir();
    void SetRecordingsDir(string? path);

    // Журнал звонков
    IReadOnlyList<CallLogEntry> GetCallHistory();
    void ClearCallHistory();
    int GetMissedSince(long sinceUnixSeconds);
}
