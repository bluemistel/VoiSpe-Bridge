# AIVoiceBridge build & packaging script
#
# Usage:
#   .\build.ps1                          # Release build (default)
#   .\build.ps1 -Configuration Debug     # Debug build
#   .\build.ps1 -SkipZip                 # Generate dist\ without ZIP
#
param(
    [string]$Configuration = "Release",
    [switch]$SkipZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root        = $PSScriptRoot
$distApp     = Join-Path $root "dist\app"
$distPlugins = Join-Path $root "dist\plugins"

Write-Host "=== AIVoiceBridge build start ($Configuration) ===" -ForegroundColor Cyan

# ---- Init output folders ----------------------------------------
Write-Host "`n[0/6] Cleaning dist\..."
if (Test-Path $distApp)     { Remove-Item $distApp     -Recurse -Force }
if (Test-Path $distPlugins) { Remove-Item $distPlugins -Recurse -Force }
New-Item -ItemType Directory -Path $distApp     | Out-Null
New-Item -ItemType Directory -Path $distPlugins | Out-Null

# ---- [1] Main app -----------------------------------------------
Write-Host "`n[1/6] Publishing main app..."
dotnet publish "src\AIVoiceBridge.App\AIVoiceBridge.App.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $distApp `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "Main app build failed" }

# Create empty plugins folder (plugins are distributed separately)
New-Item -ItemType Directory -Path (Join-Path $distApp "plugins") -Force | Out-Null

# ---- [2] A.I.VOICE2 plugin -------------------------------------
Write-Host "`n[2/6] Building A.I.VOICE2 plugin..."
$aivoice2Proj = "src\Plugins\AIVoiceBridge.Plugin.AIVoice2\AIVoiceBridge.Plugin.AIVoice2.csproj"
dotnet build $aivoice2Proj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "A.I.VOICE2 plugin build failed (skipped)"
} else {
    $dst = Join-Path $distPlugins "AIVoice2"
    New-Item -ItemType Directory -Path $dst | Out-Null
    Copy-Item (Join-Path $root "plugins\AIVoiceBridge.Plugin.AIVoice2.dll") $dst
    $lines = @(
        "[A.I.VOICE2 Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to AIVoiceBridge.exe.",
        "",
        "No additional DLLs required.",
        "Start A.I.VOICE2 first, then connect from the app."
    )
    $lines | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [3] VOICEPEAK plugin ---------------------------------------
Write-Host "`n[3/6] Building VOICEPEAK plugin..."
$vpeakProj = "src\Plugins\AIVoiceBridge.Plugin.Voicepeak\AIVoiceBridge.Plugin.Voicepeak.csproj"
dotnet build $vpeakProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "VOICEPEAK plugin build failed (skipped)"
} else {
    $dst = Join-Path $distPlugins "VOICEPEAK"
    New-Item -ItemType Directory -Path $dst | Out-Null
    Copy-Item (Join-Path $root "plugins\AIVoiceBridge.Plugin.Voicepeak.dll") $dst
    $lines = @(
        "[VOICEPEAK Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to AIVoiceBridge.exe.",
        "",
        "No additional DLLs required.",
        "VOICEPEAK is auto-detected at: C:\Program Files\VOICEPEAK\voicepeak.exe"
    )
    $lines | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [4] CeVIO AI bridge + plugin -------------------------------
Write-Host "`n[4/6] Building CeVIO AI bridge (.NET 4.8)..."
$bridgeProj = "src\CeVIOBridge\AIVoiceBridge.CeVIOBridge.csproj"
$bridgeOk = $true
dotnet build $bridgeProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "CeVIO AI bridge build failed (skipped)"
    $bridgeOk = $false
}

Write-Host "`n[5/6] Building CeVIO AI plugin..."
$cevioProj = "src\Plugins\AIVoiceBridge.Plugin.CeVIOAI\AIVoiceBridge.Plugin.CeVIOAI.csproj"
dotnet build $cevioProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "CeVIO AI plugin build failed (skipped)"
} else {
    $dst = Join-Path $distPlugins "CeVIOAI"
    New-Item -ItemType Directory -Path $dst | Out-Null
    Copy-Item (Join-Path $root "plugins\AIVoiceBridge.Plugin.CeVIOAI.dll") $dst
    if ($bridgeOk) {
        Copy-Item (Join-Path $root "plugins\AIVoiceBridge.CeVIOBridge.exe")        $dst
        Copy-Item (Join-Path $root "plugins\AIVoiceBridge.CeVIOBridge.exe.config") $dst
    }
    $lines = @(
        "[CeVIO AI Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to AIVoiceBridge.exe.",
        "",
        "Additional file required:",
        "  CeVIO.Talk.RemoteService2.dll",
        "  -> Copy from CeVIO AI install folder (e.g. C:\Program Files\CeVIO\CeVIO AI\)",
        "",
        "PowerShell copy command:",
        '  Copy-Item "C:\Program Files\CeVIO\CeVIO AI\CeVIO.Talk.RemoteService2.dll" .\plugins\',
        "",
        "Notes:",
        "  - .NET Framework 4.8 required (included in Windows 10/11)",
        "  - CeVIO AI external API allows only 1 app at a time"
    )
    $lines | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [6] ZIP packages -------------------------------------------
if (-not $SkipZip) {
    Write-Host "`n[6/6] Creating ZIP packages..."

    $version    = "1.0.0"
    $appZip     = Join-Path $root "dist\AIVoiceBridge-v$version.zip"
    $pluginsZip = Join-Path $root "dist\AIVoiceBridge-Plugins-v$version.zip"

    if (Test-Path $appZip)     { Remove-Item $appZip }
    if (Test-Path $pluginsZip) { Remove-Item $pluginsZip }

    Compress-Archive -Path "$distApp\*"     -DestinationPath $appZip
    Compress-Archive -Path "$distPlugins\*" -DestinationPath $pluginsZip

    Write-Host "  -> $appZip" -ForegroundColor Green
    Write-Host "  -> $pluginsZip" -ForegroundColor Green
}

Write-Host "`n=== Build complete ===" -ForegroundColor Green
Write-Host "  App:     dist\app\AIVoiceBridge.exe"
Write-Host "  Plugins: dist\plugins\"
