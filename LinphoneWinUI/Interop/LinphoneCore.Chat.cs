using System;
using System.Collections.Generic;
using System.Linq;
using static LinphoneWinUI.Interop.NativeMethods;

namespace LinphoneWinUI.Interop;

// Чат (SIP MESSAGE): приём, отправка, история, список переписок, непрочитанные.
// Часть LinphoneCore — выполняется на потоке ядра под сериализацией фасада.
internal sealed partial class LinphoneCore
{
    /// <summary>BASIC chat room с собеседником (прямой SIP MESSAGE — так работает Asterisk).
    /// Явно задаём backend=Basic в params: get_chat_room* в 5.x при заданном на аккаунте
    /// conference-factory создаёт ГРУППОВОЙ чат, который слал INVITE (у абонента «звонок от
    /// anonymous», MESSAGE не ходят). Params с backend=Basic это игнорируют.
    /// Fallback на старый путь, если этих функций нет в сборке SDK.</summary>
    private const int ChatBackendBasic = 1;

    private IntPtr BasicRoom(string peer)
    {
        string p = (peer ?? "").Trim();
        if (p.Length == 0) return IntPtr.Zero;

        // Короткий номер (без домена) — сперва ищем УЖЕ СУЩЕСТВУЮЩУЮ переписку с этим абонентом.
        // Иначе: входящее сообщение приходит с адреса АТС (sip:376@pbx.example.com), комната
        // создаётся под ним, а при открытии чата по номеру «376» interpret_url дорисует домен
        // аккаунта (sip:376@sip.example.com) — это ДРУГАЯ комната. Пользователь открывал пустой
        // двойник и решал, что переписка пропала.
        if (!p.Contains('@'))
        {
            string user = PhoneNumberUtils.SipUser(p);
            if (user.Length > 0)
            {
                IntPtr existing = FindRoomByUser(user);
                if (existing != IntPtr.Zero) return existing;
            }
        }

        IntPtr peerAddr = linphone_core_interpret_url(_core, p);
        if (peerAddr == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            IntPtr prm = linphone_core_create_default_chat_room_params(_core);
            if (prm == IntPtr.Zero) return LegacyRoom(peerAddr);
            linphone_chat_room_params_set_backend(prm, ChatBackendBasic);

            // Уже существующий basic-диалог с этим собеседником (borrowed) — иначе создаём.
            IntPtr room = linphone_core_search_chat_room(_core, prm, IntPtr.Zero, peerAddr, IntPtr.Zero);
            if (room == IntPtr.Zero)
            {
                IntPtr participants = bctbx_list_append(IntPtr.Zero, peerAddr);
                room = linphone_core_create_chat_room_6(_core, prm, IntPtr.Zero, participants);
                if (participants != IntPtr.Zero) bctbx_list_free(participants);
                // create_chat_room_6 возвращает комнату с НАШЕЙ ссылкой (ref=1). Core уже
                // держит свою (комната в его списке переписок — см. GetConversations),
                // поэтому свою отпускаем сразу и дальше работаем с borrowed-указателем,
                // как и в ветке search. Раньше ссылка утекала на каждую новую переписку.
                if (room != IntPtr.Zero) { try { linphone_chat_room_unref(room); } catch { } }
            }
            linphone_chat_room_params_unref(prm);
            return room;
        }
        catch (EntryPointNotFoundException) { return LegacyRoom(peerAddr); }
        finally { linphone_address_unref(peerAddr); }
    }

    /// <summary>Старый путь на случай отсутствия params-API в сборке SDK (address НЕ освобождаем — снаружи).</summary>
    private IntPtr LegacyRoom(IntPtr peerAddr) => linphone_core_get_chat_room(_core, peerAddr);

    /// <summary>Комната с этим абонентом по user-части, без учёта домена (borrowed-указатель).
    /// Если двойники уже накопились от прошлых версий — берём самую свежую по времени
    /// последнего обновления: там лежит актуальная переписка.</summary>
    private IntPtr FindRoomByUser(string user)
    {
        IntPtr best = IntPtr.Zero;
        long bestUpdate = long.MinValue;
        for (IntPtr it = linphone_core_get_chat_rooms(_core); it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr room = bctbx_list_get_data(it);
            if (room == IntPtr.Zero) continue;
            IntPtr addr = linphone_chat_room_get_peer_address(room);
            if (addr == IntPtr.Zero) continue;
            if (!string.Equals(PtrToUtf8(linphone_address_get_username(addr)), user, StringComparison.OrdinalIgnoreCase))
                continue;
            long updated = linphone_chat_room_get_last_update_time(room);
            if (updated >= bestUpdate) { bestUpdate = updated; best = room; }
        }
        return best;
    }

    private void OnMessageReceived(IntPtr core, IntPtr room, IntPtr message) =>
        // Через SafeCallback, как и остальные нативные колбэки: прежний глухой catch защищал
        // от падения процесса, но и от диагностики — потерянное сообщение не оставляло следа.
        SafeCallback(nameof(OnMessageReceived), () =>
        {
            string text = PtrToUtf8(linphone_chat_message_get_utf8_text(message)) ?? "";
            // Отдаём ПОЛНЫЙ чистый адрес (sip:user@host), а не только username: подписчик
            // должен пометить прочитанной именно ту комнату, куда легло сообщение — комнаты
            // с одним user, но разными доменами (адрес АТС vs домен аккаунта) различаются.
            IntPtr from = linphone_chat_message_get_from_address(message);
            string peer = "";
            if (from != IntPtr.Zero)
            {
                string user = PtrToUtf8(linphone_address_get_username(from)) ?? "";
                string domain = PtrToUtf8(linphone_address_get_domain(from)) ?? "";
                peer = user.Length == 0 ? "" : (domain.Length == 0 ? user : $"sip:{user}@{domain}");
            }
            ChatMessageReceived?.Invoke(peer, text);
        });

    /// <summary>Отправить текстовое сообщение собеседнику (номер/SIP-адрес).</summary>
    public bool SendChat(string peer, string text)
    {
        if (_core == IntPtr.Zero || string.IsNullOrWhiteSpace(peer) || string.IsNullOrEmpty(text)) return false;
        IntPtr room = BasicRoom(peer);
        if (room == IntPtr.Zero) return false;
        IntPtr msg = linphone_chat_room_create_message_from_utf8(room, text);
        if (msg == IntPtr.Zero) return false;
        linphone_chat_message_send(msg);
        // create_message_from_utf8 отдаёт сообщение с НАШЕЙ ссылкой (ref=1); send берёт
        // свою на время доставки. Свою отпускаем — иначе утечка на каждое отправленное.
        try { linphone_chat_message_unref(msg); } catch { }
        return true;
    }

    /// <summary>История переписки с собеседником (до 100 последних сообщений).</summary>
    public List<ChatLine> GetChatHistory(string peer)
    {
        var result = new List<ChatLine>();
        if (_core == IntPtr.Zero || string.IsNullOrWhiteSpace(peer)) return result;
        IntPtr room = BasicRoom(peer);
        if (room == IntPtr.Zero) return result;
        IntPtr list = linphone_chat_room_get_history(room, 100); // сообщения с ref=1 — наши, unref обязателен
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr msg = bctbx_list_get_data(it);
            if (msg == IntPtr.Zero) continue;
            string text = PtrToUtf8(linphone_chat_message_get_utf8_text(msg)) ?? "";
            bool mine = linphone_chat_message_is_outgoing(msg) != 0;
            if (text.Length > 0) result.Add(new ChatLine(text, mine));
            try { linphone_chat_message_unref(msg); } catch { }
        }
        if (list != IntPtr.Zero) bctbx_list_free(list);
        return result;
    }

    /// <summary>Всего непрочитанных входящих сообщений (по всем перепискам).</summary>
    public int GetUnreadChatCount() =>
        _core == IntPtr.Zero ? 0 : linphone_core_get_unread_chat_message_count(_core);

    /// <summary>Список переписок: собеседник, последнее сообщение, кол-во непрочитанных, время.
    /// Отсортирован по свежести (свежие первыми).</summary>
    public List<ChatConversation> GetConversations()
    {
        var result = new List<ChatConversation>();
        if (_core == IntPtr.Zero) return result;
        IntPtr list = linphone_core_get_chat_rooms(_core); // const — не освобождаем
        for (IntPtr it = list; it != IntPtr.Zero; it = bctbx_list_next(it))
        {
            IntPtr room = bctbx_list_get_data(it);
            if (room == IntPtr.Zero) continue;
            IntPtr addr = linphone_chat_room_get_peer_address(room);
            string? user = addr == IntPtr.Zero ? null : PtrToUtf8(linphone_address_get_username(addr));
            if (string.IsNullOrEmpty(user)) continue;
            string? domain = PtrToUtf8(linphone_address_get_domain(addr));
            string peer = string.IsNullOrEmpty(domain) ? user : $"sip:{user}@{domain}";

            int unread = linphone_chat_room_get_unread_messages_count(room);
            long updated = linphone_chat_room_get_last_update_time(room);
            string last = "";
            IntPtr lastMsg = linphone_chat_room_get_last_message_in_history(room); // владение передаётся НАМ
            if (lastMsg != IntPtr.Zero)
            {
                last = PtrToUtf8(linphone_chat_message_get_utf8_text(lastMsg)) ?? "";
                try { linphone_chat_message_unref(lastMsg); } catch { }
            }
            result.Add(new ChatConversation(peer, user, last, unread, updated));
        }
        return result.OrderByDescending(c => c.LastUpdate).ToList();
    }

    /// <summary>Отметить переписку с собеседником как прочитанную.</summary>
    public void MarkChatRead(string peer)
    {
        if (_core == IntPtr.Zero || string.IsNullOrWhiteSpace(peer)) return;
        IntPtr room = BasicRoom(peer);
        if (room != IntPtr.Zero) linphone_chat_room_mark_as_read(room);
    }

    /// <summary>Удалить переписку целиком (комнату вместе с историей) — например, дубль,
    /// оставшийся от старого адреса собеседника.</summary>
    public void DeleteChat(string peer)
    {
        if (_core == IntPtr.Zero || string.IsNullOrWhiteSpace(peer)) return;
        IntPtr room = BasicRoom(peer);
        if (room == IntPtr.Zero) return;
        try { linphone_core_delete_chat_room(_core, room); }
        catch (EntryPointNotFoundException) { /* нет в этой сборке SDK */ }
    }
}
