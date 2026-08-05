using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI;

/// <summary>
/// Сообщает АТС о смене ручного статуса пользователя HTTP-запросом.
///
/// Зачем не SIP PUBLISH: Asterisk его не принимает — на каждый PUBLISH пишет в лог
/// «No registered publish handler for event presence». Поэтому статус уходит в pbx-admin
/// (GET /prov/status?ext=N&amp;state=available|away|dnd) — тем же механизмом, что Action URL
/// на настольных Fanvil. Дальше АТС сама: ставит CustomPresence (виден коллегам в BLF и
/// избранном), dnd дополнительно заворачивает звонки в голосовую почту, away — только показ.
///
/// Вынесено из MainWindow: к окну эта логика отношения не имеет, а сетевой код с гонками
/// внутри окна невозможно ни прочитать отдельно, ни проверить.
/// </summary>
internal sealed class PresenceHookClient
{
    // АТС стоит во внутренней сети, а системный прокси заворачивает туда же — статус не
    // доезжал бы вовсе на машине с включённым VPN-клиентом (см. LocalBypassProxy).
    private static readonly HttpClient Http =
        new(Interop.LocalBypassProxy.Handler()) { Timeout = TimeSpan.FromSeconds(5) };

    private readonly ISipService _sip;
    private readonly Action<string> _log;

    // Запросы сериализуем. Раньше «что отправлено» записывалось ДО await, и два быстрых
    // переключения статуса гонялись за этой переменной — на АТС мог остаться устаревший.
    // Теперь один «рабочий» цикл всегда досылает САМЫЙ свежий желаемый статус.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _desired;   // последний желаемый статус (перезаписывается на лету)
    private string? _sent;               // что реально дошло до АТС (для отсева дублей)

    public PresenceHookClient(ISipService sip, Action<string> log)
    {
        _sip = sip;
        _log = log;
    }

    /// <summary>Сообщить АТС новый статус. Возвращается немедленно: отправка идёт фоном.
    /// Автоматические статусы (📞 «Разговаривает», Offline) не шлются — их АТС видит сама.</summary>
    public void Notify(PresenceStatus status)
    {
        if (PbxHook.HookState(status) is not { } state) return;
        _desired = state;
        _ = PumpAsync();
    }

    /// <summary>Отправляет последний желаемый статус; параллельные вызовы сливаются в один цикл.</summary>
    private async Task PumpAsync()
    {
        if (!await _gate.WaitAsync(0)) return;   // цикл уже идёт — он подхватит _desired
        try
        {
            string template = _sip.GetAppString("presence_hook_url", "");
            if (template.Length == 0) return;    // хук не настроен — это нормально
            string ext = _sip.GetDefaultAccountUsername() ?? "";
            if (ext.Length == 0) return;
            string secret = _sip.GetPresenceHookSecret();

            // Пока шлём один статус, пользователь мог переключить ещё раз — добираем свежий.
            while (_desired is { } state && state != _sent)
            {
                _sent = state;
                string url = PbxHook.BuildUrl(template, ext, state, secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                try
                {
                    using var response = await Http.GetAsync(url);
                    _log($"Статус-хук АТС ({state}): HTTP {(int)response.StatusCode}");
                    // 401/403 и прочие отказы — не считаем доставленным, повторим при следующей смене.
                    if (!response.IsSuccessStatusCode) _sent = null;
                }
                catch (Exception ex)
                {
                    _sent = null;   // не дошло — при следующей смене попробуем снова
                    _log("Статус-хук АТС не сработал: " + ex.Message);
                    break;
                }
            }
        }
        finally { _gate.Release(); }
    }
}
