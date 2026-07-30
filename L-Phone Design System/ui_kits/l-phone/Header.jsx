// L-Phone — account header (navy bar). Shares via window.LPhoneKit.
function Header({ account, registered, onOpenSettings, onSimulateIncoming }) {
  const { Avatar, IconButton } = window.LPhoneDesignSystem_5e292e;
  const [menu, setMenu] = React.useState(false);
  React.useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(false);
    const t = setTimeout(() => document.addEventListener("click", close), 0);
    return () => { clearTimeout(t); document.removeEventListener("click", close); };
  }, [menu]);

  return (
    <div style={{
      position: "relative", display: "flex", alignItems: "center", gap: 12,
      background: "var(--surface-brand)", padding: "10px 14px",
    }}>
      <Avatar
        initials={registered ? account.initials : "—"}
        size={40}
        tone="slate"
        status={registered ? "online" : "offline"}
        ringColor="#22334d"
      />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{
          color: "#fff", fontSize: 16, fontWeight: "var(--fw-semibold)",
          lineHeight: "20px", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
        }}>
          {registered ? account.displayName : "Не подключён"}
        </div>
        <div style={{
          color: "var(--text-on-navy-muted)", fontSize: 12, lineHeight: "16px",
          overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
        }}>
          {registered ? account.sip : "Откройте настройки для входа"}
        </div>
      </div>
      <IconButton
        variant="glass"
        size={34}
        icon="settings"
        iconColor="#ffffff"
        title="Меню"
        onClick={(e) => { e.stopPropagation(); setMenu((m) => !m); }}
      />
      {menu ? (
        <div style={{
          position: "absolute", right: 12, top: 56, zIndex: 50, minWidth: 220,
          background: "var(--surface-card)", border: "1px solid var(--stroke-card)",
          borderRadius: "var(--radius-md)", boxShadow: "var(--shadow-flyout)", padding: 4,
        }}>
          <MenuItem icon="contact" label="Мой аккаунт" onClick={onOpenSettings} />
          <MenuItem icon="settings" label="Настройки" onClick={onOpenSettings} />
          <MenuItem icon="incoming-call" label="Симулировать входящий" onClick={onSimulateIncoming} />
          <div style={{ height: 1, background: "var(--stroke-divider)", margin: "4px 6px" }} />
          <MenuItem icon="info" label="О программе" onClick={onOpenSettings} />
        </div>
      ) : null}
    </div>
  );
}

function MenuItem({ icon, label, onClick }) {
  const { Icon } = window.LPhoneDesignSystem_5e292e;
  const [hover, setHover] = React.useState(false);
  return (
    <button
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      onClick={onClick}
      style={{
        display: "flex", alignItems: "center", gap: 12, width: "100%", textAlign: "left",
        border: 0, background: hover ? "var(--lp-subtle-hover)" : "transparent",
        padding: "8px 10px", borderRadius: "var(--radius-sm)", cursor: "pointer",
        fontFamily: "var(--font-sans)", fontSize: 14, color: "var(--text-primary)",
      }}
    >
      <Icon name={icon} size={16} color="#4e6075" />
      <span>{label}</span>
    </button>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { Header });
