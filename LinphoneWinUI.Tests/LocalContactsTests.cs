using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Формат файла локальной книги. Сам файл (%LOCALAPPDATA%\…\contacts.dat) тестами
/// не трогаем — он рабочий. Проверяем разбор и сериализацию: именно там краевые случаи,
/// и именно там ошибка стоит дороже всего — потерянные контакты пользователь не восстановит.</summary>
public class LocalContactsTests
{
    [Fact]
    public void ParseLine_ReadsAllFields()
    {
        var c = LocalContacts.ParseLine("abc123\tИван Петров\t1001\t+79001234567");

        Assert.NotNull(c);
        Assert.Equal("abc123", c!.Id);
        Assert.Equal("Иван Петров", c.Name);
        Assert.Equal("1001", c.Number);
        Assert.Equal("+79001234567", c.Mobile);
        Assert.True(c.IsLocal);
    }

    // Файл правится и руками, и прошлыми версиями. Битая строка обязана пропускаться,
    // а не ронять загрузку всей книги.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("только-id")]
    [InlineData("id\tимя\tномер")]          // не хватает колонки
    [InlineData("\tимя\tномер\tмобильный")] // пустой Id — запись нельзя ни править, ни удалять
    public void ParseLine_SkipsBrokenLines(string? line) => Assert.Null(LocalContacts.ParseLine(line));

    [Fact]
    public void FormatLine_ThenParse_RoundTrips()
    {
        var src = new ContactEntry("Пётр", "375", "89001112233", "", IsLocal: true, Id: "id42");

        var back = LocalContacts.ParseLine(LocalContacts.FormatLine(src));

        Assert.NotNull(back);
        Assert.Equal(src.Id, back!.Id);
        Assert.Equal(src.Name, back.Name);
        Assert.Equal(src.Number, back.Number);
        Assert.Equal(src.Mobile, back.Mobile);
    }

    // Таб или перевод строки в имени сдвинули бы колонки и превратили запись в мусор.
    [Fact]
    public void FormatLine_NeutralizesSeparatorsInName()
    {
        var src = new ContactEntry("Иван\tПетров\nМладший", "375", "", "", IsLocal: true, Id: "id1");

        string line = LocalContacts.FormatLine(src);

        Assert.Equal(4, line.Split('\t').Length);   // колонки не разъехались
        Assert.DoesNotContain("\n", line);

        var back = LocalContacts.ParseLine(line);
        Assert.NotNull(back);
        Assert.Equal("id1", back!.Id);
        Assert.Equal("375", back.Number);
    }
}
