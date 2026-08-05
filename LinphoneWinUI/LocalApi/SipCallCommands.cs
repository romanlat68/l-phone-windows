using System;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.LocalApi;

/// <summary>
/// Команды локального API поверх телефона. Отдельный тонкий слой, а не прямое обращение
/// сервера к <see cref="ISipService"/>: сервер живёт на своих потоках, и всё, что он умеет
/// делать с телефоном, должно быть перечислено в одном месте — иначе завтра в API появится
/// то, чего там быть не должно.
/// </summary>
public sealed class SipCallCommands : ICallCommands
{
    // Именно ICallService и IAccountService, а не ISipService целиком: интеграция с CRM
    // не должна иметь возможности сменить учётную запись, переставить кодеки или выключить
    // шифрование. Что API умеет — видно прямо здесь, в списке зависимостей.
    private readonly ICallService _calls;
    private readonly IAccountService _account;
    private readonly Func<bool> _hasIncoming;
    private readonly Action<string>? _log;

    private readonly Func<bool> _isRegistered;

    public SipCallCommands(ICallService calls, IAccountService account,
                           Func<bool> hasIncoming, Func<bool> isRegistered,
                           Action<string>? log = null)
    {
        _calls = calls;
        _account = account;
        _hasIncoming = hasIncoming;
        _isRegistered = isRegistered;
        _log = log;
    }

    public bool Call(string number)
    {
        string target = (number ?? "").Trim();
        if (target.Length == 0) return false;
        // Звонить, когда линия занята, API не даёт: сброс текущего разговора «по просьбе CRM»
        // был бы худшим сюрпризом из возможных.
        if (_calls.CallCount > 0) { _log?.Invoke($"API: звонок на {target} отклонён — линия занята"); return false; }
        _log?.Invoke($"API: звоню на {target}");
        _calls.Call(target);
        return true;
    }

    public bool Hangup()
    {
        if (_calls.CallCount == 0 && !_hasIncoming()) return false;
        _log?.Invoke("API: завершаю звонок");
        _calls.Hangup();
        return true;
    }

    public bool Answer()
    {
        if (!_hasIncoming()) return false;
        _log?.Invoke("API: принимаю входящий");
        _calls.AcceptIncoming();
        return true;
    }

    public ApiStatus Status()
    {
        var account = _account.GetAccountInfo();
        string state = _calls.CallCount > 0 ? "incall" : _hasIncoming() ? "ringing" : "idle";
        return new ApiStatus(
            Registered: _isRegistered(),
            Account: account?.Login ?? "",
            State: state,
            // Version, а не InformationalVersion: последняя у .NET несёт хвост с git-хэшем.
            Version: typeof(SipCallCommands).Assembly.GetName().Version?.ToString(3) ?? "");
    }
}
