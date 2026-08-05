using System.Collections.Generic;
using System.Linq;
using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Отбор записей журнала. Это единственное место, решающее, что оператор увидит в списке,
/// поэтому проверяется отдельно от интерфейса. Главная опасность здесь — не «не нашли»,
/// а «нашли не то и спрятали остальное»: пропущенный звонок, не попавший в список, для
/// человека равен несуществующему.
/// </summary>
public class HistoryFilterTests
{
    private static CallLogEntry E(string remote, bool incoming, bool missed = false, string title = "") =>
        new() { Remote = remote, Incoming = incoming, Missed = missed, Title = title };

    private static readonly List<CallLogEntry> Log = new()
    {
        E("sip:377@pbx.example.com", incoming: false, title: "377"),
        E("sip:378@pbx.example.com", incoming: true, title: "Иванов"),
        E("sip:401@pbx.example.com", incoming: true, missed: true, title: "Отдел продаж"),
        E("sip:+79993771234@pbx.example.com", incoming: false, title: "+7 999 377-12-34"),
    };

    private static string[] Remotes(HistoryFilter f, string? q) =>
        HistoryViewModel.Match(Log, f, q).Select(e => e.Remote).ToArray();

    [Fact]
    public void All_WithoutQuery_ReturnsEverything() =>
        Assert.Equal(Log.Count, Remotes(HistoryFilter.All, "").Length);

    [Fact]
    public void Missed_ReturnsOnlyMissed() =>
        Assert.Equal(new[] { "sip:401@pbx.example.com" }, Remotes(HistoryFilter.Missed, ""));

    // Пропущенный — тоже входящий. Если «Входящие» его скроют, список будет врать о том,
    // кто звонил, и человек не перезвонит.
    [Fact]
    public void Incoming_IncludesMissed()
    {
        var r = Remotes(HistoryFilter.Incoming, "");

        Assert.Contains("sip:401@pbx.example.com", r);
        Assert.Equal(2, r.Length);
    }

    [Fact]
    public void Outgoing_ExcludesIncoming() =>
        Assert.All(HistoryViewModel.Match(Log, HistoryFilter.Outgoing, ""), e => Assert.False(e.Incoming));

    [Fact]
    public void Query_MatchesName() =>
        Assert.Equal(new[] { "sip:378@pbx.example.com" }, Remotes(HistoryFilter.All, "иван"));

    [Fact]
    public void Query_IsCaseInsensitive() =>
        Assert.Single(Remotes(HistoryFilter.All, "ИВАНОВ"));

    // Номер набирают как попало: «+7 (999) 377-12-34», «79993771234». Разделители не должны
    // мешать поиску.
    [Theory]
    [InlineData("79993771234")]
    [InlineData("3771234")]
    [InlineData("+7 999 377")]
    public void Query_IgnoresSeparatorsInNumbers(string q) =>
        Assert.Contains("sip:+79993771234@pbx.example.com", Remotes(HistoryFilter.All, q));

    [Fact]
    public void Query_MatchesShortExtension()
    {
        var r = Remotes(HistoryFilter.All, "377");

        Assert.Contains("sip:377@pbx.example.com", r);
        Assert.Contains("sip:+79993771234@pbx.example.com", r);   // 377 внутри номера — тоже совпадение
    }

    // Домен есть у КАЖДОЙ записи: если искать по нему, любой запрос вроде «ru» или «pbx»
    // выдавал бы весь журнал — поиск стал бы бесполезным.
    [Theory]
    [InlineData("example")]
    [InlineData("ru")]
    [InlineData("sip")]
    public void Query_DoesNotMatchDomainOrScheme(string q) =>
        Assert.Empty(Remotes(HistoryFilter.All, q));

    [Fact]
    public void QueryAndFilter_CombineAsAnd() =>
        Assert.Empty(Remotes(HistoryFilter.Outgoing, "иван"));   // Роман — входящий

    [Fact]
    public void Whitespace_Query_IsIgnored() =>
        Assert.Equal(Log.Count, Remotes(HistoryFilter.All, "   ").Length);

    [Fact]
    public void NoMatch_ReturnsEmpty() =>
        Assert.Empty(Remotes(HistoryFilter.All, "999999999"));

    [Theory]
    [InlineData("sip:377@pbx.example.com", "377")]
    [InlineData("sips:377@pbx", "377")]
    [InlineData("377@pbx", "377")]
    [InlineData("377", "377")]
    [InlineData("", "")]
    public void UserPart_StripsSchemeAndDomain(string remote, string expected) =>
        Assert.Equal(expected, HistoryViewModel.UserPart(remote));
}
