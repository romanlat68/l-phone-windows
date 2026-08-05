using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Статусы коллег и состояние их линий.
///
/// Две подписки, а не одна, и это принципиально: presence отвечает на вопрос «свободен ли
/// человек», а dialog — «звонит ли его телефон прямо сейчас». Перехват вызова осмыслен
/// только во втором случае, и на одном presence пульт BLF построить нельзя.
/// </summary>
public interface IPresenceService
{
    /// <summary>Чей-то статус изменился (NOTIFY presence). Поднимается С ПОТОКА ЯДРА.</summary>
    event Action? PresenceChanged;

    /// <summary>Линия сменила состояние: номер, что с ней и кто на том конце (пусто, если
    /// АТС не сообщила). Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<string, DialogState, string>? DialogStateChanged;

    void SyncPresenceFriends(IEnumerable<string> peers);

    /// <summary>Подписка на состояние линий (BLF). Задаёт ПОЛНЫЙ набор номеров: следующий
    /// вызов снимает подписки, которых нет в списке.</summary>
    void SyncDialogSubscriptions(IEnumerable<string> extensions);

    void SetPresencePublish(bool on);
    PresenceStatus MyStatus { get; }
    void SetMyStatus(PresenceStatus s);
    void PublishOnThePhone(bool onCall);
    PresenceStatus GetStatus(string peer);

    /// <summary>Статусы списка абонентов одним обращением к ядру (вместо N вызовов GetStatus).</summary>
    Dictionary<string, PresenceStatus> GetStatuses(IEnumerable<string> peers);
}
