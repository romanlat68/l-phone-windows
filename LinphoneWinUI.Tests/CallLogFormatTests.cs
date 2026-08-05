using System;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Вид строки журнала. «Сегодня» и «вчера» считаются от переданного момента, а не от
/// системных часов — иначе тест, зелёный днём, падал бы в полночь, и наоборот: ошибку на
/// границе суток нельзя было бы поймать вовсе.
/// </summary>
public class CallLogFormatTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 14, 30, 0);

    [Fact]
    public void Today_ShowsTimeOnly()
    {
        string s = CallLogFormat.When(new DateTime(2026, 8, 3, 9, 5, 0), Now);

        Assert.DoesNotContain(".", s);          // это время, а не дата
        Assert.Contains("9", s);
    }

    // Ровно полночь — всё ещё сегодня. Была бы проверка «прошло меньше суток», этот
    // звонок уехал бы во «вчера».
    [Fact]
    public void Midnight_IsStillToday()
    {
        string s = CallLogFormat.When(new DateTime(2026, 8, 3, 0, 0, 0), Now);

        Assert.NotEqual("Вчера", s);
        Assert.DoesNotContain(".", s);
    }

    [Fact]
    public void Yesterday_ShowsWord() =>
        Assert.Equal(Loc.S("St_Yesterday"), CallLogFormat.When(new DateTime(2026, 8, 2, 23, 59, 0), Now));

    // Звонок в 23:59 вчера и в 00:01 сегодня разделены двумя минутами, но это разные дни —
    // и в журнале они обязаны выглядеть по-разному.
    [Fact]
    public void TwoMinutesApart_AcrossMidnight_DifferentLabels()
    {
        string late = CallLogFormat.When(new DateTime(2026, 8, 2, 23, 59, 0), Now);
        string early = CallLogFormat.When(new DateTime(2026, 8, 3, 0, 1, 0), Now);

        Assert.NotEqual(late, early);
    }

    [Fact]
    public void Older_ShowsDayAndMonth() =>
        Assert.Equal("31.07", CallLogFormat.When(new DateTime(2026, 7, 31, 8, 11, 0), Now));

    [Fact]
    public void LastYear_ShowsDayAndMonthWithoutYear() =>
        Assert.Equal("25.12", CallLogFormat.When(new DateTime(2025, 12, 25, 8, 11, 0), Now));

    [Theory]
    [InlineData(0, "")]
    [InlineData(5, "00:05")]
    [InlineData(65, "01:05")]
    [InlineData(3600, "60:00")]
    public void Duration_IsMinutesAndSeconds(int seconds, string expected) =>
        Assert.Equal(expected, CallLogFormat.Duration(seconds));

    // Пропущенный не имеет длительности: «00:00» рядом с номером читалось бы как разговор,
    // который состоялся и длился ноль секунд.
    [Fact]
    public void Missed_HasNoDurationPart()
    {
        string s = CallLogFormat.Subtitle(new DateTime(2026, 8, 3, 9, 5, 0), Now, seconds: 0);

        Assert.DoesNotContain("·", s);
        Assert.DoesNotContain("00:00", s);
    }

    [Fact]
    public void Answered_ShowsWhenAndDuration()
    {
        string s = CallLogFormat.Subtitle(new DateTime(2026, 7, 31, 8, 11, 0), Now, seconds: 65);

        Assert.Equal("31.07 · 01:05", s);
    }
}
