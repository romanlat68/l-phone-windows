// L-Phone — Contacts (external directory) screen.
function ContactsScreen({ contacts, onPick }) {
  const { SearchBox, ListRow, Avatar } = window.LPhoneDesignSystem_5e292e;
  const [q, setQ] = React.useState("");
  const filtered = contacts.filter((c) =>
    (c.name + " " + c.sip).toLowerCase().includes(q.toLowerCase())
  );
  return (
    <div style={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "column" }}>
      <div style={{ padding: "8px 10px 4px" }}>
        <SearchBox
          placeholder="Поиск по имени или номеру"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
      </div>
      <div style={{ flex: 1, minHeight: 0, overflow: "auto", padding: "2px 6px" }}>
        {filtered.map((c) => (
          <ListRow
            key={c.sip}
            leading={<Avatar initials={c.initials} size={38} tone="navy" />}
            title={c.name}
            subtitle={c.sip}
            onClick={() => onPick(c.number)}
          />
        ))}
        {filtered.length === 0 ? (
          <div style={{ textAlign: "center", color: "var(--text-tertiary)", fontSize: 13, padding: "32px 0" }}>
            Ничего не найдено
          </div>
        ) : null}
      </div>
      <div style={{ padding: "6px 12px 10px", color: "var(--text-tertiary)", fontSize: 12 }}>
        Внешняя адресная книга · {contacts.length} контактов
      </div>
    </div>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { ContactsScreen });
