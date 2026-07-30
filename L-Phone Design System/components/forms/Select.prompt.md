Fluent ComboBox / dropdown — Settings selects (transport, DTMF mode, encryption, theme, audio devices).

```jsx
<Select label="Транспорт" defaultValue="tls" options={[
  { value: "udp", label: "UDP" },
  { value: "tcp", label: "TCP" },
  { value: "tls", label: "TLS" },
]} />
```

Notes:
- Opens a shadow-flyout list; selected option shows a check + accent.
- Closes on outside click. Controlled (`value`+`onChange`) or uncontrolled (`defaultValue`).
