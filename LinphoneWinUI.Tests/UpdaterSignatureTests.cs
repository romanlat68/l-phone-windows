using System;
using System.IO;
using System.Linq;
using LinphoneWinUI;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Сквозная проверка доверия к установщику: WinVerifyTrust + пиннинг отпечатка.
///
/// Зачем отдельно от UpdaterTests: там чистая логика, здесь — настоящая криптография Windows
/// на настоящем подписанном файле. Ошибка тут не видна ни в сборке, ни в юнит-тестах, а стоит
/// дорого: клиент молча перестаёт принимать обновления, и узнаём мы об этом от пользователей,
/// которые месяцами сидят на старой версии.
///
/// Установщик — артефакт сборки и в репозиторий не попадает (см. .gitignore), поэтому на CI
/// проверять нечего и тест просто ничего не делает. Локально, после build-installer.ps1,
/// он работает по-настоящему.
/// </summary>
public class UpdaterSignatureTests
{
    /// <summary>Самый свежий подписанный установщик из installer\, либо null.</summary>
    private static string? FindSignedInstaller()
    {
        try
        {
            // От каталога сборки тестов поднимаемся до корня репозитория.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "installer");
                if (!Directory.Exists(candidate)) continue;
                return new DirectoryInfo(candidate).GetFiles("L-Phone-Setup-*.exe")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()?.FullName;
            }
        }
        catch { }
        return null;
    }

    [Fact]
    public void SignedInstaller_IsTrusted()
    {
        string? installer = FindSignedInstaller();
        if (installer == null) return;   // нечего проверять (CI, чистая машина) — это не провал

        Assert.True(Updater.IsTrusted(installer),
            $"Наш собственный подписанный установщик не прошёл проверку доверия: {installer}. " +
            "Это означает, что обновления не установятся ни у кого — проверьте VerifyTrust " +
            "и TrustedThumbprints.");
    }

    [Fact]
    public void UnsignedFile_IsNotTrusted()
    {
        // Любой заведомо неподписанный файл (наша же тестовая сборка) обязан быть отвергнут:
        // без этого «проверка подписи» пропускала бы что угодно.
        string self = Path.Combine(AppContext.BaseDirectory, "LinphoneWinUI.Tests.dll");
        if (!File.Exists(self)) return;

        Assert.False(Updater.IsTrusted(self));
    }

    [Fact]
    public void MissingFile_IsNotTrusted() =>
        Assert.False(Updater.IsTrusted(Path.Combine(Path.GetTempPath(), "нет-такого-файла-" + Guid.NewGuid() + ".exe")));
}
