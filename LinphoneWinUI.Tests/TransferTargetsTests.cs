using System.Collections.Generic;
using System.Linq;
using LinphoneWinUI;
using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Кого показывать в списке «кому перевести». Ошибка здесь стоит чужого звонка: оператор
/// отдаёт разговор тому, кого увидел в списке, и не увидеть нужного человека — то же самое,
/// что его не иметь.
/// </summary>
public class TransferTargetsTests
{
    private static readonly List<ContactEntry> Book = new()
    {
        new ContactEntry("Набиева Диана", "301", "", "Бухгалтерия"),
        new ContactEntry("Горячев Алексей", "382", "+79005556677", "ИТ"),
        new ContactEntry("Ресепшн", "302", "", "Офис"),
    };

    private static List<FavoriteRow> Favourites() => new()
    {
        new FavoriteRow(new FavoriteEntry("Roman Test", "376"), PresenceStatus.Available),
        new FavoriteRow(new FavoriteEntry("Горячев Алексей ИТ", "382"), PresenceStatus.Available),
    };

    private static string[] Numbers(string? query) =>
        TransferTargetsViewModel.Build(Favourites(), Book, query).Select(r => r.Number).ToArray();

    // Пустое поле — избранные: переводят чаще всего одним и тем же людям.
    [Fact]
    public void EmptyQuery_ShowsFavourites() => Assert.Equal(new[] { "376", "382" }, Numbers(""));

    [Fact]
    public void EmptyQuery_KeepsLamps()
    {
        var rows = TransferTargetsViewModel.Build(Favourites(), Book, "");

        Assert.All(rows, r => Assert.True(r.HasLamp));
    }

    // Начали набирать — ищем по ВСЕЙ книге, а не только по избранному.
    [Fact]
    public void Query_FindsBeyondFavourites() => Assert.Contains("301", Numbers("набиева"));

    [Fact]
    public void Query_MatchesByNumber() => Assert.Contains("302", Numbers("302"));

    // Номер набирают как попало — сверяем по цифрам.
    [Fact]
    public void Query_IgnoresSeparators() => Assert.Contains("+79005556677", Numbers("+7 900 555"));

    // Избранные идут первыми: их лампы — единственные, которым можно верить.
    [Fact]
    public void Favourites_ComeFirst()
    {
        var rows = TransferTargetsViewModel.Build(Favourites(), Book, "382");

        Assert.True(rows[0].HasLamp);
        Assert.Equal("382", rows[0].Number);
    }

    // Один и тот же номер есть и в избранном, и в книге — строка должна быть одна,
    // иначе оператор гадает, какая из двух «правильная».
    [Fact]
    public void SameNumber_IsNotDuplicated()
    {
        var rows = TransferTargetsViewModel.Build(Favourites(), Book, "382");

        Assert.Single(rows, r => r.Number == "382");
    }

    // Найденным в книге лампу не рисуем: подписка BLF идёт по избранным, и «свободен»
    // для остальных был бы выдумкой, на которую оператор положится.
    [Fact]
    public void BookRows_HaveNoLamp()
    {
        var rows = TransferTargetsViewModel.Build(Favourites(), Book, "набиева");

        Assert.All(rows, r => Assert.False(r.HasLamp));
    }

    [Fact]
    public void NoMatch_GivesEmpty() => Assert.Empty(Numbers("яяяяя"));

    [Fact]
    public void Whitespace_IsTreatedAsEmpty() => Assert.Equal(new[] { "376", "382" }, Numbers("   "));

    // Книга на 74 человека в окне шириной 400 пикселей — это простыня, а не список.
    [Fact]
    public void LongResult_IsCapped()
    {
        var big = Enumerable.Range(100, 200)
            .Select(i => new ContactEntry("Сотрудник " + i, i.ToString(), "", ""))
            .ToList();

        var rows = TransferTargetsViewModel.Build(new List<FavoriteRow>(), big, "1");

        Assert.Equal(TransferTargetsViewModel.MaxRows, rows.Count);
    }

    // У контакта несколько номеров — в списке должны быть все: перевести могут и на мобильный.
    [Fact]
    public void AllNumbersOfContact_AreOffered()
    {
        var rows = TransferTargetsViewModel.Build(new List<FavoriteRow>(), Book, "горячев");

        Assert.Contains(rows, r => r.Number == "382");
        Assert.Contains(rows, r => r.Number == "+79005556677");
    }
}
