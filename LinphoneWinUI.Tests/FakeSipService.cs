using System;
using System.Linq;
using System.Collections.Generic;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Fake-реализация ISipService для юнит-тестов ViewModels: записывает вызовы
/// (звонки, отправленные сообщения, пометки «прочитано») и отдаёт задаваемые
/// тестом данные (история чата, переписки, журнал звонков). Ядро не трогает.
/// </summary>
#pragma warning disable CS0067 // события интерфейса в тестах не поднимаются
public sealed class FakeSipService : ISipService
{
    // --- Записанные вызовы ---
    public List<string> PlacedCalls { get; } = new();
    public List<(string Peer, string Text)> SentChats { get; } = new();
    public List<string> MarkedRead { get; } = new();
    public List<string> DeletedChats { get; } = new();
    public List<string> SyncedPresencePeers { get; } = new();
    public int ClearHistoryCalls { get; private set; }

    // --- Задаваемые тестом данные ---
    public bool SendChatResult { get; set; } = true;
    public Dictionary<string, List<ChatLine>> ChatHistories { get; } = new();
    public List<ChatConversation> Conversations { get; } = new();
    public int UnreadCount { get; set; }
    public List<CallLogEntry> CallHistory { get; } = new();

    // --- Жизненный цикл ---
    public bool Start() => true;
    public bool IsStarted => true;
    public bool CoreReady => true;
    public bool CoreFailed => false;
    public string HealthSummary() => "fake";
    public string CoreVersion => "test";
    public string ProfileDir => "";
    public void Dispose() { }

    // --- События ---
    public event Action<LinphoneRegistrationState, string?>? RegistrationStateChanged;
    public event Action<CallEvent>? CallStateChanged;
    public event Action<string>? Log;
    public event Action<string, string>? ChatMessageReceived;
    public event Action? PresenceChanged;
    public event Action<string>? RegistrationAuthStopped;
    public event Action<bool>? TransferResult;
    public event Action<bool, int>? VoicemailChanged;

    // --- Звонки ---
    public void Call(string target) => PlacedCalls.Add(target);
    public void Hangup() => HungUp = true;
    public void AcceptIncoming() => Accepted = true;

    /// <summary>Последний набранный номер — для проверок, что команда дошла именно с ним.</summary>
    public string? LastCalled => PlacedCalls.Count > 0 ? PlacedCalls[^1] : null;
    public bool HungUp { get; private set; }
    public bool Accepted { get; private set; }

    /// <summary>Сколько звонков «идёт» — этим управляют тесты занятой линии.</summary>
    public int CallCountValue { get; set; }
    public void DeclineIncoming() { }
    public void DeclineBusy() { }
    public int CallCount => CallCountValue;
    public void SwapCalls() { }
    public void SendDtmf(char digit) { }
    public void PlayLocalDtmf(char digit) { }
    /// <summary>Куда переводили — тесты сверяют цель, а не только факт.</summary>
    public string? LastTransferTarget;
    public CommandOutcome TransferOutcome = CommandOutcome.Done;
    public CommandOutcome Transfer(string target) { LastTransferTarget = target; return TransferOutcome; }
    public CommandOutcome TransferToHeldCall() => TransferOutcome;
    /// <summary>Команды переключения: тест проверяет, что они дошли, а состояние приходит
    /// событием (в бою — с потока ядра).</summary>
    public int MuteToggles { get; private set; }
    public int HoldToggles { get; private set; }

    public void ToggleMute() { MuteToggles++; RaiseMicMute(!MicMuted); }
    public void ToggleHold() => HoldToggles++;
    public bool MicMuted { get; private set; }
    public event Action<bool>? MicMuteChanged;

    /// <summary>Сымитировать сообщение ядра о смене состояния микрофона.</summary>
    public void RaiseMicMute(bool muted) { MicMuted = muted; MicMuteChanged?.Invoke(muted); }
    public int CurrentCallDuration => 0;
    public void RefreshNetwork() { }
    /// <summary>Последнее объявленное состояние сети (тесты сна/пробуждения).</summary>
    public bool? NetworkReachable;
    public void SetNetworkReachable(bool reachable) => NetworkReachable = reachable;
    /// <summary>Чем зашифрован разговор. Подменяется тестами экрана звонка.</summary>
    public string Encryption { get; set; } = "none";
    public string CurrentCallEncryption() => Encryption;

    /// <summary>Идёт ли сигнализация по TLS — задаётся тестом (по умолчанию нет, как у
    /// аккаунта на UDP: именно в этом сочетании SRTP-SDES и оказывается декоративным).</summary>
    public bool SignallingTls { get; set; }
    public CallSecurity CurrentCallSecurity() => new(Encryption, SignallingTls);
    public CallQuality Quality { get; set; } = CallQuality.None;
    public CallQuality CurrentQuality() => Quality;
    public (float Mic, float Speaker) Levels { get; set; }
    public (float Mic, float Speaker) CurrentAudioLevels() => Levels;
    public event Action? AudioDevicesChanged;
    public IReadOnlyList<string> AudioDeviceIds(bool input) => input ? InputDeviceIds : OutputDeviceIds;

    /// <summary>Есть ли живой микрофон — задаётся тестом (по умолчанию есть).</summary>
    public bool HasRealMicrophone { get; set; } = true;
    public CallEvent? CurrentCallSnapshot() => null;
    public CallEvent? DisplayCallSnapshot() => null;

    // --- Конференция ---
    public bool IsInConference => false;
    public void HoldCurrent() { }
    public CommandOutcome MergeCallsIntoConference() => CommandOutcome.Rejected;
    public void TerminateConference() { }

    // --- Запись разговора ---
    public bool Recording;
    public bool IsRecording => Recording;
    public event Action<bool>? RecordingChanged;
    public string? RecordPath => null;
    public void ToggleRecording()
    {
        Recording = !Recording;
        RecordingChanged?.Invoke(Recording);
    }
    public string GetRecordingsDir() => "";
    public void SetRecordingsDir(string? path) { }

    // --- Чат ---
    public bool SendChat(string peer, string text)
    {
        if (!SendChatResult) return false;
        SentChats.Add((peer, text));
        return true;
    }

    public List<ChatLine> GetChatHistory(string peer) =>
        ChatHistories.TryGetValue(peer, out var h) ? new List<ChatLine>(h) : new List<ChatLine>();

    public int GetUnreadChatCount() => UnreadCount;
    public List<ChatConversation> GetConversations() => new(Conversations);
    public void MarkChatRead(string peer) => MarkedRead.Add(peer);
    public void DeleteChat(string peer) { DeletedChats.Add(peer); Conversations.RemoveAll(c => c.Peer == peer); }

    // --- Presence ---
    public void SyncPresenceFriends(IEnumerable<string> peers) => SyncedPresencePeers.AddRange(peers);
    public List<string> SyncedDialogPeers { get; } = new();
    public void SyncDialogSubscriptions(IEnumerable<string> extensions) => SyncedDialogPeers.AddRange(extensions);
#pragma warning disable CS0067 // тестовый двойник события не поднимает
    public event Action<string, DialogState, string>? DialogStateChanged;
#pragma warning restore CS0067
    public void SetPresencePublish(bool on) { }
    public PresenceStatus MyStatus => PresenceStatus.Available;
    public void SetMyStatus(PresenceStatus s) { }
    public void PublishOnThePhone(bool onCall) { }
    public PresenceStatus GetStatus(string peer) => Statuses.TryGetValue(peer, out var s) ? s : PresenceStatus.Offline;

    /// <summary>Задаваемые тестом статусы абонентов.</summary>
    public Dictionary<string, PresenceStatus> Statuses { get; } = new();
    public int GetStatusesCalls { get; private set; }

    public Dictionary<string, PresenceStatus> GetStatuses(IEnumerable<string> peers)
    {
        GetStatusesCalls++;
        var r = new Dictionary<string, PresenceStatus>();
        foreach (var p in peers) r[p] = GetStatus(p);
        return r;
    }

    // --- Аудио/видео устройства ---
    // --- Аудиоустройства: подставной набор для тестов маршрутизации ---
    public string[] InputDeviceIds = Array.Empty<string>();
    public string[] OutputDeviceIds = Array.Empty<string>();
    public string? InputDeviceId;
    public string? OutputDeviceId;
    public int ReloadCount;
    /// <summary>Порядок присвоений — тесты перепривязки проверяют именно его.</summary>
    public readonly List<string> InputDeviceSets = new();
    public readonly List<string> OutputDeviceSets = new();

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        InputDeviceIds.Select(id => new AudioDeviceInfo(id, id)).ToList();
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        OutputDeviceIds.Select(id => new AudioDeviceInfo(id, id)).ToList();
    public void ReloadSoundDevices() => ReloadCount++;
    public string? CurrentInputDeviceId => InputDeviceId;
    public string? CurrentOutputDeviceId => OutputDeviceId;
    public void SetInputDevice(string id) { InputDeviceSets.Add(id); InputDeviceId = id; }
    public void SetOutputDevice(string id) { OutputDeviceSets.Add(id); OutputDeviceId = id; }
    public string GetRingerDeviceId() => "";
    public void SetRingerDevice(string id) { }
    public List<string> GetCameras() => new();
    public string? CurrentCamera => null;
    public void SetCamera(string name) { }
    public bool StartMicRecording(string wavPath) => false;
    public void StopMicRecording() { }

    // --- Видео ---
    public void SetupVideo() { }
    public bool CameraOn;
    public event Action<bool>? CameraStateChanged;
    public void ToggleVideo()
    {
        CameraOn = !CameraOn;
        CameraStateChanged?.Invoke(CameraOn);
    }
    public void StartCameraPreview() { }
    public void StopCameraPreview() { }
    public bool IsSendingVideo() => false;
    public bool IsCameraOn() => false;
    public bool IsVideoActive() => false;

    // --- Кодеки и шифрование ---
    public List<CodecInfo> GetAudioCodecs() => new();
    public List<CodecInfo> GetVideoCodecs() => new();
    public void SetCodecEnabled(IntPtr pt, bool on) { }
    public void SetAudioCodecsOrder(IEnumerable<IntPtr> pts) { }
    public void SetVideoCodecsOrder(IEnumerable<IntPtr> pts) { }
    public string GetMediaEncryption() => "none";
    public void SetMediaEncryption(string enc) { }
    public bool GetEchoCancellation() => false;
    public void SetEchoCancellation(bool on) { }
    public string GetDtmfMethod() => "rfc2833";
    public void SetDtmfMethod(string method) { }

    // --- Аккаунты ---
    public string? GetDefaultAccountUsername() => null;
    public AccountInfo? GetAccountInfo() => null;
    public List<AccountInfo> GetAccounts() => new();
    public void AddOrUpdateAccount(string displayName, string login, string password, string server,
                                   int port, string transport, int expires, bool makeDefault) { }
    public void RemoveAccount(string sipIdentity) { }
    public void SetDefaultAccount(string sipIdentity) { }

    // --- Журнал звонков ---
    public IReadOnlyList<CallLogEntry> GetCallHistory() => CallHistory.ToArray();
    public void ClearCallHistory() { ClearHistoryCalls++; CallHistory.Clear(); }
    public int GetMissedSince(long sinceUnixSeconds) => 0;

    // --- Конфигурация (rc) ---
    public void SyncConfig() { }
    public string GetPhonebookUrl() => "";
    public void SetPhonebookUrl(string url) { }
    public (string User, string Password) GetPhonebookAuth() => ("", "");
    public void SetPhonebookAuth(string user, string password) { }
    public string GetVoicemailNumber() => "*97";
    public void SetVoicemailNumber(string number) { }
    public string GetPresenceHookSecret() => "";
    public string ApiToken = "";
    public string GetApiToken() => ApiToken;
    public void SetApiToken(string token) => ApiToken = token;
    public void SetPresenceHookSecret(string secret) { }
    public string GetRingtone() => "";
    public void SetRingtone(string? path) { }
    public List<string> GetBuiltInRingtones() => new();
    public string GetProvisioningUri() => "";
    public void SetProvisioningUri(string uri) { }
    public (string Stun, bool Ice, bool Turn, string TurnUser) GetNat() => ("", false, false, "");
    public bool SetNat(string stun, bool ice, bool turn, string turnUser, string turnPass, string turnRealm) => true;
    public bool HasTurnPassword(string turnUser) => false;
    public (string CaFile, bool Verify) GetTls() => ("", true);
    public bool SetTls(string caFile, bool verify) => true;
    public string GetTheme() => "system";
    public void SetTheme(string theme) { }
    /// <summary>Настройки rc, которые тест может задать заранее (иначе — значение по умолчанию).</summary>
    public Dictionary<string, bool> Flags { get; } = new();
    public Dictionary<string, string> Strings { get; } = new();

    public bool GetAppFlag(string key, bool def) => Flags.TryGetValue(key, out bool v) ? v : def;
    public void SetAppFlag(string key, bool value) => Flags[key] = value;
    public string GetAppString(string key, string def) => Strings.TryGetValue(key, out string? v) ? v : def;
    public void SetAppString(string key, string value) => Strings[key] = value;
    public void SetVerboseLogs(bool verbose) { }
}
#pragma warning restore CS0067
