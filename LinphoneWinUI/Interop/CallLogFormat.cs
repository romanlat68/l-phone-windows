using System;
using System.Globalization;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Как выглядит строка журнала. Вынесено из ядра отдельно, потому что здесь легко ошибиться
/// незаметно: «вчера» на границе суток, длительность у пропущенного, полночь как «0:00».
/// Проверяется тестами с явно переданным «сейчас» — на живых часах такое не поймать.
/// </summary>
public static class CallLogFormat
{
    /// <summary>Когда звонили. Сегодня — время, вчера — «Вчера», раньше — день и месяц.
    /// Год не показываем: в журнале, отсортированном по времени, он не помогает опознать
    /// звонок, а место в узком окне занимает.</summary>
    public static string When(DateTime dt, DateTime now)
    {
        var c = CultureInfo.CurrentCulture;
        if (dt.Date == now.Date) return dt.ToString("t", c);
        if (dt.Date == now.Date.AddDays(-1)) return Loc.S("St_Yesterday");
        // "M" — «день месяц» по локали («2 августа»), в узком окне длинновато, поэтому
        // короткий числовой вид: день и месяц без года.
        return dt.ToString("dd.MM", CultureInfo.InvariantCulture);
    }

    /// <summary>Длительность разговора. Пусто, если разговора не было (пропущенный,
    /// неотвеченный, сброшенный) — «00:00» рядом с номером читалось бы как нулевой разговор.</summary>
    public static string Duration(int seconds) =>
        seconds > 0 ? $"{seconds / 60:00}:{seconds % 60:00}" : "";

    /// <summary>Вторая строка: когда и сколько. Слова «Пропущенный» здесь больше нет —
    /// пропущенный виден по красному номеру, а дублировать его текстом значит занимать
    /// строку тем, что и так очевидно.</summary>
    public static string Subtitle(DateTime dt, DateTime now, int seconds)
    {
        string when = When(dt, now);
        string dur = Duration(seconds);
        return dur.Length == 0 ? when : $"{when} · {dur}";
    }
}
