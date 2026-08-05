using System;
using System.Collections.Generic;
using System.Linq;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Что делать, когда список аудиоустройств изменился прямо во время разговора.
///
/// Сценарий бытовой и частый: оператор втыкает USB-гарнитуру, не прерывая звонок, — или,
/// наоборот, выдёргивает её. liblinphone сам ничего не переключает: звук продолжает идти
/// в исчезнувшее устройство, то есть в никуда. Собеседник при этом слышит тишину и вешает
/// трубку, а оператор считает, что «программа сглючила».
///
/// Правило простое и предсказуемое: вернувшееся ЛЮБИМОЕ устройство побеждает, пропавшее
/// текущее заменяется хоть чем-нибудь, в остальных случаях ничего не трогаем. Последнее
/// важно не меньше первого: переключать устройство без причины посреди разговора нельзя.
/// </summary>
public static class AudioDeviceChoice
{
    /// <summary>Какое устройство выбрать. null — оставить как есть.</summary>
    /// <param name="preferredId">Выбранное пользователем в настройках (пусто — «системное»).</param>
    /// <param name="currentId">Что используется прямо сейчас.</param>
    /// <param name="available">Что доступно после изменения списка.</param>
    public static string? Pick(string? preferredId, string? currentId, IReadOnlyList<string> available)
    {
        if (available == null || available.Count == 0) return null;   // звук исчез целиком — не наш случай

        bool Has(string? id) => id is { Length: > 0 } && available.Any(a => a == id);

        // Любимое устройство вернулось (гарнитуру воткнули обратно) — возвращаемся на него.
        if (Has(preferredId) && preferredId != currentId) return preferredId;

        // Текущее пропало (гарнитуру выдернули) — уходим на первое доступное, иначе звука
        // не будет вовсе. Список ядро отдаёт в порядке предпочтения системы.
        if (!Has(currentId)) return available[0];

        return null;
    }

    /// <summary>Устройство «по умолчанию» — псевдоустройство WASAPI, за которым стоит
    /// текущий системный выбор. Из списка оно не исчезает НИКОГДА, поэтому правило
    /// «текущего больше нет» на нём не срабатывает: гарнитуру убрали, а ядро по-прежнему
    /// указывает на Default и продолжает лить звук в неё (проверено на живой машине —
    /// Bose QC Earbuds II пропали из списка, а звук на встроенные не перешёл).</summary>
    public static bool IsSystemDefault(string? id) =>
        id is { Length: > 0 } && id.Contains("Default", StringComparison.OrdinalIgnoreCase);

    /// <summary>Нужно ли переустановить текущее устройство, чтобы ядро пересоздало поток.
    /// Зовётся, когда набор устройств ИЗМЕНИЛСЯ: для псевдоустройства «по умолчанию» это
    /// единственный способ перепривязать звук к новому системному выбору.</summary>
    public static string? Rebind(string? currentId, IReadOnlyList<string> available)
    {
        if (!IsSystemDefault(currentId)) return null;
        if (available == null || available.Count == 0) return null;
        return available.Any(a => a == currentId) ? currentId : null;
    }

    /// <summary>Изменился ли набор устройств. Сравниваем как множества: порядок в списке
    /// ядра меняется сам по себе, и реагировать на его перестановку значило бы дёргать
    /// звук на ровном месте.</summary>
    public static bool SetChanged(IReadOnlyList<string>? before, IReadOnlyList<string>? after)
    {
        if (before == null || after == null) return before != after;
        if (before.Count != after.Count) return true;
        var set = new HashSet<string>(before, StringComparer.Ordinal);
        return after.Any(a => !set.Contains(a));
    }
}
