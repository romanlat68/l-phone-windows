using System;
using System.Collections.Generic;
using System.Linq;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Одно сочетание глобальной горячей клавиши: модификаторы + клавиша.
///
/// Хранится в конфигурации строкой вида <c>ctrl+alt+A</c> — читаемой и правимой руками.
/// Разбор и запись вынесены сюда отдельно от регистрации в Windows, чтобы формат можно было
/// проверить тестами: испорченная строка в конфиге не должна ни ронять приложение, ни молча
/// оставлять пользователя без горячей клавиши.
/// </summary>
internal readonly record struct HotkeySpec(uint Mods, uint Vk)
{
    // Значения RegisterHotKey (winuser.h).
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Сочетание пригодно к регистрации. Модификатор ОБЯЗАТЕЛЕН: без него мы
    /// забрали бы у системы одиночную клавишу — например «A» перестала бы печататься
    /// во всех программах сразу.</summary>
    public bool IsValid => Mods != 0 && Vk != 0 && Mods != ModWin;

    /// <summary>Человеческий вид: «Ctrl+Alt+A». Порядок модификаторов фиксирован, чтобы
    /// одно и то же сочетание всегда выглядело одинаково.</summary>
    public string Format()
    {
        if (Vk == 0) return "";
        var parts = new List<string>();
        if ((Mods & ModControl) != 0) parts.Add("Ctrl");
        if ((Mods & ModAlt) != 0) parts.Add("Alt");
        if ((Mods & ModShift) != 0) parts.Add("Shift");
        if ((Mods & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(Vk));
        return string.Join("+", parts);
    }

    /// <summary>Разбор строки из конфигурации. null — строка испорчена или сочетание
    /// непригодно; вызывающий подставит значение по умолчанию.</summary>
    public static HotkeySpec? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0, vk = 0;
        foreach (string raw in text!.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModControl; continue;
                case "alt": mods |= ModAlt; continue;
                case "shift": mods |= ModShift; continue;
                case "win": mods |= ModWin; continue;
            }
            if (vk != 0) return null;          // две клавиши в одном сочетании — мусор
            vk = KeyCode(p);
            if (vk == 0) return null;
        }
        var spec = new HotkeySpec(mods, vk);
        return spec.IsValid ? spec : null;
    }

    /// <summary>Имя клавиши по коду. Поддержаны буквы, цифры и F1–F24 — их хватает, а
    /// экзотика вроде PrintScreen или медиаклавиш чаще уже занята системой.</summary>
    public static string KeyName(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),                    // 0–9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),                    // A–Z
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),                     // F1–F24
        0x20 => "Space",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        _ => "0x" + vk.ToString("X2"),
    };

    /// <summary>Код клавиши по имени; 0 — не распознано.</summary>
    private static uint KeyCode(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z') return c;
            return 0;
        }
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') &&
            int.TryParse(name[1..], out int n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1);
        return name.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "insert" or "ins" => 0x2D,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            _ => 0,
        };
    }

    public override string ToString() => Format();
}
