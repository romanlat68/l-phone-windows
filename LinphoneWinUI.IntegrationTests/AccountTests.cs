using System;
using System.IO;
using System.Linq;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.IntegrationTests;

/// <summary>
/// Аккаунты на настоящем ядре: AddOrUpdateAccount — самый рискованный метод interop-слоя.
/// В нём восемь нативных объектов с ручным подсчётом ссылок, пересоздание аккаунта при
/// обновлении и запись пароля в DPAPI-хранилище. Ошибка здесь ломает регистрацию у всех
/// и не видна ни компилятору, ни юнит-тестам.
///
/// Сервер намеренно несуществующий (домен .invalid зарезервирован RFC 2606): проверяем
/// работу с конфигурацией, а не сеть — регистрация заведомо не пройдёт, и это нормально.
/// </summary>
[Collection(CoreCollection.Name)]
public class AccountTests
{
    private readonly CoreFixture _fx;
    public AccountTests(CoreFixture fx) => _fx = fx;

    private bool Skip => !_fx.Started;

    private const string Server = "pbx.test.invalid";
    private const string Login = "9001";
    private const string Identity = "sip:9001@pbx.test.invalid";

    [Fact]
    public void AddAccount_ThenRead_ThenRemove()
    {
        if (Skip) return;

        try
        {
            _fx.Sip.AddOrUpdateAccount("Тестовый", Login, "секрет", Server, 5060, "udp", 3600, makeDefault: true);

            var accounts = _fx.Sip.GetAccounts();
            var added = accounts.FirstOrDefault(a => a.SipAddress == Identity);
            Assert.True(added.SipAddress == Identity, "Аккаунт не появился в списке");
            Assert.Equal("Тестовый", added.DisplayName);
            Assert.Equal(Login, added.Login);
            Assert.Equal(Server, added.Server);
            Assert.Equal("udp", added.Transport);
            Assert.True(added.IsDefault);
            // Пароль обязан читаться обратно: liblinphone после digest стирает plaintext,
            // и если бы не своё хранилище, форма аккаунта показывала бы пустое поле.
            Assert.Equal("секрет", added.Password);
            Assert.Equal(Login, _fx.Sip.GetDefaultAccountUsername());
        }
        finally
        {
            _fx.Sip.RemoveAccount(Identity);
        }

        Assert.DoesNotContain(_fx.Sip.GetAccounts(), a => a.SipAddress == Identity);
    }

    [Fact]
    public void UpdatingAccount_DoesNotDuplicateIt()
    {
        if (Skip) return;

        try
        {
            _fx.Sip.AddOrUpdateAccount("Первый", Login, "п1", Server, 5060, "udp", 3600, makeDefault: true);
            _fx.Sip.AddOrUpdateAccount("Второй", Login, "п2", Server, 5060, "tcp", 1800, makeDefault: false);

            var same = _fx.Sip.GetAccounts().Where(a => a.SipAddress == Identity).ToList();
            Assert.Single(same);                       // обновление, а не второй аккаунт
            Assert.Equal("Второй", same[0].DisplayName);
            Assert.Equal("tcp", same[0].Transport);
            Assert.Equal("п2", same[0].Password);
            // Обновляемый аккаунт сохраняет статус основного, даже если makeDefault=false.
            Assert.True(same[0].IsDefault);
        }
        finally { _fx.Sip.RemoveAccount(Identity); }
    }

    [Fact]
    public void EmptyPassword_KeepsPreviouslySavedOne()
    {
        if (Skip) return;

        try
        {
            _fx.Sip.AddOrUpdateAccount("", Login, "исходный", Server, 5060, "udp", 3600, makeDefault: true);
            // Пустое поле пароля в форме означает «не менять» — иначе пользователь, поправивший
            // только порт, молча остался бы без учётных данных.
            _fx.Sip.AddOrUpdateAccount("", Login, "", Server, 5061, "udp", 3600, makeDefault: true);

            var acc = _fx.Sip.GetAccounts().Single(a => a.SipAddress == Identity);
            Assert.Equal("исходный", acc.Password);
            Assert.Equal(5061, acc.Port);
        }
        finally { _fx.Sip.RemoveAccount(Identity); }
    }

    [Fact]
    public void Passwords_AreStoredInsideTestProfile_NotUserProfile()
    {
        if (Skip) return;

        try
        {
            _fx.Sip.AddOrUpdateAccount("", Login, "секрет", Server, 5060, "udp", 3600, makeDefault: true);

            // Главная страховка всего набора: хранилище паролей не должно трогать рабочий
            // cred.dat пользователя. Раньше путь был статическим, и тест писал бы прямо в него.
            Assert.True(File.Exists(Path.Combine(_fx.ProfileDir, "cred.dat")),
                "Пароль не сохранён во временный профиль — проверьте, куда пишет CredPath");
        }
        finally { _fx.Sip.RemoveAccount(Identity); }
    }

    [Fact]
    public void RemovingAccount_AlsoRemovesItsPassword()
    {
        if (Skip) return;

        _fx.Sip.AddOrUpdateAccount("", Login, "секрет", Server, 5060, "udp", 3600, makeDefault: true);
        _fx.Sip.RemoveAccount(Identity);

        // Пересоздаём с пустым паролем: если старый не удалён, он «воскреснет».
        try
        {
            _fx.Sip.AddOrUpdateAccount("", Login, "", Server, 5060, "udp", 3600, makeDefault: true);
            var acc = _fx.Sip.GetAccounts().Single(a => a.SipAddress == Identity);
            Assert.Equal("", acc.Password);
        }
        finally { _fx.Sip.RemoveAccount(Identity); }
    }
}
