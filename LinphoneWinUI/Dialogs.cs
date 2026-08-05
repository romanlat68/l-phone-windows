using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace LinphoneWinUI;

/// <summary>
/// Единственная дверь, через которую показываются модальные диалоги.
///
/// Зачем. WinUI 3 допускает РОВНО ОДИН открытый <see cref="ContentDialog"/> на XamlRoot;
/// попытка показать второй бросает исключение. Все наши вызовы приходят из обработчиков
/// <c>async void</c>, а исключение из такого обработчика не ловится ни
/// <c>Application.UnhandledException</c> (он про исключения самого XAML-фреймворка), ни
/// <c>TaskScheduler.UnobservedTaskException</c> (задача никем не наблюдается) — процесс
/// просто умирает.
///
/// Сойтись двум диалогам легко: предложение обновиться приходит по таймеру и может попасть
/// ровно на диалог «Перевод не удался, перенабрать?» — а тот появляется в момент, когда
/// человек нервничает и кликает. Софтфон при этом закрывался вместе с разговором.
///
/// Правило: второй диалог не показывается и считается закрытым без выбора
/// (<see cref="ContentDialogResult.None"/>). Все вызывающие уже трактуют «не Primary» как
/// «пользователь не подтвердил», поэтому потерянный диалог приводит к бездействию —
/// это заведомо лучше, чем оборванный звонок.
/// </summary>
internal static class Dialogs
{
    private static ContentDialog? _open;

    /// <summary>Открыт ли сейчас модальный диалог (например, чтобы не ставить в очередь ещё один).</summary>
    public static bool IsOpen => _open != null;

    /// <summary>Показать диалог. Никогда не бросает: любая осечка — это
    /// <see cref="ContentDialogResult.None"/> и строка в журнале.</summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (dialog == null) return ContentDialogResult.None;
        if (_open != null)
        {
            AppLog.Write($"Диалог «{dialog.Title}» не показан: на экране уже другой");
            return ContentDialogResult.None;
        }

        _open = dialog;
        try { return await dialog.ShowAsync(); }
        catch (Exception ex)
        {
            // Сюда попадает и «XamlRoot ещё не готов» (диалог до первой отрисовки окна),
            // и гонка с чужим диалогом, который открыли мимо этого шлюза.
            AppLog.Write($"Диалог «{dialog.Title}» не показан: {ex.GetType().Name}: {ex.Message}");
            return ContentDialogResult.None;
        }
        finally { _open = null; }
    }
}
