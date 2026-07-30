using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

public class DialerViewModelTests
{
    private static DialerViewModel NewVm(FakeSipService sip) =>
        new(sip, n => n == "42" ? "Петров" : null);

    [Fact]
    public void AppendDigit_UpdatesNumberFlagsAndMatch()
    {
        var vm = NewVm(new FakeSipService());

        vm.AppendDigit("4");
        Assert.Equal("4", vm.Number);
        Assert.True(vm.HasNumber);
        Assert.Equal("", vm.MatchedName);

        vm.AppendDigit("2");
        Assert.Equal("42", vm.Number);
        Assert.Equal("Петров", vm.MatchedName);   // имя совпавшего контакта под полем
    }

    [Fact]
    public void Backspace_RemovesLastDigit_AndClearsMatch()
    {
        var vm = NewVm(new FakeSipService());
        vm.Number = "42";

        vm.Backspace();
        Assert.Equal("4", vm.Number);
        Assert.Equal("", vm.MatchedName);

        vm.Backspace();
        Assert.Equal("", vm.Number);
        Assert.False(vm.HasNumber);

        vm.Backspace();   // на пустом поле — не падает
        Assert.Equal("", vm.Number);
    }

    [Fact]
    public void Call_TrimsNumber()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.Number = "  1001  ";

        vm.Call();

        Assert.Equal("1001", Assert.Single(sip.PlacedCalls));
    }

    [Fact]
    public void Call_EmptyNumber_DoesNothing()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.Number = "   ";

        vm.Call();

        Assert.Empty(sip.PlacedCalls);
    }
}
