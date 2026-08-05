# Создаёт СВОЙ сертификат подписи кода — тот же подход, что у Android-версии с её keystore:
# доверие обеспечивается не публичным центром сертификации, а тем, что сертификат заранее
# роздан на машины (в домене — групповой политикой). Для внутренней раскатки этого достаточно.
#
# ЧЕГО ЭТО НЕ ДАЁТ: SmartScreen у постороннего пользователя, скачавшего установщик из интернета,
# всё равно предупредит — репутацию даёт только сертификат публичного УЦ (OV/EV). Для продажи
# наружу понадобится покупной; менять придётся ровно одну переменную LPHONE_SIGN_PFX.
#
# Запускать ОДИН раз. Готовый .pfx хранить вне репозитория и не коммитить.
[CmdletBinding()]
param(
    [string]$Subject  = "CN=L-Phone, O=L-Phone, C=RU",
    [string]$OutPfx   = "$env:USERPROFILE\.lphone\code-signing.pfx",
    [int]   $Years    = 5
)
$ErrorActionPreference = "Stop"

if (Test-Path $OutPfx) {
    throw "Файл уже существует: $OutPfx. Удалите его вручную, если действительно нужен новый сертификат " +
          "(старые подписи перестанут соответствовать новому — машины придётся переучивать)."
}

$pass = Read-Host "Пароль для .pfx (запомните — он понадобится при каждой сборке)" -AsSecureString
if ($pass.Length -eq 0) { throw "Пустой пароль не годится: файл с закрытым ключом останется без защиты." }

Write-Host "==> Создаю самоподписанный сертификат подписи кода"
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears($Years)

New-Item -ItemType Directory -Force -Path (Split-Path $OutPfx) | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $OutPfx -Password $pass | Out-Null

$cerPath = [IO.Path]::ChangeExtension($OutPfx, ".cer")
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Host ""
Write-Host "Готово."
Write-Host "  Закрытый ключ (НЕ коммитить, беречь):  $OutPfx"
Write-Host "  Открытая часть (раздавать на машины):  $cerPath"
Write-Host "  Отпечаток:                             $($cert.Thumbprint)"
Write-Host ""
Write-Host "Дальше:"
Write-Host "  1) Задать переменные окружения для сборки:"
Write-Host "       setx LPHONE_SIGN_PFX  `"$OutPfx`""
Write-Host "       setx LPHONE_SIGN_PASS `"<пароль>`""
Write-Host "  2) Раздать $cerPath на машины — в «Доверенные корневые центры» и"
Write-Host "     «Доверенные издатели» (в домене: групповая политика, см. docs/code-signing.md)."
