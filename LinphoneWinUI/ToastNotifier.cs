using System;
using System.Linq;
using CommunityToolkit.WinUI.Notifications;
using Windows.UI.Notifications;

namespace LinphoneWinUI;

/// <summary>Что нажали в уведомлении.</summary>
internal enum ToastAction { Open, Accept, Decline, Chat, CallBack }

/// <summary>
/// Уведомления Windows: входящий звонок (с кнопками «Принять»/«Отклонить») и новое сообщение чата.
///
/// Вынесено из MainWindow: сборка XML уведомления, теги, группы и разбор аргументов активации
/// к окну отношения не имеют. Окно теперь только решает, ЧТО показать, и что делать по нажатию.
///
/// Используется классический путь (Windows.UI.Notifications через CommunityToolkit): он работает
/// в unpackaged self-contained без рантайма уведомлений WinAppSDK. Современный
/// Microsoft.Windows.AppNotifications не мигрирован осознанно — переход затрагивает COM-активатор.
/// </summary>
internal sealed class ToastNotifier
{
    private const string IncomingTag = "incoming";
    private const string CallsGroup = "calls";
    private const string ChatGroup = "chat";

    private readonly Action<string> _log;

    public ToastNotifier(Action<string> log) => _log = log;

    /// <summary>Нажатие на уведомление или его кнопку. peer заполнен только для чата.</summary>
    public event Action<ToastAction, string>? Activated;

    /// <summary>Подписаться на активацию уведомлений. Регистрирует COM-активатор, поэтому
    /// ошибку не роняем в старт: без уведомлений приложение всё равно работает.</summary>
    public void Subscribe()
    {
        try { ToastNotificationManagerCompat.OnActivated += OnActivated; }
        catch (Exception ex) { _log("Уведомления недоступны: " + ex.Message); }
    }

    /// <summary>Уведомление о входящем с кнопками. Звук приглушён — рингтон проигрывает сам
    /// liblinphone, иначе звонок был бы двойным.</summary>
    public void ShowIncoming(string caller, string number)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")   // клик по телу — открыть окно
                .AddText(Loc.S("St_IncomingCall"))
                .AddText(caller);
            if (!string.IsNullOrEmpty(number)) builder.AddText(number);   // номер второй строкой, если есть имя
            var content = builder
                .AddButton(new ToastButton(Loc.S("BtnAccept.Content"), "action=accept"))
                .AddButton(new ToastButton(Loc.S("BtnDecline.Content"), "action=decline"))
                .GetToastContent();
            content.Scenario = ToastScenario.Reminder;        // не пропадает сам, ждёт реакции
            content.Audio = new ToastAudio { Silent = true };

            ToastNotificationManagerCompat.CreateToastNotifier().Show(
                new ToastNotification(content.GetXml()) { Tag = IncomingTag, Group = CallsGroup });
        }
        catch (Exception ex) { _log("Уведомление о входящем: " + ex.Message); }
    }

    public void ClearIncoming()
    {
        try { ToastNotificationManagerCompat.History.Remove(IncomingTag, CallsGroup); }
        catch { }
    }

    /// <summary>
    /// Уведомление о пропущенном звонке с кнопкой «Перезвонить».
    ///
    /// Бейдж на вкладке и точка в трее говорят «что-то было», но не говорят КТО и не дают
    /// перезвонить одним движением — а это первое, что делает человек, вернувшись к столу.
    /// В отличие от уведомления о входящем это НЕ Reminder: оно должно уехать в центр
    /// уведомлений само, а не висеть на экране до нажатия.
    /// </summary>
    public void ShowMissed(string caller, string number)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "callback")
                .AddText(Loc.S("St_MissedCall"))
                .AddText(caller);
            if (!string.IsNullOrEmpty(number))
            {
                builder.AddText(number);
                // Кнопка только когда есть куда звонить: «Перезвонить» на скрытый номер —
                // обещание, которого не выполнить. Номер кладём в АРГУМЕНТ КНОПКИ, а не тела:
                // клик по телу должен открыть окно, а не набрать номер сам собой.
                builder.AddButton(new ToastButton(Loc.S("St_CallBack"),
                    $"action=callback&peer={Uri.EscapeDataString(number)}"));
            }
            var content = builder.GetToastContent();

            ToastNotificationManagerCompat.CreateToastNotifier().Show(
                // Тег по номеру: десять пропущенных от одного человека — одно уведомление,
                // а не десять одинаковых плиток.
                new ToastNotification(content.GetXml()) { Tag = SafeTag("m" + number), Group = CallsGroup });
        }
        catch (Exception ex) { _log("Уведомление о пропущенном: " + ex.Message); }
    }

    /// <summary>Уведомление о новом сообщении. Тег = собеседник, поэтому на переписку
    /// приходится одно уведомление, а не лента.</summary>
    public void ShowChat(string peer, string display, string text)
    {
        try
        {
            var content = new ToastContentBuilder()
                .AddArgument("action", "chat")
                .AddArgument("peer", peer)
                .AddText(display)
                .AddText(text)
                .GetToastContent();

            ToastNotificationManagerCompat.CreateToastNotifier().Show(
                new ToastNotification(content.GetXml()) { Tag = SafeTag(peer), Group = ChatGroup });
        }
        catch (Exception ex) { _log("Уведомление чата: " + ex.Message); }
    }

    /// <summary>Tag для уведомления: только буквы и цифры, не длиннее лимита WinRT (16 символов).
    /// Иначе Show бросает, и уведомление не показывается вовсе.</summary>
    internal static string SafeTag(string? s)
    {
        string t = new((s ?? "").Where(char.IsLetterOrDigit).ToArray());
        return t.Length == 0 ? "peer" : (t.Length > 16 ? t[^16..] : t);
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        string action = args.Contains("action") ? args["action"] : "open";
        string peer = args.Contains("peer") ? args["peer"] : "";
        Activated?.Invoke(action switch
        {
            "accept" => ToastAction.Accept,
            "decline" => ToastAction.Decline,
            "chat" => ToastAction.Chat,
            "callback" => ToastAction.CallBack,
            _ => ToastAction.Open,
        }, peer);
    }
}
