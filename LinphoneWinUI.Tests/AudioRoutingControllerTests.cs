using System;
using System.Collections.Generic;
using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Аудиомаршрутизация: что происходит со звуком, когда гарнитуру втыкают и выдёргивают.
///
/// До выноса из MainWindow эти правила не проверялись ВООБЩЕ, а каждое из них родилось из
/// живой поломки: бесконечный цикл перечитывания (приложение съедало процессор), молчащий
/// при отключении Bluetooth и звук, остающийся в снятой гарнитуре, потому что
/// псевдоустройство «по умолчанию» из списка не исчезает.
/// </summary>
public class AudioRoutingControllerTests
{
    private readonly FakeSipService _sip = new();
    private readonly List<string> _log = new();
    private int _switched;

    private AudioRoutingController New()
    {
        var c = new AudioRoutingController(_sip, m => _log.Add(m));
        c.DevicesSwitched += () => _switched++;
        return c;
    }

    /// <summary>Задать состав устройств и текущий выбор.</summary>
    private void Devices(string[] mics, string current)
    {
        _sip.InputDeviceIds = mics;
        _sip.OutputDeviceIds = mics;
        _sip.InputDeviceId = current;
        _sip.OutputDeviceId = current;
    }

    [Fact]
    public void PreferredDeviceReturns_ItIsSelectedBack()
    {
        // Гарнитуру выдернули и воткнули обратно — звук обязан вернуться в неё, а не остаться
        // во встроенном динамике.
        Devices(new[] { "usb-headset", "builtin" }, "usb-headset");
        var c = New();
        c.RememberChoice();

        Devices(new[] { "builtin" }, "builtin");   // гарнитуру выдернули
        c.Apply();
        Devices(new[] { "usb-headset", "builtin" }, "builtin");   // воткнули обратно
        c.Apply();

        Assert.Equal("usb-headset", _sip.InputDeviceId);
        Assert.Equal("usb-headset", _sip.OutputDeviceId);
    }

    [Fact]
    public void SelfReload_DoesNotTriggerAnotherReload()
    {
        // Правило 1: перечитывание САМО поднимает событие «список изменился». Ответить на
        // него новым перечитыванием — бесконечный цикл, который однажды подвесил рабочую
        // машину. Своё эхо должно распознаваться и игнорироваться.
        Devices(new[] { "builtin" }, "builtin");
        var c = New();

        c.CheckDrift();                       // наше перечитывание
        int reloadsAfterDrift = _sip.ReloadCount;
        c.OnDevicesChanged();                 // эхо от ядра, пришедшее следом

        Assert.Equal(reloadsAfterDrift, _sip.ReloadCount);   // второго перечитывания не было
    }

    [Fact]
    public void DriftWithoutEvent_IsNoticedAndHandled()
    {
        // Правило 2: событие об отключении приходит не от всякого драйвера (Bluetooth молчит).
        Devices(new[] { "bt-headset", "builtin" }, "bt-headset");
        var c = New();
        c.RememberChoice();
        c.CheckDrift();          // запомнили состав

        Devices(new[] { "builtin" }, "builtin");   // наушники пропали молча
        c.CheckDrift();

        Assert.Contains(_log, m => m.Contains("без уведомления ядра"));
    }

    [Fact]
    public void SystemDefaultDevice_IsReboundByForce()
    {
        // Правило 3: «по умолчанию» из списка не исчезает никогда, поэтому правило «текущего
        // больше нет» на нём не срабатывает, а повторная установка того же устройства ядро не
        // перепривязывает. Поток надо пересоздать силой — уйти на соседнее и вернуться.
        Devices(new[] { "Default Capture", "builtin" }, "Default Capture");
        var c = New();
        c.RememberChoice();

        _sip.InputDeviceSets.Clear();
        c.Apply();

        // Именно ДВА присвоения: сначала соседнее, затем прежнее.
        Assert.Equal(new[] { "builtin", "Default Capture" }, _sip.InputDeviceSets);
        Assert.Contains(_log, m => m.Contains("перепривязан"));
        Assert.True(_switched > 0);   // и View об этом узнал
    }

    [Fact]
    public void NothingChanged_NoSwitchReported()
    {
        // Ложная тревога хуже молчания: подсказка «звук переключён» на каждое обновление
        // списка приучает её не замечать.
        Devices(new[] { "usb-headset", "builtin" }, "usb-headset");
        var c = New();
        c.RememberChoice();

        c.Apply();

        Assert.Equal(0, _switched);
    }
}
