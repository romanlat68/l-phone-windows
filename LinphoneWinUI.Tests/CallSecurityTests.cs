using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Замок «разговор защищён» — единственный индикатор, по которому человек решает, можно ли
/// говорить свободно. Ошибка здесь не косметическая: она означает, что клиент пообещал защиту,
/// которой нет.
///
/// Ключевой случай — SRTP в режиме SDES (наш режим по умолчанию): ключ сессии передаётся ВНУТРИ
/// SDP, то есть в теле SIP-сообщения. При транспорте UDP или TCP этот ключ виден любому на пути,
/// и «зашифрованный» разговор читается целиком. При TLS сигнализация закрыта, и SRTP работает
/// как заявлено. У ZRTP и DTLS-SRTP ключ вырабатывается в самом медиапотоке, поэтому транспорт
/// сигнализации им безразличен.
/// </summary>
public class CallSecurityTests
{
    [Theory]
    // шифрование, сигнализация по TLS → защищено, слабая защита
    [InlineData("none", false, false, false)]
    [InlineData("none", true, false, false)]
    [InlineData("srtp", true, true, false)]    // SDES по TLS — как задумано
    [InlineData("srtp", false, true, true)]    // SDES по UDP/TCP — ключ ехал открытым
    [InlineData("zrtp", false, true, false)]   // ключ из медиапотока — сигнализация ни при чём
    [InlineData("dtls", false, true, false)]   // ключ из DTLS-рукопожатия
    public void Weakness_DependsOnSignalling_OnlyForSdes(
        string encryption, bool tls, bool expectedEncrypted, bool expectedWeak)
    {
        var security = new CallSecurity(encryption, tls);

        Assert.Equal(expectedEncrypted, security.IsEncrypted);
        Assert.Equal(expectedWeak, security.IsWeak);
    }

    [Fact]
    public void None_IsNeitherEncryptedNorWeak()
    {
        Assert.False(CallSecurity.None.IsEncrypted);
        Assert.False(CallSecurity.None.IsWeak);
    }

    // --- То же самое глазами интерфейса ---

    [Fact]
    public void ViewModel_ShowsLock_ButMarksItWeak_WhenSignallingIsPlain()
    {
        var vm = new CallViewModel();

        vm.SetSecurity(new CallSecurity("srtp", SignallingTls: false));

        // Замок показываем: шифрование действительно согласовано. Но цветом и подсказкой
        // говорим правду о его цене — молчать здесь значит обманывать.
        Assert.True(vm.IsEncrypted);
        Assert.True(vm.EncryptionWeak);
        Assert.NotEqual(Loc.F("St_EncSrtp", "SRTP"), vm.EncryptionTip);
    }

    [Fact]
    public void ViewModel_PlainLock_WhenSignallingIsTls()
    {
        var vm = new CallViewModel();

        vm.SetSecurity(new CallSecurity("srtp", SignallingTls: true));

        Assert.True(vm.IsEncrypted);
        Assert.False(vm.EncryptionWeak);
    }

    // Замок не должен пережить звонок, к которому относился: следующий разговор может идти
    // без шифрования вовсе, а оставшийся значок утверждал бы обратное.
    [Fact]
    public void ViewModel_Reset_ClearsBothFlags()
    {
        var vm = new CallViewModel();
        vm.SetSecurity(new CallSecurity("srtp", SignallingTls: false));

        vm.SetSecurity(CallSecurity.None);

        Assert.False(vm.IsEncrypted);
        Assert.False(vm.EncryptionWeak);
        Assert.Equal("", vm.EncryptionTip);
    }

    // Транспорт сменился (аккаунт перевели на TLS) — подсказка обязана перестать пугать.
    [Fact]
    public void ViewModel_TipFollowsSignallingChange()
    {
        var vm = new CallViewModel();
        vm.SetSecurity(new CallSecurity("srtp", SignallingTls: false));
        string weakTip = vm.EncryptionTip;

        vm.SetSecurity(new CallSecurity("srtp", SignallingTls: true));

        Assert.NotEqual(weakTip, vm.EncryptionTip);
    }
}
