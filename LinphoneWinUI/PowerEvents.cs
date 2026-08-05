using System;
using Microsoft.Win32;

namespace LinphoneWinUI;

/// <summary>Что произошло с машиной или сеансом.</summary>
internal enum PowerEvent
{
    /// <summary>Машина засыпает. Регистрацию надо снять СЕЙЧАС, пока сеть ещё есть.</summary>
    Suspend,
    /// <summary>Машина проснулась. Регистрация протухла, NAT-биндинг умер, TCP/TLS порван.</summary>
    Resume,
    /// <summary>Рабочая станция заблокирована (Win+L, тайм-аут, уход с рабочего места).</summary>
    SessionLock,
    /// <summary>Пользователь вернулся.</summary>
    SessionUnlock,
}

/// <summary>
/// Питание и сеанс Windows — события, которых софтфону не хватало больше всего.
///
/// Пробуждение из сна НЕ равнозначно смене IP-адреса, а подписаны мы были только на неё:
/// если ноутбук проснулся в той же сети, <c>NetworkAddressChanged</c> не поднимается вовсе.
/// При этом за время сна успело сломаться всё сразу — регистрация протухла (таймеры стояли,
/// re-REGISTER не уходил), пробитый NAT-биндинг умер за минуты, TCP/TLS-соединение с сервером
/// разорвано. Asterisk считает контакт живым до конца expires (по умолчанию час) и шлёт INVITE
/// в никуда: звонящий слышит гудки, сотрудник — тишину. Это первая по частоте жалоба на любой
/// софтфон, и до сих пор в журнале не оставалось даже следа о том, что машина спала.
///
/// Отдельным классом, а не полем окна: подписка на <c>SystemEvents</c> требует снятия (иначе
/// статическое событие держит обработчик, а с ним и окно), а логика реакции нужна тестируемой
/// отдельно от XAML.
/// </summary>
internal sealed class PowerEvents : IDisposable
{
    private readonly Action<PowerEvent> _handler;
    private bool _subscribed;

    /// <param name="handler">Вызывается С ЧУЖОГО ПОТОКА (SystemEvents) — маршалить в свой
    /// обязан подписчик.</param>
    public PowerEvents(Action<PowerEvent> handler) => _handler = handler;

    public void Subscribe()
    {
        if (_subscribed) return;
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _subscribed = true;
        }
        catch (Exception)
        {
            // SystemEvents недоступны (сервисный сеанс без окон сообщений). Не повод падать:
            // телефон просто останется без реакции на сон, как было раньше.
            _subscribed = false;
        }
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        _subscribed = false;
        try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend: _handler(PowerEvent.Suspend); break;
            case PowerModes.Resume: _handler(PowerEvent.Resume); break;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            // Отключение RDP-сеанса и его блокировка для нас одно и то же: человека нет за
            // машиной, и коллегам это стоит знать.
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
            case SessionSwitchReason.RemoteDisconnect:
                _handler(PowerEvent.SessionLock);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
            case SessionSwitchReason.RemoteConnect:
                _handler(PowerEvent.SessionUnlock);
                break;
        }
    }
}
