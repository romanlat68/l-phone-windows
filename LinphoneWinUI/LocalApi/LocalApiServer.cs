using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinphoneWinUI.LocalApi;

/// <summary>Что API умеет делать с телефоном. Отдельно от сервера, чтобы его можно было
/// проверить тестами, не поднимая сокетов.</summary>
public interface ICallCommands
{
    /// <summary>Позвонить. false — номер не принят.</summary>
    bool Call(string number);
    /// <summary>Завершить текущий разговор или входящий вызов.</summary>
    bool Hangup();
    /// <summary>Принять входящий.</summary>
    bool Answer();
    /// <summary>Состояние для /status.</summary>
    ApiStatus Status();
}

/// <summary>Снимок состояния для /status.</summary>
public readonly record struct ApiStatus(bool Registered, string Account, string State, string Version);

/// <summary>
/// Локальный HTTP-API для интеграций: click-to-call из CRM (1С, Битрикс24, amoCRM) и
/// опрос состояния. Без него интеграция возможна только через `L-Phone.exe tel:` — то есть
/// запуском процесса на каждый звонок, без ответа о результате.
///
/// БЕЗОПАСНОСТЬ — здесь она важнее функциональности, потому что API умеет звонить:
/// — слушаем ТОЛЬКО 127.0.0.1. На 0.0.0.0 любой в сети смог бы звонить с чужого телефона;
/// — обязательный токен на КАЖДЫЙ запрос, включая /status: без него любая открытая в браузере
///   страница могла бы дёрнуть 127.0.0.1 картинкой и позвонить куда угодно от вашего имени;
/// — заголовки ограничены по размеру и по времени: сокет, который молчит, занимает поток;
/// — по умолчанию API ВЫКЛЮЧЕН. Функция, которая умеет звонить, не включается сама.
/// </summary>
public sealed class LocalApiServer : IDisposable
{
    private readonly ICallCommands _commands;
    private readonly Action<string>? _log;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _token = "";

    /// <summary>Порт по умолчанию. Выше 1024 (не требует прав), вне диапазона динамических
    /// портов Windows (49152+), где его мог бы занять случайный клиент.</summary>
    public const int DefaultPort = 8712;

    private const int MaxHeaderBytes = 8 * 1024;
    private const int ReadTimeoutMs = 5000;

    public LocalApiServer(ICallCommands commands, Action<string>? log = null)
    {
        _commands = commands;
        _log = log;
    }

    public bool IsRunning => _listener != null;
    public int Port { get; private set; }

    /// <summary>Запустить. Возвращает false, если порт занят — молчать нельзя, иначе
    /// интеграция не работает, а причина не видна.</summary>
    public bool Start(int port, string token)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log?.Invoke("Локальный API: пустой токен — не запускаю");
            return false;
        }
        try
        {
            _token = token;
            _listener = new TcpListener(IPAddress.Loopback, port);   // только 127.0.0.1
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _log?.Invoke($"Локальный API слушает 127.0.0.1:{Port}");
            return true;
        }
        catch (SocketException e)
        {
            _log?.Invoke($"Локальный API: порт {port} занять не удалось — {e.SocketErrorCode}");
            _listener = null;
            return false;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            // Каждое соединение обслуживаем отдельно: зависший клиент не должен
            // останавливать приём остальных.
            _ = Task.Run(() => HandleAsync(client, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = ReadTimeoutMs;
                client.SendTimeout = ReadTimeoutMs;
                using var stream = client.GetStream();
                var (request, headers) = await ReadRequestAsync(stream, ct);
                if (request == null) return;

                var (status, json) = Route(request, headers);
                await WriteAsync(stream, status, json, ct);
            }
            catch (IOException) { /* клиент отвалился — обычное дело */ }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                // Тип обязателен: COM-исключение «не тот поток» приходит с ПУСТЫМ Message,
                // и по строке «Локальный API: » понять причину невозможно (проверено).
                _log?.Invoke($"Локальный API: {e.GetType().Name} {e.Message}".TrimEnd());
                try { await WriteAsync(client.GetStream(), 500, Error("internal"), ct); } catch { }
            }
        }
    }

    private static async Task<(HttpRequest?, Dictionary<string, string>)> ReadRequestAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes];
        int total = 0;
        int headerEnd = -1;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0) break;
            total += read;
            headerEnd = FindHeaderEnd(buffer, total);
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return (null, new Dictionary<string, string>());

        string text = Encoding.UTF8.GetString(buffer, 0, headerEnd);
        string[] lines = text.Split("\r\n");
        var request = HttpRequest.ParseRequestLine(lines.Length > 0 ? lines[0] : null);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }
        return (request, headers);
    }

    private static int FindHeaderEnd(byte[] b, int len)
    {
        for (int i = 3; i < len; i++)
            if (b[i] == '\n' && b[i - 1] == '\r' && b[i - 2] == '\n' && b[i - 3] == '\r') return i - 3;
        return -1;
    }

    /// <summary>Разбор команды. Вынесено из сетевой части: так маршрутизацию и проверку
    /// токена можно проверить тестами, не открывая сокетов.</summary>
    internal (int Status, string Json) Route(HttpRequest request, IReadOnlyDictionary<string, string> headers)
    {
        // Host обязан быть петлёй. Мы слушаем только 127.0.0.1, но этого мало против DNS
        // rebinding: страница на evil.com, чей домен через несколько секунд начинает
        // резолвиться в 127.0.0.1, для браузера остаётся ТЕМ ЖЕ origin — CORS не применяется,
        // и ответ читается. Настоящий клиент интеграции ходит на 127.0.0.1 и его же присылает
        // в Host; подменить DNS атакующий может, а заставить браузер отправить чужой Host — нет.
        if (!HostIsLoopback(headers)) return (403, Error("bad host"));

        // OPTIONS обрывается ЗДЕСЬ и без побочных эффектов. Это не педантичность: разбор
        // шёл только по пути, и «OPTIONS /call?number=…» реально ЗВОНИЛ. А OPTIONS — это
        // ровно то, что браузер шлёт сам, предварительным запросом, ещё до того как решит,
        // позволено ли ему обращаться к нашему адресу.
        if (request.Method == "OPTIONS") return (204, "");

        // Токен принимаем и параметром, и заголовком: CRM часто умеет только открыть URL,
        // а нормальный клиент предпочтёт не светить секрет в логах прокси.
        string token = request.Param("token");
        if (token.Length == 0 && headers.TryGetValue("X-Token", out string? h)) token = h;
        if (!TokenMatches(token)) return (401, Error("bad token"));

        switch (request.Path)
        {
            case "/status":
                var s = _commands.Status();
                return (200, "{" +
                    $"\"registered\":{(s.Registered ? "true" : "false")}," +
                    $"\"account\":{Quote(s.Account)}," +
                    $"\"state\":{Quote(s.State)}," +
                    $"\"version\":{Quote(s.Version)}}}");

            case "/call":
                string number = request.Param("number");
                if (number.Length == 0) return (400, Error("number required"));
                return _commands.Call(number)
                    ? (200, $"{{\"ok\":true,\"number\":{Quote(number)}}}")
                    : (409, Error("call rejected"));

            case "/hangup":
                return (200, $"{{\"ok\":{(_commands.Hangup() ? "true" : "false")}}}");

            case "/answer":
                return (200, $"{{\"ok\":{(_commands.Answer() ? "true" : "false")}}}");

            default:
                return (404, Error("unknown command"));
        }
    }

    /// <summary>Обращаются ли к нам по петлевому адресу. Заголовка нет — пропускаем: HTTP/1.0
    /// и простейшие скрипты его не шлют, а браузер шлёт всегда, и именно от браузера мы
    /// защищаемся. Порт в значении игнорируем.</summary>
    internal static bool HostIsLoopback(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Host", out string? host) || string.IsNullOrWhiteSpace(host)) return true;
        string h = host.Trim();
        if (h.StartsWith('['))   // IPv6 в скобках: [::1]:8712
        {
            int close = h.IndexOf(']');
            if (close > 0) h = h[1..close];
        }
        else
        {
            int colon = h.IndexOf(':');
            if (colon > 0) h = h[..colon];
        }
        return h.Equals("127.0.0.1", StringComparison.Ordinal)
            || h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || h.Equals("::1", StringComparison.Ordinal);
    }

    /// <summary>Сравнение за постоянное время. Токен локальный, но привычку сравнивать
    /// секреты обычным == лучше не заводить.</summary>
    private bool TokenMatches(string given)
    {
        if (given.Length != _token.Length || _token.Length == 0) return false;
        int diff = 0;
        for (int i = 0; i < given.Length; i++) diff |= given[i] ^ _token[i];
        return diff == 0;
    }

    private static string Error(string message) => $"{{\"error\":{Quote(message)}}}";

    /// <summary>Строка в JSON. Свой экранировщик — на четыре поля тянуть сериализатор незачем,
    /// но кавычки и обратные слэши экранировать обязательно: имя абонента приходит извне.</summary>
    internal static string Quote(string? value)
    {
        var sb = new StringBuilder("\"", (value?.Length ?? 0) + 8);
        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string json, CancellationToken ct)
    {
        string reason = status switch
        {
            200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized",
            403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", _ => "Error",
        };
        byte[] body = Encoding.UTF8.GetBytes(json);
        // Access-Control-Allow-Origin НЕ отдаём сознательно: пусть браузерная страница,
        // даже зная токен, не сможет прочитать ответ.
        string head = $"HTTP/1.1 {status} {reason}\r\n" +
                      "Content-Type: application/json; charset=utf-8\r\n" +
                      $"Content-Length: {body.Length}\r\n" +
                      "Cache-Control: no-store\r\n" +
                      "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Новый токен. 32 символа из безопасного генератора — его вставляют в настройки
    /// CRM руками, поэтому без похожих друг на друга символов.</summary>
    public static string NewToken()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";   // без l, o, 0, 1
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var sb = new StringBuilder(32);
        foreach (byte b in bytes) sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }
}
