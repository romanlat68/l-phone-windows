using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Пометка шифрования в записи журнала. Хранится в ref_key — общем слоте, куда мог написать
/// кто угодно, поэтому разбор обязан быть терпимым. И главное: замок не должен появляться
/// там, где защиты не было, — обещать безопасность, которой нет, хуже, чем молчать.
/// </summary>
public class CallLogTagTests
{
    [Theory]
    [InlineData("srtp")]
    [InlineData("zrtp")]
    [InlineData("dtls")]
    public void RoundTrips_KnownEncryption(string enc)
    {
        Assert.Equal(enc, CallLogTag.Encryption(CallLogTag.Format(enc)));
    }

    // Незашифрованный звонок не помечаем: отсутствие метки и так значит «защиты не было».
    [Theory]
    [InlineData("none")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Format_WritesNothingWithoutEncryption(string? enc)
    {
        Assert.Equal("", CallLogTag.Format(enc));
    }

    // Выдумывать защиту по неизвестному ответу ядра нельзя — новая версия SDK может вернуть
    // механизм, о котором мы не знаем, и замок означал бы то, чего мы не проверяли.
    [Fact]
    public void Format_RejectsUnknownEncryption()
    {
        Assert.Equal("", CallLogTag.Format("постквантовое"));
    }

    [Fact]
    public void Format_NormalisesCase()
    {
        Assert.Equal("srtp", CallLogTag.Encryption(CallLogTag.Format(" SRTP ")));
    }

    // Старые записи (сделанные до появления пометок) и пустой слот — «не знаем», без замка.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Encryption_EmptyMeansUnknown(string? refKey)
    {
        Assert.Equal("", CallLogTag.Encryption(refKey));
    }

    // ref_key — общая помойка: там могла оказаться метка другого клиента на том же профиле.
    [Theory]
    [InlineData("crm-id-4815")]
    [InlineData("=")]
    [InlineData("=srtp")]          // ключа нет
    [InlineData("enc=")]           // значения нет
    [InlineData("enc=выдумка")]
    [InlineData("encoding=srtp")]  // похожий, но другой ключ
    public void Encryption_ForeignContentIsIgnored(string refKey)
    {
        Assert.Equal("", CallLogTag.Encryption(refKey));
    }

    // Своё значение находим и тогда, когда рядом лежит чужое: место общее, и вычищать
    // чужие пометки мы не вправе.
    [Fact]
    public void Encryption_FoundAmongForeignPairs()
    {
        Assert.Equal("srtp", CallLogTag.Encryption("crm=4815;enc=srtp;theme=dark"));
    }

    [Fact]
    public void Encryption_ToleratesSpacesAndCase()
    {
        Assert.Equal("zrtp", CallLogTag.Encryption(" ENC = ZRTP "));
    }
}
