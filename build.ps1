# VoiSpe-Bridge build & packaging script
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

Write-Host "=== VoiSpe-Bridge build start ($Configuration) ===" -ForegroundColor Cyan

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
    Copy-Item (Join-Path $root "plugins\VoiSpeBridge.Plugin.AIVoice2.dll") $dst
    @(
        "[A.I.VOICE2 Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to VoiSpeBridge.exe.",
        "",
        "No additional DLLs required.",
        "Start A.I.VOICE2 first, then connect from the app."
    ) | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
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
    Copy-Item (Join-Path $root "plugins\VoiSpeBridge.Plugin.Voicepeak.dll") $dst
    @(
        "[VOICEPEAK Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to VoiSpeBridge.exe.",
        "",
        "No additional DLLs required.",
        "VOICEPEAK is auto-detected at: C:\Program Files\VOICEPEAK\voicepeak.exe"
    ) | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [4] A.I.VOICE v1 bridge + plugin --------------------------
Write-Host "`n[4/8] Building A.I.VOICE v1 bridge (.NET 4.8)..."
$av1BridgeProj = "src\AIVoice1Bridge\AIVoiceBridge.AIVoice1Bridge.csproj"
$av1BridgeOk = $true
dotnet build $av1BridgeProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "A.I.VOICE v1 bridge build failed (skipped)"
    $av1BridgeOk = $false
}

Write-Host "`n[5/8] Building A.I.VOICE v1 plugin..."
$av1Proj = "src\Plugins\AIVoiceBridge.Plugin.AIVoice\AIVoiceBridge.Plugin.AIVoice.csproj"
dotnet build $av1Proj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "A.I.VOICE v1 plugin build failed (skipped)"
} else {
    $dst = Join-Path $distPlugins "AIVoice"
    New-Item -ItemType Directory -Path $dst | Out-Null
    Copy-Item (Join-Path $root "plugins\VoiSpeBridge.Plugin.AIVoice.dll") $dst
    Copy-Item (Join-Path $root "plugins\AI.Talk.Editor.Api.dll")          $dst -ErrorAction SilentlyContinue
    if ($av1BridgeOk) {
        Copy-Item (Join-Path $root "plugins\VoiSpeBridge.AIVoice1Bridge.exe")        $dst -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $root "plugins\VoiSpeBridge.AIVoice1Bridge.exe.config") $dst -ErrorAction SilentlyContinue
    }
    @(
        "[A.I.VOICE (v1) Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to VoiSpeBridge.exe.",
        "",
        "Requirements:",
        "  - A.I.VOICE v1 must be installed",
        "  - .NET Framework 4.8 required (included in Windows 10/11)"
    ) | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [6] CeVIO AI bridge + plugin -------------------------------
Write-Host "`n[6/8] Building CeVIO AI bridge (.NET 4.8)..."
$bridgeProj = "src\CeVIOBridge\AIVoiceBridge.CeVIOBridge.csproj"
$bridgeOk = $true
dotnet build $bridgeProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "CeVIO AI bridge build failed (skipped)"
    $bridgeOk = $false
}

Write-Host "`n[7/8] Building CeVIO AI plugin..."
$cevioProj = "src\Plugins\AIVoiceBridge.Plugin.CeVIOAI\AIVoiceBridge.Plugin.CeVIOAI.csproj"
dotnet build $cevioProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "CeVIO AI plugin build failed (skipped)"
} else {
    $dst = Join-Path $distPlugins "CeVIOAI"
    New-Item -ItemType Directory -Path $dst | Out-Null
    Copy-Item (Join-Path $root "plugins\VoiSpeBridge.Plugin.CeVIOAI.dll") $dst
    if ($bridgeOk) {
        Copy-Item (Join-Path $root "plugins\VoiSpeBridge.CeVIOBridge.exe")        $dst -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $root "plugins\VoiSpeBridge.CeVIOBridge.exe.config") $dst -ErrorAction SilentlyContinue
    }
    @(
        "[CeVIO AI Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to VoiSpeBridge.exe.",
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
    ) | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [8] VoisonaTalk plugin ------------------------------------
Write-Host "`n[8/9] Building VoisonaTalk plugin..."
$voisonaProj = "src\Plugins\AIVoiceBridge.Plugin.VoisonaTalk\AIVoiceBridge.Plugin.VoisonaTalk.csproj"
dotnet build $voisonaProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Warning "VoisonaTalk plugin build failed (skipped)"
} else {
    $dst = Join-Path $distPlugins "VoisonaTalk"
    New-Item -ItemType Directory -Path $dst | Out-Null
    Copy-Item (Join-Path $root "plugins\VoiSpeBridge.Plugin.VoisonaTalk.dll") $dst
    @(
        "[VoisonaTalk Plugin Setup]",
        "Place the contents of this folder into the plugins\ folder next to VoiSpeBridge.exe.",
        "",
        "Requirements:",
        "  - VoisonaTalk が起動していること",
        "  - VoisonaTalk の 設定 > API タブで REST API を有効化していること",
        "  - アプリ内「音声合成設定 > 接続設定」でメールアドレスと API パスワードを入力し「再接続」を押すこと",
        "",
        "Default port: 32766 (VoisonaTalk のデフォルト値)"
    ) | Set-Content -Encoding UTF8 (Join-Path $dst "README_SETUP.txt")
}

# ---- [9] ZIP packages -------------------------------------------
if (-not $SkipZip) {
    Write-Host "`n[9/9] Creating ZIP packages..."

    $version    = "1.0.0"
    $appZip     = Join-Path $root "dist\VoiSpeBridge-v$version.zip"
    $pluginsZip = Join-Path $root "dist\VoiSpeBridge-Plugins-v$version.zip"

    if (Test-Path $appZip)     { Remove-Item $appZip }
    if (Test-Path $pluginsZip) { Remove-Item $pluginsZip }

    Compress-Archive -Path "$distApp\*"     -DestinationPath $appZip
    Compress-Archive -Path "$distPlugins\*" -DestinationPath $pluginsZip

    Write-Host "  -> $appZip" -ForegroundColor Green
    Write-Host "  -> $pluginsZip" -ForegroundColor Green
}

Write-Host "`n=== Build complete ===" -ForegroundColor Green
Write-Host "  App:     dist\app\VoiSpeBridge.exe"
Write-Host "  Plugins: dist\plugins\"
