using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LinphoneWinUI;

/// <summary>Номер с подписью («Рабочий», «Мобильный», «Добавочный»).</summary>
public sealed record LabeledNumber(string Label, string Number);

/// <summary>Запись адресной книги: из внешней XML (IsLocal=false) или локальная (IsLocal=true, со своим Id).</summary>
public sealed record ContactEntry(string Name, string Number, string Mobile, string Group,
                                  bool IsLocal = false, string Id = "")
{
    /// <summary>Номера сверх основного и мобильного: Telephone2/Telephone3, Other и прочее,
    /// что кладут в книгу производители телефонов. У корпоративного абонента их легко
    /// три-четыре, и звонок «на первый попавшийся» — не то, что нужно человеку.</summary>
    public IReadOnlyList<LabeledNumber> Extra { get; init; } = Array.Empty<LabeledNumber>();

    /// <summary>Все номера контакта по порядку важности, без пустых и повторов.</summary>
    public IReadOnlyList<LabeledNumber> AllNumbers
    {
        get
        {
            var list = new List<LabeledNumber>(2 + Extra.Count);
            void Add(string label, string number)
            {
                if (number.Length == 0) return;
                if (list.Any(n => n.Number == number)) return;   // тот же номер в двух полях — не дубль в меню
                list.Add(new LabeledNumber(label, number));
            }
            Add(Loc.S("Num_Work"), Number);
            Add(Loc.S("Num_Mobile"), Mobile);
            foreach (var x in Extra) Add(x.Label, x.Number);
            return list;
        }
    }

    /// <summary>Номеров больше одного — значит есть из чего выбирать.</summary>
    public bool HasChoice => AllNumbers.Count > 1;

    public string Initials =>
        string.IsNullOrEmpty(Name) ? "?" : (Name.Length >= 2 ? Name[..2].ToUpperInvariant() : Name.ToUpperInvariant());

    /// <summary>Вторая строка: все имеющиеся номера и, если есть, группа.</summary>
    public string SubText =>
        string.Join(" · ", AllNumbers.Select(n => n.Number).Append(Group)
                                     .Where(v => !string.IsNullOrEmpty(v)).Distinct());

    /// <summary>Номер для звонка/избранного: основной, иначе мобильный.</summary>
    public string CallNumber => Number.Length > 0 ? Number : Mobile;
}

/// <summary>Загрузка/парсинг внешней адресной книги (XML вида PhoneBook → DirectoryEntry) с кэшем.</summary>
public static class Phonebook
{
    // Книга почти всегда лежит на самой АТС, во внутренней сети: системный прокси заворачивал
    // бы туда и запрос не доходил (см. Interop.LocalBypassProxy).
    private static readonly HttpClient Http =
        new(Interop.LocalBypassProxy.Handler()) { Timeout = TimeSpan.FromSeconds(8) };

    private static string CachePath => Path.Combine(AppPaths.Cache, "phonebook.xml");

    /// <summary>Контакты из локального кэша (мгновенно, работает офлайн).</summary>
    public static List<ContactEntry> LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath)) return new();
            using var fs = File.OpenRead(CachePath);
            return Parse(XDocument.Load(fs));
        }
        catch { return new(); }
    }

    /// <summary>Скачать книгу по URL, обновить кэш и вернуть контакты.
    /// user/password — опциональная HTTP-авторизация для удалённых книг (Basic/Digest —
    /// по challenge сервера); пустой user = анонимно, как для локальных книг.</summary>
    public static async Task<List<ContactEntry>> FetchAsync(string url, string user = "", string password = "")
    {
        // ВАЖНО: кодировку определяет XML-декларация, а не мы. Корпоративные книги
        // (Asterisk/FreePBX/Yealink-формат) сплошь и рядом в windows-1251 — принудительный
        // UTF-8 превращал имена в кракозябры и отравлял кэш.
        // С учётными данными клиент создаётся на запрос: Credentials задаются только в
        // handler'е, а общий статический Http менять нельзя. Книга качается редко.
        using HttpClient? authed = string.IsNullOrEmpty(user) ? null
            : new HttpClient(Interop.LocalBypassProxy.Handler(
                  new System.Net.NetworkCredential(user, password)))
              { Timeout = TimeSpan.FromSeconds(8) };
        using var stream = await (authed ?? Http).GetStreamAsync(url).ConfigureAwait(false);
        var doc = XDocument.Load(stream);
        var list = Parse(doc);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            doc.Save(CachePath);   // кэш нормализуется в UTF-8 вместе с декларацией
        }
        catch { }
        return list;
    }

    private static List<ContactEntry> Parse(XDocument doc)
    {
        var list = new List<ContactEntry>();

        foreach (var e in doc.Descendants("DirectoryEntry"))
        {
            string name = ((string?)e.Element("Name") ?? "").Trim();
            string tel = ((string?)e.Element("Telephone") ?? "").Trim();
            string mob = ((string?)e.Element("Mobile") ?? "").Trim();
            string grp = ((string?)e.Element("Group") ?? "").Trim();

            // Производители кладут дополнительные номера кто во что горазд: Yealink —
            // Telephone1/2/3, Fanvil — Work/Home/Other, Grandstream — OtherNumber.
            // Берём всё, что нашли: лишний номер в меню безвреден, потерянный — нет.
            var extra = new List<LabeledNumber>();
            foreach (var el in e.Elements())
            {
                string tag = el.Name.LocalName;
                string value = (el.Value ?? "").Trim();
                if (value.Length == 0) continue;
                if (tag is "Name" or "Telephone" or "Mobile" or "Group" or "Ring") continue;
                if (!LooksLikeNumberTag(tag)) continue;
                if (value == tel || value == mob) continue;
                extra.Add(new LabeledNumber(NiceLabel(tag), value));
            }

            // Пустая запись без имени и без единого номера — мусор в книге, не контакт.
            if (name.Length == 0 && tel.Length == 0 && mob.Length == 0 && extra.Count == 0) continue;
            list.Add(new ContactEntry(name, tel, mob, grp) { Extra = extra });
        }
        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Похож ли тег на номер телефона. Проверяем ИМЯ тега, а не содержимое:
    /// поле вроде Email или Photo тоже непустое, но звонить туда некуда.</summary>
    internal static bool LooksLikeNumberTag(string tag) =>
        tag.Contains("Tel", StringComparison.OrdinalIgnoreCase)
        || tag.Contains("Phone", StringComparison.OrdinalIgnoreCase)
        || tag.Contains("Number", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("Work", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("Home", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("Office", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("Ext", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("Extension", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("Other", StringComparison.OrdinalIgnoreCase);

    /// <summary>Человеческая подпись для тега. Известные переводим, остальные оставляем
    /// как есть: «Telephone3» в меню лучше, чем безымянный номер.</summary>
    internal static string NiceLabel(string tag) => tag.ToLowerInvariant() switch
    {
        "work" or "office" or "worktelephone" or "officephone" => Loc.S("Num_Work"),
        "home" or "hometelephone" or "homephone" => Loc.S("Num_Home"),
        "ext" or "extension" => Loc.S("Num_Ext"),
        "other" or "othernumber" or "otherphone" => Loc.S("Num_Other"),
        // Telephone2, Phone3 — техническое имя поля, а не подпись для человека.
        // Превращаем в «Номер 2»: цифру сохраняем, потому что она различает номера.
        _ => NumberedLabel(tag),
    };

    private static string NumberedLabel(string tag)
    {
        int i = tag.Length;
        while (i > 0 && char.IsDigit(tag[i - 1])) i--;
        string head = tag[..i], digits = tag[i..];
        bool generic = head.Equals("Telephone", StringComparison.OrdinalIgnoreCase)
                    || head.Equals("Phone", StringComparison.OrdinalIgnoreCase)
                    || head.Equals("Tel", StringComparison.OrdinalIgnoreCase)
                    || head.Equals("Number", StringComparison.OrdinalIgnoreCase);
        return generic && digits.Length > 0 ? Loc.F("Num_Numbered", digits) : tag;
    }
}
