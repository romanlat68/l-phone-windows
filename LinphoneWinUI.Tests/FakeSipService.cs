using System;
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
    public void Start() { }
    public bool IsStarted => true;
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
    public void Hangup() { }
    public void AcceptIncoming() { }
    public void DeclineIncoming() { }
    public void DeclineBusy() { }
    public int CallCount => 0;
    public void SwapCalls() { }
    public void SendDtmf(char digit) { }
    public void PlayLocalDtmf(char digit) { }
    public bool Transfer(string target) => true;
    public bool TransferToHeldCall() => true;
    public bool ToggleMute() => false;
    public bool ToggleHold() => false;
    public int CurrentCallDuration => 0;
    public void RefreshNetwork() { }
    public CallEvent? CurrentCallSnapshot() => null;
    public CallEvent? DisplayCallSnapshot() => null;

    // --- Конференция ---
    public bool IsInConference => false;
    public void HoldCurrent() { }
    public bool MergeCallsIntoConference() => false;
    public void TerminateConference() { }

    // --- Запись разговора ---
    public bool IsRecording => false;
    public string? RecordPath => null;
    public bool ToggleRecording() => false;
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
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => Array.Empty<AudioDeviceInfo>();
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => Array.Empty<AudioDeviceInfo>();
    public void ReloadSoundDevices() { }
    public string? CurrentInputDeviceId => null;
    public string? CurrentOutputDeviceId => null;
    public void SetInputDevice(string id) { }
    public void SetOutputDevice(string id) { }
    public string GetRingerDeviceId() => "";
    public void SetRingerDevice(string id) { }
    public List<string> GetCameras() => new();
    public string? CurrentCamera => null;
    public void SetCamera(string name) { }
    public bool StartMicRecording(string wavPath) => false;
    public void StopMicRecording() { }

    // --- Видео ---
    public void SetupVideo() { }
    public bool ToggleVideo() => false;
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
    public void SetPresenceHookSecret(string secret) { }
    public string GetRingtone() => "";
    public void SetRingtone(string? path) { }
    public List<string> GetBuiltInRingtones() => new();
    public string GetProvisioningUri() => "";
    public void SetProvisioningUri(string uri) { }
    public (string Stun, bool Ice, bool Turn, string TurnUser) GetNat() => ("", false, false, "");
    public bool SetNat(string stun, bool ice, bool turn, string turnUser, string turnPass, string turnRealm) => true;
    public (string CaFile, bool Verify) GetTls() => ("", true);
    public bool SetTls(string caFile, bool verify) => true;
    public string GetTheme() => "system";
    public void SetTheme(string theme) { }
    public bool GetAppFlag(string key, bool def) => def;
    public void SetAppFlag(string key, bool value) { }
    public string GetAppString(string key, string def) => def;
    public void SetAppString(string key, string value) { }
    public void SetVerboseLogs(bool verbose) { }
}
#pragma warning restore CS0067
