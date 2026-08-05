using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Железо и медиа: микрофоны, динамики, камеры, кодеки, шифрование медиа, видео.
/// </summary>
public interface IDeviceService
{
    /// <summary>Набор аудиоустройств изменился: воткнули или выдернули гарнитуру.
    /// Само ядро при этом ничего не переключает — решение за приложением.</summary>
    event Action? AudioDevicesChanged;

    /// <summary>Идентификаторы доступных устройств (input=true — микрофоны).</summary>
    IReadOnlyList<string> AudioDeviceIds(bool input);

    /// <summary>Есть ли НАСТОЯЩИЙ микрофон, а не только псевдоустройство «по умолчанию».
    /// Отдаётся снимком: этот вопрос задаётся во время разговора несколько раз в секунду,
    /// а перечисление аудиоустройств для такой частоты слишком дорого.</summary>
    bool HasRealMicrophone { get; }

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    void ReloadSoundDevices();
    string? CurrentInputDeviceId { get; }
    string? CurrentOutputDeviceId { get; }
    void SetInputDevice(string id);
    void SetOutputDevice(string id);
    string GetRingerDeviceId();
    void SetRingerDevice(string id);
    List<string> GetCameras();
    string? CurrentCamera { get; }
    void SetCamera(string name);
    bool StartMicRecording(string wavPath);
    void StopMicRecording();

    // Видео
    void SetupVideo();

    /// <summary>Переключить свою камеру. Новое состояние приходит событием
    /// <see cref="CameraStateChanged"/>: команда уходит на поток ядра, и ждать её результата
    /// синхронно нельзя — по таймауту кнопка нарисовала бы состояние, обратное настоящему.</summary>
    void ToggleVideo();

    /// <summary>Камеру включили или выключили. Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<bool>? CameraStateChanged;

    void StartCameraPreview();
    void StopCameraPreview();
    bool IsSendingVideo();

    /// <summary>Идут ли реальные кадры с камеры (снимок ядра; не подменена статичной картинкой).</summary>
    bool IsCameraOn();
    bool IsVideoActive();

    // Кодеки и шифрование
    List<CodecInfo> GetAudioCodecs();
    List<CodecInfo> GetVideoCodecs();
    void SetCodecEnabled(IntPtr pt, bool on);
    void SetAudioCodecsOrder(IEnumerable<IntPtr> pts);
    void SetVideoCodecsOrder(IEnumerable<IntPtr> pts);
    string GetMediaEncryption();
    void SetMediaEncryption(string enc);
    bool GetEchoCancellation();
    void SetEchoCancellation(bool on);
    string GetDtmfMethod();
    void SetDtmfMethod(string method);
}
