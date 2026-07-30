// L-Phone — in-call control bar (navy strip above the bottom nav).
function InCallBar({ muted, held, video, enabled, onMute, onHold, onVideo, onHangup }) {
  const { IconButton } = window.LPhoneDesignSystem_5e292e;
  const ctrl = (icon, label, active, onClick, key) => (
    <div key={key} style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 3 }}>
      <IconButton
        variant={active ? "accent" : "glass"}
        size={48}
        icon={icon}
        iconColor="#ffffff"
        disabled={!enabled}
        onClick={onClick}
        title={label}
      />
      <span style={{ fontSize: 10, color: "var(--text-on-navy-muted)" }}>{label}</span>
    </div>
  );
  return (
    <div style={{
      display: "flex", justifyContent: "center", gap: 14, alignItems: "flex-start",
      background: "var(--surface-brand)", padding: "10px 8px",
      borderTop: "1px solid var(--stroke-on-navy)",
    }}>
      {ctrl("video", "Видео", video, onVideo, "v")}
      {ctrl("microphone", muted ? "Вкл. микр." : "Микрофон", muted, onMute, "m")}
      {ctrl("pause", "Удержание", held, onHold, "h")}
      {ctrl("switch", "Перевод", false, null, "t")}
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 3 }}>
        <IconButton variant="end" size={48} icon="decline-call" iconColor="#ffffff" title="Сброс" onClick={onHangup} />
        <span style={{ fontSize: 10, color: "var(--text-on-navy-muted)" }}>Сброс</span>
      </div>
    </div>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { InCallBar });
