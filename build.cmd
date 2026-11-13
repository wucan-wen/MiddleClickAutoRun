@echo off
chcp 65001 >nul
cd /d "%~dp0AutoRunKeeper"
set NUGET_CERT_REVOCATION_MODE=offline
dotnet build AutoRunKeeper.csproj -c Debug -p:Platform=x64 -p:DalamudLibPath="%APPDATA%\XIVLauncherCN\addon\Hooks\dev\"
if errorlevel 1 goto :eof
set OUT=..\..\devPlugins\AutoRunKeeper
if not exist "%OUT%" mkdir "%OUT%"
copy /y "bin\x64\Debug\AutoRunKeeper.dll" "%OUT%\" >nul
copy /y "bin\x64\Debug\AutoRunKeeper.json" "%OUT%\" >nul
copy /y "bin\x64\Debug\AutoRunKeeper.deps.json" "%OUT%\" >nul
echo 已构建并部署到 %OUT%
