using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Слоты парковки, заданные строкой из настроек («501-520», «501-520,530»).
///
/// Диапазон обязан совпадать с `parkpos` в res_parking.conf на АТС: клиент подписывается
/// ровно на эти номера, и лишний слот — это SUBSCRIBE в пустоту и строка «has no associated
/// hint» в логе Asterisk на каждое обновление, а недостающий — звонок, который припаркован,
/// но нигде не показан. Поэтому разбор строгий: мусор молча не проглатываем.
/// </summary>
public static class ParkingSlots
{
    /// <summary>Сколько слотов имеет смысл показывать. Пульт из сотни плиток — не пульт,
    /// а список; и подписка на каждую из них — это трафик NOTIFY на ровном месте.</summary>
    public const int MaxSlots = 40;

    /// <summary>Разобрать «501-520» или «501-520,530» в список номеров. Пустой список —
    /// строка не разобрана; вызывающий обязан отличать это от «слотов нет».</summary>
    public static IReadOnlyList<string> Parse(string? spec)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(spec)) return result;

        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string p = part.Trim();
            int dash = p.IndexOf('-');
            if (dash < 0)
            {
                if (IsNumber(p)) Add(result, p);
                else return new List<string>();      // мусор в одном месте портит весь список
                continue;
            }

            string a = p[..dash].Trim(), b = p[(dash + 1)..].Trim();
            if (!IsNumber(a) || !IsNumber(b)) return new List<string>();
            if (!int.TryParse(a, out int from) || !int.TryParse(b, out int to)) return new List<string>();
            // Перевёрнутый диапазон — опечатка, а не «пусто»: молча вернуть ноль слотов
            // значило бы показать пустой пульт и оставить человека гадать.
            if (to < from) return new List<string>();
            // Ширина номера сохраняется («0501-0520» останутся четырёхзначными).
            int width = a.Length;
            for (int n = from; n <= to && result.Count < MaxSlots; n++)
                Add(result, n.ToString().PadLeft(width, '0'));
        }
        return result;
    }

    private static void Add(List<string> list, string slot)
    {
        if (list.Count < MaxSlots && !list.Contains(slot)) list.Add(slot);
    }

    private static bool IsNumber(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s) if (!char.IsDigit(c)) return false;
        return true;
    }
}
