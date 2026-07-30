using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

public class CallViewModelTests
{
    [Fact]
    public void AppendDtmf_Accumulates_ResetClears()
    {
        var vm = new CallViewModel();

        vm.AppendDtmf('1');
        vm.AppendDtmf('2');
        vm.AppendDtmf('#');
        Assert.Equal("12#", vm.DtmfText);

        vm.ResetDtmf();
        Assert.Equal("", vm.DtmfText);
    }
}

public class FavoritesViewModelTests
{
    [Fact]
    public void Load_FetchesStatuses_InOneBatchCall()
    {
        var sip = new FakeSipService();
        sip.Statuses["375"] = PresenceStatus.DoNotDisturb;
        var vm = new FavoritesViewModel(sip);

        vm.Load();

        // Ровно один переход на поток ядра за статусами, сколько бы ни было избранных:
        // список перечитывается на каждый presence-NOTIFY.
        Assert.Equal(1, sip.GetStatusesCalls);
    }
}

public class HistoryViewModelTests
{
    [Fact]
    public void Load_FillsEntries()
    {
        var sip = new FakeSipService();
        sip.CallHistory.Add(new CallLogEntry { Remote = "sip:1001@pbx", Title = "↙  1001", Subtitle = "12:00" });
        sip.CallHistory.Add(new CallLogEntry { Remote = "sip:1002@pbx", Title = "↗  1002", Subtitle = "13:00", Missed = true });
        var vm = new HistoryViewModel(sip);

        vm.Load();

        Assert.Equal(2, vm.Entries.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Load_EmptyJournal_SetsIsEmpty()
    {
        var vm = new HistoryViewModel(new FakeSipService());
        vm.Load();
        Assert.True(vm.IsEmpty);
    }

    // «Повтор последнего»: берём самый свежий ИСХОДЯЩИЙ по времени начала.
    // Порядок, в котором ядро отдаёт журнал, не документирован — на «первый в списке»
    // опираться нельзя (Android-клиент тоже выбирает по startDate).
    [Fact]
    public void LastDialed_PicksNewestOutgoing_IgnoringListOrder()
    {
        var log = new[]
        {
            new CallLogEntry { Remote = "sip:1001@pbx", Incoming = true,  StartUnix = 300 },  // входящий свежее
            new CallLogEntry { Remote = "sip:1002@pbx", Incoming = false, StartUnix = 100 },
            new CallLogEntry { Remote = "sip:1003@pbx", Incoming = false, StartUnix = 200 },  // ← он
        };

        Assert.Equal("sip:1003@pbx", HistoryViewModel.LastDialed(log));
    }

    [Fact]
    public void LastDialed_NoOutgoing_ReturnsEmpty()
    {
        var log = new[] { new CallLogEntry { Remote = "sip:1001@pbx", Incoming = true, StartUnix = 100 } };

        Assert.Equal("", HistoryViewModel.LastDialed(log));
    }

    [Fact]
    public void LastDialed_EmptyJournal_ReturnsEmpty()
        => Assert.Equal("", HistoryViewModel.LastDialed(System.Array.Empty<CallLogEntry>()));

    [Fact]
    public void Clear_ClearsCoreJournalAndReloads()
    {
        var sip = new FakeSipService();
        sip.CallHistory.Add(new CallLogEntry { Remote = "sip:1001@pbx" });
        var vm = new HistoryViewModel(sip);
        vm.Load();

        vm.Clear();

        Assert.Equal(1, sip.ClearHistoryCalls);
        Assert.Empty(vm.Entries);
        Assert.True(vm.IsEmpty);
    }
}
