using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LinphoneWinUI;

/// <summary>Описание доступной версии из update.json.</summary>
public sealed record UpdateInfo(string Version, string Url, string Notes);

/// <summary>
/// Проверка и установка обновлений.
///
/// Источник — статический <c>update.json</c> на raw.githubusercontent.com, а установщик берётся
/// из release-ассетов. Вместо GitHub API это сделано намеренно: у неавторизованных запросов к API
/// лимит 60 в час НА IP, а десятки рабочих мест сидят за одним внешним адресом — лимит выбирался бы
/// в первые минуты рабочего дня, и обновления просто перестали бы находиться. raw и release-ассеты
/// раздаются через CDN и под этот лимит не попадают.
///
/// Доверие обеспечивает НЕ транспорт, а подпись: скачанный файл проверяется через WinVerifyTrust,
/// и отпечаток подписавшего сверяется с нашим. Подменённый установщик не пройдёт проверку даже
/// если его отдали по открытому HTTP.
/// </summary>
public static class Updater
{
    /// <summary>Отпечаток сертификата, которым подписываются наши сборки (см. docs/code-signing.md).
    /// При смене сертификата обновить ЗДЕСЬ, иначе клиенты отвергнут новые сборки как чужие —
    /// это осознанная жёсткость: лучше не обновиться, чем поставить чужое.</summary>
    public const string SignerThumbprint = "3249411F7BECD99BED5C27193A01683463B8AD99";

    /// <summary>Где лежит описание версии. Меняется в rc [app] update_url.</summary>
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/romanlat68/l-phone-windows/main/update.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static Updater()
    {
        // GitHub отдаёт 403 запросам без User-Agent.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("L-Phone-Updater");
    }

    /// <summary>Разбор update.json. Возвращает null на любом мусоре — обновление не то место,
    /// где стоит падать: не разобрали, значит просто не предлагаем.</summary>
    public static UpdateInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            string? url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            string notes = root.TryGetProperty("notes", out var n) ? (n.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url)) return null;
            // Ссылку принимаем только https: иначе update.json смог бы увести клиент на любой хост
            // по открытому каналу. Подпись мы всё равно проверим, но лишний шаг к атаке не нужен.
            if (!url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return new UpdateInfo(version!.TrimStart('v', 'V'), url, notes);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Строго ли candidate новее current. Нечисловые части версии игнорируем:
    /// «1.2.0» и «v1.2» должны сравниваться одинаково.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var a = ParseVersion(candidate);
        var b = ParseVersion(current);
        return a != null && b != null && a > b;
    }

    private static Version? ParseVersion(string? s)
    {
        s = (s ?? "").Trim().TrimStart('v', 'V');
        if (s.Length == 0) return null;
        // Version.TryParse не берёт «1.2» без второй точки в некоторых формах — дополняем.
        int dots = 0;
        foreach (char c in s) if (c == '.') dots++;
        while (dots < 2) { s += ".0"; dots++; }
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Текущая версия приложения.</summary>
    public static string CurrentVersion =>
        typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Есть ли версия новее текущей. null — обновления нет либо проверить не удалось
    /// (сеть, мусор в json): молчим, это фоновая проверка, а не действие пользователя.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string? feedUrl = null, CancellationToken ct = default)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(feedUrl) ? DefaultFeedUrl : feedUrl!;
            var info = Parse(await Http.GetStringAsync(url, ct));
            return info != null && IsNewer(info.Version, CurrentVersion) ? info : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Скачать установщик во временный файл. Возвращает путь или null.</summary>
    public static async Task<string?> DownloadAsync(UpdateInfo info, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "L-Phone-update");
        try
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"L-Phone-Setup-{info.Version}.exe");
            using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = File.Create(path);
                await src.CopyToAsync(dst, ct);
            }
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Подписан ли файл НАШИМ сертификатом и цела ли подпись.
    /// Это единственная настоящая защита канала обновлений: без неё подменённый на пути файл
    /// установился бы с правами администратора.</summary>
    public static bool IsTrusted(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            // 1. Подпись цела и цепочка строится (WinVerifyTrust — та же проверка, что у Windows).
            if (!VerifyTrust(path)) return false;
            // 2. Подписал именно наш сертификат, а не любой валидный.
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return string.Equals(cert.Thumbprint, SignerThumbprint, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException)
        {
            return false;   // не подписан вовсе
        }
    }

    // --- WinVerifyTrust ---------------------------------------------------------------
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static bool VerifyTrust(string path)
    {
        var file = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = Marshal.StringToCoTaskMemUni(path),
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };
        IntPtr pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(file, pFile, false);

        var data = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            dwUIChoice = 2,             // WTD_UI_NONE — молча, без диалогов
            fdwRevocationChecks = 0,    // WTD_REVOKE_NONE: отзыв не проверяем, иначе без интернета
                                        // обновление ломается на ровном месте
            dwUnionChoice = 1,          // WTD_CHOICE_FILE
            pFile = pFile,
            dwStateAction = 0,
            dwProvFlags = 0x00000010,   // WTD_SAFER_FLAG
        };
        try
        {
            Guid action = WintrustActionGenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(file.pcwszFilePath);
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeCoTaskMem(pFile);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
}
