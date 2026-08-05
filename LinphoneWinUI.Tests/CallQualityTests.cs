using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Классификация качества связи. Ошибка здесь стоит доверия к индикатору: показать «плохо»
/// на исправной линии — значит научить оператора не смотреть на палочки вовсе, а показать
/// «отлично» при разваливающемся разговоре — значит оставить его без объяснения, почему
/// собеседник переспрашивает.
/// </summary>
public class CallQualityTests
{
    // Пока RTCP не ответил, судить не по чему. «Плохо» в этот момент — клевета на связь.
    [Fact]
    public void NoRating_IsUnknown() => Assert.Equal(QualityLevel.Unknown, CallQuality.None.Level);

    [Fact]
    public void NegativeRating_IsUnknown() =>
        Assert.Equal(QualityLevel.Unknown, new CallQuality(-1f, 0f, 0, 0).Level);

    [Fact]
    public void CleanLine_IsExcellent() =>
        Assert.Equal(QualityLevel.Excellent, new CallQuality(4.5f, 0.1f, 40, 5).Level);

    [Theory]
    [InlineData(1.0f, QualityLevel.Bad)]
    [InlineData(2.5f, QualityLevel.Fair)]
    [InlineData(3.5f, QualityLevel.Good)]
    [InlineData(4.2f, QualityLevel.Excellent)]
    public void Rating_MapsToLevel(float rating, QualityLevel expected) =>
        Assert.Equal(expected, new CallQuality(rating, 0f, 30, 3).Level);

    // Главное правило: потери перебивают благодушную оценку библиотеки. Разговор с 6 %
    // потерь рвётся на слух, каким бы ни был расчётный балл.
    [Fact]
    public void HighLoss_OverridesGoodRating() =>
        Assert.Equal(QualityLevel.Bad, new CallQuality(5f, 6f, 20, 2).Level);

    [Fact]
    public void ModerateLoss_CapsAtFair() =>
        Assert.Equal(QualityLevel.Fair, new CallQuality(5f, 3.5f, 20, 2).Level);

    // Задержка свыше 300 мс — разговор с перебиваниями (G.114), даже когда пакеты доходят все.
    [Fact]
    public void HighLatency_OverridesGoodRating() =>
        Assert.Equal(QualityLevel.Fair, new CallQuality(5f, 0f, 350, 2).Level);

    [Fact]
    public void VeryHighLatency_IsBad() =>
        Assert.Equal(QualityLevel.Bad, new CallQuality(5f, 0f, 600, 2).Level);

    // Уровень — это МИНИМУМ по всем признакам: одна плохая метрика портит разговор целиком.
    [Fact]
    public void WorstMetricWins() =>
        Assert.Equal(QualityLevel.Bad, new CallQuality(1.5f, 0f, 10, 1).Level);

    [Fact]
    public void Describe_ListsMeasuredMetrics()
    {
        string s = new CallQuality(4.2f, 1.5f, 120, 8).Describe();

        Assert.Contains("1,5%", s.Replace(".", ","));
        Assert.Contains("120", s);
        Assert.Contains("8", s);
    }

    // Ноль у задержки означает «RTCP ещё не ответил», а не «нулевая задержка»: выдавать его
    // за измерение нельзя.
    [Fact]
    public void Describe_OmitsUnmeasuredLatencyAndJitter()
    {
        string s = new CallQuality(4.2f, 0.5f, 0, 0).Describe();

        Assert.DoesNotContain(Loc.S("Q_Rtt"), s);
        Assert.DoesNotContain(Loc.S("Q_Jitter"), s);
    }

    [Fact]
    public void Describe_EmptyWhenNoRating() => Assert.Equal("", CallQuality.None.Describe());
}
