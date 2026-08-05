using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Формат запомненной геометрии окна. Сам файл (%LOCALAPPDATA%\…\window.txt) тестами
/// не трогаем — он рабочий. Проверяем разбор: испорченный файл обязан приводить к «размеру по
/// умолчанию», а не к окну нулевой высоты или окну за пределами экранов.</summary>
public class WindowGeometryTests
{
    [Fact]
    public void Parse_ReadsAllFields()
    {
        var g = AppSettings.ParseGeometry("100,200,900,700");

        Assert.NotNull(g);
        Assert.Equal(100, g!.Value.X);
        Assert.Equal(200, g.Value.Y);
        Assert.Equal(900, g.Value.Width);
        Assert.Equal(700, g.Value.Height);
    }

    // Монитор слева от основного даёт отрицательные координаты — это нормальная геометрия.
    [Fact]
    public void Parse_AcceptsNegativeOrigin()
    {
        var g = AppSettings.ParseGeometry("-1920,-40,800,600");

        Assert.NotNull(g);
        Assert.Equal(-1920, g!.Value.X);
        Assert.Equal(-40, g.Value.Y);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("100,200,900")]        // не хватает поля
    [InlineData("100,200,900,700,5")]  // лишнее поле
    [InlineData("a,b,c,d")]
    [InlineData("100,200,0,700")]      // нулевая ширина — окна не будет видно
    [InlineData("100,200,900,0")]      // нулевая высота
    [InlineData("100,200,-900,700")]   // отрицательный размер
    public void Parse_RejectsGarbage(string? text) => Assert.Null(AppSettings.ParseGeometry(text));

    [Fact]
    public void Format_ThenParse_RoundTrips()
    {
        var src = new WindowGeometry(-30, 15, 1024, 768);

        var back = AppSettings.ParseGeometry(AppSettings.FormatGeometry(src));

        Assert.Equal(src, back);
    }

    // Файл дописывается целиком, но лишние пробелы/перевод строки от ручной правки
    // не должны превращать сохранённый размер в «не помню».
    [Fact]
    public void Parse_TolerantToWhitespace()
    {
        var g = AppSettings.ParseGeometry(" 10 , 20 , 300 , 400 \r\n");

        Assert.Equal(new WindowGeometry(10, 20, 300, 400), g);
    }
}
