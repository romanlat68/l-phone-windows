using System;
using Microsoft.Win32;

namespace LinphoneWinUI;

/// <summary>
/// Регистрация обработчика ссылок tel:/callto:/sip: за текущим пользователем (HKCU, без прав
/// администратора): клик по номеру в браузере/письме открывает L-Phone и набирает номер.
/// URI приходит аргументом командной строки; второй экземпляр передаёт его живому (Program.cs).
/// </summary>
public static class ClickToCall
{
    private static readonly string[] Schemes = { "tel", "callto", "sip", "sips" };

    private static string ExePath => Environment.ProcessPath ?? "";

    private static string Command => $"\"{ExePath}\" \"%1\"";

    /// <summary>Зарегистрированы ли схемы на НАШ exe (а не на другой софтфон).</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\tel\shell\open\command");
            return key?.GetValue(null) as string == Command;
        }
        catch { return false; }
    }

    public static void Register()
    {
        foreach (string scheme in Schemes)
        {
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
                root.SetValue(null, $"URL:{scheme} protocol (L-Phone)");
                root.SetValue("URL Protocol", "");
                using var icon = root.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"\"{ExePath}\",0");
                using var cmd = root.CreateSubKey(@"shell\open\command");
                cmd.SetValue(null, Command);
            }
            catch { /* нет доступа к реестру — просто не регистрируем */ }
        }
    }

    /// <summary>Снимает регистрацию, только если схема указывает на наш exe — чужие не трогаем.</summary>
    public static void Unregister()
    {
        foreach (string scheme in Schemes)
        {
            try
            {
                using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}\shell\open\command");
                if (cmd?.GetValue(null) as string != Command) continue;
                cmd.Dispose();
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{scheme}", throwOnMissingSubKey: false);
            }
            catch { }
        }
    }
}
