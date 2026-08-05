using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Код языка интерфейса → квалификатор PRI.
///
/// Регрессия: в списке языков стоял тег «en-US», а строки после сборки PRI лежат под «en».
/// Кандидат не находился, ResourceManager отдавал язык по умолчанию, и выбор «English»
/// не включал английский ВООБЩЕ — при этом настройка честно сохранялась, поэтому со стороны
/// это выглядело как «программа забыла мой выбор».
/// </summary>
public class LanguageCodeTests
{
    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("ru-RU", "ru")]
    [InlineData("de-DE", "de")]
    public void RegionalSubtagIsDropped(string stored, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeLanguage(stored));
    }

    // Письменность — не регион: упрощённый и традиционный китайский суть разные наборы строк,
    // и в PRI они лежат под полными тегами. Схлопнуть их значит потерять один из двух.
    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void ScriptSubtagIsKept(string stored)
    {
        Assert.Equal(stored, AppSettings.NormalizeLanguage(stored));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("ru", "ru")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void PlainCodesPassThrough(string? stored, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeLanguage(stored));
    }

    // Файл настройки правят руками и переносят между машинами — мусор не должен ронять старт.
    [Theory]
    [InlineData("  en-US \r\n", "en")]
    [InlineData("-", "-")]
    [InlineData("zh-", "zh")]
    public void JunkDoesNotThrow(string stored, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeLanguage(stored));
    }
}
