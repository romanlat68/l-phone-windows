using System.Collections.Generic;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Как сохранённая в rc строка превращается в реально занимаемое сочетание. Правило разное
/// для «ещё не настраивали» и «настроили и убрали», и перепутать их нельзя: в первом случае
/// клавиша должна работать сразу, во втором — не возвращаться вопреки решению пользователя.
/// Этой функцией пользуются и регистрация в главном окне, и показ в настройках, поэтому
/// расхождение здесь означало бы «в настройках написано одно, работает другое».
/// </summary>
public class HotkeyConfigTests
{
    // Настройки не трогали — работают клавиши по умолчанию.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_UsesDefault(string? saved)
    {
        foreach (var action in GlobalHotkeys.AllActions)
            Assert.Equal(GlobalHotkeys.Defaults[action], GlobalHotkeys.Effective(action, saved));
    }

    // Пользователь снял назначение — клавиши быть не должно.
    [Fact]
    public void Dash_MeansNoHotkey()
    {
        foreach (var action in GlobalHotkeys.AllActions)
            Assert.Null(GlobalHotkeys.Effective(action, "-"));
    }

    [Fact]
    public void Saved_IsUsedAsIs()
    {
        var spec = GlobalHotkeys.Effective(HotkeyAction.Answer, "Ctrl+Shift+F9");

        Assert.Equal("Ctrl+Shift+F9", spec!.Value.Format());
    }

    // Испорченный конфиг (правили руками, ошиблись) не должен оставлять оператора без
    // клавиши — откатываемся к умолчанию, а не к «не назначено».
    [Theory]
    [InlineData("мусор")]
    [InlineData("A")]              // без модификатора — зарегистрировать нельзя
    [InlineData("ctrl+alt+Ъ")]
    public void Garbage_FallsBackToDefault(string saved)
    {
        Assert.Equal(GlobalHotkeys.Defaults[HotkeyAction.Mute], GlobalHotkeys.Effective(HotkeyAction.Mute, saved));
    }

    // Умолчания обязаны быть регистрируемыми и различаться между собой, иначе часть
    // действий молча не получит клавишу.
    [Fact]
    public void Defaults_AreValidAndDistinct()
    {
        var seen = new HashSet<HotkeySpec>();
        foreach (var action in GlobalHotkeys.AllActions)
        {
            var spec = GlobalHotkeys.Defaults[action];
            Assert.True(spec.IsValid, $"умолчание для {action} нельзя зарегистрировать");
            Assert.True(seen.Add(spec), $"умолчание {spec.Format()} повторяется");
        }
    }

    // То, что показано в настройках, обязано разбираться обратно — иначе после
    // перезапуска пользователь получит не ту клавишу, которую видел.
    [Fact]
    public void FormattedDefault_ParsesBack()
    {
        foreach (var action in GlobalHotkeys.AllActions)
        {
            var spec = GlobalHotkeys.Defaults[action];
            Assert.Equal(spec, GlobalHotkeys.Effective(action, spec.Format()));
        }
    }
}
