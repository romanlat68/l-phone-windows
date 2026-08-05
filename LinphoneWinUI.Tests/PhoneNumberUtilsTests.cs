using Xunit;

namespace LinphoneWinUI.Tests;

public class PhoneNumberUtilsTests
{
    // ---------------- DigitsOnly ----------------
    [Theory]
    [InlineData("+7 (495) 123-45-67", "74951234567")]
    [InlineData("1001", "1001")]
    [InlineData("sip:1001@pbx", "1001")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void DigitsOnly_ExtractsDigits(string? input, string expected) =>
        Assert.Equal(expected, PhoneNumberUtils.DigitsOnly(input));

    // ---------------- SipUser ----------------
    [Theory]
    [InlineData("\"Иван 123\" <sip:1001@pbx.local;transport=udp>", "1001")] // имя с цифрами не ломает разбор
    [InlineData("<sip:1001@pbx.local>", "1001")]
    [InlineData("sip:1001@pbx.local", "1001")]
    [InlineData("sips:1001@pbx.local", "1001")]
    [InlineData("1001@pbx.local", "1001")]
    [InlineData("1001;user=phone", "1001")]
    [InlineData("  1001  ", "1001")]
    [InlineData("1001", "1001")]
    [InlineData("<sip:1001@pbx", "1001")]   // незакрытая скобка — берём до конца
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SipUser_ExtractsUserPart(string? input, string expected) =>
        Assert.Equal(expected, PhoneNumberUtils.SipUser(input));

    // ---------------- Pretty ----------------
    [Theory]
    [InlineData("sip:1001@pbx.local;transport=udp", "1001@pbx.local")]
    [InlineData("sip:1001@pbx.local", "1001@pbx.local")]
    [InlineData("1001", "1001")]
    [InlineData("", "—")]
    [InlineData(null, "—")]
    public void Pretty_StripsSchemeAndParams(string? input, string expected) =>
        Assert.Equal(expected, PhoneNumberUtils.Pretty(input));

    // ---------------- IsLikelyInternal ----------------
    [Theory]
    [InlineData("375", true)]                     // внутренний 3-значный
    [InlineData("1001", true)]
    [InlineData("sip:375@pbx.local", true)]       // SIP-адрес внутреннего
    [InlineData("123456", true)]                  // граница: 6 цифр
    [InlineData("+79963360197", false)]           // мобильный — не подписываемся
    [InlineData("79963360197", false)]            // 11 цифр без «+»
    [InlineData("1234567", false)]                // 7 цифр — уже городской
    [InlineData("support", false)]                // буквенный user
    [InlineData("9", false)]                      // 1 знак — служебный код
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLikelyInternal_FiltersPresenceSubscriptions(string? input, bool expected) =>
        Assert.Equal(expected, PhoneNumberUtils.IsLikelyInternal(input));

    // ---------------- NormalizeDialTarget ----------------
    [Theory]
    [InlineData("tel:+7 (495) 123-45-67", "+74951234567")]
    [InlineData("TEL:123", "123")]                          // схема без учёта регистра
    [InlineData("callto:1001", "1001")]
    [InlineData("tel:%2B74951234567", "+74951234567")]      // URL-экранированный «+»
    [InlineData("sip:1001@pbx.local", "sip:1001@pbx.local")] // sip: проходит как есть
    [InlineData("sips:1001@pbx.local", "sips:1001@pbx.local")]
    [InlineData("*70#", "*70#")]                            // feature-коды АТС не теряются
    [InlineData("8 (800) 555.35-35", "88005553535")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeDialTarget_CleansUri(string? input, string expected) =>
        Assert.Equal(expected, PhoneNumberUtils.NormalizeDialTarget(input));
}

public class ExternalDialTargetTests
{
    // Цель приходит из внешней ссылки и уходит в INVITE — управляющие символы и параметры,
    // которыми страница могла бы влиять на запрос, обязаны отсекаться на входе.
    [Theory]
    [InlineData("sip:1001@pbx.local?Route=%3Csip:evil%3E", "sip:1001@pbx.local")]
    [InlineData("sip:1001@pbx.local;transport=tcp", "sip:1001@pbx.local")]
    [InlineData("tel:+7 (495) 123-45-67", "+74951234567")]
    [InlineData("tel:1234;phone-context=evil", "1234")]
    public void NormalizeDialTarget_StripsRequestControls(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumberUtils.NormalizeDialTarget(raw));

    [Fact]
    public void NormalizeDialTarget_RejectsControlChars() =>
        Assert.Equal("", PhoneNumberUtils.NormalizeDialTarget("sip:1001@pbx%0d%0aSubject:x"));

    // Доверяем только своим: номер уйдёт через аккаунт по умолчанию, адрес — если домен наш.
    [Theory]
    [InlineData("1001", true)]
    [InlineData("+74951234567", true)]
    [InlineData("*97", true)]
    [InlineData("sip:1001@pbx.example.com", true)]
    [InlineData("sip:1001@pbx.example.com:5060", true)]
    [InlineData("sip:1001@evil.example.com", false)]
    [InlineData("1001@evil.example.com", false)]
    [InlineData("", false)]
    public void IsTrustedDialTarget_OnlyOwnServers(string target, bool expected) =>
        Assert.Equal(expected, PhoneNumberUtils.IsTrustedDialTarget(
            target, new[] { "pbx.example.com:5060", "sip2.example.com" }));

    [Fact]
    public void IsTrustedDialTarget_NoAccounts_ExternalAddressNotTrusted() =>
        Assert.False(PhoneNumberUtils.IsTrustedDialTarget("sip:1001@pbx.example.com", new string[0]));

    // ------------------------------------------------------------------
    // Можно ли перезвонить: скрытый номер приходит НЕ пустым
    // ------------------------------------------------------------------
    // Найдено живым звонком: originate без caller ID дал
    // «Anonymous» <sip:anonymous@anonymous.invalid> (так предписывает RFC 3323), и наивная
    // проверка «строка не пуста» принимала это за номер — уведомление о пропущенном
    // предлагало «Перезвонить» на «anonymous». Кнопка, которая заведомо не сработает,
    // хуже отсутствующей.

    [Theory]
    [InlineData("sip:377@sip.example.com")]
    [InlineData("377")]
    [InlineData("+79161234567")]
    [InlineData("*97")]
    [InlineData("\"Иванов\" <sip:376@pbx>")]
    public void Callable_RealNumbers(string address) =>
        Assert.True(PhoneNumberUtils.IsCallable(address));

    [Theory]
    [InlineData("\"Anonymous\" <sip:anonymous@anonymous.invalid>")]
    [InlineData("sip:anonymous@anonymous.invalid")]
    [InlineData("anonymous")]
    [InlineData("sip:unknown@pbx")]
    [InlineData("unavailable")]
    [InlineData("restricted")]
    [InlineData("")]
    [InlineData(null)]
    public void NotCallable_HiddenOrUnknown(string? address) =>
        Assert.False(PhoneNumberUtils.IsCallable(address));
}
