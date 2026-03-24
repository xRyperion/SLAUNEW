@echo off
chcp 65001 >nul
title Веб-приложение: Распределённое решение СЛАУ

echo ╔═══════════════════════════════════════════════════════════╗
echo ║   Распределённое решение СЛАУ (веб-приложение)           ║
echo ╚═══════════════════════════════════════════════════════════╝
echo
echo Запуск веб-сервера...
echo

cd /d "%~dp0"
start "" http://localhost:5000
echo Открываем браузер: http://localhost:5000
echo

dotnet DistributedSLAU.Web.dll --urls="http://0.0.0.0:5000"

pause
