# L-Phone для Windows

SIP-софтфон для Windows на **WinUI 3** и **liblinphone 5.4**: звонки и видео, чат по SIP MESSAGE,
журнал, контакты и телефонная книга по HTTP, presence и BLF-пульт, голосовая почта (MWI),
запись разговоров, перевод и конференция, ICE/STUN/TURN.

Интерфейс на русском, английском и китайском. Тёмная и светлая темы.

## Установка

Готовый установщик — во вкладке [Releases](../../releases). Он подписан; при установке
Windows покажет издателя **L-Phone**.

Чтобы Windows считала подпись доверенной, откройте PowerShell от администратора и разово
внесите сертификат издателя в доверенные:

```powershell
Import-Certificate -FilePath lphone.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath lphone.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

Именно так, с явным указанием хранилища: мастер импорта по двойному щелчку кладёт сертификат
в «Промежуточные центры сертификации», где для самоподписанного сертификата он бесполезен.
Подробнее — [docs/code-signing.md](docs/code-signing.md).

## Обновления

Приложение проверяет [`update.json`](update.json) при запуске и предлагает обновиться,
если вышла версия новее. Скачанный установщик проверяется по подписи: если файл подписан
не нашим сертификатом или подпись повреждена, установка отменяется. Проверку можно выключить
в Настройки → Дополнительно.

## Сборка из исходников

Нужны .NET 10 SDK, Windows App SDK и (для установщика) Inno Setup 6.

```powershell
dotnet build LinphoneWinUI.sln -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

`build-installer.ps1` публикует self-contained сборку, вычищает лишнее из рантайма SDK
(тестовые данные, ML-рантайм, отладочные символы), кладёт рядом VC++ Runtime и собирает
установщик. Подпись включается переменными `LPHONE_SIGN_PFX` / `LPHONE_SIGN_PASS`;
не заданы — сборка идёт без подписи.

`LinphoneSDK.Windows` приезжает с собственного фида Belledonne, он прописан в `nuget.config`.

## Устройство

| Слой | Где |
|---|---|
| Нативное ядро liblinphone | `LinphoneWinUI/Interop/NativeMethods.cs` — P/Invoke с комментариями владения |
| Однопоточный диспетчер ядра | `Interop/CoreDispatcher.cs` — Post / Query с таймаутом / Invoke |
| Фасад и интерфейсный шов | `Interop/LinphoneServiceFacade.cs`, `Interop/ISipService.cs` |
| ViewModels | `ViewModels/` |
| Окна | `MainWindow.xaml`, `SettingsWindow.xaml` |

liblinphone требует строгой однопоточности: единственный поток `linphone-core` гоняет
`iterate()` и исполняет задания из очереди. Интерфейс никогда не трогает ядро напрямую —
только через фасад, и для запросов с таймаутом, чтобы зависшее ядро не вешало окно.

Тесты — `LinphoneWinUI.Tests` (xUnit), запускаются на каждый push через GitHub Actions.

## Дизайн-система

`L-Phone Design System/` — токены, компоненты и рекомендации по интерфейсу, снятые с самого
приложения. Шрифты Segoe в поставку не входят: они проприетарные и в Windows уже установлены.

## Лицензия

Права на код принадлежат автору. liblinphone поставляется по GNU GPL v3 —
см. [linphone.org](https://www.linphone.org/).
