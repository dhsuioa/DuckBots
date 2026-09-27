@echo off
rem Сборка мода DuckBots. Исходник здесь, готовый DLL уезжает туда, где его ждёт игра.
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set DG=D:\Games\Steam\steamapps\common\Duck Game
set MOD=%APPDATA%\DuckGame\Mods\DuckBots

"%CSC%" -nologo -target:library -platform:anycpu ^
  -out:"%MOD%\DuckBots.dll" ^
  -r:"%DG%\DuckGame.exe" ^
  -r:"%MOD%\0Harmony.dll" ^
  -r:System.dll -r:System.Core.dll -codepage:65001 ^
  -recurse:"%~dp0src\*.cs"

if errorlevel 1 (
  echo.
  echo СБОРКА УПАЛА
  exit /b 1
)
echo Готово: %MOD%\DuckBots.dll
