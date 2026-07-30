// L-Phone — Settings window (NavigationView rail + panels).
function SettingsScreen({ account, onClose }) {
  const NS = window.LPhoneDesignSystem_5e292e;
  const { NavRail, NavRailItem, TextField, Select, ToggleSwitch, Button, StatusDot, Icon } = NS;
  const [section, setSection] = React.useState("account");

  const items = [
    { tag: "account", icon: "contact", color: "#8a2be2", label: "Аккаунт" },
    { tag: "devices", icon: "microphone", color: "#1f8a3c", label: "Звонки и устройства" },
    { tag: "contacts", icon: "people", color: "#ee82ee", label: "Контакты" },
    { tag: "codecs", icon: "lock", color: "#c71585", label: "Кодеки и шифрование" },
    { tag: "advanced", icon: "settings", color: "#0078d7", label: "Дополнительно" },
    { tag: "about", icon: "info", color: "#2b88d8", label: "О программе" },
  ];

  return (
    <div style={{
      position: "absolute", inset: 0, zIndex: 80, display: "flex", alignItems: "center",
      justifyContent: "center", background: "rgba(0,0,0,.35)",
    }}>
      <div style={{
        width: 660, height: 540, display: "flex", flexDirection: "column",
        background: "var(--surface-card)", borderRadius: 8, overflow: "hidden",
        boxShadow: "var(--shadow-dialog)",
      }}>
        <WinTitleBar title="Настройки — L-Phone" onClose={onClose} />
        <div style={{ flex: 1, minHeight: 0, display: "flex" }}>
          <div style={{ width: 210, borderRight: "1px solid var(--stroke-divider)", background: "var(--surface-page)" }}>
            <NavRail>
              {items.map((it) => (
                <NavRailItem
                  key={it.tag}
                  icon={it.icon}
                  iconColor={it.color}
                  label={it.label}
                  selected={section === it.tag}
                  onClick={() => setSection(it.tag)}
                />
              ))}
            </NavRail>
          </div>
          <div style={{ flex: 1, minWidth: 0, overflow: "auto", padding: "16px 18px" }}>
            {section === "account" ? <AccountPanel account={account} NS={NS} /> : null}
            {section === "devices" ? <DevicesPanel NS={NS} /> : null}
            {section === "advanced" ? <AdvancedPanel NS={NS} /> : null}
            {section === "about" ? <AboutPanel NS={NS} /> : null}
            {(section === "contacts" || section === "codecs") ? (
              <PlaceholderPanel section={section} NS={NS} />
            ) : null}
          </div>
        </div>
      </div>
    </div>
  );
}

function SectionTitle({ children }) {
  return <div style={{ fontSize: 22, fontWeight: "var(--fw-semibold)", marginBottom: 14 }}>{children}</div>;
}

function AccountPanel({ account, NS }) {
  const { TextField, Select, Button, StatusDot } = NS;
  return (
    <div style={{ maxWidth: 460 }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
        <div style={{ fontSize: 22, fontWeight: "var(--fw-semibold)" }}>Аккаунт</div>
        <Button variant="accent">Сохранить</Button>
      </div>
      <div style={{ fontSize: 14, color: "var(--text-secondary)" }}>SIP-адрес</div>
      <div style={{ fontSize: 18, marginBottom: 8 }}>{account.sip}</div>
      <div style={{ marginBottom: 16 }}><StatusDot status="online" label="Подключён" /></div>
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        <TextField label="Отображаемое имя" defaultValue={account.displayName} />
        <TextField label="Логин" defaultValue={account.login} />
        <TextField label="Пароль" type="password" defaultValue="secret123" />
        <TextField label="Имя сервера" defaultValue={account.server} />
        <TextField label="Порт" defaultValue={account.port} />
        <Select label="Транспорт" defaultValue={account.transport} options={[
          { value: "udp", label: "UDP" }, { value: "tcp", label: "TCP" }, { value: "tls", label: "TLS" },
        ]} />
        <TextField label="Таймаут регистрации (сек)" defaultValue={account.expires} />
        <div style={{ marginTop: 6 }}>
          <Button variant="destructive" block>Удалить аккаунт</Button>
        </div>
      </div>
    </div>
  );
}

function DevicesPanel({ NS }) {
  const { Select, ToggleSwitch } = NS;
  return (
    <div style={{ maxWidth: 460 }}>
      <SectionTitle>Звонки и устройства</SectionTitle>
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <ToggleSwitch label="Эхоподавитель" defaultChecked />
        <Select label="Способ передачи DTMF" defaultValue="rfc2833" options={[
          { value: "rfc2833", label: "RFC2833 (RTP)" }, { value: "info", label: "SIP INFO" },
        ]} />
        <Select label="Микрофон" defaultValue="default" options={[
          { value: "default", label: "Микрофон (Realtek Audio)" },
          { value: "headset", label: "Гарнитура (USB)" },
        ]} />
        <Select label="Динамик" defaultValue="default" options={[
          { value: "default", label: "Динамики (Realtek Audio)" },
          { value: "headset", label: "Гарнитура (USB)" },
        ]} />
        <Select label="Камера" defaultValue="none" options={[
          { value: "none", label: "(нет камеры)" },
          { value: "cam", label: "Integrated Webcam" },
        ]} />
      </div>
    </div>
  );
}

function AdvancedPanel({ NS }) {
  const { Select, ToggleSwitch, TextField, Button } = NS;
  const setTheme = (v) => {
    const root = document.documentElement;
    if (v === "dark") root.setAttribute("data-theme", "dark");
    else root.removeAttribute("data-theme");
  };
  return (
    <div style={{ maxWidth: 460 }}>
      <SectionTitle>Дополнительно</SectionTitle>
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <Select label="Оформление" defaultValue="light" onChange={setTheme} options={[
          { value: "system", label: "Системная" },
          { value: "light", label: "Светлая" },
          { value: "dark", label: "Тёмная" },
        ]} />
        <ToggleSwitch label="Запускать вместе с Windows" />
        <ToggleSwitch label="Сворачивать в трей при закрытии" defaultChecked />
        <div style={{ fontWeight: "var(--fw-semibold)", marginTop: 6 }}>Сеть (NAT)</div>
        <TextField label="STUN-сервер" placeholder="stun.example.com:3478" />
        <ToggleSwitch label="Использовать ICE" />
        <Button variant="standard">Применить (сеть)</Button>
      </div>
    </div>
  );
}

function AboutPanel({ NS }) {
  const { Icon } = NS;
  const row = (icon, color, label, value) => (
    <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
      <Icon name={icon} size={22} color={color} />
      <div>
        <div style={{ fontSize: 12, color: "var(--text-tertiary)" }}>{label}</div>
        <div style={{ fontSize: 14, fontWeight: "var(--fw-semibold)" }}>{value}</div>
      </div>
    </div>
  );
  return (
    <div style={{ maxWidth: 460, display: "flex", flexDirection: "column", gap: 14 }}>
      <SectionTitle>О продукте</SectionTitle>
      {row("info", "#2b88d8", "Версия", "L-Phone 1.0")}
      {row("completed-solid", "#2ba84a", "Лицензия", "GPLv3")}
      {row("lock", "#2b88d8", "liblinphone SDK", "5.3.0")}
      {row("settings", "#7e57c2", "Рантайм", "WinAppSDK 2.1.3 · .NET 10")}
    </div>
  );
}

function PlaceholderPanel({ section, NS }) {
  const title = section === "contacts" ? "Контакты" : "Кодеки и шифрование";
  const note = section === "contacts"
    ? "Внешняя адресная книга загружается по HTTP (XML вида PhoneBook → DirectoryEntry)."
    : "Приоритет аудио/видео кодеков и шифрование медиа (SRTP / ZRTP / DTLS).";
  return (
    <div style={{ maxWidth: 460 }}>
      <SectionTitle>{title}</SectionTitle>
      <div style={{ color: "var(--text-secondary)", fontSize: 13 }}>{note}</div>
    </div>
  );
}

function WinTitleBar({ title, onClose }) {
  const { Icon } = window.LPhoneDesignSystem_5e292e;
  return (
    <div style={{
      display: "flex", alignItems: "center", height: 36, flex: "none",
      background: "var(--surface-card)", borderBottom: "1px solid var(--stroke-divider)",
      paddingLeft: 12, justifyContent: "space-between",
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12, color: "var(--text-secondary)" }}>
        <span style={{
          width: 16, height: 16, borderRadius: 3, background: "var(--surface-brand)", color: "#fff",
          display: "inline-flex", alignItems: "center", justifyContent: "center",
          fontSize: 11, fontWeight: "var(--fw-semibold)",
        }}>L</span>
        {title}
      </div>
      <div style={{ display: "flex", height: "100%" }}>
        <CaptionBtn glyph="chrome-minimize" />
        <CaptionBtn glyph="checkbox" />
        <CaptionBtn glyph="chrome-close" danger onClick={onClose} />
      </div>
    </div>
  );
}

function CaptionBtn({ glyph, danger, onClick }) {
  const { Icon } = window.LPhoneDesignSystem_5e292e;
  const [hover, setHover] = React.useState(false);
  return (
    <button
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      onClick={onClick}
      style={{
        width: 44, height: "100%", border: 0, cursor: "pointer",
        background: hover ? (danger ? "#c42b1c" : "var(--lp-subtle-hover)") : "transparent",
        display: "inline-flex", alignItems: "center", justifyContent: "center",
      }}
    >
      <Icon name={glyph} size={11} color={hover && danger ? "#ffffff" : "#5d5d5d"} />
    </button>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { SettingsScreen, WinTitleBar });
