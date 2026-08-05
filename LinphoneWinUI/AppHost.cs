using System;
using LinphoneWinUI.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace LinphoneWinUI;

/// <summary>
/// Композиционный корень: единственное место, где создаются долгоживущие службы приложения.
///
/// Зачем. Раньше <c>MainWindow</c> в своём конструкторе делал <c>new LinphoneService()</c>, а
/// окна настроек и мини-звонка принимали этот КОНКРЕТНЫЙ класс. Подменить SIP-ядро было нечем,
/// поэтому ни одно окно не поддавалось проверке даже теоретически, а время жизни ядра было
/// размазано: создавалось в конструкторе окна, освобождалось в его же обработчике выхода.
///
/// Теперь ядро живёт в контейнере, окна зависят от <see cref="ISipService"/>, а освобождение
/// идёт через <see cref="Shutdown"/> — один вызов вместо ручной уборки в нескольких местах.
/// </summary>
internal static class AppHost
{
    private static ServiceProvider? _provider;

    /// <summary>Контейнер приложения. Обращение до <see cref="Build"/> — ошибка сборки состава,
    /// поэтому падаем громко, а не отдаём null.</summary>
    public static IServiceProvider Services =>
        _provider ?? throw new InvalidOperationException("AppHost.Build() не вызван.");

    /// <param name="profileDirOverride">Каталог профиля вместо боевого — только для тестов.</param>
    public static void Build(string? profileDirOverride = null)
    {
        if (_provider != null) return;

        var services = new ServiceCollection();

        // SIP-ядро — одно на приложение: liblinphone строго однопоточен и не рассчитан на
        // несколько Core в процессе (второй роняет глобальную фабрику).
        services.AddSingleton<ISipService>(_ => new LinphoneService(profileDirOverride));

        // ViewModels без внешних зависимостей. Остальные пока собирает MainWindow: им нужны
        // функции представления (локализация, поиск имени по номеру), и вынести их — задача
        // разреза MainWindow, а не этого шага.
        services.AddSingleton<ViewModels.HeaderViewModel>();
        services.AddSingleton<ViewModels.CallViewModel>();

        _provider = services.BuildServiceProvider();
    }

    /// <summary>Создать объект, дозаполнив его зависимости из контейнера. Нужен для окон:
    /// WinUI создаёт их сам через конструктор, и зарегистрировать их как службы нельзя.</summary>
    public static T Create<T>(params object[] extraArgs) where T : class =>
        ActivatorUtilities.CreateInstance<T>(Services, extraArgs);

    /// <summary>Освободить всё, что создал контейнер (в первую очередь SIP-ядро).</summary>
    public static void Shutdown()
    {
        var p = _provider;
        _provider = null;
        try { p?.Dispose(); } catch { }
    }
}
