@echo OFF
REM *******************************
REM Make sure you have:
REM   1. Installed the .NET Global Tool first.
REM      More information: https://github.com/PublicApiGenerator/PublicApiGenerator?tab=readme-ov-file#install
REM      ---
REM        dotnet tool install -g PublicApiGenerator.Tool
REM      ---
REM   2. Built CoreIpc.sln.
REM *******************************

echo *******************************
echo Make sure you have:
echo   1. Installed the .NET Global Tool first.
echo      More information: https://github.com/PublicApiGenerator/PublicApiGenerator?tab=readme-ov-file#install
echo      ---
echo        dotnet tool install -g PublicApiGenerator.Tool
echo      ---
echo   2. Built CoreIpc.sln.

set "outputPath=%~dp0"
set "outputPath=%outputPath:~0,-1%" :: trim the final backslash

set "projectPath=%~dp0..\"
pushd "%projectPath%" >nul 2>&1
if %errorlevel% neq 0 (
    echo Invalid path
    exit /b 1
)
set "projectPath=%CD%\UiPath.CoreIpc.csproj"
popd

echo ON

REM generate-public-api --target-frameworks "net6.0-windows" --assembly UiPath.Ipc.dll --project-path "%projectPath%" --output-directory "%outputPath%"
REM generate-public-api --target-frameworks "net6.0"         --assembly UiPath.Ipc.dll --project-path "%projectPath%" --output-directory "%outputPath%"
REM generate-public-api --target-frameworks "net461"         --assembly UiPath.Ipc.dll --project-path "%projectPath%" --output-directory "%outputPath%"

generate-public-api --target-frameworks "net6.0-windows" --assembly UiPath.Ipc.dll --project-path "%projectPath%" --output-directory "%outputPath%" --verbose --leave-artifacts
REM generate-public-api --target-frameworks "net6.0"         --assembly UiPath.Ipc.dll --project-path "D:\Alt\coreipc\src\UiPath.CoreIpc\UiPath.CoreIpc.csproj" --output-directory "D:\Alt\coreipc\src\UiPath.CoreIpc\report" --verbose
REM generate-public-api --target-frameworks "net461"         --assembly UiPath.Ipc.dll --project-path "D:\Alt\coreipc\src\UiPath.CoreIpc\UiPath.CoreIpc.csproj" --output-directory "D:\Alt\coreipc\src\UiPath.CoreIpc\report" --verbose
