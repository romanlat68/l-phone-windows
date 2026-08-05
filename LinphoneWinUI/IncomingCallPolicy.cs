namespace LinphoneWinUI;

/// <summary>Обстановка в момент прихода входящего — всё, от чего зависит показ.</summary>
internal readonly record struct IncomingCallContext(
    bool DoNotDisturb,
    bool AlreadyInCall,
    bool WindowVisible,
    bool WindowIsForeground,
    bool MiniWindowEnabled,
    bool BringToFrontEnabled,
    bool AutoAnswerEnabled,
    /// <summary>АТС просит ответить сама (интерком/оповещение) через столько секунд;
    /// null — обычный звонок.</summary>
    int? IntercomAnswerAfter = null,
    /// <summary>Слушаться ли просьбы АТС об автоответе (настройка пользователя).</summary>
    bool IntercomEnabled = true,
    /// <summary>Открыта вкладка «Набор» — единственная, где помещается полноэкранный экран
    /// входящего. По умолчанию true: это обычное положение дел, и тесты, которым вкладка
    /// безразлична, не обязаны о ней знать.</summary>
    bool DialerTabOpen = true);

/// <summary>Что именно сделать со входящим.</summary>
internal readonly record struct IncomingCallPlan(
    bool RejectBusy,
    bool ShowBanner,
    bool ShowFullScreen,
    bool ShowMiniWindow,
    bool ShowToast,
    bool FlashTaskbar,
    bool BringToFront,
    bool StartAutoAnswer,
    /// <summary>Через сколько секунд ответить автоматически (имеет смысл при StartAutoAnswer).</summary>
    int AutoAnswerAfterSeconds = 0,
    /// <summary>Автоответ по просьбе АТС, а не по своей настройке — это стоит написать в журнал:
    /// «телефон ответил сам» иначе выглядит как дефект.</summary>
    bool AutoAnswerIsIntercom = false);

/// <summary>
/// Решение «как показать входящий звонок».
///
/// Вынесено из MainWindow отдельно от отображения. Условий здесь семь, и переплетены они
/// неочевидно: уведомление Windows нужно только когда мини-окна нет И окно не на переднем
/// плане; мигание в панели задач — только когда окно вообще видно; автоответ — только когда
/// мы свободны. Именно в этом узле жил баг, из-за которого автоответ принимал ВТОРОЙ входящий
/// и терял живой разговор, — и заметить его в двухсотстрочном обработчике было нечем.
///
/// Функция чистая: никаких обращений к ядру и к окнам, только вход → выход. Поэтому её
/// таблица истинности проверяется тестами целиком.
/// </summary>
internal static class IncomingCallPolicy
{
    /// <summary>
    /// Помещается ли полноэкранный экран входящего. Он живёт секцией вкладки «Набор», поэтому
    /// возможен только там и только когда не идёт разговор. В остальных случаях звонок
    /// показывается баннером поверх текущей вкладки — с теми же кнопками «Принять/Отклонить».
    ///
    /// Отдельной функцией потому, что ответ нужен дважды: при приходе звонка (здесь) и при
    /// каждом переключении вкладок, пока телефон звонит. Два места, считающих это по-разному, —
    /// готовый дефект: экран входящего и баннер оказались бы видны одновременно.
    /// </summary>
    public static bool FullScreenFits(bool alreadyInCall, bool dialerTabOpen) =>
        !alreadyInCall && dialerTabOpen;

    public static IncomingCallPlan Decide(IncomingCallContext ctx)
    {
        // «Не беспокоить» решает всё: отклоняем «занято» и ничего не показываем — окно
        // пользователя не трогаем вовсе.
        // «Не беспокоить» сильнее просьбы АТС: пользователь выключил звонки сознательно, и
        // телефон, отвечающий сам вопреки этому, — худшее, что может случиться на совещании.
        if (ctx.DoNotDisturb) return new IncomingCallPlan(RejectBusy: true, false, false, false, false, false, false, false);

        // Интерком: АТС просит ответить сама. Только когда мы свободны — иначе оповещение
        // оборвало бы живой разговор, а этого не делает ни один аппаратный телефон.
        bool intercom = ctx.IntercomEnabled && ctx.IntercomAnswerAfter is not null && !ctx.AlreadyInCall;

        // Мини-окно заменяет уведомление Windows: оно всегда поверх, не уезжает в центр
        // уведомлений и позволяет ответить, не трогая главное окно.
        //
        // Но НЕ когда человек и так смотрит на наше окно и не занят разговором: там уже есть
        // чем ответить — полный экран на «Наборе» или баннер на любой другой вкладке, — и
        // плавающее окно всплывает поверх, давая два одинаковых предложения ответить друг на
        // друге, причём верхнее перехватывает клик, нацеленный в нижнее. Во время разговора
        // мини-окно нужно: баннер там делит экран с активным звонком и легко теряется.
        bool onScreenAlready = ctx.WindowVisible && ctx.WindowIsForeground && !ctx.AlreadyInCall;
        bool mini = ctx.MiniWindowEnabled && !onScreenAlready;

        // Уведомление — запасной путь. Не нужно, если есть мини-окно или если пользователь
        // и так смотрит на наше окно.
        bool toast = !mini && !(ctx.WindowVisible && ctx.WindowIsForeground);

        // Полный экран забирает вкладку «Набор» целиком, поэтому годится не всегда.
        bool fullScreen = FullScreenFits(ctx.AlreadyInCall, ctx.DialerTabOpen);

        return new IncomingCallPlan(
            RejectBusy: false,
            // Компактный баннер поверх любой вкладки — когда полный экран не годится:
            // во время разговора он спрятал бы активный звонок, а на «Контактах» или в журнале
            // выдернул бы человека с той вкладки, где он работает. Кнопки ответа в баннере те же.
            ShowBanner: !fullScreen,
            ShowFullScreen: fullScreen,
            ShowMiniWindow: mini,
            ShowToast: toast,
            // Мигание имеет смысл только у видимого окна: оно может быть перекрыто или
            // неактивно, и тогда звонок замечали лишь по динамику.
            FlashTaskbar: ctx.WindowVisible,
            BringToFront: ctx.WindowVisible && ctx.BringToFrontEnabled,
            // Автоответ — только когда мы СВОБОДНЫ. Иначе второй входящий принимается сам
            // собой, ставя текущий разговор на удержание: пользователь теряет живой звонок
            // без единого своего действия и не понимает, что произошло.
            StartAutoAnswer: intercom || (!ctx.AlreadyInCall && ctx.AutoAnswerEnabled),
            // Просьба АТС задаёт свою задержку (обычно 0 — «сразу»); своя настройка
            // пользователя приносит свою, её подставляет вызывающий.
            AutoAnswerAfterSeconds: intercom ? ctx.IntercomAnswerAfter!.Value : 0,
            AutoAnswerIsIntercom: intercom);
    }
}
