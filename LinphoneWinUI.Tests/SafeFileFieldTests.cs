using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Экранирование полей TSV (contacts.dat / favorites.dat): таб и перевод строки
/// в значении не должны ломать формат «поле\tполе» при следующем чтении.</summary>
public class SafeFileFieldTests
{
    [Theory]
    [InlineData("Иван\tПетров", "Иван Петров")]         // таб — разделитель формата
    [InlineData("Иван\r\nПетров", "Иван  Петров")]       // перевод строки (2 символа → 2 пробела)
    [InlineData("Иван Петров", "Иван Петров")]           // обычное значение не меняется
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Field_ReplacesSeparatorsWithSpaces(string? input, string expected) =>
        Assert.Equal(expected, SafeFile.Field(input));

    [Fact]
    public void Field_KeepsTsvLineParseable()
    {
        string line = $"{SafeFile.Field("Имя\tс табом")}\t{SafeFile.Field("1001\n")}";
        string[] parts = line.Split('\t');
        Assert.Equal(2, parts.Length);   // ровно два поля, сколько бы мусора ни было в значениях
    }
}
