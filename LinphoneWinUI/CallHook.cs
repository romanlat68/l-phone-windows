using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace LinphoneWinUI;

/// <summary>Что случилось со звонком — подставляется в шаблон как {event}.</summary>
public enum CallHookEvent
{
    /// <summary>Входящий зазвонил. Самое ценное событие: карточку клиента надо показать
    /// ДО того, как оператор снял трубку.</summary>
    Incoming,
    /// <summary>Разговор начался.</summary>
    Answered,
    /// <summary>Разговор закончился.</summary>
    Ended,
    /// <summary>Исходящий начат.</summary>
    Outgoing,
}

/// <summary>Данные события для подстановки в шаблон.</summary>
public readonly record struct CallHookData(
    CallHookEvent Event, string Number, string Name, int DurationSeconds, string CallId);

/// <summary>
/// Подстановка данных звонка в пользовательский шаблон URL.
///
/// Смысл всей затеи: при входящем открыть карточку клиента в CRM (или тихо уведомить её),
/// чтобы оператор снимал трубку, уже видя, кто звонит и о чём говорили в прошлый раз.
/// Это то, ради чего софтфон вообще ставят рядом с CRM.
/// </summary>
public static class CallHook
{
    /// <summary>Заполнить шаблон. Значения кодируются для URL: номер с плюсом, имя с пробелами
    /// и кириллицей иначе развалят ссылку. Неизвестные подстановки оставляем как есть —
    /// это опечатка пользователя, и молча съесть её означало бы спрятать причину.</summary>
    public static string Format(string? template, CallHookData data)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = data.Number,
            ["name"] = data.Name,
            ["event"] = data.Event.ToString().ToLowerInvariant(),
            ["direction"] = data.Event is CallHookEvent.Outgoing ? "out" : "in",
            ["duration"] = data.DurationSeconds.ToString(),
            ["callid"] = data.CallId,
        };

        var sb = new StringBuilder(template!.Length + 32);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0) { sb.Append(template, i, template.Length - i); break; }
            int close = template.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }

            sb.Append(template, i, open - i);
            string key = template[(open + 1)..close];
            sb.Append(map.TryGetValue(key, out string? v) ? Uri.EscapeDataString(v) : template[open..(close + 1)]);
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Можно ли открывать такую ссылку в браузере. Только http и https: шаблон
    /// задаёт пользователь, и превращать поле настроек в «запусти что угодно» нельзя —
    /// file:// и произвольные схемы открыли бы путь к запуску программ с чужого диска.</summary>
    public static bool IsBrowsable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}

/// <summary>
/// Отправка событий звонка наружу: тихий запрос в CRM либо открытие карточки в браузере.
/// Оба режима нужны: интеграция по API умеет принять данные сама, а «показать карточку
/// оператору» решается только открытым окном браузера.
/// </summary>
internal sealed class CallHookClient
{
    // Отдельный клиент с коротким таймаутом: недоступная CRM не должна задерживать ничего
    // в телефоне. Событие звонка — не та вещь, ради которой стоит ждать.
    // Прокси в обход локальных адресов: коннектор CRM часто стоит на этой же машине или
    // в той же сети, а системный прокси такие запросы съедает (см. LocalBypassProxy).
    private static readonly HttpClient Http =
        new(Interop.LocalBypassProxy.Handler()) { Timeout = TimeSpan.FromSeconds(4) };

    private readonly Func<string> _template;
    private readonly Func<bool> _openInBrowser;
    private readonly Func<bool> _incomingOnly;
    private readonly Action<string> _log;

    public CallHookClient(Func<string> template, Func<bool> openInBrowser,
                          Func<bool> incomingOnly, Action<string> log)
    {
        _template = template;
        _openInBrowser = openInBrowser;
        _incomingOnly = incomingOnly;
        _log = log;
    }

    /// <summary>Сообщить о событии. Возвращается немедленно — отправка идёт фоном.</summary>
    public void Notify(CallHookData data)
    {
        string template = _template();
        if (template.Length == 0) return;                       // хук не настроен — это нормально
        if (_incomingOnly() && data.Event != CallHookEvent.Incoming) return;

        string url = CallHook.Format(template, data);
        if (url.Length == 0) return;

        if (_openInBrowser())
        {
            if (!CallHook.IsBrowsable(url))
            {
                _log($"CRM-хук: {url} — открыть можно только http/https");
                return;
            }
            try
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                _log($"CRM-хук: открываю {Mask(url)}");
            }
            catch (Exception e) { _log($"CRM-хук: не удалось открыть — {e.Message}"); }
            return;
        }

        _ = SendAsync(url);
    }

    private async Task SendAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url);
            // Сообщаем и об успехе, и о коде ответа: молчащий хук неотличим от неработающего.
            _log($"CRM-хук: {Mask(url)} → {(int)response.StatusCode}");
        }
        catch (TaskCanceledException) { _log("CRM-хук: CRM не ответила за 4 с"); }
        catch (Exception e) { _log($"CRM-хук: {e.GetType().Name} {e.Message}".TrimEnd()); }
    }

    /// <summary>В журнал ссылку пишем без строки запроса: там номер клиента, а журнал
    /// уезжает в поддержку целиком.</summary>
    private static string Mask(string url)
    {
        int q = url.IndexOf('?');
        return q < 0 ? url : url[..q] + "?…";
    }
}
