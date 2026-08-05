using System.Collections.Generic;
using System.Linq;
using LinphoneWinUI;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Отбор контактов: поиск плюс фильтр по группе. Контакт, не попавший в список, для
/// человека не существует — поэтому «не нашли того, кто есть» здесь худшая из ошибок.
/// </summary>
public class ContactsFilterTests
{
    private const string All = "Все группы";

    private static readonly List<ContactEntry> Book = new()
    {
        new ContactEntry("Набиева Диана", "301", "", "Бухгалтерия"),
        new ContactEntry("Ресепшн", "302", "", "Офис"),
        new ContactEntry("Камалова Римма", "304", "+79001234567", "ОРК"),
        new ContactEntry("Горячев Алексей", "382", "", "ИТ")
        {
            Extra = new[] { new LabeledNumber("Домашний", "8432223344") },
        },
    };

    private static string[] Names(string? q, string? group) =>
        ContactsViewModel.Filter(Book, q, group, All).Select(c => c.Name).ToArray();

    [Fact]
    public void NoFilters_ReturnsEverything() => Assert.Equal(Book.Count, Names("", "").Length);

    [Fact]
    public void AllGroupsLabel_IsNotAFilter() => Assert.Equal(Book.Count, Names("", All).Length);

    [Fact]
    public void Group_NarrowsList() => Assert.Equal(new[] { "Горячев Алексей" }, Names("", "ИТ"));

    [Fact]
    public void Search_ByName() => Assert.Equal(new[] { "Ресепшн" }, Names("ресепшн", All));

    [Fact]
    public void Search_ByMainNumber() => Assert.Equal(new[] { "Набиева Диана" }, Names("301", All));

    [Fact]
    public void Search_ByMobile() => Assert.Equal(new[] { "Камалова Римма" }, Names("7900123", All));

    // Контакт с тремя номерами не должен теряться из-за того, что искали по третьему —
    // именно так выглядит «в книге он есть, а найти не могу».
    [Fact]
    public void Search_ByExtraNumber() => Assert.Equal(new[] { "Горячев Алексей" }, Names("8432223344", All));

    [Fact]
    public void SearchAndGroup_CombineAsAnd()
    {
        Assert.Empty(Names("ресепшн", "ИТ"));
        Assert.Single(Names("горячев", "ИТ"));
    }

    [Fact]
    public void UnknownGroup_GivesEmpty() => Assert.Empty(Names("", "Склад"));

    [Fact]
    public void Search_IsCaseInsensitive() => Assert.Single(Names("КАМАЛОВА", All));

    [Fact]
    public void Whitespace_IsIgnored() => Assert.Equal(Book.Count, Names("   ", All).Length);
}

/// <summary>
/// Номера контакта: сколько их, в каком порядке и как они попадают в подпись.
/// </summary>
public class ContactNumbersTests
{
    [Fact]
    public void SingleNumber_MeansNoChoice()
    {
        var c = new ContactEntry("Ресепшн", "302", "", "");

        Assert.False(c.HasChoice);
        Assert.Single(c.AllNumbers);
    }

    [Fact]
    public void WorkAndMobile_GiveChoice()
    {
        var c = new ContactEntry("Иван", "301", "+79001234567", "");

        Assert.True(c.HasChoice);
        Assert.Equal(2, c.AllNumbers.Count);
        Assert.Equal("301", c.AllNumbers[0].Number);   // рабочий первым — по нему звонят чаще
    }

    // Один и тот же номер в двух полях книги — обычное дело при выгрузке из АТС.
    // В меню он должен быть один, иначе выбор превращается в загадку.
    [Fact]
    public void SameNumberTwice_IsListedOnce()
    {
        var c = new ContactEntry("Иван", "301", "301", "");

        Assert.Single(c.AllNumbers);
        Assert.False(c.HasChoice);
    }

    [Fact]
    public void ExtraNumbers_AreIncluded()
    {
        var c = new ContactEntry("Иван", "301", "", "")
        {
            Extra = new[] { new LabeledNumber("Домашний", "8432223344") },
        };

        Assert.Equal(2, c.AllNumbers.Count);
        Assert.Contains(c.AllNumbers, n => n.Number == "8432223344");
    }

    [Fact]
    public void SubText_ListsAllNumbersAndGroup()
    {
        var c = new ContactEntry("Иван", "301", "+79001234567", "ИТ");

        Assert.Contains("301", c.SubText);
        Assert.Contains("+79001234567", c.SubText);
        Assert.Contains("ИТ", c.SubText);
    }

    // Имя тега проверяем, а не содержимое: <Email> тоже непустой, но звонить туда некуда.
    [Theory]
    [InlineData("Telephone2", true)]
    [InlineData("OtherNumber", true)]
    [InlineData("Work", true)]
    [InlineData("Extension", true)]
    [InlineData("Email", false)]
    [InlineData("Photo", false)]
    [InlineData("Title", false)]
    public void NumberTags_AreRecognized(string tag, bool expected) =>
        Assert.Equal(expected, Phonebook.LooksLikeNumberTag(tag));
}
