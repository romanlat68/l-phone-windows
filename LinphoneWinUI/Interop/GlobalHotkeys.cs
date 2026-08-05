using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LinphoneWinUI.Interop;

/// <summary>Что делает горячая клавиша. Значения пишутся в rc — не менять.</summary>
public enum HotkeyAction { Answer = 1, Hangup = 2, Mute = 3 }

/// <summary>
/// Глобальные горячие клавиши: работают, когда окно софтфона НЕ в фокусе.
///
/// Зачем: оператор живёт в CRM, а не в софтфоне. Дотянуться мышью до окна, которое сейчас
/// под другим приложением, — это те самые секунды, за которые звонящий кладёт трубку.
///
/// Регистрируем через RegisterHotKey на окно приложения и ловим WM_HOTKEY подклассом
/// оконной процедуры — тем же приёмом, что MinSizeHelper. Комбинация занимается СИСТЕМОЙ
/// целиком: если её уже держит другая программа, регистрация не удастся, и об этом надо
/// сказать пользователю, а не молчать.
/// </summary>
internal static class GlobalHotkeys
{
    private const uint WM_HOTKEY = 0x0312;

    // Модификаторы RegisterHotKey.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;   // удержание не должно сыпать событиями

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private static SubclassProc? _proc;          // держим в статике: GC не должен собрать под нативным колбэком
    private static IntPtr _hwnd;
    private static readonly UIntPtr SubclassId = new(0xA4B1);

    /// <summary>Срабатывание. Поднимается на потоке UI (сообщение окна) — маршалить не нужно.</summary>
    public static event Action<HotkeyAction>? Pressed;

    /// <summary>Ключ настройки для действия — под ним сочетание лежит в rc.</summary>
    public static string ConfigKey(HotkeyAction action) => "hotkey_" + action.ToString().ToLowerInvariant();

    /// <summary>Клавиши по умолчанию. Ctrl+Alt выбран сознательно: одиночные F-клавиши и
    /// сочетания с Win часто заняты системой и CRM, а Ctrl+Alt+буква — редкость.
    /// Пользователь может переназначить любое (см. настройки → Дополнительно).</summary>
    public static IReadOnlyDictionary<HotkeyAction, HotkeySpec> Defaults { get; } =
        new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Answer] = new(ModControl | ModAlt, 'A'),
            [HotkeyAction.Hangup] = new(ModControl | ModAlt, 'H'),
            [HotkeyAction.Mute] = new(ModControl | ModAlt, 'M'),
        };

    /// <summary>Все действия, которым можно назначить сочетание.</summary>
    public static IEnumerable<HotkeyAction> AllActions => Defaults.Keys;

    /// <summary>Что реально будет занято для действия по сохранённой в rc строке.
    /// Пусто — умолчание (настройки ещё не трогали); «-» — пользователь снял назначение,
    /// и подставлять умолчание нельзя, клавиша вернулась бы вопреки его решению;
    /// испорченное значение — умолчание, потому что оставить оператора без клавиши
    /// из-за опечатки в конфиге хуже, чем не угадать его намерение.</summary>
    public static HotkeySpec? Effective(HotkeyAction action, string? saved)
    {
        if (saved == "-") return null;
        if (string.IsNullOrWhiteSpace(saved)) return Defaults[action];
        return HotkeySpec.Parse(saved) ?? Defaults[action];
    }

    /// <summary>Зарегистрировать заданный набор. Возвращает сочетания, которые занять НЕ
    /// удалось: их держит другая программа. Вызывающий обязан показать это пользователю —
    /// молча неработающая горячая клавиша хуже отсутствующей, человек будет жать и считать,
    /// что сломался софтфон.</summary>
    public static List<string> Register(IntPtr hwnd, IReadOnlyDictionary<HotkeyAction, HotkeySpec> keys)
    {
        Attach(hwnd);
        Suspend();
        _current = keys;
        return Occupy();
    }

    /// <summary>Подписаться на WM_HOTKEY, ничего пока не занимая. Вызывается всегда, даже
    /// когда горячие клавиши выключены: иначе включить их в настройках можно было бы только
    /// с перезапуском — окно, которому слать сообщения, осталось бы неизвестным.</summary>
    public static void Attach(IntPtr hwnd)
    {
        if (_hwnd == hwnd) return;
        _proc ??= WndProc;
        SetWindowSubclass(hwnd, _proc, SubclassId, UIntPtr.Zero);
        _hwnd = hwnd;
    }

    /// <summary>Сменить набор на лету. Требовать перезапуск ради переназначенной клавиши —
    /// неуважение к оператору, у которого в этот момент идёт рабочий день.</summary>
    public static List<string> Rebind(IReadOnlyDictionary<HotkeyAction, HotkeySpec> keys)
    {
        if (_hwnd == IntPtr.Zero) return new List<string>();
        Suspend();
        _current = keys;
        return Occupy();
    }

    /// <summary>Прочитать назначения из настроек приложения.</summary>
    public static Dictionary<HotkeyAction, HotkeySpec> FromConfig(ISipService sip)
    {
        var result = new Dictionary<HotkeyAction, HotkeySpec>();
        foreach (var action in AllActions)
            if (Effective(action, sip.GetAppString(ConfigKey(action), "")) is { } spec)
                result[action] = spec;
        return result;
    }

    private static IReadOnlyDictionary<HotkeyAction, HotkeySpec> _current =
        new Dictionary<HotkeyAction, HotkeySpec>();

    private static List<string> Occupy()
    {
        _registered.Clear();
        var failed = new List<string>();
        foreach (var (action, spec) in _current)
        {
            if (!spec.IsValid) continue;   // пользователь снял назначение — это не ошибка
            if (RegisterHotKey(_hwnd, (int)action, spec.Mods | ModNoRepeat, spec.Vk))
                _registered.Add(action);
            else
                failed.Add(spec.Format());
        }
        return failed;
    }

    /// <summary>Отпустить свои сочетания, не забывая их. Нужно окну настроек на время
    /// захвата: зарегистрированное сочетание система отдаёт ТОЛЬКО как WM_HOTKEY главному
    /// окну, поэтому назначить Ctrl+Alt+H поверх Ctrl+Alt+H было бы физически невозможно
    /// (окно настроек не увидело бы нажатия), а Ctrl+Alt+A вместо захвата ответил бы на звонок.</summary>
    public static void Suspend()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var action in _registered) UnregisterHotKey(_hwnd, (int)action);
        _registered.Clear();
    }

    /// <summary>Вернуть занятые сочетания после Suspend.</summary>
    public static void Resume()
    {
        if (_hwnd == IntPtr.Zero || _registered.Count > 0) return;
        Occupy();
    }

    // Что реально удалось занять — снимать надо ровно это, иначе UnregisterHotKey
    // отработает вхолостую по чужим идентификаторам.
    private static readonly List<HotkeyAction> _registered = new();

    public static void Unregister()
    {
        if (_hwnd == IntPtr.Zero) return;
        Suspend();
        if (_proc != null) RemoveWindowSubclass(_hwnd, _proc, SubclassId);
        _hwnd = IntPtr.Zero;
    }

    /// <summary>Проверить, свободно ли сочетание СЕЙЧАС: занимаем на мгновение и отпускаем.
    /// Нужно для настроек — сказать «занято другой программой» до сохранения, а не после
    /// перезапуска. Идентификатор берём заведомо не пересекающийся с рабочими.</summary>
    public static bool IsAvailable(IntPtr hwnd, HotkeySpec spec)
    {
        if (!spec.IsValid) return false;
        const int probeId = 0x4C50;   // 'LP'
        if (!RegisterHotKey(hwnd, probeId, spec.Mods | ModNoRepeat, spec.Vk)) return false;
        UnregisterHotKey(hwnd, probeId);
        return true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (Enum.IsDefined(typeof(HotkeyAction), id)) Pressed?.Invoke((HotkeyAction)id);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
