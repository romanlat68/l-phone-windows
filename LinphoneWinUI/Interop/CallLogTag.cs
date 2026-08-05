using System;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Наши собственные пометки к записи журнала звонков.
///
/// В журнале liblinphone шифрования нет — в API call_log его просто не существует
/// (есть направление, статус, длительность, качество, call-id, но не защита медиа).
/// Поэтому пишем сами в ref_key: штатный слот произвольной строки, который ядро хранит
/// вместе с записью. Формат «ключ=значение» через «;» — чтобы завтра можно было добавить
/// вторую пометку, не сломав чтение уже накопленных записей.
///
/// Разбор нарочно терпимый: ref_key — общая помойка, куда мог написать кто угодно
/// (другая сборка, другой клиент на том же профиле, будущая версия нас самих).
/// Непонятное содержимое означает «не знаем», а не испорченный журнал.
/// </summary>
public static class CallLogTag
{
    private const string EncKey = "enc";

    /// <summary>Известные виды защиты медиа. Всё остальное из ядра считаем незашифрованным:
    /// показать замок там, где его не было, хуже, чем не показать там, где он был.</summary>
    private static bool IsKnownEncryption(string v) => v is "srtp" or "zrtp" or "dtls";

    /// <summary>Собрать метку для записи журнала. Пустая строка — писать нечего.</summary>
    public static string Format(string? encryption)
    {
        string enc = (encryption ?? "").Trim().ToLowerInvariant();
        return IsKnownEncryption(enc) ? $"{EncKey}={enc}" : "";
    }

    /// <summary>Чем был зашифрован разговор по нашей метке: "srtp"/"zrtp"/"dtls"
    /// или "" — не знаем (старая запись, чужая метка, мусор).</summary>
    public static string Encryption(string? refKey)
    {
        if (string.IsNullOrWhiteSpace(refKey)) return "";

        foreach (string part in refKey.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;

            if (!part.AsSpan(0, eq).Trim().Equals(EncKey, StringComparison.OrdinalIgnoreCase)) continue;

            string v = part[(eq + 1)..].Trim().ToLowerInvariant();
            if (IsKnownEncryption(v)) return v;
        }
        return "";
    }
}
