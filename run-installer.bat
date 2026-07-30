@echo off
REM Батник для запуска build-installer.ps1 из Visual Studio
REM Этот файл вызывает PowerShell скрипт, который собирает Inno Setup установщик

setlocal enabledelayedexpansion

REM Получаем директорию скрипта
set SCRIPT_DIR=%~dp0
set PS_SCRIPT=%SCRIPT_DIR%build-installer.ps1

REM Проверяем наличие скрипта
if not exist "!PS_SCRIPT!" (
	echo Ошибка: build-installer.ps1 не найден в !PS_SCRIPT!
	pause
	exit /b 1
)

REM Запускаем PowerShell скрипт с правильной политикой выполнения
echo Запуск build-installer.ps1...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!PS_SCRIPT!"

if errorlevel 1 (
	echo.
	echo Ошибка при выполнении build-installer.ps1
	pause
	exit /b 1
) else (
	echo.
	echo Установщик успешно собран!
	pause
)

endlocal
