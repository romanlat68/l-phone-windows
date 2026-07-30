using Xunit;

namespace LinphoneWinUI.Tests;

public class FavoritesKeyTests
{
    [Theory]
    [InlineData("1001@pbx.local", "1001")]              // user-часть до @, только цифры
    [InlineData("sip:1001@pbx.local", "1001")]
    [InlineData("+7 (495) 123-45-67", "74951234567")]
    [InlineData("1001", "1001")]
    public void Key_UsesDigits(string input, string expected) =>
        Assert.Equal(expected, Favorites.Key(input));

    [Fact]
    public void Key_WithoutDigits_FallsBackToLowercasedAddress()
    {
        Assert.Equal("support@pbx", Favorites.Key("Support@PBX"));
        Assert.Equal("abc", Favorites.Key("ABC"));
    }

    [Fact]
    public void Key_SameSubscriberFromBookAndJournal_Matches()
    {
        // Один абонент: из книги «+7 495 1234567», из журнала «sip:74951234567@pbx».
        Assert.Equal(Favorites.Key("+7 495 1234567"), Favorites.Key("sip:74951234567@pbx"));
    }
}
