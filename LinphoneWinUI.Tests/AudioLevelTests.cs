using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Шкала индикатора микрофона. Индикатор отвечает на вопрос «меня слышно?», и врать он не
/// имеет права ни в одну сторону: молчащая полоска при работающем микрофоне заставит
/// оператора лезть в настройки посреди разговора, а дрожащая при выключенном — поверить,
/// что его слышат, когда его нет.
/// </summary>
public class AudioLevelTests
{
    // liblinphone отдаёт тишину как очень большое отрицательное число. Между -120 и -60 дБ
    // никакой разницы на слух нет — обе величины обязаны давать ноль, иначе полоска
    // дёргается на пустом месте.
    [Theory]
    [InlineData(-120f)]
    [InlineData(-90f)]
    [InlineData(-60f)]
    [InlineData(-50f)]
    public void Silence_IsZero(float db) => Assert.Equal(0f, AudioLevel.Normalize(db));

    [Theory]
    [InlineData(-6f)]
    [InlineData(0f)]
    [InlineData(3f)]
    public void Loud_IsFull(float db) => Assert.Equal(1f, AudioLevel.Normalize(db));

    [Fact]
    public void Speech_IsInTheMiddle()
    {
        float level = AudioLevel.Normalize(-28f);   // типичная громкость речи

        Assert.InRange(level, 0.3f, 0.7f);
    }

    [Fact]
    public void Scale_IsMonotonic()
    {
        Assert.True(AudioLevel.Normalize(-40f) < AudioLevel.Normalize(-30f));
        Assert.True(AudioLevel.Normalize(-30f) < AudioLevel.Normalize(-20f));
        Assert.True(AudioLevel.Normalize(-20f) < AudioLevel.Normalize(-10f));
    }

    // «Нет данных» — это тишина, а не всплеск: неизвестный уровень не должен выглядеть
    // как громкий звук.
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void Unknown_IsZero(float db) => Assert.Equal(0f, AudioLevel.Normalize(db));

    // Рост мгновенный — иначе первый слог «съедается» и человек думает, что его не слышно.
    [Fact]
    public void Rise_IsImmediate() => Assert.Equal(0.9f, AudioLevel.Smooth(0.1f, 0.9f));

    // Спад плавный: речь состоит из пауз между словами, и без сглаживания полоска моргала бы
    // в такт слогам вместо того, чтобы показывать «тебя слышно».
    [Fact]
    public void Fall_IsGradual()
    {
        float next = AudioLevel.Smooth(0.9f, 0f);

        Assert.True(next > 0f);
        Assert.True(next < 0.9f);
    }

    [Fact]
    public void Fall_ReachesTargetEventually()
    {
        float v = 1f;
        for (int i = 0; i < 20; i++) v = AudioLevel.Smooth(v, 0f);

        Assert.Equal(0f, v);
    }

    // Сглаживание не должно проскакивать ниже цели — иначе полоска уходила бы в минус.
    [Fact]
    public void Fall_DoesNotOvershoot() => Assert.Equal(0.5f, AudioLevel.Smooth(0.55f, 0.5f, 0.12f));
}
