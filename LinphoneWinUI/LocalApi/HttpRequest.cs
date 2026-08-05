using System;
using System.Collections.Generic;

namespace LinphoneWinUI.LocalApi;

/// <summary>
/// Разобранный HTTP-запрос: метод, путь и параметры. Своего разбора здесь ровно столько,
/// сколько нужно локальному API из пяти команд — полноценный веб-сервер нам не нужен,
/// а <c>HttpListener</c> на Windows требует регистрации URL-ACL, то есть прав администратора
/// при первом запуске. Требовать их ради click-to-call неприемлемо.
/// </summary>
public sealed record HttpRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query)
{
    /// <summary>Параметр запроса; пусто, если его нет.</summary>
    public string Param(string name) => Query.TryGetValue(name, out string? v) ? v : "";

    /// <summary>
    /// Разобрать стартовую строку («GET /call?number=377&amp;token=abc HTTP/1.1»).
    /// null — строка не похожа на HTTP-запрос: отвечать на такое нельзя, соединение закрываем.
    /// </summary>
    public static HttpRequest? ParseRequestLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        string method = parts[0].ToUpperInvariant();
        // Принимаем только то, что сами обещаем: неизвестный метод — не наш клиент.
        if (method is not ("GET" or "POST" or "OPTIONS")) return null;

        string target = parts[1];
        if (target.Length == 0 || target[0] != '/') return null;

        int q = target.IndexOf('?');
        string path = q < 0 ? target : target[..q];
        string query = q < 0 ? "" : target[(q + 1)..];

        // Хвостовой слэш не должен делать /call и /call/ разными командами.
        if (path.Length > 1 && path[^1] == '/') path = path[..^1];

        return new HttpRequest(method, path.ToLowerInvariant(), ParseQuery(query));
    }

    /// <summary>Разбор «a=1&amp;b=%2B7999» в словарь. Повторный ключ выигрывает последним —
    /// как в большинстве серверов; молча склеивать значения было бы хуже.</summary>
    public static IReadOnlyDictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = eq < 0 ? pair : pair[..eq];
            string value = eq < 0 ? "" : pair[(eq + 1)..];
            key = Decode(key);
            if (key.Length > 0) result[key] = Decode(value);
        }
        return result;
    }

    /// <summary>Раскодировать %XX и «+». Битую последовательность оставляем как есть:
    /// уронить разбор из-за одного кривого процента значило бы потерять весь запрос.</summary>
    private static string Decode(string s)
    {
        if (s.IndexOf('%') < 0 && s.IndexOf('+') < 0) return s;
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }
}
