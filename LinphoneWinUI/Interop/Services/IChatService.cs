using System;
using System.Collections.Generic;

namespace LinphoneWinUI.Interop;

/// <summary>Переписка по SIP MESSAGE: отправка, история, счётчик непрочитанных.</summary>
public interface IChatService
{
    /// <summary>Пришло сообщение: (собеседник, текст). Поднимается С ПОТОКА ЯДРА.</summary>
    event Action<string, string>? ChatMessageReceived;

    bool SendChat(string peer, string text);
    List<ChatLine> GetChatHistory(string peer);
    int GetUnreadChatCount();
    List<ChatConversation> GetConversations();
    void MarkChatRead(string peer);
    void DeleteChat(string peer);
}
