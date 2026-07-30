using System;
using System.Collections.Generic;
using System.Linq;

namespace LinphoneWinUI;

/// <summary>
/// Единый источник нормализации телефонных номеров и SIP-адресов. Раньше эти операции
/// были рассыпаны по MainWindow (SipUser/DigitsOnly/Pretty/NormalizeDialTarget) и Favorites.Key
/// с чуть разной семантикой — гарантированный источник багов сравнения номеров.
/// </summary>
internal static class PhoneNumberUtils
{
    /// <summary>Только цифры из строки (для сопоставления номеров без оформления).</summary>
    public static string DigitsOnly(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Пользовательская часть адреса любого вида:
    /// «"Имя" &lt;sip:1001@host;params&gt;», sip:1001@host, 1001@host, 1001 → 1001.
    /// Без этого имя-префикс (часто с цифрами) ломал сопоставление с книгой.</summary>
    public static string SipUser(string? address)
    {
        string s = address ?? "";
        int lt = s.IndexOf('<');
        if (lt >= 0)                                   // берём то, что внутри <...>
        {
            int gt = s.IndexOf('>', lt + 1);
            s = gt > lt ? s[(lt + 1)..gt] : s[(lt + 1)..];
        }
        if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) s = s[5..];
        int at = s.IndexOf('@');
        if (at >= 0) s = s[..at];                      // user@host → user
        int semi = s.IndexOf(';');
        if (semi >= 0) s = s[..semi];                  // user;params → user
        return s.Trim();
    }

    /// <summary>Адрес для показа: убирает схему sip: и ;params, но оставляет @домен. Пусто → «—».</summary>
    public static string Pretty(string? sipAddress)
    {
        if (string.IsNullOrEmpty(sipAddress)) return "—";
        string s = sipAddress;
        if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        int semi = s.IndexOf(';');
        if (semi >= 0) s = s[..semi];
        return s;
    }

    /// <summary>Похож ли номер на внутренний АТС (для подписки на presence): user-часть только
    /// из цифр, 2–6 знаков. Внешние (+7…, городские 7+ цифр, буквенные) отсеиваем — SUBSCRIBE
    /// на них бессмыслен и засоряет лог Asterisk («does not exist… or has no associated hint»).</summary>
    public static bool IsLikelyInternal(string? number)
    {
        string u = SipUser(number);
        if (u.Length is < 2 or > 6) return false;
        foreach (char c in u) if (!char.IsDigit(c)) return false;
        return true;
    }

    /// <summary>Цель для набора из tel:/callto:/sip: URI (click-to-call, буфер обмена):
    /// tel:+7 (495) 123-45-67 → +74951234567; sip:-адрес остаётся адресом.
    /// Источник ВНЕШНИЙ (ссылка на странице, файл-почтовый ящик второго экземпляра), а результат
    /// уходит в linphone_core_invite и попадает в заголовки INVITE — поэтому здесь же режем всё,
    /// чем внешняя ссылка могла бы управлять запросом.</summary>
    public static string NormalizeDialTarget(string? raw)
    {
        string s = Uri.UnescapeDataString((raw ?? "").Trim());
        // Управляющие символы (CRLF из %0d%0a и пр.) — прямой путь к инъекции заголовков.
        foreach (char c in s) if (char.IsControl(c)) return "";
        if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
        {
            // «?header=value» в SIP-URI — штатный способ подставить заголовки в исходящий INVITE,
            // «;params» тоже задаёт поведение запроса. Из внешней ссылки ни то ни другое не берём.
            int cut = s.IndexOfAny(new[] { '?', ';' });
            return cut >= 0 ? s[..cut] : s;
        }
        if (s.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("callto:", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        int q = s.IndexOfAny(new[] { '?', ';' });
        if (q >= 0) s = s[..q];
        // Телефонный номер: убираем оформление (скобки, дефисы, пробелы, точки).
        return new string(s.Where(c => char.IsLetterOrDigit(c) || c is '+' or '*' or '#' or '@').ToArray());
    }

    /// <summary>Можно ли набрать цель, пришедшую ИЗВНЕ, не спрашивая пользователя.
    /// Доверяем двум случаям: чистый номер (уйдёт через аккаунт по умолчанию, то есть через свою
    /// АТС) и адрес на домене одного из наших аккаунтов. Всё остальное — чужой SIP-сервер: такой
    /// звонок раскрывает адрес клиента, отдаёт ему медиапоток и может стоить денег, а инициатором
    /// выступает произвольная веб-страница. Не запрещаем — спрашиваем.</summary>
    public static bool IsTrustedDialTarget(string? target, IEnumerable<string>? ourServers)
    {
        string s = (target ?? "").Trim();
        if (s.Length == 0) return false;
        if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) s = s[5..];

        int at = s.LastIndexOf('@');
        if (at < 0)
        {
            foreach (char c in s) if (!char.IsDigit(c) && c is not ('+' or '*' or '#')) return false;
            return s.Length > 0;
        }

        string host = HostOnly(s[(at + 1)..]);
        if (host.Length == 0) return false;
        foreach (string srv in ourServers ?? Enumerable.Empty<string>())
            if (host.Equals(HostOnly(srv ?? ""), StringComparison.OrdinalIgnoreCase)) return true;
        return false;

        // «pbx.example.com:5060» / «pbx.example.com;transport=udp» → «pbx.example.com»
        static string HostOnly(string h)
        {
            h = h.Trim();
            int cut = h.IndexOfAny(new[] { ':', ';', '>' });
            return (cut >= 0 ? h[..cut] : h).Trim();
        }
    }
}
