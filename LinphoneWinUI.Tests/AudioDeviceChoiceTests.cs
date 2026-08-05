using System;
using System.Collections.Generic;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Выбор аудиоустройства, когда список изменился посреди разговора. Ошибка тут стоит
/// разговора целиком: звук уходит в исчезнувшую гарнитуру, собеседник слышит тишину и
/// вешает трубку. Ошибка в другую сторону не лучше — переключить устройство без причины
/// значит оборвать звук в момент, когда всё работало.
/// </summary>
public class AudioDeviceChoiceTests
{
    private static readonly List<string> Built = new() { "speakers", "webcam-mic" };

    // Гарнитуру выдернули: текущего устройства больше нет, звук обязан уйти хоть куда-то.
    [Fact]
    public void CurrentGone_FallsBackToFirstAvailable() =>
        Assert.Equal("speakers", AudioDeviceChoice.Pick("headset", "headset", Built));

    // Гарнитуру воткнули обратно — возвращаемся на неё, а не остаёмся на встроенном динамике.
    [Fact]
    public void PreferredReturned_IsRestored()
    {
        var available = new List<string> { "speakers", "webcam-mic", "headset" };

        Assert.Equal("headset", AudioDeviceChoice.Pick("headset", "speakers", available));
    }

    // Ничего значимого не изменилось — не трогаем звук. Это правило важно не меньше
    // остальных: лишнее переключение слышно как щелчок и обрыв.
    [Fact]
    public void NothingChanged_KeepsCurrent() =>
        Assert.Null(AudioDeviceChoice.Pick("headset", "headset", new List<string> { "headset", "speakers" }));

    [Fact]
    public void PreferredAlreadyCurrent_DoesNothing() =>
        Assert.Null(AudioDeviceChoice.Pick("speakers", "speakers", Built));

    // Появилось постороннее устройство (веб-камера с микрофоном), но наше на месте —
    // перескакивать на новинку нельзя.
    [Fact]
    public void NewUnrelatedDevice_IsIgnored() =>
        Assert.Null(AudioDeviceChoice.Pick("speakers", "speakers",
            new List<string> { "speakers", "webcam-mic", "hdmi-tv" }));

    // Предпочтение не задано (пользователь ничего не выбирал) — работает только правило
    // «текущее пропало».
    [Fact]
    public void NoPreference_StillRescuesLostCurrent() =>
        Assert.Equal("speakers", AudioDeviceChoice.Pick("", "gone", Built));

    [Fact]
    public void NoPreference_AndCurrentAlive_DoesNothing() =>
        Assert.Null(AudioDeviceChoice.Pick(null, "speakers", Built));

    // Устройств не осталось вовсе — выбирать не из чего, и притворяться, что мы что-то
    // сделали, нельзя.
    [Fact]
    public void NoDevices_ReturnsNull() =>
        Assert.Null(AudioDeviceChoice.Pick("headset", "headset", new List<string>()));

    [Fact]
    public void NullList_ReturnsNull() => Assert.Null(AudioDeviceChoice.Pick("a", "b", null!));

    // Порядок в списке ядра меняется сам по себе; реагировать на перестановку значило бы
    // дёргать звук на ровном месте.
    [Fact]
    public void SetChanged_IgnoresOrder() =>
        Assert.False(AudioDeviceChoice.SetChanged(
            new List<string> { "a", "b" }, new List<string> { "b", "a" }));

    [Fact]
    public void SetChanged_DetectsAdded() =>
        Assert.True(AudioDeviceChoice.SetChanged(
            new List<string> { "a" }, new List<string> { "a", "b" }));

    [Fact]
    public void SetChanged_DetectsRemoved() =>
        Assert.True(AudioDeviceChoice.SetChanged(
            new List<string> { "a", "b" }, new List<string> { "a" }));

    [Fact]
    public void SetChanged_DetectsReplacement() =>
        Assert.True(AudioDeviceChoice.SetChanged(
            new List<string> { "a", "b" }, new List<string> { "a", "c" }));
}
