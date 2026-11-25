param(
    [switch]$Portable
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\GameStateAssistant.csproj'

if ($Portable) {
    $publishDir = Join-Path $root 'src\bin\Release\net10.0-windows\win-x64\publish_portable'
    Write-Host "Building GameStateAssistant v4.0 Portable (Self-Contained Single-File)..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

    $outExe = Join-Path $root 'GameStateAssistant_Portable_v4.0.exe'
    Copy-Item (Join-Path $publishDir 'GameStateAssistant.exe') $outExe -Force
    $sizeMb = [math]::Round((Get-Item $outExe).Length / 1MB, 1)
    Write-Host "Built Portable successfully ($sizeMb MB):" -ForegroundColor Green
    Write-Host "  -> $outExe"
}
else {
    $publishDir = Join-Path $root 'src\bin\Release\net10.0-windows\win-x64\publish'
    Write-Host "Building GameStateAssistant v4.0 (.NET 10 WPF Dual-Platform Intel/AMD Single-File)..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

    $exe = Join-Path $publishDir 'GameStateAssistant.exe'
    Copy-Item $exe (Join-Path $root 'GameStateAssistant.exe') -Force
    Copy-Item $exe (Join-Path $root '游戏状态助手.exe') -Force

    Write-Host "Built successfully:" -ForegroundColor Green
    Write-Host "  -> $(Join-Path $root 'GameStateAssistant.exe')"
    Write-Host "  -> $(Join-Path $root '游戏状态助手.exe')"
}
