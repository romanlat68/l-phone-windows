using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Разбор ленты обновлений и сравнение версий. Скачивание и проверку подписи
/// тестами не покрыть (сеть + WinVerifyTrust), но именно эта часть решает, предложим ли
/// мы пользователю обновление — и ошибиться здесь означает либо молчать всегда,
/// либо предлагать «обновиться» на ту же версию.</summary>
public class UpdaterTests
{
    [Fact]
    public void Parse_ReadsVersionUrlNotes()
    {
        var info = Updater.Parse("""
            {"version":"1.2.0","url":"https://example.com/setup.exe","notes":"исправления"}
            """);

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.Version);
        Assert.Equal("https://example.com/setup.exe", info.Url);
        Assert.Equal("исправления", info.Notes);
    }

    [Fact]
    public void Parse_StripsLeadingV() =>
        Assert.Equal("1.3", Updater.Parse("""{"version":"v1.3","url":"https://e.com/s.exe"}""")!.Version);

    // Ссылка из ленты ведёт к файлу, который поставится с правами администратора.
    // Подпись мы проверим, но давать ленте уводить клиент на любой хост по открытому каналу незачем.
    [Fact]
    public void Parse_RejectsNonHttps() =>
        Assert.Null(Updater.Parse("""{"version":"1.2","url":"http://example.com/setup.exe"}"""));

    [Theory]
    [InlineData("")]
    [InlineData("не json")]
    [InlineData("{}")]
    [InlineData("""{"version":"1.2"}""")]
    [InlineData("""{"url":"https://e.com/s.exe"}""")]
    [InlineData(null)]
    public void Parse_ReturnsNullOnGarbage(string? json) => Assert.Null(Updater.Parse(json));

    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.2", "1.1.9", true)]
    [InlineData("v1.2.0", "1.2.0", false)]   // та же версия — не предлагать
    [InlineData("1.0.0", "1.1.0", false)]    // старее — тем более
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("мусор", "1.0.0", false)]
    [InlineData("1.0.0", null, false)]
    public void IsNewer_ComparesNumerically(string? candidate, string? current, bool expected) =>
        Assert.Equal(expected, Updater.IsNewer(candidate, current));

    [Fact]
    public void IsTrusted_UnsignedOrMissingFile_IsRejected()
    {
        Assert.False(Updater.IsTrusted(@"C:\заведомо\нет\такого.exe"));

        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "не исполняемый файл");
            Assert.False(Updater.IsTrusted(tmp));
        }
        finally { System.IO.File.Delete(tmp); }
    }
}
