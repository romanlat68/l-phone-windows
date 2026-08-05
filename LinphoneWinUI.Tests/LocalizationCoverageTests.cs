using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LinphoneWinUI.Tests;

/// <summary>
/// Покрытие локализации: каждый ключ, который приложение спрашивает у ресурсов, обязан быть
/// во ВСЕХ трёх языках.
///
/// Зачем тестом, а не глазами: пропущенный ключ не ломает ни сборку, ни запуск. Loc.S на
/// промахе возвращает сам ключ, и пользователь видит в интерфейсе «BoxLogin.Header» —
/// причём только тот, кто переключил язык. Русский интерфейс выглядит идеально, а английский
/// и китайский тихо разъезжаются от релиза к релизу.
/// </summary>
public class LocalizationCoverageTests
{
    private static readonly string[] Locales = { "ru-RU", "en-US", "zh-Hans" };

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "LinphoneWinUI", "Strings")))
                return dir.FullName;
        return null;
    }

    private static Dictionary<string, HashSet<string>> LoadResources(string root)
    {
        var result = new Dictionary<string, HashSet<string>>();
        var name = new Regex("<data\\s+name=\"([^\"]+)\"", RegexOptions.Compiled);
        foreach (string loc in Locales)
        {
            string path = Path.Combine(root, "LinphoneWinUI", "Strings", loc, "Resources.resw");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in name.Matches(File.ReadAllText(path))) keys.Add(m.Groups[1].Value);
            result[loc] = keys;
        }
        return result;
    }

    /// <summary>Ключи, запрошенные из XAML (Tr.Key) и из кода (Loc.S/Loc.F с литералом).</summary>
    private static SortedSet<string> UsedKeys(string root)
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        string src = Path.Combine(root, "LinphoneWinUI");

        var trKey = new Regex("Tr\\.Key\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        foreach (string f in Directory.GetFiles(src, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in trKey.Matches(File.ReadAllText(f))) used.Add(m.Groups[1].Value);

        // Loc.S("X") / Loc.F("X", …) / Tr.Set(el, "X") — только целые строковые литералы.
        // Хвост [,)] обязателен: без него из склейки Loc.S("PS_" + статус) выхватывался
        // огрызок «PS_». Собираемые в рантайме ключи проверяются отдельно (см. PresenceStatus).
        var locCall = new Regex("(?:Loc\\.[SF]|Tr\\.Set)\\s*\\([^\")]*\"([A-Za-z][A-Za-z0-9_.]*)\"\\s*[,)]",
            RegexOptions.Compiled);
        foreach (string f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in locCall.Matches(File.ReadAllText(f))) used.Add(m.Groups[1].Value);
        }
        return used;
    }

    [Fact]
    public void AllThreeLocales_HaveIdenticalKeySets()
    {
        string? root = RepoRoot();
        if (root == null) return;   // не из репозитория — проверять нечего

        var res = LoadResources(root);
        foreach (string loc in Locales.Skip(1))
        {
            var missing = res["ru-RU"].Except(res[loc]).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = res[loc].Except(res["ru-RU"]).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0,
                $"В {loc} нет ключей, которые есть в ru-RU ({missing.Count}): {string.Join(", ", missing.Take(20))}");
            Assert.True(extra.Count == 0,
                $"В {loc} есть лишние ключи против ru-RU ({extra.Count}): {string.Join(", ", extra.Take(20))}");
        }
    }

    /// <summary>Ключи, которые собираются в рантайме и потому невидимы для разбора исходников.
    /// Подпись статуса — Loc.S("PS_" + значение перечисления), то есть новое значение
    /// PresenceStatus молча покажет в интерфейсе «PS_Meeting» вместо текста.</summary>
    [Fact]
    public void RuntimeComposedKeys_ExistInEveryLocale()
    {
        string? root = RepoRoot();
        if (root == null) return;

        var res = LoadResources(root);
        var problems = new List<string>();
        foreach (string status in Enum.GetNames(typeof(LinphoneWinUI.Interop.PresenceStatus)))
            foreach (string loc in Locales)
                if (!res[loc].Contains("PS_" + status)) problems.Add($"PS_{status} нет в {loc}");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryUsedKey_ExistsInEveryLocale()
    {
        string? root = RepoRoot();
        if (root == null) return;

        var res = LoadResources(root);
        var used = UsedKeys(root);
        Assert.NotEmpty(used);   // регулярки перестали находить ключи — тест бесполезен, чиним

        var problems = new List<string>();
        foreach (string key in used)
            foreach (string loc in Locales)
                if (!res[loc].Contains(key))
                    problems.Add($"{key} нет в {loc}");

        Assert.True(problems.Count == 0,
            $"Ключи локализации без перевода ({problems.Count}):{Environment.NewLine}"
            + string.Join(Environment.NewLine, problems.Take(40)));
    }
}
