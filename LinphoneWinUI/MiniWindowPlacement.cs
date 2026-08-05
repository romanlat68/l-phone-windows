using System;
using System.Globalization;

namespace LinphoneWinUI;

/// <summary>
/// Запомненное положение мини-окна в виде строки «X,Y».
///
/// Хранится в настройках как текст, а значит переживает и правку файла руками, и перенос
/// профиля на другую машину с другим числом мониторов. Поэтому разбор терпимый к мусору,
/// но не терпимый к бессмыслице: координата, которой не соответствует ни один экран,
/// должна быть отвергнута — иначе окно уедет «в никуда», и человек решит, что оно пропало.
/// Проверку экрана делает вызывающий (MinSizeHelper), здесь — только формат.
/// </summary>
public static class MiniWindowPlacement
{
    /// <summary>Собрать строку для сохранения.</summary>
    public static string Format(int x, int y) =>
        string.Create(CultureInfo.InvariantCulture, $"{x},{y}");

    /// <summary>Разобрать «X,Y». false — значения нет или оно испорчено.</summary>
    public static bool TryParse(string? saved, out int x, out int y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(saved)) return false;

        string[] parts = saved.Split(',');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out x) ||
            !int.TryParse(parts[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y))
            return false;

        // Отрицательные координаты законны: монитор слева от основного даёт минус по X.
        // А вот заведомо невозможные значения отбрасываем — это испорченный конфиг,
        // и окно по ним ушло бы за пределы любого разумного рабочего стола.
        return Math.Abs(x) <= 32000 && Math.Abs(y) <= 32000;
    }

    /// <summary>
    /// Углы окна, по которым проверяют, целиком ли оно на экранах. Точки вдвинуты на пиксель
    /// внутрь: угол ровно на границе принадлежит соседнему монитору, и окно, аккуратно
    /// прижатое к краю, иначе считалось бы вылезшим.
    /// </summary>
    public static (int X, int Y)[] Corners(int x, int y, int width, int height) =>
    [
        (x + 1, y + 1),
        (x + width - 1, y + 1),
        (x + 1, y + height - 1),
        (x + width - 1, y + height - 1),
    ];

    /// <summary>
    /// Вернуть окно в границы рабочей области, чтобы оно было видно ЦЕЛИКОМ.
    ///
    /// Вызывать только когда окно действительно вылезло за экраны (см. <see cref="Corners"/>):
    /// у монитора справа от основного начало координат по X больше нуля, и окно, законно
    /// стоящее на стыке двух экранов, зажимать нельзя — иначе оно будет прыгать от края
    /// к середине у людей с двумя мониторами.
    ///
    /// Если окно крупнее области (мелкий экран, огромный масштаб) — прижимаем к левому
    /// верхнему углу: пусть лучше торчит правый край, чем кнопки сброса окажутся за границей.
    /// </summary>
    public static (int X, int Y) Clamp(int x, int y, int width, int height,
                                       int areaX, int areaY, int areaWidth, int areaHeight)
    {
        int maxX = areaX + Math.Max(0, areaWidth - width);
        int maxY = areaY + Math.Max(0, areaHeight - height);
        return (Math.Clamp(x, areaX, maxX), Math.Clamp(y, areaY, maxY));
    }
}
