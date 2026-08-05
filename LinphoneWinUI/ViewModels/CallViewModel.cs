using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LinphoneWinUI.ViewModels;

/// <summary>
/// Наблюдаемое состояние экрана активного звонка: имя собеседника, строка статуса
/// (таймер / «На удержании» / «Соединение…»), набранные за разговор DTMF-точки.
/// Только отображаемое состояние — режимы экрана, доступность кнопок и видимости
/// пока управляет code-behind View (мигрируют отдельными шагами).
/// </summary>
public partial class CallViewModel : ObservableObject
{
    /// <summary>Идёт разговор. Нужно не только экрану звонка: на пульте BLF во время
    /// разговора появляется плашка, которую перетаскивают на коллегу, чтобы перевести.</summary>
    [ObservableProperty] public partial bool Active { get; set; }

    [ObservableProperty] public partial string RemoteName { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string DtmfText { get; set; } = "";

    /// <summary>Чем зашифрован разговор ("none"/"srtp"/"zrtp"/"dtls") — как ответило ядро.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEncrypted))]
    [NotifyPropertyChangedFor(nameof(EncryptionGlyph))]
    [NotifyPropertyChangedFor(nameof(EncryptionTip))]
    [NotifyPropertyChangedFor(nameof(EncryptionWeak))]
    public partial string Encryption { get; set; } = "none";

    /// <summary>Шла ли сигнализация звонка по TLS. От этого зависит, чего стоит SRTP-SDES:
    /// его ключ передаётся внутри SDP, и по UDP/TCP он виден любому на пути.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncryptionTip))]
    [NotifyPropertyChangedFor(nameof(EncryptionWeak))]
    public partial bool SignallingTls { get; set; }

    /// <summary>Применить состояние защиты, пришедшее от ядра, одним куском — чтобы два
    /// свойства не разъезжались и подсказка не успевала показать полуправду.</summary>
    public void SetSecurity(Interop.CallSecurity security)
    {
        Encryption = security.Encryption;
        SignallingTls = security.SignallingTls;
    }

    /// <summary>Шифрование есть, но ключ ехал открытым каналом. Замок в этом случае
    /// показываем предупреждающим цветом: обещать защиту, которой нет, опаснее, чем
    /// не обещать ничего, — человек говорит свободно именно потому, что видит замок.</summary>
    public bool EncryptionWeak => Encryption == "srtp" && !SignallingTls;

    /// <summary>Показывать ли значок замка. Открытый разговор не помечаем НИЧЕМ: перечёркнутый
    /// замок на каждом звонке через обычный шлюз превратился бы в фон, который перестают замечать,
    /// и обесценил бы предупреждение там, где оно действительно важно.</summary>
    public bool IsEncrypted => Encryption is "srtp" or "zrtp" or "dtls";

    /// <summary>Закрытый замок — договорённое шифрование медиа (Segoe Fluent Icons).</summary>
    public string EncryptionGlyph => "";

    /// <summary>Подсказка к замку: какой именно механизм, а не просто «защищено».
    /// ZRTP помечаем особо — без сверки контрольной фразы он не защищает от подмены,
    /// и обещать пользователю больше, чем есть, нельзя.</summary>
    public string EncryptionTip => Encryption switch
    {
        // Порядок веток важен: слабый случай обязан перебивать обычный SRTP.
        "srtp" when EncryptionWeak => Loc.S("St_EncSrtpWeak"),
        "srtp" => Loc.F("St_EncSrtp", "SRTP"),
        "zrtp" => Loc.S("St_EncZrtpUnverified"),
        "dtls" => Loc.F("St_EncSrtp", "DTLS-SRTP"),
        _ => "",
    };

    /// <summary>Качество связи прямо сейчас. Опрашивается по таймеру во время разговора.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityVisible))]
    [NotifyPropertyChangedFor(nameof(QualityPoor))]
    [NotifyPropertyChangedFor(nameof(QualityBar1))]
    [NotifyPropertyChangedFor(nameof(QualityBar2))]
    [NotifyPropertyChangedFor(nameof(QualityBar3))]
    [NotifyPropertyChangedFor(nameof(QualityTip))]
    public partial Interop.CallQuality Quality { get; set; } = Interop.CallQuality.None;

    /// <summary>Показывать индикатор. Пока оценки нет (первые секунды разговора — RTCP ещё
    /// не ответил), не показываем ничего: пустые палочки читались бы как «связи нет».</summary>
    public bool QualityVisible => Quality.Level != Interop.QualityLevel.Unknown;

    /// <summary>Связь плохая — палочки красим тревожным цветом. Отдельное свойство, потому
    /// что цвет меняется скачком, а не плавно по уровню.</summary>
    public bool QualityPoor => Quality.Level == Interop.QualityLevel.Bad;

    // Три палочки: горят снизу вверх. Три, а не пять — индикатор стоит рядом с таймером,
    // и различать пять градаций на такой высоте всё равно невозможно.
    public bool QualityBar1 => Quality.Level >= Interop.QualityLevel.Bad;
    public bool QualityBar2 => Quality.Level >= Interop.QualityLevel.Fair;
    public bool QualityBar3 => Quality.Level >= Interop.QualityLevel.Good;

    /// <summary>Подсказка с цифрами: потери, задержка, джиттер. Именно она превращает
    /// «плохо слышно» из спора в измерение.</summary>
    public string QualityTip => Quality.Describe();

    /// <summary>Уровень микрофона 0..1. Отвечает на вопрос «меня вообще слышно?» — тот самый,
    /// который оператор задаёт собеседнику вслух, когда индикатора нет.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicBarWidth))]
    public partial double MicLevel { get; set; }

    /// <summary>Микрофон выключен: полоску гасим и красим, а не оставляем «тишиной» —
    /// молчащий индикатор при включённом микрофоне и при выключенном должен выглядеть
    /// по-разному, иначе он бесполезен ровно в тот момент, когда нужен.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicBarWidth))]
    public partial bool MicMuted { get; set; }

    /// <summary>Микрофона в системе нет вовсе (например, сняли Bluetooth-гарнитуру, а
    /// встроенного микрофона у машины нет). Полоска при этом обязана показывать ноль:
    /// замершая на середине означала бы «вас слышно», когда вас не слышит никто.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicBarWidth))]
    public partial bool MicMissing { get; set; }

    /// <summary>Ширина заполненной части полоски (сама полоска — 56 px под кнопкой).
    /// При выключенном микрофоне полоса заполнена ЦЕЛИКОМ и красная: пустая полоска
    /// означала бы «тихо», а это совсем другая беда, чем «вас не слышат вообще».</summary>
    public double MicBarWidth => MicMuted || MicMissing ? 56 : Math.Round(56 * Math.Clamp(MicLevel, 0, 1));

    /// <summary>Добавить набранную во время разговора цифру к строке DTMF.</summary>
    public void AppendDtmf(char digit) => DtmfText += digit;

    /// <summary>Сбросить набранные DTMF (вход в разговор / его завершение).</summary>
    public void ResetDtmf() => DtmfText = "";
}
