using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Публичная поверхность SIP-ядра (реализуется <see cref="LinphoneService"/>).
/// ViewModels зависят от этого интерфейса, а не от конкретной обёртки liblinphone —
/// это seam для юнит-тестов (можно подставить fake) и граница, за которой при
/// желании когда-нибудь можно сменить SIP-стек, не трогая UI.
///
/// События поднимаются С ПОТОКА ЯДРА — подписчики маршалятся в свой поток сами.
/// </summary>
public interface ISipService : IDisposable
{
    // Жизненный цикл
    void Start();
    bool IsStarted { get; }
    string CoreVersion { get; }
    string ProfileDir { get; }

    // События
    event Action<LinphoneRegistrationState, string?>? RegistrationStateChanged;
    event Action<CallEvent>? CallStateChanged;
    event Action<string>? Log;
    event Action<string, string>? ChatMessageReceived;
    event Action? PresenceChanged;
    event Action<string>? RegistrationAuthStopped;
    event Action<bool>? TransferResult;
    /// <summary>MWI (голосовая почта): (есть ожидающие, число новых).</summary>
    event Action<bool, int>? VoicemailChanged;

    // Звонки
    void Call(string target);
    void Hangup();
    void AcceptIncoming();
    void DeclineIncoming();
    void DeclineBusy();
    int CallCount { get; }
    void SwapCalls();
    void SendDtmf(char digit);
    void PlayLocalDtmf(char digit);
    bool Transfer(string target);
    bool TransferToHeldCall();
    bool ToggleMute();
    bool ToggleHold();
    int CurrentCallDuration { get; }
    void RefreshNetwork();
    CallEvent? CurrentCallSnapshot();
    CallEvent? DisplayCallSnapshot();

    // Конференция
    bool IsInConference { get; }
    void HoldCurrent();
    bool MergeCallsIntoConference();
    void TerminateConference();

    // Запись разговора
    bool IsRecording { get; }
    string? RecordPath { get; }
    bool ToggleRecording();
    string GetRecordingsDir();
    void SetRecordingsDir(string? path);

    // Чат
    bool SendChat(string peer, string text);
    List<ChatLine> GetChatHistory(string peer);
    int GetUnreadChatCount();
    List<ChatConversation> GetConversations();
    void MarkChatRead(string peer);
    void DeleteChat(string peer);

    // Presence
    void SyncPresenceFriends(IEnumerable<string> peers);

    /// <summary>Подписка на состояние линий (BLF): presence отвечает «свободен ли»,
    /// dialog — «звонит ли прямо сейчас». Перехват осмыслен только во втором случае.</summary>
    void SyncDialogSubscriptions(IEnumerable<string> extensions);

    /// <summary>Линия коллеги сменила состояние. Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<string, DialogState>? DialogStateChanged;
    void SetPresencePublish(bool on);
    PresenceStatus MyStatus { get; }
    void SetMyStatus(PresenceStatus s);
    void PublishOnThePhone(bool onCall);
    PresenceStatus GetStatus(string peer);
    /// <summary>Статусы списка абонентов одним обращением к ядру (вместо N вызовов GetStatus).</summary>
    Dictionary<string, PresenceStatus> GetStatuses(IEnumerable<string> peers);

    // Аудио/видео устройства
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
    bool ToggleVideo();
    void StartCameraPreview();
    void StopCameraPreview();
    bool IsSendingVideo();
    /// <summary>Идут ли реальные кадры с камеры (не подменена статичной картинкой).</summary>
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

    // Аккаунты
    string? GetDefaultAccountUsername();
    AccountInfo? GetAccountInfo();
    List<AccountInfo> GetAccounts();
    void AddOrUpdateAccount(string displayName, string login, string password, string server,
                            int port, string transport, int expires, bool makeDefault);
    void RemoveAccount(string sipIdentity);
    void SetDefaultAccount(string sipIdentity);

    // Журнал звонков
    IReadOnlyList<CallLogEntry> GetCallHistory();
    void ClearCallHistory();
    int GetMissedSince(long sinceUnixSeconds);

    // Конфигурация (rc)
    void SyncConfig();
    string GetPhonebookUrl();
    void SetPhonebookUrl(string url);
    (string User, string Password) GetPhonebookAuth();
    void SetPhonebookAuth(string user, string password);
    string GetVoicemailNumber();
    void SetVoicemailNumber(string number);
    /// <summary>Секрет HMAC для подписи хука статуса ("" = без подписи).</summary>
    string GetPresenceHookSecret();
    void SetPresenceHookSecret(string secret);
    string GetRingtone();
    void SetRingtone(string? path);
    List<string> GetBuiltInRingtones();
    string GetProvisioningUri();
    void SetProvisioningUri(string uri);
    (string Stun, bool Ice, bool Turn, string TurnUser) GetNat();
    /// <summary>false — настройки НЕ применены (ядро недоступно/не ответило); UI обязан сообщить.
    /// turnRealm — realm TURN-сервера; с ним пароль ложится в rc хешем ha1, без него открытым.</summary>
    bool SetNat(string stun, bool ice, bool turn, string turnUser, string turnPass, string turnRealm);

    /// <summary>Доверие TLS: файл CA (пусто — бандл SDK) и включена ли проверка сертификата.</summary>
    (string CaFile, bool Verify) GetTls();

    /// <summary>false — не применено (нет ядра, нет функций в SDK либо указанного файла CA нет).</summary>
    bool SetTls(string caFile, bool verify);
    string GetTheme();
    void SetTheme(string theme);
    bool GetAppFlag(string key, bool def);
    void SetAppFlag(string key, bool value);
    string GetAppString(string key, string def);
    void SetAppString(string key, string value);
    void SetVerboseLogs(bool verbose);
}
