using LinphoneWinUI.Interop;
using LinphoneWinUI.ViewModels;
using Xunit;

namespace LinphoneWinUI.Tests;

public class ChatViewModelTests
{
    private static ChatViewModel NewVm(FakeSipService sip) => new(sip, peer => "Имя " + peer);

    // ---------------- OpenConversation ----------------
    [Fact]
    public void OpenConversation_LoadsHistoryAndMarksRead()
    {
        var sip = new FakeSipService();
        sip.ChatHistories["1001"] = new() { new ChatLine("привет", false), new ChatLine("ага", true) };
        var vm = NewVm(sip);
        bool scrolled = false;
        vm.ScrollToEnd += () => scrolled = true;

        vm.OpenConversation("1001");

        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("Имя 1001", vm.ChatHeader);
        Assert.False(vm.IsListView);
        Assert.True(vm.HasOpenConversation);
        Assert.Contains("1001", sip.MarkedRead);
        Assert.True(scrolled);
    }

    // ---------------- Send ----------------
    [Fact]
    public void Send_TrimsAndAppendsOwnMessage()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.OpenConversation("1001");

        Assert.True(vm.Send("  привет  "));

        Assert.Equal(("1001", "привет"), Assert.Single(sip.SentChats));
        var line = Assert.Single(vm.Messages);
        Assert.True(line.Mine);
        Assert.Equal("привет", line.Text);
    }

    [Fact]
    public void Send_EmptyTextOrNoPeer_DoesNothing()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);

        Assert.False(vm.Send("привет"));   // переписка не открыта
        vm.OpenConversation("1001");
        Assert.False(vm.Send("   "));      // пустой текст
        Assert.Empty(sip.SentChats);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public void Send_FailureFromService_DoesNotAppendMessage()
    {
        var sip = new FakeSipService { SendChatResult = false };
        var vm = NewVm(sip);
        vm.OpenConversation("1001");

        Assert.False(vm.Send("привет"));
        Assert.Empty(vm.Messages);   // неотправленное не показываем как отправленное
    }

    // ---------------- HandleIncoming: точное сопоставление собеседника ----------------
    [Fact]
    public void HandleIncoming_SamePeer_ShownInOpenConversation()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.OpenConversation("1001");
        sip.MarkedRead.Clear();

        bool shown = vm.HandleIncoming("sip:1001@pbx.local", "текст", conversationOnScreen: true);

        Assert.True(shown);
        var line = Assert.Single(vm.Messages);
        Assert.False(line.Mine);
        // Прочитанной помечается комната, куда сообщение реально легло (полный адрес):
        // домен отправителя может отличаться от открытой переписки — иначе непрочитанные зависают.
        Assert.Contains("sip:1001@pbx.local", sip.MarkedRead);
    }

    [Theory]
    [InlineData("1001", "100")]    // пишет «100», открыт «1001» — префикс не должен матчиться
    [InlineData("100", "1001")]    // и наоборот
    public void HandleIncoming_PrefixPeer_NotShown(string open, string from)
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.OpenConversation(open);

        Assert.False(vm.HandleIncoming(from, "текст", conversationOnScreen: true));
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public void HandleIncoming_ConversationNotOnScreen_NotShown()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.OpenConversation("1001");

        Assert.False(vm.HandleIncoming("1001", "текст", conversationOnScreen: false));
        Assert.Empty(vm.Messages);   // View покажет toast, в открытую переписку не пишем
    }

    [Fact]
    public void HandleIncoming_InListView_RefreshesConversationsAndBadge()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.ShowList();
        sip.Conversations.Add(new ChatConversation("sip:1001@pbx.local", "1001", "новое", 1, 10));
        bool badge = false;
        vm.BadgeChanged += () => badge = true;

        Assert.False(vm.HandleIncoming("1001", "новое", conversationOnScreen: true));

        var row = Assert.Single(vm.Conversations);
        Assert.Equal("sip:1001@pbx.local", row.Peer);   // строка несёт ПОЛНЫЙ адрес комнаты
        Assert.Equal("новое", row.Preview);
        Assert.True(badge);
        Assert.False(vm.ConversationsEmpty);
    }

    // ---------------- DeleteConversation ----------------
    [Fact]
    public void DeleteConversation_RemovesRoomAndRefreshesList()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        sip.Conversations.Add(new ChatConversation("sip:376@pbx", "376", "старое", 4, 10));
        sip.Conversations.Add(new ChatConversation("sip:376@office", "376", "новое", 0, 20));
        vm.ShowList();
        bool badge = false;
        vm.BadgeChanged += () => badge = true;

        vm.DeleteConversation("sip:376@pbx");   // убираем дубль со «застрявшими» непрочитанными

        Assert.Equal("sip:376@pbx", Assert.Single(sip.DeletedChats));
        var left = Assert.Single(vm.Conversations);
        Assert.Equal("sip:376@office", left.Peer);
        Assert.True(badge);
    }

    [Fact]
    public void DeleteConversation_OpenOne_ReturnsToList()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.OpenConversation("sip:376@pbx");

        vm.DeleteConversation("sip:376@pbx");

        Assert.True(vm.IsListView);
        Assert.False(vm.HasOpenConversation);
    }

    // ---------------- ShowList ----------------
    [Fact]
    public void ShowList_ClosesConversationAndPopulates()
    {
        var sip = new FakeSipService();
        var vm = NewVm(sip);
        vm.OpenConversation("1001");

        vm.ShowList();

        Assert.True(vm.IsListView);
        Assert.False(vm.HasOpenConversation);
        Assert.True(vm.ConversationsEmpty);
    }
}
