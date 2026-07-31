using System;
using System.Linq;
using System.Xml.Linq;

namespace LinphoneWinUI;

/// <summary>Состояние линии коллеги из dialog-info (RFC 4235).</summary>
public enum DialogState
{
    /// <summary>NOTIFY не разобран или тела нет — состояние неизвестно, лампу не трогаем.</summary>
    Unknown,
    /// <summary>Разговоров нет.</summary>
    Terminated,
    /// <summary>ЗВОНИТ. Ради этого состояния всё и делается: обычный presence его не показывает,
    /// а именно в нём имеет смысл перехват вызова.</summary>
    Early,
    /// <summary>Разговаривает.</summary>
    Confirmed,
}

/// <summary>
/// Разбор NOTIFY по подписке «Event: dialog» (dialog-info+xml, RFC 4235).
///
/// Зачем: presence показывает только «свободен / занят / не беспокоить» и не отличает
/// звонящий телефон от разговаривающего. Перехват (directed pickup) осмыслен ровно в
/// состоянии early — в остальных он даёт отбойник. Asterisk отдаёт dialog-info по тем же
/// hint'ам, что и presence, отдельной настройки на АТС не требуется.
/// </summary>
public static class DialogInfoParser
{
    private static readonly XNamespace Ns = "urn:ietf:params:xml:ns:dialog-info";

    /// <summary>Состояние линии из тела NOTIFY. Пустое/битое тело → Unknown: лампу в этом
    /// случае не трогаем вовсе, потому что «не знаю» и «не звонит» — разные вещи.</summary>
    public static DialogState Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return DialogState.Unknown;
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root == null || root.Name.LocalName != "dialog-info") return DialogState.Unknown;

            // Диалогов может быть несколько (несколько линий у аппарата). Показываем самое
            // «активное»: разговор важнее звонка, звонок важнее тишины — оператор должен
            // видеть, что коллега занят, даже если параллельно к нему кто-то дозванивается.
            var states = root.Elements().Where(e => e.Name.LocalName == "dialog")
                             .SelectMany(d => d.Elements().Where(e => e.Name.LocalName == "state"))
                             .Select(s => (s.Value ?? "").Trim().ToLowerInvariant())
                             .ToList();
            if (states.Count == 0) return DialogState.Terminated;   // <dialog-info/> без диалогов = тишина
            if (states.Contains("confirmed")) return DialogState.Confirmed;
            // early и proceeding — обе стадии «идёт вызов»; trying у Asterisk тоже встречается.
            if (states.Any(s => s is "early" or "proceeding" or "trying")) return DialogState.Early;
            return DialogState.Terminated;
        }
        catch (System.Xml.XmlException) { return DialogState.Unknown; }
    }

    /// <summary>Чей это диалог: user-часть из атрибута entity (sip:376@pbx → 376).
    /// NOTIFY приходят по разным подпискам в один колбэк, и без этого их не различить.</summary>
    public static string Entity(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return "";
        try
        {
            string entity = XDocument.Parse(xml).Root?.Attribute("entity")?.Value ?? "";
            return PhoneNumberUtils.SipUser(entity);
        }
        catch (System.Xml.XmlException) { return ""; }
    }
}
