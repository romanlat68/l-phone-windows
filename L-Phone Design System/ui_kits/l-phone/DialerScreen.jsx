// L-Phone — dialer content area: number entry, dial pad, call pill, incoming.
function DialerScreen(props) {
  const { number, onKey, onBackspace, onCall, callState, remote, callStatus, duration, onAccept, onDecline } = props;
  const { DialPad, IconButton, Button, Card } = window.LPhoneDesignSystem_5e292e;
  const inCall = callState === "calling" || callState === "active";

  return (
    <div style={{
      flex: 1, minHeight: 0, overflow: "auto", display: "flex", flexDirection: "column",
      alignItems: "center", padding: "14px 16px 20px",
    }}>
      {callState === "incoming" ? (
        <Card padding={16} style={{ width: "100%", marginBottom: 8, background: "rgba(34,51,77,.07)", borderColor: "rgba(34,51,77,.20)" }}>
          <div style={{ textAlign: "center", fontWeight: "var(--fw-semibold)", marginBottom: 4 }}>Входящий звонок</div>
          <div style={{ textAlign: "center", color: "var(--text-secondary)", fontSize: 14, marginBottom: 14 }}>{remote}</div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <Button variant="accent" block onClick={onAccept}>Принять</Button>
            <Button variant="standard" block onClick={onDecline}>Отклонить</Button>
          </div>
        </Card>
      ) : null}

      {inCall ? (
        <div style={{
          margin: "14px 0 18px", padding: "10px 22px", borderRadius: "var(--radius-pill)",
          background: "var(--call-accept)", textAlign: "center",
          boxShadow: "0 4px 14px rgba(43,168,74,.30)",
        }}>
          <div style={{ color: "#fff", fontSize: 18, fontWeight: "var(--fw-semibold)" }}>{remote}</div>
          <div style={{ color: "#e6f4ea", fontSize: 13 }}>
            {callState === "active" ? duration : callStatus}
          </div>
        </div>
      ) : null}

      {callState === "idle" ? (
        <div style={{
          display: "flex", alignItems: "center", width: "100%", maxWidth: 250,
          margin: "36px 0 20px", padding: "8px 8px 8px 14px", boxSizing: "border-box",
          background: "var(--lp-subtle-hover)", border: "1px solid var(--stroke-control)",
          borderRadius: "var(--radius-md)", minHeight: 44,
        }}>
          <span style={{
            flex: 1, fontSize: 24, color: number ? "var(--text-primary)" : "var(--text-tertiary)",
            fontFamily: "var(--font-sans)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
          }}>
            {number || "Номер"}
          </span>
          {number ? (
            <button
              onClick={onBackspace}
              title="Стереть"
              style={{
                display: "inline-flex", alignItems: "center", justifyContent: "center",
                width: 32, height: 32, flex: "none", border: 0, borderRadius: "var(--radius-round)",
                background: "transparent", cursor: "pointer", color: "var(--text-secondary)",
                fontSize: 17, lineHeight: 1,
              }}
            >
              {"\u232B"}
            </button>
          ) : null}
        </div>
      ) : null}

      {callState !== "incoming" ? (
        <div style={{ width: "100%", maxWidth: 230, marginTop: inCall ? 4 : 0 }}>
          <DialPad onPress={onKey} />
        </div>
      ) : null}

      {callState === "idle" ? (
        <div style={{ marginTop: 18 }}>
          <IconButton variant="accept" size={64} icon="phone" iconColor="#ffffff" title="Позвонить" onClick={onCall} />
        </div>
      ) : null}
    </div>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { DialerScreen });
