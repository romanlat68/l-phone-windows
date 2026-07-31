using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Разбор dialog-info+xml (RFC 4235) — тел NOTIFY по подписке «Event: dialog».
/// От этого разбора зависит, загорится ли лампа «звонит» и будет ли доступен перехват:
/// ошибка здесь означает либо мёртвый пульт, либо перехват вслепую с отбойником.</summary>
public class DialogInfoTests
{
    private const string Early = """
        <?xml version="1.0"?>
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="1" state="full" entity="sip:376@pbx.example.com">
          <dialog id="376"><state>early</state></dialog>
        </dialog-info>
        """;

    private const string Confirmed = """
        <?xml version="1.0"?>
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="2" state="full" entity="sip:376@pbx.example.com">
          <dialog id="376"><state>confirmed</state></dialog>
        </dialog-info>
        """;

    [Fact]
    public void Parse_Early() => Assert.Equal(DialogState.Early, DialogInfoParser.Parse(Early));

    [Fact]
    public void Parse_Confirmed() => Assert.Equal(DialogState.Confirmed, DialogInfoParser.Parse(Confirmed));

    [Fact]
    public void Parse_Terminated() => Assert.Equal(DialogState.Terminated, DialogInfoParser.Parse("""
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="3" state="full" entity="sip:376@pbx">
          <dialog id="376"><state>terminated</state></dialog>
        </dialog-info>
        """));

    // Пустой dialog-info Asterisk шлёт, когда на линии тишина.
    [Fact]
    public void Parse_NoDialogs_IsTerminated() => Assert.Equal(DialogState.Terminated, DialogInfoParser.Parse("""
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="4" state="full" entity="sip:376@pbx" />
        """));

    // Разговор важнее звонка: коллега занят, даже если параллельно к нему дозваниваются.
    [Fact]
    public void Parse_ConfirmedWinsOverEarly() => Assert.Equal(DialogState.Confirmed, DialogInfoParser.Parse("""
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="5" state="full" entity="sip:376@pbx">
          <dialog id="a"><state>early</state></dialog>
          <dialog id="b"><state>confirmed</state></dialog>
        </dialog-info>
        """));

    [Theory]
    [InlineData("proceeding")]
    [InlineData("trying")]
    public void Parse_TreatsProceedingAsRinging(string state) =>
        Assert.Equal(DialogState.Early, DialogInfoParser.Parse(
            $"""<dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" entity="sip:376@pbx"><dialog id="1"><state>{state}</state></dialog></dialog-info>"""));

    // «Не знаю» и «не звонит» — разные вещи: на мусоре лампу трогать нельзя.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не xml вовсе")]
    [InlineData("<html><body>404</body></html>")]
    public void Parse_GarbageIsUnknown(string? xml) =>
        Assert.Equal(DialogState.Unknown, DialogInfoParser.Parse(xml));

    [Fact]
    public void Entity_ExtractsUserPart() => Assert.Equal("376", DialogInfoParser.Entity(Early));

    [Theory]
    [InlineData(null)]
    [InlineData("не xml")]
    public void Entity_GarbageIsEmpty(string? xml) => Assert.Equal("", DialogInfoParser.Entity(xml));
}
