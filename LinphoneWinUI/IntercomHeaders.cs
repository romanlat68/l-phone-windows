using System;
using System.Globalization;

namespace LinphoneWinUI;

/// <summary>
/// Разбор SIP-заголовков «ответь сам»: интерком, оповещение, громкая связь.
///
/// Зачем. На Asterisk и FreePBX это делается заголовком у INVITE — так работают
/// интерком-коды (<c>*80</c> и подобные), групповые оповещения (paging) и домофоны.
/// Аппаратные телефоны (Fanvil, Yealink, Grandstream, Cisco) это понимают из коробки,
/// а софтфон без такой поддержки в схему интеркома просто не встраивается: оператору
/// приходится брать трубку руками, чего вся затея и должна была избежать.
///
/// Единого стандарта нет — за годы накопилось несколько форм, и АТС шлёт ту, что настроена:
/// <list type="bullet">
/// <item><c>Alert-Info: Auto Answer</c> — Asterisk по умолчанию</item>
/// <item><c>Alert-Info: &lt;http://…&gt;;info=alert-autoanswer</c> — FreePBX, Yealink</item>
/// <item><c>Alert-Info: ;answer-after=0</c> — Grandstream, Snom</item>
/// <item><c>Call-Info: &lt;sip:…&gt;;answer-after=0</c> — Polycom, Cisco</item>
/// </list>
/// Поэтому разбор терпимый: ищем признак в любой из форм и в любом регистре.
/// </summary>
internal static class IntercomHeaders
{
    /// <summary>Через сколько секунд отвечать автоматически. null — обычный звонок,
    /// автоответ не запрашивался. 0 — отвечать немедленно.</summary>
    public static int? AutoAnswerDelaySeconds(string? alertInfo, string? callInfo)
    {
        foreach (string? header in new[] { alertInfo, callInfo })
        {
            if (string.IsNullOrWhiteSpace(header)) continue;
            string h = header!.ToLowerInvariant();

            // answer-after=N — задержка задана явно (0 = сразу). Форма Polycom/Cisco/Grandstream.
            int at = h.IndexOf("answer-after", StringComparison.Ordinal);
            if (at >= 0)
            {
                int eq = h.IndexOf('=', at);
                if (eq > 0)
                {
                    int end = eq + 1;
                    while (end < h.Length && char.IsDigit(h[end])) end++;
                    if (end > eq + 1 &&
                        int.TryParse(h[(eq + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out int sec))
                        // Разумный потолок: заголовок приходит снаружи, и «ответить через час»
                        // означало бы занятую линию и зависший экран входящего.
                        return Math.Clamp(sec, 0, 60);
                }
                return 0;   // «answer-after» без числа — отвечаем сразу
            }

            // Признак без задержки — отвечаем немедленно. Пробел в «auto answer» необязателен.
            if (h.Contains("alert-autoanswer", StringComparison.Ordinal) ||
                h.Contains("autoanswer", StringComparison.Ordinal) ||
                h.Contains("auto answer", StringComparison.Ordinal) ||
                h.Contains("intercom", StringComparison.Ordinal))
                return 0;
        }
        return null;
    }
}
