using System;
using System.Collections.Generic;
using LinphoneWinUI.LocalApi;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Разбор HTTP-запроса локального API. Запрос приходит извне приложения, поэтому здесь
/// нельзя ни падать на мусоре, ни принимать за команду то, что командой не является.
/// </summary>
public class HttpRequestTests
{
    [Fact]
    public void SimpleGet_IsParsed()
    {
        var r = HttpRequest.ParseRequestLine("GET /status HTTP/1.1");

        Assert.Equal("GET", r!.Method);
        Assert.Equal("/status", r.Path);
    }

    [Fact]
    public void Query_IsParsed()
    {
        var r = HttpRequest.ParseRequestLine("GET /call?number=377&token=abc HTTP/1.1");

        Assert.Equal("377", r!.Param("number"));
        Assert.Equal("abc", r.Param("token"));
    }

    // Номер в CRM почти всегда с плюсом, а плюс в URL кодируется. Не раскодировав его,
    // мы позвоним на «7999…» вместо «+7999…» — по межгороду это разные номера.
    [Fact]
    public void PercentEncoding_IsDecoded() =>
        Assert.Equal("+79993770000", HttpRequest.ParseRequestLine("GET /call?number=%2B79993770000 HTTP/1.1")!.Param("number"));

    [Fact]
    public void Plus_MeansSpace_InQuery() =>
        Assert.Equal("Иван Петров", HttpRequest.ParseRequestLine("GET /call?name=%D0%98%D0%B2%D0%B0%D0%BD+%D0%9F%D0%B5%D1%82%D1%80%D0%BE%D0%B2 HTTP/1.1")!.Param("name"));

    [Fact]
    public void PathCase_IsNormalized() =>
        Assert.Equal("/status", HttpRequest.ParseRequestLine("GET /STATUS HTTP/1.1")!.Path);

    // /call и /call/ — одна и та же команда: заставлять интегратора угадывать слэш незачем.
    [Fact]
    public void TrailingSlash_IsIgnored() =>
        Assert.Equal("/call", HttpRequest.ParseRequestLine("POST /call/ HTTP/1.1")!.Path);

    [Fact]
    public void MissingParam_IsEmpty() =>
        Assert.Equal("", HttpRequest.ParseRequestLine("GET /call HTTP/1.1")!.Param("number"));

    [Fact]
    public void ParamWithoutValue_IsEmpty() =>
        Assert.Equal("", HttpRequest.ParseRequestLine("GET /call?number= HTTP/1.1")!.Param("number"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GET")]                      // нет цели
    [InlineData("GET call HTTP/1.1")]        // цель без ведущего слэша
    [InlineData("DELETE /call HTTP/1.1")]    // метод, которого мы не обещали
    [InlineData("\x16\x03\x01 мусор")]       // так выглядит начало TLS: браузер зашёл по https
    public void Garbage_IsRejected(string? line) => Assert.Null(HttpRequest.ParseRequestLine(line));

    // Битый процент не должен ронять разбор целиком — иначе один кривой символ съедает
    // всю команду вместе с токеном.
    [Fact]
    public void BrokenEncoding_DoesNotThrow()
    {
        var r = HttpRequest.ParseRequestLine("GET /call?number=%ZZ HTTP/1.1");

        Assert.NotNull(r);
        Assert.Equal("%ZZ", r!.Param("number"));
    }

    [Fact]
    public void RepeatedParam_LastWins() =>
        Assert.Equal("2", HttpRequest.ParseRequestLine("GET /call?number=1&number=2 HTTP/1.1")!.Param("number"));
}

/// <summary>
/// Маршрутизация и защита локального API. Он умеет ЗВОНИТЬ, поэтому проверка токена —
/// не формальность: без неё любая открытая в браузере страница смогла бы дёрнуть
/// 127.0.0.1 обычной картинкой и позвонить куда угодно от имени владельца телефона.
/// </summary>
public class LocalApiRouteTests
{
    private sealed class FakeCommands : ICallCommands
    {
        public string? Called;
        public bool HangupCalled, AnswerCalled;
        public bool CallResult = true;

        public bool Call(string number) { Called = number; return CallResult; }
        public bool Hangup() { HangupCalled = true; return true; }
        public bool Answer() { AnswerCalled = true; return true; }
        public ApiStatus Status() => new(true, "377", "idle", "1.2.0");
    }

    private const string Token = "secrettoken";

    private static (LocalApiServer, FakeCommands) Make()
    {
        var cmd = new FakeCommands();
        var server = new LocalApiServer(cmd);
        // Start() поднял бы сокет; для маршрутизации это не нужно, токен задаём через него же
        // только в живом режиме — здесь достаточно рефлексии-свободного пути: Route принимает
        // токен из запроса и сверяет с полем, которое выставляет Start.
        return (server, cmd);
    }

    private static (int, string) Route(LocalApiServer s, string line) =>
        s.Route(HttpRequest.ParseRequestLine(line)!, new Dictionary<string, string>());

    [Fact]
    public void WithoutToken_IsUnauthorized()
    {
        var (server, cmd) = Make();
        server.Start(0, Token);          // порт 0 — система выдаст свободный
        try
        {
            var (status, _) = Route(server, "GET /call?number=377 HTTP/1.1");

            Assert.Equal(401, status);
            Assert.Null(cmd.Called);     // и, главное, звонка НЕ случилось
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void WrongToken_IsUnauthorized()
    {
        var (server, cmd) = Make();
        server.Start(0, Token);
        try
        {
            var (status, _) = Route(server, "GET /call?number=377&token=nope HTTP/1.1");

            Assert.Equal(401, status);
            Assert.Null(cmd.Called);
        }
        finally { server.Stop(); }
    }

    // Даже /status закрыт токеном: без этого посторонняя страница узнала бы, что на машине
    // стоит софтфон и под каким номером человек зарегистрирован.
    [Fact]
    public void Status_AlsoRequiresToken()
    {
        var (server, _) = Make();
        server.Start(0, Token);
        try
        {
            Assert.Equal(401, Route(server, "GET /status HTTP/1.1").Item1);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void ValidToken_PlacesCall()
    {
        var (server, cmd) = Make();
        server.Start(0, Token);
        try
        {
            var (status, json) = Route(server, $"GET /call?number=377&token={Token} HTTP/1.1");

            Assert.Equal(200, status);
            Assert.Equal("377", cmd.Called);
            Assert.Contains("\"ok\":true", json);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void TokenInHeader_IsAccepted()
    {
        var (server, cmd) = Make();
        server.Start(0, Token);
        try
        {
            var headers = new Dictionary<string, string> { ["X-Token"] = Token };
            var (status, _) = server.Route(HttpRequest.ParseRequestLine("POST /call?number=401 HTTP/1.1")!, headers);

            Assert.Equal(200, status);
            Assert.Equal("401", cmd.Called);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void CallWithoutNumber_IsBadRequest()
    {
        var (server, cmd) = Make();
        server.Start(0, Token);
        try
        {
            Assert.Equal(400, Route(server, $"GET /call?token={Token} HTTP/1.1").Item1);
            Assert.Null(cmd.Called);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void RejectedCall_IsConflict()
    {
        var (server, cmd) = Make();
        cmd.CallResult = false;
        server.Start(0, Token);
        try
        {
            Assert.Equal(409, Route(server, $"GET /call?number=377&token={Token} HTTP/1.1").Item1);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void UnknownPath_IsNotFound()
    {
        var (server, _) = Make();
        server.Start(0, Token);
        try
        {
            Assert.Equal(404, Route(server, $"GET /выключи-свет?token={Token} HTTP/1.1").Item1);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void Status_ReportsAccount()
    {
        var (server, _) = Make();
        server.Start(0, Token);
        try
        {
            var (status, json) = Route(server, $"GET /status?token={Token} HTTP/1.1");

            Assert.Equal(200, status);
            Assert.Contains("\"account\":\"377\"", json);
            Assert.Contains("\"registered\":true", json);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void EmptyToken_NeverStarts()
    {
        var (server, _) = Make();

        Assert.False(server.Start(0, ""));
        Assert.False(server.IsRunning);
    }

    // Имя абонента попадает в JSON как есть; кавычка в нём сломала бы ответ, а управляющий
    // символ — разбор на стороне CRM.
    [Theory]
    [InlineData("Иван \"Ваня\" П.", "\"Иван \\\"Ваня\\\" П.\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("строка\nвторая", "\"строка\\nвторая\"")]
    public void Json_EscapesDangerousCharacters(string input, string expected) =>
        Assert.Equal(expected, LocalApiServer.Quote(input));

    [Fact]
    public void NewToken_IsLongAndUnique()
    {
        string a = LocalApiServer.NewToken(), b = LocalApiServer.NewToken();

        Assert.Equal(32, a.Length);
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('0', a);   // без символов, которые путают при переписывании
        Assert.DoesNotContain('l', a);
    }

    // ------------------------------------------------------------------
    // Проверка Host: защита от DNS rebinding
    // ------------------------------------------------------------------
    // Слушать только петлю недостаточно. Страница на evil.com, чей домен через несколько
    // секунд начинает резолвиться в 127.0.0.1, для браузера остаётся ТЕМ ЖЕ origin — CORS
    // не применяется, и ответ читается. Подменить DNS атакующий может; заставить браузер
    // отправить чужой Host — нет.

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:8712")]
    [InlineData("localhost")]
    [InlineData("localhost:8712")]
    [InlineData("[::1]:8712")]
    public void LoopbackHost_IsAccepted(string host)
    {
        Assert.True(LocalApiServer.HostIsLoopback(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = host }));
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("evil.com:8712")]
    [InlineData("attacker.internal")]
    public void ForeignHost_IsRejected(string host)
    {
        Assert.False(LocalApiServer.HostIsLoopback(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = host }));
    }

    [Fact]
    public void MissingHostHeader_IsAccepted()
    {
        // HTTP/1.0 и простейшие скрипты Host не шлют, а защищаемся мы от браузера,
        // который шлёт его всегда.
        Assert.True(LocalApiServer.HostIsLoopback(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void OptionsRequest_DoesNotPlaceCall()
    {
        // Разбор шёл ТОЛЬКО по пути, поэтому «OPTIONS /call?number=…» реально звонил.
        // А OPTIONS — это ровно то, что браузер шлёт сам, предварительным запросом, прежде
        // чем решить, позволено ли ему вообще обращаться к нашему адресу.
        var calls = new RecordingCommands();
        var server = new LocalApiServer(calls);
        server.Start(0, "tok");
        try
        {
            var req = HttpRequest.ParseRequestLine("OPTIONS /call?number=112&token=tok HTTP/1.1")!;
            var (status, _) = server.Route(req, Empty);

            Assert.Equal(204, status);
            Assert.Empty(calls.Called);   // ключевое: звонка НЕ было
        }
        finally { server.Dispose(); }
    }

    private static readonly Dictionary<string, string> Empty =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Двойник, который только записывает, что у него просили сделать.</summary>
    private sealed class RecordingCommands : ICallCommands
    {
        public readonly List<string> Called = new();
        public bool Call(string number) { Called.Add(number); return true; }
        public bool Hangup() { Called.Add("hangup"); return true; }
        public bool Answer() { Called.Add("answer"); return true; }
        public ApiStatus Status() => new(false, "", "idle", "1.0");
    }
}
