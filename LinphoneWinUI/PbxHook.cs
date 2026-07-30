using System;
using System.Security.Cryptography;
using System.Text;

namespace LinphoneWinUI;

/// <summary>
/// Сборка URL хука статуса для pbx-admin. Вынесено из MainWindow, чтобы подпись
/// (HMAC) тестировалась без UI и SIP.
///
/// Хук — управляющий запрос (меняет presence коллеги на АТС), поэтому при заданном
/// секрете он подписывается: к URL добавляются <c>ts</c> (unix-время) и <c>sig</c> =
/// HMAC-SHA256(secret, "ext\nstate\nts"). Сервер проверяет свежесть ts (против replay)
/// и подпись. Без секрета URL идёт как есть (обратная совместимость с раскаткой).
/// </summary>
internal static class PbxHook
{
    /// <summary>Значение {state} для трёх ручных статусов; null — статус не шлём
    /// (📞 «Разговаривает» АТС видит сама по device state, Offline — не ручной выбор).</summary>
    public static string? HookState(Interop.PresenceStatus s) => s switch
    {
        Interop.PresenceStatus.Available => "available",
        Interop.PresenceStatus.Away => "away",
        Interop.PresenceStatus.DoNotDisturb => "dnd",
        _ => null,
    };

    /// <summary>Подставляет {ext}/{state} в шаблон и, если задан секрет, дописывает подпись.</summary>
    public static string BuildUrl(string template, string ext, string state, string secret, long unixTime)
    {
        string url = template
            .Replace("{ext}", Uri.EscapeDataString(ext))
            .Replace("{state}", Uri.EscapeDataString(state));
        if (string.IsNullOrEmpty(secret)) return url;

        string sig = Sign(secret, ext, state, unixTime);
        char sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}ts={unixTime}&sig={sig}";
    }

    /// <summary>HMAC-SHA256(secret, "ext\nstate\nts") в нижнем hex. Порядок и разделитель
    /// должны совпадать с проверкой на сервере (pbx-admin handlers_dnd.go).</summary>
    public static string Sign(string secret, string ext, string state, long unixTime)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] mac = h.ComputeHash(Encoding.UTF8.GetBytes($"{ext}\n{state}\n{unixTime}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }
}
