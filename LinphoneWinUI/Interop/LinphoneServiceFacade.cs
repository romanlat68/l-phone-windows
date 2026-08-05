using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>
/// Публичный фасад SIP-ядра. liblinphone требует строгой однопоточности: единственный поток
/// «linphone-core» (см. <see cref="CoreDispatcher"/>) гоняет iterate() и исполняет задания.
///
/// Правило вызовов — от него зависит отзывчивость интерфейса:
/// <list type="bullet">
/// <item><b>Команды</b> (Call, Hangup, SetAppFlag, MarkChatRead…) — <c>Post</c>: UI не ждёт ядро.</item>
/// <item><b>Запросы</b> — <c>Query</c> с таймаутом и безопасным значением по умолчанию.</item>
/// <item><b>Критичные команды</b> (сохранение аккаунта) — <c>Invoke</c>: провал виден вызывающему.</item>
/// </list>
/// Часто читаемое состояние (число звонков, активность видео, настройки rc) UI берёт из
/// снимков/кэша — без перехода на поток ядра вообще.
///
/// События поднимаются С ПОТОКА ЯДРА — подписчики маршалятся в свой поток сами
/// (в MainWindow/SettingsWindow это уже так: DispatcherQueue.TryEnqueue).
/// </summary>
public sealed class LinphoneService : ISipService
{
    private const int IterateIntervalMs = 20;

    private readonly LinphoneCore _core;
    private readonly CoreDispatcher _dispatcher;

    // Кэш настроек rc: их читают из UI десятками вызовов (флаги DND/автоответ, номера, URL),
    // а меняются они только через этот же фасад. Держим в памяти — ядро не тревожим.
    private readonly ConcurrentDictionary<string, bool> _flagCache = new();
    private readonly ConcurrentDictionary<string, string> _stringCache = new();

    /// <param name="profileDirOverride">Каталог профиля вместо боевого. Задают ТОЛЬКО
    /// интеграционные тесты — иначе прогон писал бы в рабочий linphonerc пользователя.</param>
    public LinphoneService(string? profileDirOverride = null)
    {
        _core = new LinphoneCore(profileDirOverride);
        _dispatcher = new CoreDispatcher(_core.Iterate, IterateIntervalMs);
        _dispatcher.Diagnostics += msg => _core.RaiseLog(msg);
    }

    // ---------------- Жизненный цикл ----------------
    /// <summary>Запускает ядро на выделенном потоке; ждёт готовности не дольше таймаута
    /// диспетчера (или бросает ошибку старта).</summary>
    /// <returns>false — ядро ещё поднимается: интерфейс показывать можно, но состояние
    /// регистрации появится позже.</returns>
    public bool Start()
    {
        // Ядро откладывает через эту очередь действия, которые нельзя делать из нативного
        // колбэка (например, resume удержанного звонка из call_state_changed).
        _core.Defer = _dispatcher.Post;
        return _dispatcher.Start(_core.Start, "linphone-core");
    }

    public void Dispose()
    {
        _dispatcher.Stop(_core.Dispose, TimeSpan.FromSeconds(3));
        _dispatcher.Dispose();
    }

    private void Post(Action a) => _dispatcher.Post(a);
    private T Query<T>(Func<T> f, T fallback = default!,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "") => _dispatcher.Query(f, fallback, caller);

    /// <summary>Запрос, результат которого пойдёт в кэш: false — ядро не ответило, кэшировать НЕЛЬЗЯ.</summary>
    private bool TryQuery<T>(Func<T> f, T fallback, out T value,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "") => _dispatcher.TryQuery(f, fallback, out value, caller);

    /// <summary>Здоровье ядра за сеанс одной строкой (медленные вызовы, просрочки).
    /// Ядро НЕ трогает — только собственные счётчики диспетчера, поэтому безопасно
    /// звать даже при мёртвом Core, в том числе при выходе.</summary>
    public string HealthSummary() => _dispatcher.HealthSummary();

    public bool IsStarted => Query(() => _core.IsStarted, false);

    /// <summary>Готовность ядра БЕЗ обращения к нему — три поля диспетчера. Именно это должен
    /// опрашивать интерфейс: <see cref="IsStarted"/> во время инициализации ждёт полный таймаут,
    /// потому что поток ядра занят init и очередь не разбирает.</summary>
    public bool CoreReady => _dispatcher.CoreReady;

    /// <summary>Ядро упало при старте (а не «ещё поднимается»).</summary>
    public bool CoreFailed => _dispatcher.StartFailed;
    public string CoreVersion => Query(() => _core.CoreVersion, "?");
    public string ProfileDir => Query(() => _core.ProfileDir, "");

    // ---------------- События (поднимаются с потока ядра!) ----------------
    public event Action<LinphoneRegistrationState, string?>? RegistrationStateChanged
    { add => _core.RegistrationStateChanged += value; remove => _core.RegistrationStateChanged -= value; }

    public event Action<CallEvent>? CallStateChanged
    { add => _core.CallStateChanged += value; remove => _core.CallStateChanged -= value; }

    public event Action<string>? Log
    { add => _core.Log += value; remove => _core.Log -= value; }

    public event Action<string, string>? ChatMessageReceived
    { add => _core.ChatMessageReceived += value; remove => _core.ChatMessageReceived -= value; }

    public event Action? PresenceChanged
    { add => _core.PresenceChanged += value; remove => _core.PresenceChanged -= value; }

    public event Action<string>? RegistrationAuthStopped
    { add => _core.RegistrationAuthStopped += value; remove => _core.RegistrationAuthStopped -= value; }

    public event Action<bool>? TransferResult
    { add => _core.TransferResult += value; remove => _core.TransferResult -= value; }

    public event Action<bool, int>? VoicemailChanged
    { add => _core.VoicemailChanged += value; remove => _core.VoicemailChanged -= value; }

    // ---------------- Звонки ----------------
    // Команды звонка — Post: клик по «Позвонить»/«Сброс» не должен ждать конца iterate().
    public void Call(string target) => Post(() => _core.Call(target));
    public void Hangup() => Post(_core.Hangup);
    public void AcceptIncoming() => Post(_core.AcceptIncoming);
    public void DeclineIncoming() => Post(_core.DeclineIncoming);
    public void DeclineBusy() => Post(_core.DeclineBusy);
    /// <summary>Снимок: обновляется ядром при каждой смене состояния звонка.</summary>
    public int CallCount => _core.CallCountSnapshot;
    public void SwapCalls() => Post(_core.SwapCalls);
    public void SendDtmf(char digit) => Post(() => _core.SendDtmf(digit));
    public void PlayLocalDtmf(char digit) => Post(() => _core.PlayLocalDtmf(digit));
    // Итог перевода различает «ядро отказало» и «ядро промолчало»: пользователю в этих
    // случаях надо сказать разное, а прежний общий false их склеивал.
    public CommandOutcome Transfer(string target) => Outcome(() => _core.Transfer(target));
    public CommandOutcome TransferToHeldCall() => Outcome(_core.TransferToHeldCall);

    /// <summary>Команда, чей провал видит пользователь: различаем отказ ядра и его молчание.</summary>
    private CommandOutcome Outcome(Func<bool> command,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (!TryQuery(command, false, out bool ok, caller)) return CommandOutcome.CoreUnavailable;
        return ok ? CommandOutcome.Done : CommandOutcome.Rejected;
    }
    // Команды, а не запросы: результат приходит событием/снимком. Раньше это были Query,
    // то есть клик по «Микрофону» ждал ядро — и по таймауту рисовал состояние, обратное
    // тому, которое ядро устанавливало секундой позже.
    public void ToggleMute() => Post(_core.ToggleMute);
    public void ToggleHold() => Post(_core.ToggleHold);

    /// <summary>Микрофон заглушён (снимок ядра, без перехода на его поток).</summary>
    public bool MicMuted => _core.MicMutedSnapshot;

    public event Action<bool>? MicMuteChanged
    { add => _core.MicMuteChanged += value; remove => _core.MicMuteChanged -= value; }
    public int CurrentCallDuration => Query(() => _core.CurrentCallDuration, 0);
    public void RefreshNetwork() => Post(_core.RefreshNetwork);
    public void SetNetworkReachable(bool reachable) => Post(() => _core.SetNetworkReachable(reachable));
    public string CurrentCallEncryption() => Query(_core.CurrentCallEncryption, "none");
    public CallSecurity CurrentCallSecurity() => Query(_core.CurrentCallSecurity, CallSecurity.None);
    public CallQuality CurrentQuality() => Query(_core.CurrentQuality, CallQuality.None);

    /// <summary>Уровни микрофона и динамика. СНИМОК, обновляемый на потоке ядра: индикатор
    /// опрашивает их восемь раз в секунду, и синхронный переход на поток ядра с такой частотой
    /// подвешивал интерфейс каждый раз, когда ядро задерживалось внутри iterate.</summary>
    public (float Mic, float Speaker) CurrentAudioLevels() =>
        (_core.MicLevelSnapshot, _core.SpeakerLevelSnapshot);

    /// <summary>Есть ли настоящий микрофон (снимок; см. CurrentAudioLevels).</summary>
    public bool HasRealMicrophone => _core.HasRealMicSnapshot;

    public event Action? AudioDevicesChanged
    { add => _core.AudioDevicesChanged += value; remove => _core.AudioDevicesChanged -= value; }

    public IReadOnlyList<string> AudioDeviceIds(bool input) =>
        Query(() => _core.AudioDeviceIds(input), (IReadOnlyList<string>)System.Array.Empty<string>());
    public CallEvent? CurrentCallSnapshot() => Query<CallEvent?>(_core.CurrentCallSnapshot, null);
    public CallEvent? DisplayCallSnapshot() => Query<CallEvent?>(_core.DisplayCallSnapshot, null);

    // ---------------- Конференция ----------------
    public bool IsInConference => Query(() => _core.IsInConference, false);
    public void HoldCurrent() => Post(_core.HoldCurrent);
    public CommandOutcome MergeCallsIntoConference() => Outcome(_core.MergeCallsIntoConference);
    public void TerminateConference() => Post(_core.TerminateConference);

    // ---------------- Запись разговора ----------------
    // Команда, а не запрос: результат приходит событием и снимком. Прежний Query по просрочке
    // возвращал «не пишется», интерфейс гасил красную точку — а запись при этом шла.
    public bool IsRecording => _core.RecordingSnapshot;
    public string? RecordPath => Query<string?>(() => _core.RecordPath, null);
    public void ToggleRecording() => Post(_core.ToggleRecording);

    public event Action<bool>? RecordingChanged
    { add => _core.RecordingChanged += value; remove => _core.RecordingChanged -= value; }
    public string GetRecordingsDir() => Query(_core.GetRecordingsDir, "");
    public void SetRecordingsDir(string? path) => Post(() => _core.SetRecordingsDir(path));

    // ---------------- Чат ----------------
    public bool SendChat(string peer, string text) => Query(() => _core.SendChat(peer, text), false);
    public List<ChatLine> GetChatHistory(string peer) => Query(() => _core.GetChatHistory(peer), new List<ChatLine>());
    public int GetUnreadChatCount() => Query(_core.GetUnreadChatCount, 0);
    public List<ChatConversation> GetConversations() => Query(_core.GetConversations, new List<ChatConversation>());
    public void MarkChatRead(string peer) => Post(() => _core.MarkChatRead(peer));
    public void DeleteChat(string peer) => Post(() => _core.DeleteChat(peer));

    // ---------------- Presence ----------------
    public event Action<string, DialogState, string>? DialogStateChanged
    { add => _core.DialogStateChanged += value; remove => _core.DialogStateChanged -= value; }

    public void SyncDialogSubscriptions(IEnumerable<string> extensions)
    { var s = new List<string>(extensions); Post(() => _core.SyncDialogSubscriptions(s)); }

    public void SyncPresenceFriends(IEnumerable<string> peers)
    {
        var snapshot = new List<string>(peers);   // копия: задание исполнится позже, на другом потоке
        Post(() => _core.SyncPresenceFriends(snapshot));
    }
    public void SetPresencePublish(bool on) => Post(() => _core.SetPresencePublish(on));
    public PresenceStatus MyStatus => Query(() => _core.MyStatus, PresenceStatus.Offline);
    public void SetMyStatus(PresenceStatus s) => Post(() => _core.SetMyStatus(s));
    public void PublishOnThePhone(bool onCall) => Post(() => _core.PublishOnThePhone(onCall));
    public PresenceStatus GetStatus(string peer) => Query(() => _core.GetStatus(peer), PresenceStatus.Offline);
    /// <summary>Статусы списка одним переходом на поток ядра (вместо N вызовов GetStatus).</summary>
    public Dictionary<string, PresenceStatus> GetStatuses(IEnumerable<string> peers)
    {
        var snapshot = new List<string>(peers);
        return Query(() => _core.GetStatuses(snapshot), new Dictionary<string, PresenceStatus>());
    }

    // ---------------- Аудио/видео устройства ----------------
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => Query(_core.GetInputDevices, Array.Empty<AudioDeviceInfo>());
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => Query(_core.GetOutputDevices, Array.Empty<AudioDeviceInfo>());
    // Порядок в очереди гарантирован: следующий за этим GetInputDevices увидит перечитанный список.
    public void ReloadSoundDevices() => Post(_core.ReloadSoundDevices);
    public string? CurrentInputDeviceId => Query<string?>(() => _core.CurrentInputDeviceId, null);
    public string? CurrentOutputDeviceId => Query<string?>(() => _core.CurrentOutputDeviceId, null);
    public void SetInputDevice(string id) => Post(() => _core.SetInputDevice(id));
    public void SetOutputDevice(string id) => Post(() => _core.SetOutputDevice(id));
    public string GetRingerDeviceId() => Query(_core.GetRingerDeviceId, "");
    public void SetRingerDevice(string id) => Post(() => _core.SetRingerDevice(id));
    public List<string> GetCameras() => Query(_core.GetCameras, new List<string>());
    public string? CurrentCamera => Query<string?>(() => _core.CurrentCamera, null);
    public void SetCamera(string name) => Post(() => _core.SetCamera(name));
    public bool StartMicRecording(string wavPath) => Query(() => _core.StartMicRecording(wavPath), false);
    public void StopMicRecording() => Post(_core.StopMicRecording);

    // ---------------- Видео ----------------
    public void SetupVideo() => Post(_core.SetupVideo);
    // Команда, а не запрос (см. ToggleRecording): состояние камеры приходит событием и снимком.
    public void ToggleVideo() => Post(_core.ToggleVideo);

    public event Action<bool>? CameraStateChanged
    { add => _core.CameraStateChanged += value; remove => _core.CameraStateChanged -= value; }

    public void StartCameraPreview() => Post(_core.StartCameraPreview);
    public void StopCameraPreview() => Post(_core.StopCameraPreview);
    public bool IsSendingVideo() => Query(_core.IsSendingVideo, false);
    public bool IsCameraOn() => _core.CameraOnSnapshot;
    /// <summary>Снимок: читается на каждом переключении вкладки — ядро тревожить нельзя.</summary>
    public bool IsVideoActive() => _core.VideoActiveSnapshot;

    // ---------------- Кодеки и шифрование ----------------
    public List<CodecInfo> GetAudioCodecs() => Query(_core.GetAudioCodecs, new List<CodecInfo>());
    public List<CodecInfo> GetVideoCodecs() => Query(_core.GetVideoCodecs, new List<CodecInfo>());
    public void SetCodecEnabled(IntPtr pt, bool on) => Post(() => _core.SetCodecEnabled(pt, on));
    public void SetAudioCodecsOrder(IEnumerable<IntPtr> pts)
    { var s = new List<IntPtr>(pts); Post(() => _core.SetAudioCodecsOrder(s)); }
    public void SetVideoCodecsOrder(IEnumerable<IntPtr> pts)
    { var s = new List<IntPtr>(pts); Post(() => _core.SetVideoCodecsOrder(s)); }
    public string GetMediaEncryption() => Query(_core.GetMediaEncryption, "none");
    public void SetMediaEncryption(string enc) => Post(() => _core.SetMediaEncryption(enc));
    public bool GetEchoCancellation() => Query(_core.GetEchoCancellation, false);
    public void SetEchoCancellation(bool on) => Post(() => _core.SetEchoCancellation(on));
    public string GetDtmfMethod() => Query(_core.GetDtmfMethod, "rfc2833");
    public void SetDtmfMethod(string method) => Post(() => _core.SetDtmfMethod(method));

    // ---------------- Аккаунты ----------------
    public string? GetDefaultAccountUsername() => Query<string?>(_core.GetDefaultAccountUsername, null);
    public AccountInfo? GetAccountInfo() => Query<AccountInfo?>(_core.GetAccountInfo, null);
    public List<AccountInfo> GetAccounts() => Query(_core.GetAccounts, new List<AccountInfo>());
    /// <summary>Критично: ошибку обязан увидеть пользователь (форма аккаунта показывает её).</summary>
    public void AddOrUpdateAccount(string displayName, string login, string password, string server,
                                   int port, string transport, int expires, bool makeDefault)
        => _dispatcher.Invoke(() => _core.AddOrUpdateAccount(displayName, login, password, server, port, transport, expires, makeDefault));
    public void RemoveAccount(string sipIdentity) => _dispatcher.Invoke(() => _core.RemoveAccount(sipIdentity));
    public void SetDefaultAccount(string sipIdentity) => _dispatcher.Invoke(() => _core.SetDefaultAccount(sipIdentity));

    // ---------------- Журнал звонков ----------------
    public IReadOnlyList<CallLogEntry> GetCallHistory() => Query(_core.GetCallHistory, Array.Empty<CallLogEntry>());
    public void ClearCallHistory() => Post(_core.ClearCallHistory);
    public int GetMissedSince(long sinceUnixSeconds) => Query(() => _core.GetMissedSince(sinceUnixSeconds), 0);

    // ---------------- Конфигурация (rc) ----------------
    public void SyncConfig() => Post(_core.SyncConfig);
    public string GetPhonebookUrl() => GetAppStringCached("phonebook_url", () => _core.GetPhonebookUrl(), "");
    public void SetPhonebookUrl(string url)
    {
        // Нормализуем ОДИН раз и передаём дальше уже непустую строку: раньше в кэш ложилось
        // "", а в ядро — исходный null, то есть кэш и конфиг расходились на ровном месте.
        string v = url ?? "";
        _stringCache["phonebook_url"] = v;
        Post(() => _core.SetPhonebookUrl(v));
    }
    public (string User, string Password) GetPhonebookAuth() => Query(_core.GetPhonebookAuth, ("", ""));
    public void SetPhonebookAuth(string user, string password) => Post(() => _core.SetPhonebookAuth(user, password));
    public string GetVoicemailNumber() => GetAppStringCached("voicemail_number", () => _core.GetVoicemailNumber(), "*97");
    public void SetVoicemailNumber(string number)
    {
        string v = number ?? "";   // см. SetPhonebookUrl
        _stringCache["voicemail_number"] = v;
        Post(() => _core.SetVoicemailNumber(v));
    }
    public string GetPresenceHookSecret() => Query(_core.GetPresenceHookSecret, "");
    public void SetPresenceHookSecret(string secret) => Post(() => _core.SetPresenceHookSecret(secret));
    /// <summary>Токен локального API. Хранится в DPAPI-хранилище, не в rc (см. LinphoneCore).</summary>
    public string GetApiToken() => Query(_core.GetApiToken, "");
    public void SetApiToken(string token) => Post(() => _core.SetApiToken(token));
    public string GetRingtone() => Query(_core.GetRingtone, "");
    public void SetRingtone(string? path) => Post(() => _core.SetRingtone(path));
    public List<string> GetBuiltInRingtones() => Query(_core.GetBuiltInRingtones, new List<string>());
    public string GetProvisioningUri() => Query(_core.GetProvisioningUri, "");
    public void SetProvisioningUri(string uri) => Post(() => _core.SetProvisioningUri(uri));
    public (string Stun, bool Ice, bool Turn, string TurnUser) GetNat() => Query(_core.GetNat, ("", false, false, ""));
    /// <summary>Не Post, а Query: результат «Применить» обязан увидеть пользователь —
    /// по просрочке/остановленному ядру вернётся false, а не тихое «применено».</summary>
    public bool SetNat(string stun, bool ice, bool turn, string turnUser, string turnPass, string turnRealm)
        => Query(() => _core.SetNat(stun, ice, turn, turnUser, turnPass, turnRealm), false);
    /// <summary>Ядро молчит — отвечаем «не задан»: подсказка под полем не стоит того, чтобы
    /// подвесить настройки, а введённый заново пароль всё равно перезапишет сохранённый.</summary>
    public bool HasTurnPassword(string turnUser) => Query(() => _core.HasTurnPassword(turnUser), false);
    public (string CaFile, bool Verify) GetTls() => Query(_core.GetTls, ("", true));
    /// <summary>Query, а не Post: пользователь должен увидеть, применилось ли доверие TLS.</summary>
    public bool SetTls(string caFile, bool verify) => Query(() => _core.SetTls(caFile, verify), false);
    public string GetTheme() => GetAppStringCached("theme", () => _core.GetTheme(), "system");
    public void SetTheme(string theme)
    { _stringCache["theme"] = theme; Post(() => _core.SetTheme(theme)); }

    // Флаги/строки [app]: читаются из UI на каждый чих (DND, автоответ, бейджи) — кэшируем.
    // ВАЖНО: кэшируем ТОЛЬКО ответ живого ядра. Значение по умолчанию, полученное до старта Core
    // (или при его смерти), в кэш попадать не должно — иначе оно навсегда подменит собой то, что
    // записано в конфиге, и настройка перестанет работать, оставаясь «включённой» на вид.
    public bool GetAppFlag(string key, bool def)
    {
        if (_flagCache.TryGetValue(key, out bool v)) return v;
        if (!TryQuery(() => _core.GetAppFlag(key, def), def, out v)) return def;
        _flagCache[key] = v;
        return v;
    }

    public void SetAppFlag(string key, bool value)
    {
        _flagCache[key] = value;
        Post(() => _core.SetAppFlag(key, value));
    }

    public string GetAppString(string key, string def)
    {
        if (_stringCache.TryGetValue(key, out string? cached)) return cached ?? def;
        if (!TryQuery(() => _core.GetAppString(key, def), def, out string? v) || v is null) return def;
        _stringCache[key] = v;
        return v;
    }

    public void SetAppString(string key, string value)
    {
        _stringCache[key] = value;
        Post(() => _core.SetAppString(key, value));
    }

    /// <summary>Строка rc со своим геттером в ядре (URL книги, номер почты, тема).</summary>
    private string GetAppStringCached(string key, Func<string> read, string fallback)
    {
        if (_stringCache.TryGetValue(key, out string? cached)) return cached ?? fallback;
        if (!TryQuery(read, fallback, out string? v) || v is null) return fallback;   // см. GetAppFlag
        _stringCache[key] = v;
        return v;
    }

    public void SetVerboseLogs(bool verbose) => Post(() => _core.SetVerboseLogs(verbose));
}
