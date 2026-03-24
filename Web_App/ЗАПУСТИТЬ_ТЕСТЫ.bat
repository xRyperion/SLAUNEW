@echo off
chcp 65001 >nul
echo ========================================
echo    ЗАПУСК МОДУЛЬНЫХ И НАГРУЗОЧНЫХ ТЕСТОВ
echo ========================================
echo.

cd /d "%~dp0"

if exist "Tests\dotnet.exe" (
    echo Запуск тестов из папки Tests...
    Tests\dotnet.exe test DistributedSLAU.Tests.dll --logger "console;verbosity=detailed"
) else (
    echo Запуск тестов через dotnet...
    dotnet test "..\DistributedSLAU.Tests\DistributedSLAU.Tests.csproj" --logger "console;verbosity=detailed"
)

echo.
echo ========================================
echo    ТЕСТЫ ЗАВЕРШЕНЫ
echo ========================================
pause
