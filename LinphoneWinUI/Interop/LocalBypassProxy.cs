using System;
using System.Net;
using System.Net.Http;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Системный прокси, но НЕ для локальных и внутренних адресов.
///
/// Зачем понадобилось. На машине с включённым прокси (VPN-клиент, корпоративный шлюз)
/// .NET отправляет через него ВСЁ, включая запросы на 127.0.0.1 и в локальную сеть.
/// Список исключений Windows (`ProxyOverride` = «&lt;local&gt;;127.*;192.168.*») при этом
/// не помогает: проверено на живой машине — `WebRequest.GetSystemWebProxy()` вернул прокси
/// даже для `http://127.0.0.1:8999/`.
///
/// Последствия молчаливые и потому неприятные: CRM-хук на локальный коннектор возвращает
/// 503 от прокси, presence не доезжает до АТС во внутренней сети, телефонная книга не
/// загружается. Всё выглядит как «интеграция не работает», а причина — в стороннем прокси,
/// про который в софтфоне никто не думает.
///
/// Наружу (обновления, внешние CRM) прокси по-прежнему используется: в корпоративной сети
/// без него интернета попросту нет.
/// </summary>
internal sealed class LocalBypassProxy : IWebProxy
{
    private readonly IWebProxy _system = WebRequest.GetSystemWebProxy();

    public ICredentials? Credentials
    {
        get => _system.Credentials;
        set => _system.Credentials = value;
    }

    public Uri? GetProxy(Uri destination) => IsLocal(destination) ? null : _system.GetProxy(destination);

    public bool IsBypassed(Uri host) => IsLocal(host) || _system.IsBypassed(host);

    /// <summary>Адрес, до которого надо идти напрямую: петля, приватная сеть или короткое
    /// имя хоста без домена (так адресуют внутренние сервисы вроде `pbx`).</summary>
    public static bool IsLocal(Uri? uri)
    {
        if (uri == null) return false;
        string host = uri.Host;
        if (host.Length == 0) return false;

        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4) return false;                       // IPv6 не разбираем: не наш случай
            return b[0] switch
            {
                10 => true,                                        // 10.0.0.0/8
                127 => true,
                169 => b[1] == 254,                                // link-local
                172 => b[1] >= 16 && b[1] <= 31,                   // 172.16.0.0/12
                192 => b[1] == 168,                                // 192.168.0.0/16
                _ => false,
            };
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        // Имя без точки — внутреннее (`pbx`, `crm`). Домены с точкой считаем внешними:
        // угадывать по суффиксу (.local, .lan) ненадёжно, а ошибиться в эту сторону дешевле.
        return !host.Contains('.');
    }

    /// <summary>Обработчик для HttpClient со всем этим внутри. credentials — для книг,
    /// закрытых HTTP-авторизацией.</summary>
    public static HttpClientHandler Handler(ICredentials? credentials = null) =>
        new() { Proxy = new LocalBypassProxy(), UseProxy = true, Credentials = credentials };
}
