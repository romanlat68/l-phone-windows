using System;

namespace LinphoneWinUI.Interop;

/// <summary>Как оценено качество разговора. Порядок значений — от плохого к хорошему.</summary>
public enum QualityLevel
{
    /// <summary>Оценки ещё нет: RTCP приходит раз в несколько секунд, и первые мгновения
    /// разговора судить не по чему. Показывать в этот момент «плохо» — клевета на связь.</summary>
    Unknown = 0,
    Bad = 1,
    Fair = 2,
    Good = 3,
    Excellent = 4,
}

/// <summary>Метрики разговора: оценка liblinphone плюс сырые числа для подсказки.</summary>
public readonly record struct CallQuality(float Rating, float LossPercent, int RttMs, int JitterMs)
{
    /// <summary>Данных нет вовсе — звонок только начался либо статистика недоступна.</summary>
    public static readonly CallQuality None = new(-1f, 0f, 0, 0);

    /// <summary>
    /// Ступень для индикатора. Опираемся на оценку liblinphone (0..5, она же MOS-подобная),
    /// но потери перебивают её вниз: разговор с 5 % потерь звучит рвано независимо от того,
    /// что насчитала библиотека, и оператору важно увидеть это раньше, чем собеседник
    /// начнёт переспрашивать.
    ///
    /// Пороги по потерям взяты из практики VoIP: до 1 % незаметно, до 3 % терпимо,
    /// свыше 5 % разговор рассыпается.
    /// </summary>
    public QualityLevel Level
    {
        get
        {
            if (Rating < 0f) return QualityLevel.Unknown;

            QualityLevel byRating = Rating >= 4f ? QualityLevel.Excellent
                                  : Rating >= 3f ? QualityLevel.Good
                                  : Rating >= 2f ? QualityLevel.Fair
                                  : QualityLevel.Bad;

            QualityLevel byLoss = LossPercent >= 5f ? QualityLevel.Bad
                                : LossPercent >= 3f ? QualityLevel.Fair
                                : LossPercent >= 1f ? QualityLevel.Good
                                : QualityLevel.Excellent;

            // Задержка свыше 300 мс — это уже разговор с перебиваниями (рекомендация G.114),
            // даже когда пакеты доходят все до одного.
            QualityLevel byRtt = RttMs >= 500 ? QualityLevel.Bad
                               : RttMs >= 300 ? QualityLevel.Fair
                               : QualityLevel.Excellent;

            return Min(Min(byRating, byLoss), byRtt);
        }
    }

    private static QualityLevel Min(QualityLevel a, QualityLevel b) => a < b ? a : b;

    /// <summary>Строка для подсказки и лога: только те метрики, которые реально измерены.
    /// Ноль у задержки означает «RTCP ещё не ответил», а не «нулевая задержка», и выдавать
    /// его за измерение нельзя.</summary>
    public string Describe()
    {
        if (Rating < 0f) return "";
        var parts = new System.Collections.Generic.List<string>(4)
        {
            $"{Loc.S("Q_Loss")}: {LossPercent:0.0}%",
        };
        if (RttMs > 0) parts.Add($"{Loc.S("Q_Rtt")}: {RttMs} {Loc.S("Q_Ms")}");
        if (JitterMs > 0) parts.Add($"{Loc.S("Q_Jitter")}: {JitterMs} {Loc.S("Q_Ms")}");
        parts.Add($"{Loc.S("Q_Rating")}: {Rating:0.0}/5");
        return string.Join(" · ", parts);
    }
}
