using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Маскировка ПД в app.log (AppLog.Mask): длинные (внешние) номера
/// прячутся в середине, короткие внутренние (3–6 цифр) остаются как есть.</summary>
public class LogMaskTests
{
    [Theory]
    [InlineData("Звоним: +74951234567", "Звоним: +7495*****67")]
    [InlineData("1234567", "1234*67")]              // ровно 7 цифр — минимальная маскировка
    [InlineData("вызов 1001 завершён", "вызов 1001 завершён")]   // короткий внутренний — не трогаем
    [InlineData("123456", "123456")]                // 6 цифр — граница, не маскируем
    [InlineData("с 74951234567 на 78122223344", "с 7495*****67 на 7812*****44")] // оба номера
    [InlineData("без номеров", "без номеров")]
    public void MaskNumbers_HidesLongNumbersOnly(string input, string expected) =>
        Assert.Equal(expected, AppLog.Mask(input));
}
