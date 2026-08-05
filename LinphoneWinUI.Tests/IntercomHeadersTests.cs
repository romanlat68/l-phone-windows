using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Заголовки «ответь сам» от АТС. Единого стандарта нет, и каждая система шлёт свою форму —
/// ошибка разбора означает либо неработающий интерком (звонок просто звонит), либо, что хуже,
/// софтфон, отвечающий сам на ОБЫЧНЫЕ звонки.
/// </summary>
public class IntercomHeadersTests
{
    // --- Формы, которые встречаются в жизни ---

    [Theory]
    [InlineData("Auto Answer")]                                        // Asterisk по умолчанию
    [InlineData("auto answer")]
    [InlineData("<http://pbx/alert>;info=alert-autoanswer")]           // FreePBX, Yealink
    [InlineData("info=alert-autoanswer")]
    [InlineData("Alert-Autoanswer")]
    [InlineData("<sip:pbx>;intercom")]                                 // некоторые сборки
    public void KnownForms_MeanAnswerImmediately(string alertInfo) =>
        Assert.Equal(0, IntercomHeaders.AutoAnswerDelaySeconds(alertInfo, null));

    [Theory]
    [InlineData(";answer-after=0", 0)]                                 // Grandstream, Snom
    [InlineData("<sip:pbx>;answer-after=0", 0)]                        // Polycom, Cisco
    [InlineData("<sip:pbx>;answer-after=5", 5)]
    [InlineData("answer-after=3", 3)]
    public void AnswerAfter_SetsDelay(string header, int expected) =>
        Assert.Equal(expected, IntercomHeaders.AutoAnswerDelaySeconds(header, null));

    [Fact]
    public void CallInfo_IsHonoredToo()
    {
        Assert.Equal(0, IntercomHeaders.AutoAnswerDelaySeconds(null, "<sip:pbx>;answer-after=0"));
        Assert.Equal(0, IntercomHeaders.AutoAnswerDelaySeconds(null, "Auto Answer"));
    }

    // --- Обычные звонки трогать нельзя ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<http://pbx/ring.wav>")]                  // просто мелодия звонка
    [InlineData("Bellcore-dr2")]                           // выбор рингтона
    [InlineData("<http://pbx/external>;info=external")]    // пометка внешнего вызова
    public void OrdinaryCalls_AreNotAutoAnswered(string? alertInfo) =>
        Assert.Null(IntercomHeaders.AutoAnswerDelaySeconds(alertInfo, null));

    // Заголовок приходит СНАРУЖИ: «ответить через час» означало бы занятую линию и
    // зависший экран входящего, поэтому задержка ограничена.
    [Fact]
    public void AbsurdDelay_IsClamped() =>
        Assert.Equal(60, IntercomHeaders.AutoAnswerDelaySeconds("answer-after=99999", null));

    [Fact]
    public void AnswerAfter_WithoutNumber_MeansImmediately() =>
        Assert.Equal(0, IntercomHeaders.AutoAnswerDelaySeconds("answer-after=", null));

    // Alert-Info важнее: если пришли оба, берём первый распознанный.
    [Fact]
    public void AlertInfo_WinsOverCallInfo() =>
        Assert.Equal(0, IntercomHeaders.AutoAnswerDelaySeconds("Auto Answer", "answer-after=9"));
}
