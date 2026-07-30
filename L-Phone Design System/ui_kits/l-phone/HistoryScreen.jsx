// L-Phone — Call history (Журнал) screen.
function HistoryScreen({ history, onPick }) {
  const { ListRow, Icon, Button } = window.LPhoneDesignSystem_5e292e;
  const meta = {
    in: { icon: "incoming-call", color: "#2ba84a", label: "Входящий", rot: false },
    out: { icon: "incoming-call", color: "#3a4f6f", label: "Исходящий", rot: true },
    missed: { icon: "incoming-call", color: "#c42b1c", label: "Пропущенный", rot: false },
  };
  return (
    <div style={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "column" }}>
      <div style={{ flex: 1, minHeight: 0, overflow: "auto", padding: "6px 6px" }}>
        {history.map((h, i) => {
          const m = meta[h.dir];
          return (
            <ListRow
              key={i}
              leading={<Icon name={m.icon} size={20} color={m.color} style={m.rot ? { transform: "rotate(180deg)" } : undefined} />}
              title={<span style={{ color: h.missed ? "var(--state-danger)" : "var(--text-primary)" }}>{h.name}</span>}
              subtitle={m.label + " · " + h.when}
              trailing={<span style={{ fontSize: 12 }}>{h.dur}</span>}
              onClick={() => onPick(h.number)}
            />
          );
        })}
      </div>
      <div style={{ display: "flex", justifyContent: "center", padding: "8px 0 12px", borderTop: "1px solid var(--stroke-divider)" }}>
        <Button variant="subtle" size="sm">Очистить журнал</Button>
      </div>
    </div>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { HistoryScreen });
