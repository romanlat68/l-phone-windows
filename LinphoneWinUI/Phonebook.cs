using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LinphoneWinUI;

/// <summary>Запись адресной книги: из внешней XML (IsLocal=false) или локальная (IsLocal=true, со своим Id).</summary>
public sealed record ContactEntry(string Name, string Number, string Mobile, string Group,
                                  bool IsLocal = false, string Id = "")
{
    public string Initials =>
        string.IsNullOrEmpty(Name) ? "?" : (Name.Length >= 2 ? Name[..2].ToUpperInvariant() : Name.ToUpperInvariant());

    /// <summary>Вторая строка: все имеющиеся номера (основной, мобильный) и, если есть, группа.</summary>
    public string SubText =>
        string.Join(" · ", new[] { Number, Mobile, Group }.Where(p => !string.IsNullOrEmpty(p)).Distinct());

    /// <summary>Номер для звонка/избранного: основной, иначе мобильный.</summary>
    public string CallNumber => Number.Length > 0 ? Number : Mobile;
}

/// <summary>Загрузка/парсинг внешней адресной книги (XML вида PhoneBook → DirectoryEntry) с кэшем.</summary>
public static class Phonebook
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinphoneWinUI", "phonebook.xml");

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
            : new HttpClient(new HttpClientHandler
              { Credentials = new System.Net.NetworkCredential(user, password) })
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
            if (name.Length == 0 && tel.Length == 0) continue;
            list.Add(new ContactEntry(name, tel, mob, grp));
        }
        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
