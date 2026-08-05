using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Tag уведомления чата. WinRT ограничивает его 16 символами и допускает только буквы и
/// цифры — при нарушении Show БРОСАЕТ, и уведомление не показывается вовсе. Проверять это
/// вживую пришлось бы входящим сообщением от абонента с длинным адресом.
/// </summary>
public class ToastTagTests
{
    [Theory]
    [InlineData("sip:378@sip.example.com")]
    [InlineData("378")]
    [InlineData("\"Роман\" <sip:378@pbx>")]
    [InlineData("очень-длинный-адрес-собеседника@какой-то.домен.example.com")]
    public void Tag_FitsWinRtLimits(string peer)
    {
        string tag = ToastNotifier.SafeTag(peer);

        Assert.InRange(tag.Length, 1, 16);
        Assert.All(tag, c => Assert.True(char.IsLetterOrDigit(c), $"недопустимый символ '{c}'"));
    }

    // Пустой tag WinRT тоже не принимает — подставляем запасной.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@#$%^&*()")]
    [InlineData("   ")]
    public void Tag_IsNeverEmpty(string? peer) => Assert.NotEmpty(ToastNotifier.SafeTag(peer));

    // Берём ХВОСТ адреса: у SIP-адресов различающая часть (номер) стоит в начале, но домен
    // общий и длинный — обрезая начало, мы бы схлопнули всех абонентов в один тег.
    [Fact]
    public void LongAddresses_WithSameDomain_StillDiffer()
    {
        string a = ToastNotifier.SafeTag("sip:377@sip.example.com");
        string b = ToastNotifier.SafeTag("sip:378@sip.example.com");

        Assert.NotEqual(a, b);
    }
}
