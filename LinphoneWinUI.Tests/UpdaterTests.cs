using System.Linq;
using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>Разбор ленты обновлений и сравнение версий. Скачивание и проверку подписи
/// тестами не покрыть (сеть + WinVerifyTrust), но именно эта часть решает, предложим ли
/// мы пользователю обновление — и ошибиться здесь означает либо молчать всегда,
/// либо предлагать «обновиться» на ту же версию.</summary>
public class UpdaterTests
{
    // --- Пиннинг отпечатка подписи ---
    // Единственная настоящая защита канала обновлений: файл ставится с правами администратора,
    // и подменённый на пути установщик получил бы их же.

    [Fact]
    public void TrustedThumbprints_IsNotEmpty()
    {
        // Пустой список означал бы «доверяем любой валидной подписи» — то есть чужой сборке,
        // подписанной каким угодно сертификатом.
        Assert.NotEmpty(Updater.TrustedThumbprints);
        Assert.All(Updater.TrustedThumbprints, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    [Fact]
    public void IsOurThumbprint_AcceptsKnown_RegardlessOfCaseAndSpacing()
    {
        string known = Updater.TrustedThumbprints[0];

        Assert.True(Updater.IsOurThumbprint(known));
        Assert.True(Updater.IsOurThumbprint(known.ToLowerInvariant()));
        // Оснастка сертификатов и certutil показывают отпечаток блоками по два символа —
        // скопированное оттуда значение обязано совпасть, иначе ротация молча провалится.
        Assert.True(Updater.IsOurThumbprint(string.Join(" ",
            Enumerable.Range(0, known.Length / 2).Select(i => known.Substring(i * 2, 2)))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000000000000000000000000000000000000000")]
    public void IsOurThumbprint_RejectsForeign(string? thumbprint) =>
        Assert.False(Updater.IsOurThumbprint(thumbprint));

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

    // ------------------------------------------------------------------
    // Адрес ленты обновлений приходит из rc — то есть из файла, который правят руками
    // ------------------------------------------------------------------
    // Найдено по crash.log с рабочей машины: относительный адрес давал
    // InvalidOperationException внутри HttpClient, он не попадал под фильтр исключений и
    // уходил из async void обработчика. Процесс спасал только глобальный перехватчик XAML.

    [Theory]
    [InlineData("https://example.com/update.json")]
    [InlineData("http://pbx/update.json")]
    [InlineData("  https://example.com/u.json  ")]
    public void ValidFeedUrl_IsAccepted(string url)
    {
        Assert.True(Updater.TryFeedUri(url, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("update.json")]              // относительный — ровно тот случай из crash.log
    [InlineData("/updates/update.json")]
    [InlineData("ftp://example.com/u.json")]
    [InlineData("file:///C:/secret.json")]   // чтение чужих файлов через настройку
    [InlineData("не ссылка")]
    [InlineData("://")]
    public void BadFeedUrl_IsRejected_WithoutThrowing(string url)
    {
        Assert.False(Updater.TryFeedUri(url, out var uri));
        Assert.Null(uri);
    }

    [Fact]
    public void EmptyFeedUrl_FallsBackToDefault()
    {
        Assert.True(Updater.TryFeedUri("", out var uri));
        Assert.Equal(Updater.DefaultFeedUrl, uri!.ToString());
        Assert.True(Updater.TryFeedUri(null, out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_OnBadUrl_ReturnsNull_AndDoesNotThrow()
    {
        // Главное: НЕ бросает. Фоновая проверка обновления не то место, где стоит падать.
        Assert.Null(await Updater.CheckAsync("не ссылка"));
        Assert.Null(await Updater.CheckAsync("update.json"));
    }
}
