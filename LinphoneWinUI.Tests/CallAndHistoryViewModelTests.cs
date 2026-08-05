using System.Collections.Generic;
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

/// <summary>Индикатор шифрования на экране звонка. Показываем ФАКТ договорённости сторон:
/// настройка «SRTP по умолчанию» про конкретный разговор не говорит ничего — режим optimistic
/// специально пропускает звонок открытым, если вторая сторона SRTP не умеет.</summary>
public class CallEncryptionTests
{
    [Theory]
    [InlineData("srtp")]
    [InlineData("zrtp")]
    [InlineData("dtls")]
    public void KnownMechanisms_ShowLock(string enc)
    {
        var vm = new CallViewModel { Encryption = enc };

        Assert.True(vm.IsEncrypted);
        Assert.NotEqual("", vm.EncryptionGlyph);
    }

    // Открытый разговор не помечаем ничем: перечёркнутый замок на каждом городском звонке
    // стал бы фоном и обесценил значок там, где он действительно важен.
    [Theory]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("что-то новое из будущей версии SDK")]
    public void UnencryptedOrUnknown_ShowsNothing(string enc) =>
        Assert.False(new CallViewModel { Encryption = enc }.IsEncrypted);

    [Fact]
    public void ChangingEncryption_RaisesDerivedProperties()
    {
        var vm = new CallViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        vm.Encryption = "srtp";

        // Без этих уведомлений замок не появился бы до следующей перерисовки.
        Assert.Contains(nameof(CallViewModel.IsEncrypted), changed);
        Assert.Contains(nameof(CallViewModel.EncryptionGlyph), changed);
        Assert.Contains(nameof(CallViewModel.EncryptionTip), changed);
    }
}
