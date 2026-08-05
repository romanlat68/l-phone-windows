using System;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Перевод уровня звука из децибел в шкалу индикатора.
///
/// liblinphone отдаёт громкость в dBm0: 0 — максимум, «тишина» приходит как очень большое
/// отрицательное число (обычно -120 или ниже). Показывать эти числа оператору бессмысленно,
/// а линейно растягивать диапазон нельзя: между -120 и -40 dB человеческому уху нет никакой
/// разницы — там одинаковая тишина, и полоска дёргалась бы на пустом месте.
///
/// Поэтому шкала начинается там, где начинается слышимое: тише -50 dB считаем тишиной,
/// громче -6 dB — полной шкалой. Обычная речь в этом диапазоне даёт примерно половину.
/// </summary>
public static class AudioLevel
{
    /// <summary>Ниже этого — тишина.</summary>
    public const float SilenceDb = -50f;

    /// <summary>Выше этого — полная шкала.</summary>
    public const float LoudDb = -6f;

    /// <summary>Уровень 0..1 для индикатора. NaN и «пусто» дают 0: неизвестный уровень
    /// показываем как тишину, а не как всплеск.</summary>
    public static float Normalize(float db)
    {
        if (float.IsNaN(db) || float.IsNegativeInfinity(db)) return 0f;
        if (db <= SilenceDb) return 0f;
        if (db >= LoudDb) return 1f;
        return (db - SilenceDb) / (LoudDb - SilenceDb);
    }

    /// <summary>Сглаженный уровень: индикатор растёт мгновенно, а спадает плавно.
    ///
    /// Так делают все измерители громкости, и не ради красоты: речь состоит из пауз между
    /// словами, и без спада полоска моргала бы в такт слогам, а не показывала, что человека
    /// слышно. Резкий рост при этом обязателен — иначе первый слог «съедается».</summary>
    public static float Smooth(float previous, float current, float fallPerTick = 0.12f)
    {
        if (current >= previous) return current;
        float next = previous - fallPerTick;
        return next < current ? current : next;
    }
}
