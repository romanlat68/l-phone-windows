using LinphoneWinUI;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

public class PbxHookTests
{
    private const string Tpl = "http://pbx:8091/prov/status?ext={ext}&state={state}";

    [Theory]
    [InlineData(PresenceStatus.Available, "available")]
    [InlineData(PresenceStatus.Away, "away")]
    [InlineData(PresenceStatus.DoNotDisturb, "dnd")]
    public void HookState_MapsManualStatuses(PresenceStatus s, string expected) =>
        Assert.Equal(expected, PbxHook.HookState(s));

    [Theory]
    [InlineData(PresenceStatus.OnThePhone)]   // авто — АТС видит сама
    [InlineData(PresenceStatus.Offline)]
    public void HookState_SkipsAutoStatuses(PresenceStatus s) =>
        Assert.Null(PbxHook.HookState(s));

    [Fact]
    public void BuildUrl_NoSecret_LeavesUrlUnsigned()
    {
        string url = PbxHook.BuildUrl(Tpl, "377", "dnd", secret: "", unixTime: 1000);
        Assert.Equal("http://pbx:8091/prov/status?ext=377&state=dnd", url);
        Assert.DoesNotContain("sig=", url);
    }

    [Fact]
    public void BuildUrl_WithSecret_AppendsTsAndSig()
    {
        string url = PbxHook.BuildUrl(Tpl, "377", "dnd", secret: "s3cr3t", unixTime: 1_700_000_000);
        Assert.Contains("&ts=1700000000&sig=", url);
        string sig = url[(url.IndexOf("sig=") + 4)..];
        Assert.Equal(64, sig.Length);                 // SHA256 → 32 байта → 64 hex
        Assert.Equal(PbxHook.Sign("s3cr3t", "377", "dnd", 1_700_000_000), sig);
    }

    [Fact]
    public void BuildUrl_UsesAmpersandWhenTemplateHasNoQuery()
    {
        string url = PbxHook.BuildUrl("http://pbx/hook/{ext}/{state}", "377", "away", "k", 5);
        Assert.Contains("?ts=5&sig=", url);           // нет '?' в шаблоне → добавили '?'
    }

    [Fact]
    public void Sign_IsStableAndDependsOnEveryField()
    {
        long ts = 1_700_000_000;
        string baseSig = PbxHook.Sign("k", "377", "dnd", ts);
        Assert.Equal(baseSig, PbxHook.Sign("k", "377", "dnd", ts));   // детерминизм
        Assert.NotEqual(baseSig, PbxHook.Sign("k2", "377", "dnd", ts));
        Assert.NotEqual(baseSig, PbxHook.Sign("k", "378", "dnd", ts));
        Assert.NotEqual(baseSig, PbxHook.Sign("k", "377", "away", ts));
        Assert.NotEqual(baseSig, PbxHook.Sign("k", "377", "dnd", ts + 1));
    }

    [Fact]
    public void Sign_KnownVector()
    {
        // Контрольный вектор HMAC-SHA256("secret", "377\ndnd\n100"); тем же значением его
        // должен получить сервер (pbx-admin) — при расхождении подпись сломана с одной стороны.
        Assert.Equal("bae675e3f53ba3500a01c963a74ed421272d3cf4ccb0ee0582805f19aff464e2",
                     PbxHook.Sign("secret", "377", "dnd", 100));
    }
}
