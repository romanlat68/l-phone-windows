# Собирает автономный дистрибутив L-Phone: публикует приложение (self-contained)
# и вшивает рантайм liblinphone (только нужное: DLL без Qt/pdb/exe + плагины + ресурсы).
# Результат — папка dist\, готовая к упаковке installer.iss (Inno Setup).
$ErrorActionPreference = "Stop"

$proj = "$PSScriptRoot\LinphoneWinUI\LinphoneWinUI.csproj"
$dist = "$PSScriptRoot\dist"

# ---- Подпись кода ---------------------------------------------------------------
# Сертификат берём из переменных окружения, чтобы закрытый ключ никогда не попадал в репозиторий:
#   LPHONE_SIGN_PFX  — путь к .pfx   (создать: tools\new-signing-cert.ps1)
#   LPHONE_SIGN_PASS — пароль к нему
# Не заданы — сборка идёт как раньше, но с предупреждением: неподписанный установщик
# встречает пользователя окном SmartScreen «Windows защитила ваш компьютер».
$signPfx  = $env:LPHONE_SIGN_PFX
$signPass = $env:LPHONE_SIGN_PASS

$signtool = @(
    Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
) | Where-Object { $_ } | Select-Object -First 1

function Invoke-Sign([string]$path) {
    if (-not $signPfx) { return }
    if (-not (Test-Path $path)) { throw "Нечего подписывать: $path" }
    if (-not $signtool) { throw "Задан LPHONE_SIGN_PFX, но signtool.exe не найден (нужен Windows SDK)." }
    if (-not (Test-Path $signPfx)) { throw "Сертификат не найден: $signPfx" }

    # /tr — метка времени RFC 3161. Без неё подпись «протухает» вместе с сертификатом:
    # с меткой она остаётся действительной и после истечения срока сертификата.
    $timestamps = @("http://timestamp.digicert.com", "http://timestamp.sectigo.com")
    foreach ($ts in $timestamps) {
        & $signtool sign /fd SHA256 /td SHA256 /tr $ts /f $signPfx /p $signPass $path 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    подписано ($([IO.Path]::GetFileName($path)), метка времени $ts)"
            return
        }
        Write-Host "    служба меток времени $ts недоступна, пробую следующую"
    }
    throw "Подписать не удалось: ни одна служба меток времени не ответила."
}

Write-Host "==> Очистка $dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

Write-Host "==> dotnet publish (Release, self-contained)"
dotnet publish $proj -c Release -r win-x64 --self-contained -p:Platform=x64 -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# dotnet publish теряет resources.pri (известный баг WinUI) — без него приложение
# падает при старте (0xc000027b). Копируем из build-output вручную.
Write-Host "==> Копирую resources.pri (publish его теряет)"
$buildPri = "$PSScriptRoot\LinphoneWinUI\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\resources.pri"
if (-not (Test-Path $buildPri)) { throw "resources.pri не найден: $buildPri" }
Copy-Item $buildPri (Join-Path $dist "resources.pri") -Force

# Оставляем только нужные языки (русский, английский, китайский) — WindowsAppSDK/.NET
# тащат ~80 языковых папок (af-ZA, vi-VN, …). Удаляем остальные.
Write-Host "==> Удаляю лишние языковые ресурсы (оставляю ru/en/zh)"
$keepLang = @("en-us","en-US","ru-RU","zh-CN","zh-TW")
Get-ChildItem $dist -Directory |
    Where-Object { $_.Name -match '^[a-z]{2,3}(-[A-Za-z]+)+$' -and ($keepLang -notcontains $_.Name) } |
    Remove-Item -Recurse -Force

# Рантайм liblinphone уже лежит в КОРНЕ dist — его кладёт NuGet-пакет LinphoneSDK.Windows
# при publish (liblinphone.dll, mediastreamer-плагины, share — рядом с exe). Приложение грузит
# SDK именно из корня (LinphoneService.ResolveSdkRoot → AppContext.BaseDirectory), поэтому
# отдельная папка dist\linphone НЕ нужна — раньше это был неиспользуемый дубль (~105 МБ).

# VC++ Runtime (MSVC) — liblinphone/mediastreamer собраны MSVC и требуют этих DLL; на чистом ПК
# (без установленного VC++ Redistributable) их нет → Core не стартует. Кладём в КОРЕНЬ dist,
# РЯДОМ с liblinphone.dll (только так Windows их найдёт при загрузке). Раньше скрипт клал их
# в dist\linphone\bin — не рядом с реально загружаемой DLL, т.е. бандл VC++ не работал.
Write-Host "==> Кладу VC++ runtime рядом с liblinphone.dll (корень dist)"
$sys = "$env:SystemRoot\System32"
$vc = "vcruntime140.dll","vcruntime140_1.dll","msvcp140.dll",
      "msvcp140_1.dll","msvcp140_2.dll","msvcp140_atomic_wait.dll","concrt140.dll"
foreach ($v in $vc) {
    $src = Join-Path $sys $v
    if (Test-Path $src) { Copy-Item $src $dist -Force }
}

# Подрезаем мусор из NuGet-рантайма, не нужный установленному приложению:
#  - share\*-tester  — тестовые данные SDK (~148 МБ из-за liblinphone-tester),
#  - share\linphonecs — исходник C#-обёртки (в рантайме не нужен).
Write-Host "==> Удаляю тестовые данные SDK и исходник обёртки из share"
Get-ChildItem "$dist\share" -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*tester*' -or $_.Name -eq 'linphonecs' } |
    Remove-Item -Recurse -Force

# ML-рантайм из WindowsAppSDK (Windows AI / Phi Silica) — приложение его не использует (~38 МБ).
# Если когда-нибудь понадобятся Windows AI API — закомментируй этот блок.
Write-Host "==> Удаляю неиспользуемый ML-рантайм (onnxruntime/DirectML)"
foreach ($m in "onnxruntime.dll","DirectML.dll") {
    $p = Join-Path $dist $m
    if (Test-Path $p) { Remove-Item $p -Force }
}

# Сборочные метаданные и отладочные символы — в рантайме не нужны:
#  - share\mbedtls\*.cmake (CMake-таргеты, нужны только при компиляции против SDK),
#  - *.pdb (символы обёртки и самого приложения).
Write-Host "==> Удаляю сборочные метаданные (.cmake) и символы (.pdb)"
if (Test-Path "$dist\share\mbedtls") { Remove-Item "$dist\share\mbedtls" -Recurse -Force }
Get-ChildItem $dist -File -Filter *.pdb | Remove-Item -Force

# Служебные файлы Microsoft, не относящиеся к нашему приложению:
#  - createdump.exe   — утилита crash-дампов .NET (по умолчанию не задействуется),
#  - RestartAgent.exe — агент перезапуска WindowsAppSDK (наш код не вызывает),
#  - workloads*.json  — дескрипторы AI-workloads WindowsAppSDK (Phi Silica и пр.; приложение AI не использует).
Write-Host "==> Удаляю неиспользуемые служебные файлы (createdump/RestartAgent/workloads)"
foreach ($x in "createdump.exe","RestartAgent.exe") {
    $p = Join-Path $dist $x
    if (Test-Path $p) { Remove-Item $p -Force }
}
Get-ChildItem $dist -File -Filter "workloads*.json" | Remove-Item -Force

$mb = [math]::Round(((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum)/1MB, 0)
Write-Host "==> dist готов. $mb МБ"

# Подписываем ДО упаковки: установщик несёт exe внутри себя, и подписать его потом уже нельзя.
if ($signPfx) {
    Write-Host "==> Подпись приложения"
    Invoke-Sign (Join-Path $dist "L-Phone.exe")
}
else {
    Write-Host "==> Подпись пропущена: LPHONE_SIGN_PFX не задан (см. tools\new-signing-cert.ps1)"
}

# Компиляция установщика (Inno Setup), если он установлен.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    # Версию берём из собранного exe: если задавать её в installer.iss руками, она неминуемо
    # разъедется с csproj, а автообновление сравнивает версию сборки с лентой обновлений.
    $ver = [version]((Get-Item (Join-Path $dist "L-Phone.exe")).VersionInfo.FileVersion)
    $verShort = "{0}.{1}.{2}" -f $ver.Major, $ver.Minor, $ver.Build
    Write-Host "==> Компиляция установщика (Inno Setup), версия $verShort"
    & $iscc "/DMyVer=$verShort" "$PSScriptRoot\installer.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с ошибкой" }

    $setup = Get-ChildItem "$PSScriptRoot\installer" -Filter "L-Phone-Setup-*.exe" |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($signPfx -and $setup) {
        Write-Host "==> Подпись установщика"
        Invoke-Sign $setup.FullName
    }
    Write-Host "==> ГОТОВО: $($setup.FullName)"
}
else {
    Write-Host "Inno Setup не найден. Установи:  winget install JRSoftware.InnoSetup"
    Write-Host "затем вручную:  ISCC.exe `"$PSScriptRoot\installer.iss`""
}
