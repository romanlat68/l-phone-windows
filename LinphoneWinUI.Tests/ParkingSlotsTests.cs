using System.Linq;
using LinphoneWinUI;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Разбор диапазона слотов парковки. Строка приходит из настроек и обязана совпадать с
/// `parkpos` на АТС: лишний слот — это SUBSCRIBE в пустоту и строка «has no associated hint»
/// в логе Asterisk на каждое обновление, а недостающий — звонок, который припаркован, но
/// нигде не показан. Поэтому испорченную строку разбираем в НИЧЕГО, а не в «сколько вышло».
/// </summary>
public class ParkingSlotsTests
{
    [Fact]
    public void Range_IsExpanded() =>
        Assert.Equal(new[] { "501", "502", "503" }, ParkingSlots.Parse("501-503"));

    [Fact]
    public void DefaultRange_Has20Slots()
    {
        var slots = ParkingSlots.Parse("501-520");

        Assert.Equal(20, slots.Count);
        Assert.Equal("501", slots[0]);
        Assert.Equal("520", slots[^1]);
    }

    [Fact]
    public void SingleNumber_Works() => Assert.Equal(new[] { "701" }, ParkingSlots.Parse("701"));

    [Fact]
    public void MixedList_Works() =>
        Assert.Equal(new[] { "501", "502", "530" }, ParkingSlots.Parse("501-502,530"));

    [Fact]
    public void Whitespace_IsTolerated() =>
        Assert.Equal(new[] { "501", "502" }, ParkingSlots.Parse(" 501 - 502 "));

    [Fact]
    public void Duplicates_AreDropped() =>
        Assert.Equal(new[] { "501", "502" }, ParkingSlots.Parse("501-502,501"));

    // Ведущий ноль в конфигурации АТС значим: слот «0501» и «501» — разные номера.
    [Fact]
    public void LeadingZeros_ArePreserved() =>
        Assert.Equal(new[] { "0501", "0502" }, ParkingSlots.Parse("0501-0502"));

    // Перевёрнутый диапазон — опечатка. Молча вернуть пустоту (и показать пустой пульт)
    // хуже, чем ничего: человек будет думать, что парковка сломалась на АТС.
    [Fact]
    public void ReversedRange_IsRejected() => Assert.Empty(ParkingSlots.Parse("520-501"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("501-")]
    [InlineData("-520")]
    [InlineData("501-5o5")]
    [InlineData("501..520")]
    public void Garbage_IsRejected(string? spec) => Assert.Empty(ParkingSlots.Parse(spec));

    // Одна испорченная часть портит весь список: показать половину слотов и умолчать про
    // остальные — это скрытая потеря звонков.
    [Fact]
    public void PartialGarbage_RejectsEverything() => Assert.Empty(ParkingSlots.Parse("501-502,мусор"));

    // Сотня плиток — уже не пульт, а список, и сотня подписок NOTIFY на ровном месте.
    [Fact]
    public void HugeRange_IsCapped()
    {
        var slots = ParkingSlots.Parse("1-9999");

        Assert.Equal(ParkingSlots.MaxSlots, slots.Count);
    }
}

/// <summary>
/// Кто стоит в слоте — из тела NOTIFY. Без этого плитка говорит только «занят», и оператор
/// снимает звонок вслепую.
/// </summary>
public class DialogRemoteIdentityTests
{
    private const string Xml = """
        <?xml version="1.0"?>
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="3" state="full" entity="sip:501@pbx.example.com">
          <dialog id="501">
            <state>confirmed</state>
            <remote><identity display="Ivan">sip:79001234567@pbx.example.com</identity></remote>
          </dialog>
        </dialog-info>
        """;

    [Fact]
    public void RemoteIdentity_ReturnsUserPart() =>
        Assert.Equal("79001234567", DialogInfoParser.RemoteIdentity(Xml));

    [Fact]
    public void Entity_StillWorks() => Assert.Equal("501", DialogInfoParser.Entity(Xml));

    [Fact]
    public void Confirmed_MeansOccupied() =>
        Assert.Equal(DialogState.Confirmed, DialogInfoParser.Parse(Xml));

    // АТС может не прислать remote — тогда честнее показать «занят», чем выдумывать номер.
    [Fact]
    public void NoRemote_ReturnsEmpty()
    {
        const string xml = """
            <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="1" state="full" entity="sip:501@pbx">
              <dialog id="501"><state>confirmed</state></dialog>
            </dialog-info>
            """;

        Assert.Equal("", DialogInfoParser.RemoteIdentity(xml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не xml")]
    public void Garbage_ReturnsEmpty(string? xml) => Assert.Equal("", DialogInfoParser.RemoteIdentity(xml));
}
