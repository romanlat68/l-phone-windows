using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Настройки приложения и ядра — всё, что живёт в linphonerc: адресная книга, голосовая
/// почта, NAT, TLS, тема, произвольные ключи секции [app].
/// </summary>
public interface IAppConfig
{
    void SyncConfig();
    string GetPhonebookUrl();
    void SetPhonebookUrl(string url);
    (string User, string Password) GetPhonebookAuth();
    void SetPhonebookAuth(string user, string password);
    string GetVoicemailNumber();
    void SetVoicemailNumber(string number);

    /// <summary>Секрет HMAC для подписи хука статуса ("" = без подписи).</summary>
    string GetPresenceHookSecret();
    void SetPresenceHookSecret(string secret);

    /// <summary>Токен локального API. Хранится в защищённом хранилище (DPAPI), а не в rc:
    /// он даёт право звонить с этого телефона, то есть по силе не слабее пароля аккаунта.</summary>
    string GetApiToken();
    void SetApiToken(string token);
    string GetRingtone();
    void SetRingtone(string? path);
    List<string> GetBuiltInRingtones();
    string GetProvisioningUri();
    void SetProvisioningUri(string uri);
    (string Stun, bool Ice, bool Turn, string TurnUser) GetNat();

    /// <summary>false — настройки НЕ применены (ядро недоступно/не ответило); UI обязан сообщить.
    /// turnRealm — realm TURN-сервера; с ним пароль ложится в rc хешем ha1, без него открытым.</summary>
    bool SetNat(string stun, bool ice, bool turn, string turnUser, string turnPass, string turnRealm);

    /// <summary>Задан ли пароль TURN для этого логина. Значение вернуть нельзя (с realm хранится
    /// только хеш ha1), а факт наличия — можно: пустое поле пароля иначе неотличимо от «не задан».</summary>
    bool HasTurnPassword(string turnUser);

    /// <summary>Доверие TLS: файл CA (пусто — бандл SDK) и включена ли проверка сертификата.</summary>
    (string CaFile, bool Verify) GetTls();

    /// <summary>false — не применено (нет ядра, нет функций в SDK либо указанного файла CA нет).</summary>
    bool SetTls(string caFile, bool verify);
    string GetTheme();
    void SetTheme(string theme);
    bool GetAppFlag(string key, bool def);
    void SetAppFlag(string key, bool value);
    string GetAppString(string key, string def);
    void SetAppString(string key, string value);
    void SetVerboseLogs(bool verbose);
}
