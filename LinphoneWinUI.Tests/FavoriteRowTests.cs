using LinphoneWinUI;
using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

public class FavoriteRowTests
{
    [Theory]
    [InlineData("375", true)]                 // внутренний — перехват возможен
    [InlineData("sip:376@pbx", true)]
    [InlineData("+79963360197", false)]       // мобильный — перехвата нет
    [InlineData("88005553535", false)]
    public void CanPickup_OnlyForInternalNumbers(string number, bool expected)
    {
        var row = new FavoriteRow(new FavoriteEntry("Коллега", number), PresenceStatus.Available);
        Assert.Equal(expected, row.CanPickup);
    }

    [Fact]
    public void Status_IsCarriedForTheBlfLamp()
    {
        var row = new FavoriteRow(new FavoriteEntry("П", "375"), PresenceStatus.OnThePhone);
        Assert.Equal(PresenceStatus.OnThePhone, row.Status);
    }

    [Theory]
    [InlineData(PresenceStatus.Available)]     // 🟢
    [InlineData(PresenceStatus.OnThePhone)]    // 🔴 занят
    [InlineData(PresenceStatus.DoNotDisturb)]  // 🔴 dnd
    [InlineData(PresenceStatus.Away)]          // 🟡 отошёл
    [InlineData(PresenceStatus.Offline)]       // ⚪
    public void PresenceColor_IsDefinedForEveryStatus(PresenceStatus s)
    {
        var c = PresenceUi.Color(s);
        Assert.True(c.A > 0);   // непрозрачный цвет для лампы
    }
}
