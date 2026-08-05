using LinphoneWinUI.Interop;
using LinphoneWinUI.LocalApi;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Что интеграция с CRM может сделать с телефоном. Здесь важнее обычного проверять ОТКАЗЫ:
/// это единственный слой между внешним запросом и живым разговором, и «позвонить по просьбе
/// CRM» не должно означать «оборвать то, что человек сейчас говорит».
/// </summary>
public class SipCallCommandsTests
{
    private sealed class Env
    {
        public FakeSipService Sip { get; } = new();
        public bool Incoming;
        public bool Registered = true;

        public SipCallCommands Commands => new(Sip, Sip, () => Incoming, () => Registered);
    }

    [Fact]
    public void Call_PlacesCallWhenIdle()
    {
        var env = new Env();

        Assert.True(env.Commands.Call("377"));
        Assert.Equal("377", env.Sip.LastCalled);
    }

    // Главная защита: линия занята — звонок не начинаем. Сбросить чужой разговор
    // «по просьбе CRM» было бы худшим из возможных сюрпризов.
    [Fact]
    public void Call_RefusedWhileBusy()
    {
        var env = new Env();
        env.Sip.CallCountValue = 1;

        Assert.False(env.Commands.Call("377"));
        Assert.Null(env.Sip.LastCalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Call_RefusesEmptyNumber(string? number)
    {
        var env = new Env();

        Assert.False(env.Commands.Call(number!));
        Assert.Null(env.Sip.LastCalled);
    }

    [Fact]
    public void Call_TrimsNumber()
    {
        var env = new Env();

        env.Commands.Call("  377  ");

        Assert.Equal("377", env.Sip.LastCalled);
    }

    // Завершать нечего — честно отвечаем «нет», а не делаем вид, что сработало.
    [Fact]
    public void Hangup_RefusedWhenNothingToHangUp()
    {
        var env = new Env();

        Assert.False(env.Commands.Hangup());
        Assert.False(env.Sip.HungUp);
    }

    [Fact]
    public void Hangup_WorksDuringCall()
    {
        var env = new Env();
        env.Sip.CallCountValue = 1;

        Assert.True(env.Commands.Hangup());
        Assert.True(env.Sip.HungUp);
    }

    // Входящий тоже можно отклонить через API — это ожидаемое «положить трубку».
    [Fact]
    public void Hangup_WorksOnIncoming()
    {
        var env = new Env();
        env.Incoming = true;

        Assert.True(env.Commands.Hangup());
    }

    [Fact]
    public void Answer_RefusedWithoutIncoming()
    {
        var env = new Env();

        Assert.False(env.Commands.Answer());
        Assert.False(env.Sip.Accepted);
    }

    [Fact]
    public void Answer_AcceptsIncoming()
    {
        var env = new Env();
        env.Incoming = true;

        Assert.True(env.Commands.Answer());
        Assert.True(env.Sip.Accepted);
    }

    [Fact]
    public void Status_ReportsIdle()
    {
        var env = new Env();

        Assert.Equal("idle", env.Commands.Status().State);
    }

    [Fact]
    public void Status_ReportsRinging()
    {
        var env = new Env();
        env.Incoming = true;

        Assert.Equal("ringing", env.Commands.Status().State);
    }

    // Разговор важнее звонка: если идёт и то и другое, состояние должно говорить «incall».
    [Fact]
    public void Status_PrefersInCallOverRinging()
    {
        var env = new Env();
        env.Sip.CallCountValue = 1;
        env.Incoming = true;

        Assert.Equal("incall", env.Commands.Status().State);
    }

    [Fact]
    public void Status_ReportsRegistration()
    {
        var env = new Env();
        env.Registered = false;

        Assert.False(env.Commands.Status().Registered);
    }

    [Fact]
    public void Status_ReportsVersion()
    {
        var env = new Env();

        Assert.False(string.IsNullOrWhiteSpace(env.Commands.Status().Version));
    }
}
