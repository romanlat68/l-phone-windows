using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;
using Microsoft.UI.Xaml;

namespace LinphoneWinUI.ViewModels;

/// <summary>Строка списка переписок для UI.</summary>
public sealed class ConversationRow
{
    public ConversationRow(string peer, string title, string lastMessage, int unread)
    {
        Peer = peer;
        Title = string.IsNullOrEmpty(title) ? peer : title;
        Preview = lastMessage;
        Unread = unread;
    }
    public string Peer { get; }
    public string Title { get; }
    public string Preview { get; }
    public int Unread { get; }
    public string Initials =>
        string.IsNullOrEmpty(Title) ? "?" : (Title.Length >= 2 ? Title[..2].ToUpperInvariant() : Title.ToUpperInvariant());
    public string UnreadText => Unread > 99 ? "99+" : Unread.ToString();
    public Visibility HasUnread => Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// ViewModel вкладки «Чат»: список переписок и открытая переписка. Держит коллекции,
/// текущего собеседника и режим (список ↔ переписка). UI-побочки (скролл к концу,
/// бейдж+трей, toast о новом сообщении) поднимаются событиями — их делает code-behind View.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ISipService _sip;
    private readonly Func<string, string> _displayName;

    public ObservableCollection<ConversationRow> Conversations { get; } = new();
    public ObservableCollection<ChatLine> Messages { get; } = new();

    [ObservableProperty] public partial bool IsListView { get; set; } = true;         // список переписок vs открытая переписка
    [ObservableProperty] public partial bool ConversationsEmpty { get; set; } = true;
    [ObservableProperty] public partial string ChatHeader { get; set; } = "";

    public string ChatPeer { get; private set; } = "";

    /// <summary>View должен прокрутить переписку к последнему сообщению.</summary>
    public event Action? ScrollToEnd;
    /// <summary>Изменилось число непрочитанных — View обновляет бейдж вкладки и трей.</summary>
    public event Action? BadgeChanged;

    public ChatViewModel(ISipService sip, Func<string, string> displayName)
    {
        _sip = sip;
        _displayName = displayName;
    }

    public int Unread => _sip.GetUnreadChatCount();

    public void PopulateConversations()
    {
        Conversations.Clear();
        foreach (var c in _sip.GetConversations())
            Conversations.Add(new ConversationRow(c.Peer, _displayName(c.Peer), c.LastMessage, c.Unread));
        ConversationsEmpty = Conversations.Count == 0;
    }

    /// <summary>Удалить переписку целиком (комнату с историей). Если она открыта — вернуться к списку.</summary>
    public void DeleteConversation(string peer)
    {
        if (string.IsNullOrWhiteSpace(peer)) return;
        _sip.DeleteChat(peer);
        if (HasOpenConversation && PeerMatches(ChatPeer, peer)) ShowList();
        else if (IsListView) PopulateConversations();
        BadgeChanged?.Invoke();   // непрочитанные удалённой комнаты ушли вместе с ней
    }

    /// <summary>Показать список переписок (закрыть открытую).</summary>
    public void ShowList()
    {
        ChatPeer = "";
        PopulateConversations();
        IsListView = true;
    }

    /// <summary>Открыть переписку: загрузить историю, пометить прочитанной.</summary>
    public void OpenConversation(string peer)
    {
        ChatPeer = peer;
        Messages.Clear();
        ChatHeader = _displayName(peer);
        foreach (var m in _sip.GetChatHistory(peer)) Messages.Add(m);
        IsListView = false;
        ScrollToEnd?.Invoke();
        _sip.MarkChatRead(peer);
        BadgeChanged?.Invoke();
    }

    /// <summary>Открыт ли сейчас список переписок (для навигации из вкладки).</summary>
    public bool HasOpenConversation => !IsListView && ChatPeer.Length > 0;

    public void MarkCurrentRead()
    {
        if (ChatPeer.Length > 0) _sip.MarkChatRead(ChatPeer);
        BadgeChanged?.Invoke();
    }

    /// <summary>Отправить сообщение текущему собеседнику. false — не удалось (нет собеседника/ошибка).</summary>
    public bool Send(string text)
    {
        text = (text ?? "").Trim();
        if (ChatPeer.Length == 0 || text.Length == 0) return false;
        if (!_sip.SendChat(ChatPeer, text)) return false;
        Messages.Add(new ChatLine(text, true));
        ScrollToEnd?.Invoke();
        return true;
    }

    /// <summary>Входящее сообщение. conversationOnScreen — окно видно и открыта вкладка чата.
    /// Возвращает true, если сообщение показано в открытой переписке (тогда toast не нужен).</summary>
    public bool HandleIncoming(string fromPeer, string text, bool conversationOnScreen)
    {
        bool viewingThis = conversationOnScreen && !IsListView && PeerMatches(ChatPeer, fromPeer);
        if (viewingThis)
        {
            Messages.Add(new ChatLine(text, false));
            ScrollToEnd?.Invoke();
            // Прочитанной помечаем комнату, куда сообщение реально легло (fromPeer — полный
            // адрес): у собеседника с тем же user, но другим доменом комната своя, и пометка
            // по ChatPeer оставляла бы на ней вечные «непрочитанные».
            _sip.MarkChatRead(fromPeer);
        }
        if (IsListView) PopulateConversations();   // обновить превью/счётчики в списке
        BadgeChanged?.Invoke();
        return viewingThis;
    }

    /// <summary>Тот ли собеседник: сравниваем user-части ТОЧНО. Префиксный StartsWith склеивал
    /// разные чаты (открыт «1001», пишет «100» → сообщение падало в чужую переписку).</summary>
    private static bool PeerMatches(string cur, string fromPeer) =>
        cur.Length > 0 && PhoneNumberUtils.SipUser(cur).Equals(PhoneNumberUtils.SipUser(fromPeer),
            StringComparison.OrdinalIgnoreCase);
}
