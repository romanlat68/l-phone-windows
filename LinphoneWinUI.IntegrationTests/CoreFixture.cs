using System;
using System.IO;
using LinphoneWinUI.Interop;
using Xunit;

namespace LinphoneWinUI.IntegrationTests;

/// <summary>
/// ОДНО ядро на весь прогон, общее для всех классов тестов.
///
/// Не IClassFixture: фабрика liblinphone — глобальный синглтон процесса, и второй Core рядом
/// с первым роняет её жёстко (0xC0000005 в linphone_factory_set_top_resources_dir). Коллекция
/// заодно запрещает параллельный запуск классов — ядро однопоточное по определению.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CoreCollection : ICollectionFixture<CoreFixture>
{
    public const string Name = "linphone-core";
}

/// <summary>
/// Настоящее ядро liblinphone во ВРЕМЕННОМ профиле.
///
/// Профиль обязательно свой: боевой лежит в ProgramData\L-Phone\&lt;SID&gt;, и прогон по нему
/// перетирал бы рабочий linphonerc, журнал звонков и cred.dat живого пользователя — то есть
/// портил бы настройки ради проверки. Файл паролей при заданном профиле тоже ложится рядом.
///
/// Один экземпляр на класс тестов (IClassFixture): старт Core — сотни миллисекунд, и поднимать
/// его на каждый тест было бы расточительно.
/// </summary>
public sealed class CoreFixture : IDisposable
{
    public string ProfileDir { get; }
    public LinphoneService Sip { get; }

    /// <summary>Ядро поднялось. false — среда не готова (нет нативных библиотек), тесты
    /// в этом случае не падают, а ничего не проверяют: это не дефект кода.</summary>
    public bool Started { get; }

    /// <summary>Почему не поднялось — чтобы причина не терялась молча.</summary>
    public string? StartError { get; }

    /// <summary>Файл диагностики старта ядра (переживает падение процесса).</summary>
    public string DiagPath { get; }

    public CoreFixture()
    {
        ProfileDir = Path.Combine(Path.GetTempPath(), "lphone-it-" + Guid.NewGuid().ToString("N")[..12]);
        Sip = new LinphoneService(ProfileDir);
        // Пишем сразу на диск, без буфера: нативное падение убивает процесс, и всё, что
        // осталось бы в памяти, пропало бы вместе с ним.
        DiagPath = Path.Combine(Path.GetTempPath(), "lphone-it-diag.log");
        try { File.AppendAllText(DiagPath, $"--- старт {DateTime.Now:HH:mm:ss} профиль={ProfileDir}{Environment.NewLine}"); } catch { }
        Sip.Log += m => { try { File.AppendAllText(DiagPath, m + Environment.NewLine); } catch { } };
        try
        {
            Sip.Start();
            Started = Sip.IsStarted;
            if (!Started) StartError = "Core запустился, но IsStarted = false";
        }
        catch (Exception ex)
        {
            StartError = ex.Message;
            Started = false;
        }
    }

    public void Dispose()
    {
        try { Sip.Dispose(); } catch { }
        // Профиль сносим целиком: sqlite-базы и логи ядра между прогонами не нужны.
        try { if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true); } catch { }
    }
}
