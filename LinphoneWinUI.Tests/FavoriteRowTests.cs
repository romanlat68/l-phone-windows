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

    // ------------------------------------------------------------------
    // Статус наблюдаемый — пульт обновляется без пересборки
    // ------------------------------------------------------------------
    // Раньше Load() делал Clear() + Add() на каждый presence-NOTIFY, а NOTIFY приходит дважды
    // за каждый звонок каждого коллеги. GridView выбрасывал и строил заново все сорок плиток:
    // человек терял прокрутку, подсветку под курсором и начатое перетаскивание звонка на
    // плитку — то есть ломалась ровно та функция, ради которой пульт и сделан.

    [Fact]
    public void ChangingStatus_RaisesNotifications_ForLampAndEmoji()
    {
        var row = new FavoriteRow(new FavoriteEntry("Коллега", "375"), PresenceStatus.Available);
        var changed = new System.Collections.Generic.List<string>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        row.Status = PresenceStatus.OnThePhone;

        Assert.Contains(nameof(FavoriteRow.Status), changed);
        Assert.Contains(nameof(FavoriteRow.StatusEmojiText), changed);
        Assert.Contains(nameof(FavoriteRow.LampBrush), changed);
        Assert.Equal(PresenceUi.Emoji(PresenceStatus.OnThePhone), row.StatusEmojiText);
    }

    [Fact]
    public void RingingState_IsObservable()
    {
        // Ореол «звонит» перебивает presence. Саму кисть здесь не трогаем: SolidColorBrush
        // создаётся только на потоке XAML, а тест-хост его не поднимает (по этой же причине
        // кисти пульта не проверялись никогда). Проверяем то, от чего кисть зависит.
        var row = new FavoriteRow(new FavoriteEntry("Коллега", "375"), PresenceStatus.Available);
        var changed = new System.Collections.Generic.List<string>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        row.Dialog = DialogState.Early;

        Assert.True(row.IsRinging);
        Assert.Contains(nameof(FavoriteRow.LampBrush), changed);
        Assert.Contains(nameof(FavoriteRow.ActionHint), changed);
    }
}
