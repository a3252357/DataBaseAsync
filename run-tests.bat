@echo off
echo ========================================
echo Running Database Replication System Unit Tests
echo ========================================
echo.

echo Building solution...
dotnet build DataBaseAsync.sln --configuration Debug
if %ERRORLEVEL% neq 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Build successful! Running tests...
echo.

dotnet test DataBaseAsync.Tests\DataBaseAsync.Tests.csproj --logger "console;verbosity=detailed" --collect:"XPlat Code Coverage"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================
    echo All tests completed successfully!
    echo ========================================
) else (
    echo.
    echo ========================================
    echo Test execution failed!
    echo ========================================
)

echo.
echo Press any key to exit...
pause > nul