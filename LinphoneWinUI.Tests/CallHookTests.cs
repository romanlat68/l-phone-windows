using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Подстановка данных звонка в шаблон URL для CRM. Ошибка здесь ведёт не к падению, а к
/// молча неверной ссылке: карточка откроется не на том клиенте либо не откроется вовсе,
/// и оператор снимет трубку вслепую.
/// </summary>
public class CallHookTests
{
    private static readonly CallHookData Incoming =
        new(CallHookEvent.Incoming, "+79993770000", "Иван Петров", 0, "abc123@pbx");

    [Fact]
    public void Number_IsSubstituted() =>
        Assert.Equal("https://crm/card?p=%2B79993770000",
            CallHook.Format("https://crm/card?p={number}", Incoming));

    // Плюс, пробелы и кириллица должны быть закодированы, иначе ссылка развалится
    // на первом же пробеле.
    [Fact]
    public void Name_IsUrlEncoded()
    {
        string url = CallHook.Format("https://crm/card?n={name}", Incoming);

        Assert.DoesNotContain(" ", url);
        Assert.Contains("%20", url);
    }

    [Fact]
    public void SeveralPlaceholders_AreAllSubstituted()
    {
        string url = CallHook.Format("https://crm/e?ev={event}&dir={direction}&id={callid}", Incoming);

        Assert.Contains("ev=incoming", url);
        Assert.Contains("dir=in", url);
        Assert.Contains("id=abc123%40pbx", url);
    }

    [Fact]
    public void Outgoing_HasOutDirection() =>
        Assert.Contains("dir=out", CallHook.Format("https://crm/e?dir={direction}",
            new CallHookData(CallHookEvent.Outgoing, "377", "", 0, "")));

    [Fact]
    public void Duration_IsSubstituted() =>
        Assert.Equal("https://crm/e?s=65", CallHook.Format("https://crm/e?s={duration}",
            new CallHookData(CallHookEvent.Ended, "377", "", 65, "")));

    [Fact]
    public void PlaceholderCase_IsIgnored() =>
        Assert.Equal("https://crm/e?n=377",
            CallHook.Format("https://crm/e?n={NUMBER}", new CallHookData(CallHookEvent.Incoming, "377", "", 0, "")));

    // Опечатку в имени подстановки оставляем на виду. Съев её молча, мы спрятали бы причину
    // того, что CRM получает пустой параметр.
    [Fact]
    public void UnknownPlaceholder_IsLeftAsIs() =>
        Assert.Equal("https://crm/e?x={phone}", CallHook.Format("https://crm/e?x={phone}", Incoming));

    [Fact]
    public void UnclosedBrace_DoesNotBreak() =>
        Assert.Equal("https://crm/e?x={number", CallHook.Format("https://crm/e?x={number", Incoming));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTemplate_GivesEmpty(string? template) =>
        Assert.Equal("", CallHook.Format(template, Incoming));

    [Fact]
    public void TemplateWithoutPlaceholders_IsUnchanged() =>
        Assert.Equal("https://crm/ping", CallHook.Format("https://crm/ping", Incoming));

    // Шаблон задаёт пользователь. Разрешить произвольную схему значило бы превратить поле
    // настроек в «запусти что угодно»: file:// открывает проводник, а какая-нибудь
    // зарегистрированная схема — стороннюю программу.
    [Theory]
    [InlineData("http://crm/card", true)]
    [InlineData("https://crm/card", true)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("ftp://server/file", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyHttpIsBrowsable(string? url, bool expected) =>
        Assert.Equal(expected, CallHook.IsBrowsable(url));
}
