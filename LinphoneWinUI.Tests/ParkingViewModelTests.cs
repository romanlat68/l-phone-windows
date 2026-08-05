using System.Linq;
using LinphoneWinUI;
using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Состояние стоянки. Пропущенная занятость означает звонок, который стоит и нигде не показан:
/// человек ждёт на линии, а оператор его не видит. Ложная занятость не лучше — оператор жмёт
/// на пустой слот и слышит отбой.
/// </summary>
public class ParkingViewModelTests
{
    private static ParkingViewModel Make(string slots = "501-503", string ext = "500")
    {
        var sip = new FakeSipService();
        sip.Strings["park_slots"] = slots;
        sip.Strings["park_ext"] = ext;
        var vm = new ParkingViewModel(sip);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_BuildsSlotsFromSettings()
    {
        var vm = Make();

        Assert.Equal(new[] { "501", "502", "503" }, vm.Slots.Select(s => s.Slot));
        Assert.Equal("500", vm.ParkExtension);
        Assert.True(vm.Enabled);
    }

    // Настройки пусты — берём значения нашей АТС, чтобы на типовой конфигурации
    // ничего не требовалось настраивать руками.
    [Fact]
    public void EmptySettings_FallBackToDefaults()
    {
        var vm = new ParkingViewModel(new FakeSipService());
        vm.Reload();

        Assert.Equal("500", vm.ParkExtension);
        Assert.Equal(20, vm.Slots.Count);
    }

    [Fact]
    public void FreshParking_ShowsNothing()
    {
        var vm = Make();

        Assert.False(vm.AnyOccupied);
        Assert.Empty(vm.Occupied);
    }

    [Fact]
    public void Confirmed_MarksSlotBusy()
    {
        var vm = Make();

        vm.SetDialogState("502", DialogState.Confirmed);

        Assert.True(vm.AnyOccupied);
        Assert.Equal(1, vm.OccupiedCount);
        Assert.Equal("502", vm.Occupied.Single().Slot);
    }

    [Fact]
    public void Terminated_FreesSlot()
    {
        var vm = Make();
        vm.SetDialogState("502", DialogState.Confirmed);

        vm.SetDialogState("502", DialogState.Terminated);

        Assert.False(vm.AnyOccupied);
        Assert.Empty(vm.Occupied);
    }

    // «Не знаю» и «свободен» — разные вещи. Гасить плитку по неразобранному NOTIFY значит
    // потерять припаркованный звонок из виду.
    [Fact]
    public void Unknown_DoesNotFreeSlot()
    {
        var vm = Make();
        vm.SetDialogState("502", DialogState.Confirmed);

        vm.SetDialogState("502", DialogState.Unknown);

        Assert.True(vm.AnyOccupied);
    }

    // Слоты в списке показа обязаны идти в том же порядке, что и на стоянке: перескакивающие
    // плитки — верный способ промахнуться мимо нужной, когда звонок забирают в спешке.
    [Fact]
    public void Occupied_KeepsSlotOrder()
    {
        var vm = Make("501-505");

        vm.SetDialogState("504", DialogState.Confirmed);
        vm.SetDialogState("502", DialogState.Confirmed);
        vm.SetDialogState("505", DialogState.Confirmed);

        Assert.Equal(new[] { "502", "504", "505" }, vm.Occupied.Select(s => s.Slot));
    }

    [Fact]
    public void UnknownSlot_IsIgnored()
    {
        var vm = Make();

        vm.SetDialogState("777", DialogState.Confirmed);

        Assert.False(vm.AnyOccupied);
    }

    // АТС не сообщает, КТО стоит в слоте (проверено на живой АТС: в теле NOTIFY только
    // entity и state). Подпись достаётся от нас самих — когда паркуем мы.
    [Fact]
    public void OwnParking_LabelsSlotWithCaller()
    {
        var vm = Make();
        vm.NoteParking("Иван Петров");

        vm.SetDialogState("501", DialogState.Confirmed);

        Assert.Equal("Иван Петров", vm.Occupied.Single().Caller);
    }

    [Fact]
    public void Label_IsUsedOnce()
    {
        var vm = Make();
        vm.NoteParking("Иван Петров");
        vm.SetDialogState("501", DialogState.Confirmed);

        vm.SetDialogState("502", DialogState.Confirmed);

        Assert.Equal("", vm.Slots.Single(s => s.Slot == "502").Caller);
    }

    [Fact]
    public void FreedSlot_ForgetsCaller()
    {
        var vm = Make();
        vm.NoteParking("Иван Петров");
        vm.SetDialogState("501", DialogState.Confirmed);

        vm.SetDialogState("501", DialogState.Terminated);
        vm.SetDialogState("501", DialogState.Confirmed);

        Assert.Equal("", vm.Slots.Single(s => s.Slot == "501").Caller);
    }

    // Перечитывание настроек не должно гасить занятые слоты: список слотов тот же,
    // а состояние приходит редко — до следующего NOTIFY стоянка выглядела бы пустой.
    [Fact]
    public void Reload_WithSameSlots_KeepsOccupancy()
    {
        var vm = Make();
        vm.SetDialogState("502", DialogState.Confirmed);

        vm.Reload();

        Assert.True(vm.AnyOccupied);
    }

    [Fact]
    public void BadRange_DisablesParking()
    {
        var vm = Make(slots: "мусор");

        Assert.False(vm.Enabled);
        Assert.Empty(vm.Slots);
    }
}

