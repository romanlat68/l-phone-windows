// L-Phone — app shell + call state machine. Composes the kit screens.
function Phone() {
  const NS = window.LPhoneDesignSystem_5e292e;
  const { BottomNavBar, BottomNavTab } = NS;
  const { Header, DialerScreen, InCallBar, ContactsScreen, HistoryScreen, SettingsScreen, WinTitleBar } = window.LPhoneKit;
  const data = window.LPhoneData;

  const [tab, setTab] = React.useState("dialer");
  const [number, setNumber] = React.useState("");
  const [callState, setCallState] = React.useState("idle"); // idle | incoming | calling | active
  const [remote, setRemote] = React.useState("");
  const [seconds, setSeconds] = React.useState(0);
  const [muted, setMuted] = React.useState(false);
  const [held, setHeld] = React.useState(false);
  const [video, setVideo] = React.useState(false);
  const [settings, setSettings] = React.useState(false);

  // calling -> active after a short ring
  React.useEffect(() => {
    if (callState !== "calling") return;
    const t = setTimeout(() => { setCallState("active"); setSeconds(0); }, 1500);
    return () => clearTimeout(t);
  }, [callState]);

  // duration ticker while active and not held
  React.useEffect(() => {
    if (callState !== "active" || held) return;
    const t = setInterval(() => setSeconds((s) => s + 1), 1000);
    return () => clearInterval(t);
  }, [callState, held]);

  const fmt = (s) => `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
  const pretty = (n) => {
    const c = data.contacts.find((x) => x.number === n);
    return c ? c.name : (n || "Неизвестный");
  };

  const startCall = () => {
    if (!number) return;
    setRemote(pretty(number));
    setCallState("calling");
    setMuted(false); setHeld(false); setVideo(false);
  };
  const hangup = () => { setCallState("idle"); setSeconds(0); setMuted(false); setHeld(false); setVideo(false); };
  const simulateIncoming = () => {
    setRemote("Анна Кузнецова");
    setCallState("incoming");
    setTab("dialer");
  };
  const accept = () => { setCallState("active"); setSeconds(0); };
  const pick = (n) => { setNumber(n); setTab("dialer"); };

  const inCall = callState === "calling" || callState === "active";
  const statusText = callState === "calling" ? "Вызов…" : (held ? "На удержании" : "");

  return (
    <div style={{
      width: 400, height: 700, display: "flex", flexDirection: "column",
      background: "var(--surface-card)", borderRadius: 8, overflow: "hidden",
      boxShadow: "var(--shadow-dialog)", position: "relative",
    }}>
      <WinTitleBar title="L-Phone" onClose={() => {}} />
      <Header
        account={data.account}
        registered={true}
        onOpenSettings={() => setSettings(true)}
        onSimulateIncoming={simulateIncoming}
      />

      <div style={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "column", background: "var(--surface-card)" }}>
        {tab === "dialer" ? (
          <DialerScreen
            number={number}
            onKey={(c) => setNumber((n) => n + c)}
            onBackspace={() => setNumber((n) => n.slice(0, -1))}
            onCall={startCall}
            callState={callState}
            remote={remote}
            callStatus={statusText}
            duration={fmt(seconds)}
            onAccept={accept}
            onDecline={hangup}
          />
        ) : null}
        {tab === "contacts" ? <ContactsScreen contacts={data.contacts} onPick={pick} /> : null}
        {tab === "history" ? <HistoryScreen history={data.history} onPick={pick} /> : null}
      </div>

      {inCall ? (
        <InCallBar
          muted={muted} held={held} video={video} enabled={callState === "active"}
          onMute={() => setMuted((m) => !m)}
          onHold={() => setHeld((h) => !h)}
          onVideo={() => setVideo((v) => !v)}
          onHangup={hangup}
        />
      ) : null}

      <BottomNavBar>
        <BottomNavTab icon="phone" label="Набор" active={tab === "dialer"} onClick={() => setTab("dialer")} />
        <BottomNavTab icon="contact" label="Контакты" active={tab === "contacts"} onClick={() => setTab("contacts")} />
        <BottomNavTab icon="history" label="Журнал" active={tab === "history"} onClick={() => setTab("history")} />
      </BottomNavBar>

      {settings ? <SettingsScreen account={data.account} onClose={() => setSettings(false)} /> : null}
    </div>
  );
}

window.LPhoneKit = Object.assign(window.LPhoneKit || {}, { Phone });
