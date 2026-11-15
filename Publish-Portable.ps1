$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "WinSetup.csproj"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host ".NET SDK не найден. Установи .NET 10 SDK на компьютере сборки и повтори запуск." -ForegroundColor Red
    exit 1
}
Write-Host "Проверка проекта..." -ForegroundColor Cyan
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore завершился с ошибкой $LASTEXITCODE" }
Write-Host "Сборка portable EXE (Windows x64)..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой $LASTEXITCODE" }
$output = Join-Path $PSScriptRoot "bin\Release\net10.0-windows\win-x64\publish\WinSetup.exe"
if (-not (Test-Path $output)) { throw "EXE не найден по ожидаемому пути: $output" }
Write-Host "Готово: $output" -ForegroundColor Green
