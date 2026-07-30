// Fake directory + call history for the L-Phone UI kit demo.
window.LPhoneData = (function () {
  const contacts = [
    { initials: "ИП", name: "Иван Петров", sip: "sip:1001@sip.example.com", number: "1001" },
    { initials: "АК", name: "Анна Кузнецова", sip: "+7 495 765-43-21", number: "74957654321" },
    { initials: "СВ", name: "Сергей Волков", sip: "sip:1042@sip.example.com", number: "1042" },
    { initials: "ЕМ", name: "Елена Морозова", sip: "+7 812 553-21-09", number: "78125532109" },
    { initials: "ДН", name: "Дмитрий Новиков", sip: "sip:2200@sip.example.com", number: "2200" },
    { initials: "ОС", name: "Ольга Соколова", sip: "+7 903 118-77-65", number: "79031187765" },
    { initials: "РЗ", name: "Ресепшн (общий)", sip: "sip:0@sip.example.com", number: "0" },
  ];
  const history = [
    { dir: "in", name: "+7 495 123-45-67", number: "74951234567", when: "сегодня · 14:32", dur: "2 мин 14 с", missed: false },
    { dir: "out", name: "Сергей Волков", number: "1042", when: "сегодня · 11:08", dur: "—", missed: false },
    { dir: "missed", name: "Анна Кузнецова", number: "74957654321", when: "вчера · 18:41", dur: "пропущенный", missed: true },
    { dir: "in", name: "Ресепшн (общий)", number: "0", when: "вчера · 09:15", dur: "47 с", missed: false },
    { dir: "out", name: "+7 812 553-21-09", number: "78125532109", when: "пн · 16:53", dur: "5 мин 02 с", missed: false },
  ];
  const account = {
    displayName: "Иван Петров",
    initials: "ИП",
    sip: "sip:1001@sip.example.com",
    login: "1001",
    server: "sip.example.com",
    port: "5060",
    transport: "tls",
    expires: "3600",
  };
  return { contacts, history, account };
})();
