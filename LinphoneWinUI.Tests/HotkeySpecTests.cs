using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Формат сочетаний горячих клавиш. Строка живёт в конфигурации и правится руками, поэтому
/// испорченное значение не должно ни ронять приложение, ни молча оставлять пользователя
/// без клавиши. Отдельная забота — не дать зарегистрировать сочетание без модификатора:
/// такая «горячая клавиша» отобрала бы обычную букву у всей системы.
/// </summary>
public class HotkeySpecTests
{
    [Theory]
    [InlineData("ctrl+alt+A", "Ctrl+Alt+A")]
    [InlineData("CTRL+ALT+a", "Ctrl+Alt+A")]
    [InlineData("alt+ctrl+A", "Ctrl+Alt+A")]     // порядок ввода не важен, вид canonical
    [InlineData(" ctrl + shift + F5 ", "Ctrl+Shift+F5")]
    [InlineData("ctrl+alt+shift+9", "Ctrl+Alt+Shift+9")]
    [InlineData("ctrl+Space", "Ctrl+Space")]
    [InlineData("ctrl+alt+Delete", "Ctrl+Alt+Delete")]
    public void Parse_ThenFormat_IsCanonical(string input, string expected)
    {
        var spec = HotkeySpec.Parse(input);

        Assert.NotNull(spec);
        Assert.Equal(expected, spec!.Value.Format());
    }

    [Fact]
    public void Parse_ThenFormat_RoundTrips()
    {
        foreach (string s in new[] { "Ctrl+Alt+A", "Ctrl+Shift+F12", "Alt+Shift+7", "Ctrl+Alt+Shift+Home" })
            Assert.Equal(s, HotkeySpec.Parse(s)!.Value.Format());
    }

    // Без модификатора мы забрали бы у системы обычную букву: «A» перестала бы печататься
    // во всех программах сразу.
    [Theory]
    [InlineData("A")]
    [InlineData("F5")]
    [InlineData("Space")]
    public void BareKey_IsRejected(string input) => Assert.Null(HotkeySpec.Parse(input));

    // Одна клавиша Win без другого модификатора почти всегда конфликтует с системой.
    [Fact]
    public void WinAlone_IsRejected() => Assert.Null(HotkeySpec.Parse("win+A"));

    [Fact]
    public void WinWithOtherModifier_IsAllowed() =>
        Assert.NotNull(HotkeySpec.Parse("ctrl+win+A"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl+alt")]            // только модификаторы, клавиши нет
    [InlineData("ctrl+alt+A+B")]        // две клавиши
    [InlineData("ctrl+alt+Ъ")]          // нераспознанная клавиша
    [InlineData("ctrl+alt+F25")]        // такой F-клавиши не бывает
    [InlineData("мусор")]
    public void Garbage_IsRejected(string? input) => Assert.Null(HotkeySpec.Parse(input));

    [Fact]
    public void KeyName_CoversLettersDigitsAndFunctionKeys()
    {
        Assert.Equal("A", HotkeySpec.KeyName(0x41));
        Assert.Equal("Z", HotkeySpec.KeyName(0x5A));
        Assert.Equal("0", HotkeySpec.KeyName(0x30));
        Assert.Equal("F1", HotkeySpec.KeyName(0x70));
        Assert.Equal("F24", HotkeySpec.KeyName(0x87));
    }

    // Неизвестный код не должен превращаться в пустую строку: пользователь увидит хотя бы
    // шестнадцатеричное значение и поймёт, что сочетание задано, а не потеряно.
    [Fact]
    public void UnknownKey_ShowsCode() => Assert.StartsWith("0x", HotkeySpec.KeyName(0xFF));

    [Fact]
    public void Empty_FormatsToEmpty() => Assert.Equal("", new HotkeySpec(HotkeySpec.ModControl, 0).Format());
}
