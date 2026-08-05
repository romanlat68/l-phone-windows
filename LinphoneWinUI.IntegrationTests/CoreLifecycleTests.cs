using System;
using System.IO;
using System.Linq;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.IntegrationTests;

/// <summary>
/// Проверки поверх НАСТОЯЩЕГО liblinphone: что P/Invoke-слой действительно работает с той
/// сборкой SDK, которая приезжает с пакетом.
///
/// Зачем это перед рефакторингом: юнит-тесты покрывают ViewModels и утилиты, а весь interop —
/// 2600 строк ручных сигнатур и владения нативной памятью — не покрыт ничем. Ошибка в нём не
/// видна ни компилятору, ни быстрым тестам; она проявляется на живом звонке у пользователя.
/// </summary>
[Collection(CoreCollection.Name)]
public class CoreLifecycleTests
{
    private readonly CoreFixture _fx;
    public CoreLifecycleTests(CoreFixture fx) => _fx = fx;

    /// <summary>Среда без нативных библиотек — не дефект кода, тест просто не выполняется.</summary>
    private bool Skip => !_fx.Started;

    [Fact]
    public void Core_Starts_AndReportsVersion()
    {
        if (Skip) return;

        Assert.True(_fx.Sip.IsStarted, "Core не запущен: " + _fx.StartError);
        Assert.False(string.IsNullOrWhiteSpace(_fx.Sip.CoreVersion));
        Assert.NotEqual("?", _fx.Sip.CoreVersion);
    }

    [Fact]
    public void Core_UsesGivenProfileDirectory()
    {
        if (Skip) return;

        // Ключевая гарантия всего набора: ядро пишет в ВЫДАННЫЙ каталог, а не в боевой профиль.
        Assert.Equal(_fx.ProfileDir, _fx.Sip.ProfileDir);
        Assert.True(File.Exists(Path.Combine(_fx.ProfileDir, "linphonerc")),
            "Конфигурация не создана во временном профиле — ядро пишет куда-то ещё");
    }

    [Fact]
    public void AudioCodecs_AreEnumerated_WithUsableLabels()
    {
        if (Skip) return;

        var codecs = _fx.Sip.GetAudioCodecs();

        Assert.NotEmpty(codecs);
        Assert.All(codecs, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Label));
            Assert.NotEqual(IntPtr.Zero, c.Pt);   // указатель нужен, чтобы кодек можно было включить
        });
        // PCMA/PCMU есть в любой сборке SDK: если их нет — перечисление читает не то.
        Assert.Contains(codecs, c => c.Label.Contains("PCMA", StringComparison.OrdinalIgnoreCase)
                                  || c.Label.Contains("PCMU", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CodecToggle_SurvivesReread()
    {
        if (Skip) return;

        var before = _fx.Sip.GetAudioCodecs();
        var target = before.FirstOrDefault(c => c.Enabled);
        if (target == null) return;   // всё выключено — переключать нечего

        _fx.Sip.SetCodecEnabled(target.Pt, false);
        var after = _fx.Sip.GetAudioCodecs().First(c => c.Label == target.Label);
        Assert.False(after.Enabled);

        _fx.Sip.SetCodecEnabled(after.Pt, true);   // вернуть как было
        Assert.True(_fx.Sip.GetAudioCodecs().First(c => c.Label == target.Label).Enabled);
    }

    [Fact]
    public void AppFlagsAndStrings_RoundTrip()
    {
        if (Skip) return;

        _fx.Sip.SetAppFlag("it_flag", true);
        Assert.True(_fx.Sip.GetAppFlag("it_flag", false));
        _fx.Sip.SetAppFlag("it_flag", false);
        Assert.False(_fx.Sip.GetAppFlag("it_flag", true));

        _fx.Sip.SetAppString("it_string", "значение с кириллицей");
        Assert.Equal("значение с кириллицей", _fx.Sip.GetAppString("it_string", ""));
    }

    [Fact]
    public void MediaEncryption_RoundTrips()
    {
        if (Skip) return;

        string original = _fx.Sip.GetMediaEncryption();
        try
        {
            _fx.Sip.SetMediaEncryption("srtp");
            Assert.Equal("srtp", _fx.Sip.GetMediaEncryption());
            _fx.Sip.SetMediaEncryption("none");
            Assert.Equal("none", _fx.Sip.GetMediaEncryption());
        }
        finally { _fx.Sip.SetMediaEncryption(original); }
    }

    [Fact]
    public void Nat_Applies_AndReadsBack()
    {
        if (Skip) return;

        // Политики NAT у свежего Core нет — SetNat обязан её создать, иначе настройки
        // STUN/ICE/TURN молча не сохранялись бы (так и было до правки в аудите).
        bool ok = _fx.Sip.SetNat("stun.example.org", ice: true, turn: false, "", "", "");

        Assert.True(ok, "SetNat вернул false — политика NAT не создана");
        var (stun, iceOn, turnOn, _) = _fx.Sip.GetNat();
        Assert.Equal("stun.example.org", stun);
        Assert.True(iceOn);
        Assert.False(turnOn);
    }

    [Fact]
    public void Tls_DefaultsToVerifying_AndApplies()
    {
        if (Skip) return;

        var (_, verifyByDefault) = _fx.Sip.GetTls();
        Assert.True(verifyByDefault, "Проверка сертификата сервера должна быть включена по умолчанию");

        Assert.True(_fx.Sip.SetTls("", verify: true), "Возврат к бандлу CA не применился");
        // Несуществующий файл CA принимать нельзя: молча подменить доверие хуже, чем отказать.
        Assert.False(_fx.Sip.SetTls(Path.Combine(_fx.ProfileDir, "нет-такого-ca.pem"), verify: true));
    }

    [Fact]
    public void RecordingsDir_RoundTrips_AndFallsBackToDefault()
    {
        if (Skip) return;

        string custom = Path.Combine(_fx.ProfileDir, "recs");
        _fx.Sip.SetRecordingsDir(custom);
        Assert.Equal(custom, _fx.Sip.GetRecordingsDir());

        _fx.Sip.SetRecordingsDir(null);
        Assert.Equal(LinphoneCore.DefaultRecordingsDir, _fx.Sip.GetRecordingsDir());
    }

    [Fact]
    public void CallHistory_IsReadable_OnEmptyProfile()
    {
        if (Skip) return;

        // Пустой журнал — нормальный случай; важно, что чтение не падает и не отдаёт мусор.
        var log = _fx.Sip.GetCallHistory();
        Assert.NotNull(log);
        Assert.Equal(0, _fx.Sip.GetMissedSince(0));
    }

    [Fact]
    public void NoCall_MeansNoEncryption_AndNoSnapshot()
    {
        if (Skip) return;

        Assert.Equal(0, _fx.Sip.CallCount);
        Assert.Null(_fx.Sip.CurrentCallSnapshot());
        Assert.Equal("none", _fx.Sip.CurrentCallEncryption());
        Assert.False(_fx.Sip.IsRecording);

        // Записывать нечего. Команда теперь ничего не возвращает (результат приходит событием
        // RecordingChanged — см. аудит r6, C-5), поэтому проверяем ФАКТ: состояние не
        // изменилось и события не было. Прежний возврат по просрочке врал: «не пишется» при
        // идущей записи.
        bool eventRaised = false;
        void OnChanged(bool _) => eventRaised = true;
        _fx.Sip.RecordingChanged += OnChanged;
        try
        {
            _fx.Sip.ToggleRecording();
            // Команда — Post: даём потоку ядра разобрать очередь, прежде чем судить.
            System.Threading.Thread.Sleep(300);
            Assert.False(_fx.Sip.IsRecording);
            Assert.False(eventRaised);
        }
        finally { _fx.Sip.RecordingChanged -= OnChanged; }
    }

    [Fact]
    public void CoreReady_IsTrue_OnLiveCore_AndCostsNoWait()
    {
        if (Skip) return;

        // Готовность обязана читаться БЕЗ обращения к ядру: именно этим опросом интерфейс
        // ждёт медленный старт, и Query здесь стоил бы 5 секунд на тик (аудит r6, C-1).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) Assert.True(_fx.Sip.CoreReady);
        sw.Stop();

        Assert.False(_fx.Sip.CoreFailed);
        Assert.True(sw.ElapsedMilliseconds < 50, $"сто опросов заняли {sw.ElapsedMilliseconds} мс");
    }

    [Fact]
    public void HealthSummary_IsAvailable()
    {
        if (Skip) return;

        string summary = _fx.Sip.HealthSummary();
        Assert.False(string.IsNullOrWhiteSpace(summary));
        Assert.Contains("просрочек", summary);
    }

    [Fact]
    public void KeepAlivePeriod_IsActuallySet_NotJustAttempted()
    {
        if (Skip) return;

        // Проверяем ФАКТ в конфиге, а не строку в журнале. Прямого API
        // (linphone_core_set_keep_alive_period) в поставляемом SDK 5.4.122 НЕТ, и прежний код
        // ловил EntryPointNotFound молча: период оставался умолчанием библиотеки, заявленная
        // защита от закрытия NAT-биндинга не действовала, а отсутствие строки в логе выглядело
        // как «просто не записали». За NAT это ровно тот отказ, ради которого настройка и
        // заведена: маршрутизатор закрывает пробитый UDP-порт примерно через 30 секунд, и
        // входящие перестают доходить при живой на вид регистрации.
        _fx.Sip.SyncConfig();
        string rc = Path.Combine(_fx.ProfileDir, "linphonerc");
        Assert.True(File.Exists(rc), $"нет {rc}");

        string text = File.ReadAllText(rc);
        Assert.Contains("keepalive_period=25000", text.Replace(" ", ""));
    }
}
