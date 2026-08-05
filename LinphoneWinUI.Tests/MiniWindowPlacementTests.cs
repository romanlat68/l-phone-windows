using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Запомненное положение мини-окна. Строка живёт в настройках и переживает перенос профиля
/// на машину с другим числом мониторов, поэтому испорченное значение обязано отвергаться:
/// окно, уехавшее по нему за пределы экранов, для человека просто пропало.
/// </summary>
public class MiniWindowPlacementTests
{
    [Fact]
    public void Format_ThenParse_RoundTrips()
    {
        Assert.True(MiniWindowPlacement.TryParse(MiniWindowPlacement.Format(1520, 880), out int x, out int y));
        Assert.Equal(1520, x);
        Assert.Equal(880, y);
    }

    // Монитор слева от основного даёт отрицательный X — это законное положение.
    [Fact]
    public void NegativeCoordinates_AreAllowed()
    {
        Assert.True(MiniWindowPlacement.TryParse("-1920,300", out int x, out int y));
        Assert.Equal(-1920, x);
        Assert.Equal(300, y);
    }

    [Fact]
    public void Spaces_AreTolerated() =>
        Assert.True(MiniWindowPlacement.TryParse(" 100 , 200 ", out _, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("100")]            // одна координата
    [InlineData("100,200,300")]    // три
    [InlineData("сто,двести")]
    [InlineData("100;200")]        // не тот разделитель
    [InlineData("1e5,200")]
    public void Garbage_IsRejected(string? saved) =>
        Assert.False(MiniWindowPlacement.TryParse(saved, out _, out _));

    // Заведомо невозможные значения — испорченный конфиг: по ним окно ушло бы за пределы
    // любого разумного рабочего стола.
    [Theory]
    [InlineData("999999,10")]
    [InlineData("10,-999999")]
    public void AbsurdCoordinates_AreRejected(string saved) =>
        Assert.False(MiniWindowPlacement.TryParse(saved, out _, out _));

    // Формат не должен зависеть от языка системы: в русской локали разделитель дробной
    // части — запятая, и «1520,880» обязано остаться двумя целыми, а не одним числом.
    [Fact]
    public void Format_IsCultureInvariant()
    {
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            string s = MiniWindowPlacement.Format(1520, 880);

            Assert.Equal("1520,880", s);
            Assert.True(MiniWindowPlacement.TryParse(s, out int x, out int y));
            Assert.Equal(1520, x);
            Assert.Equal(880, y);
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
    }

    // Экран 1920×1040 (рабочая область), окно 300×130 — типовой случай.
    private static (int X, int Y) OnScreen(int x, int y) =>
        MiniWindowPlacement.Clamp(x, y, 300, 130, 0, 0, 1920, 1040);

    [Fact]
    public void Clamp_LeavesWindowThatFitsAlone()
    {
        Assert.Equal((400, 250), OnScreen(400, 250));
    }

    // Ради этого всё и затевалось: окно, уведённое за правый край, восстанавливалось так,
    // что от 300 пикселей ширины на экране оставалось 60 — человек считал, что оно пропало.
    [Fact]
    public void Clamp_PullsBackWindowDraggedOffRightEdge()
    {
        var (x, _) = OnScreen(1860, 300);

        Assert.Equal(1620, x);              // правый край ровно на границе экрана
        Assert.Equal(1920, x + 300);
    }

    [Theory]
    [InlineData(400, 1000, 400, 910)]      // уехало вниз под панель задач
    [InlineData(-200, 300, 0, 300)]        // за левый край
    [InlineData(400, -80, 400, 0)]         // за верхний
    public void Clamp_PullsBackFromEveryEdge(int x, int y, int wantX, int wantY)
    {
        Assert.Equal((wantX, wantY), OnScreen(x, y));
    }

    // Монитор слева от основного: там законные отрицательные координаты, и зажимать
    // нужно в границы ЭТОГО монитора, а не выталкивать окно на основной.
    [Fact]
    public void Clamp_RespectsSecondMonitorWithNegativeOrigin()
    {
        var (x, y) = MiniWindowPlacement.Clamp(-2000, 300, 300, 130, -1920, 0, 1920, 1040);

        Assert.Equal(-1920, x);
        Assert.Equal(300, y);
    }

    // Окно крупнее рабочей области (крошечный экран, огромный масштаб): прижимаем к левому
    // верхнему углу — пусть лучше торчит правый край, чем кнопки уедут за границу.
    [Fact]
    public void Clamp_WindowLargerThanArea_GoesToTopLeft()
    {
        Assert.Equal((0, 0), MiniWindowPlacement.Clamp(50, 50, 300, 130, 0, 0, 200, 100));
    }

    // Углы решают, вылезло ли окно за экраны. Их четыре, потому что одной точки мало:
    // окно, уведённое за край, верхним левым углом всё ещё «на мониторе».
    [Fact]
    public void Corners_CoverWholeWindow()
    {
        var c = MiniWindowPlacement.Corners(100, 200, 300, 130);

        Assert.Equal(4, c.Length);
        Assert.Contains((101, 201), c);
        Assert.Contains((399, 201), c);
        Assert.Contains((101, 329), c);
        Assert.Contains((399, 329), c);
    }

    // Углы вдвинуты внутрь: точка ровно на границе принадлежит соседнему монитору, и окно,
    // аккуратно прижатое к краю экрана, иначе считалось бы вылезшим и прыгало бы с места.
    [Fact]
    public void Corners_StayInsideWindow()
    {
        // Экран 0..1920: окно прижато вплотную к правому краю.
        var c = MiniWindowPlacement.Corners(1620, 0, 300, 130);

        Assert.All(c, p => Assert.InRange(p.X, 1620, 1919));
    }
}
