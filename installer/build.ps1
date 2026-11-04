<#
.SYNOPSIS
  Builds the CentralGit installer: Velopack package + the setup wizard around it.

.DESCRIPTION
  Produces artifacts\setup\CentralGit-Setup-<version>.exe, the one file to hand out. It's an
  Inno Setup wizard (installer\CentralGit.iss) wrapping Velopack's Setup: a fresh install asks
  where to install; an existing install shows "installed X / new Y" with Update or Cancel and
  updates in place. Velopack does the actual install, uninstall and in-app updates.
  Per-user, no admin. Settings live in %APPDATA%\CentralGit and survive installs and uninstalls.

  Also writes the release feed (full + delta packages, releases.win.json) used by in-app
  updates. Keep artifacts\releases between builds so deltas can be computed.

.EXAMPLE
  .\installer\build.ps1 -Version 0.2.0
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$InnoVersion = '6.7.3'

# Inno Setup compiler from NuGet (Tools.InnoSetup), pinned and cached under artifacts\tools.
function Get-InnoCompiler([string]$ToolsDir) {
    $dir = Join-Path $ToolsDir "innosetup-$InnoVersion"
    $iscc = Join-Path $dir 'tools\ISCC.exe'
    if (-not (Test-Path $iscc)) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        $zip = Join-Path $ToolsDir "innosetup-$InnoVersion.zip"
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$InnoVersion/tools.innosetup.$InnoVersion.nupkg" -OutFile $zip -UseBasicParsing
        Expand-Archive $zip $dir -Force
        Remove-Item $zip
    }
    $iscc
}

# The wizard's side panel and header images: the app icon on a transparent background.
function Write-WizardImages([string]$Icon, [string]$OutDir) {
    Add-Type -AssemblyName System.Drawing
    $source = (New-Object System.Drawing.Icon($Icon, 256, 256)).ToBitmap()
    foreach ($spec in @(@{ Name = 'wizard-image.bmp'; W = 328; H = 628; Size = 168; Y = 180 },
                        @{ Name = 'wizard-small.bmp'; W = 110; H = 110; Size = 96; Y = 7 })) {
        $bmp = New-Object System.Drawing.Bitmap $spec.W, $spec.H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($source, [int](($spec.W - $spec.Size) / 2), $spec.Y, $spec.Size, $spec.Size)
        $g.Dispose()
        $bmp.Save((Join-Path $OutDir $spec.Name), [System.Drawing.Imaging.ImageFormat]::Bmp)
        $bmp.Dispose()
    }
    $source.Dispose()
}
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\publish\$Runtime"
$releases = Join-Path $root 'artifacts\releases'
$channel = if ($Runtime -eq 'win-arm64') { 'win-arm64' } else { 'win' }

Push-Location $root
try {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

    Write-Host "Publishing CentralGit $Version ($Runtime)..." -ForegroundColor Cyan
    dotnet publish src\CentralGit.App\CentralGit.App.csproj -c Release -r $Runtime --self-contained `
        "-p:Version=$Version" -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    # The app's XAML resources; without them the installed app crashes at startup (0xC000027B).
    if (-not (Test-Path (Join-Path $publish 'CentralGit.pri'))) { throw 'CentralGit.pri missing from publish output (EnableMsixTooling?)' }

    Write-Host "Packing installer..." -ForegroundColor Cyan
    dotnet tool restore | Out-Null
    dotnet vpk pack `
        --packId CentralGit `
        --packVersion $Version `
        --packTitle CentralGit `
        --packAuthors 'Hrvoje Kuhar' `
        --packDir $publish `
        --mainExe CentralGit.exe `
        --icon src\CentralGit.App\Assets\CentralGit.ico `
        --runtime $Runtime `
        --channel $channel `
        --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }

    $velopackSetup = Join-Path $releases "CentralGit-$channel-Setup.exe"
    if (-not (Test-Path $velopackSetup)) { throw "Velopack Setup not found: $velopackSetup" }

    Write-Host "Building setup wizard..." -ForegroundColor Cyan
    $work = Join-Path $root 'artifacts\setup-work'
    New-Item -ItemType Directory -Force $work | Out-Null
    $icon = Join-Path $root 'src\CentralGit.App\Assets\CentralGit.ico'
    Write-WizardImages -Icon $icon -OutDir $work
    $iscc = Get-InnoCompiler (Join-Path $root 'artifacts\tools')
    $setupDir = Join-Path $root 'artifacts\setup'
    & $iscc /Q "/DAppVersion=$Version" "/DVelopackSetup=$velopackSetup" "/DImageDir=$work" "/DIcon=$icon" "/DOutputDir=$setupDir" `
        (Join-Path $PSScriptRoot 'CentralGit.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)" }

    Write-Host "Setup to hand out: $(Join-Path $setupDir "CentralGit-Setup-$Version.exe")" -ForegroundColor Green
}
finally {
    Pop-Location
}
