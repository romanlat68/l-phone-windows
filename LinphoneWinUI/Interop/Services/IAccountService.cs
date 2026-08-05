using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Учётные записи SIP и всё, что приходит от АТС про саму регистрацию: её состояние,
/// остановка из-за неверного пароля, голосовая почта (MWI приходит на endpoint).
/// </summary>
public interface IAccountService
{
    /// <summary>Состояние регистрации изменилось. Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<LinphoneRegistrationState, string?>? RegistrationStateChanged;

    /// <summary>Ядро прекратило попытки регистрации (обычно — неверный пароль).
    /// Отдельное событие: молча замолчавшая регистрация неотличима от «сеть пропала».</summary>
    event Action<string>? RegistrationAuthStopped;

    /// <summary>MWI (голосовая почта): (есть ожидающие, число новых).</summary>
    event Action<bool, int>? VoicemailChanged;

    string? GetDefaultAccountUsername();
    AccountInfo? GetAccountInfo();
    List<AccountInfo> GetAccounts();
    void AddOrUpdateAccount(string displayName, string login, string password, string server,
                            int port, string transport, int expires, bool makeDefault);
    void RemoveAccount(string sipIdentity);
    void SetDefaultAccount(string sipIdentity);
}
