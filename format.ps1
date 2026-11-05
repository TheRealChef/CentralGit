# Formats the repository: CSharpier (C#), XamlStyler (XAML), Prettier (JSON, YAML, Markdown).
#   .\format.ps1          rewrite files
#   .\format.ps1 -Check   only report, exit 1 if anything is unformatted (what CI runs)
param([switch]$Check)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet tool restore | Out-Null
if (-not (Test-Path node_modules)) { npm ci --no-fund --no-audit | Out-Null }

$xaml = (git ls-files '*.xaml') -join ','
$failed = $false

if ($Check) {
    dotnet csharpier check .;                                              if ($LASTEXITCODE) { $failed = $true }
    dotnet xstyler --passive --config Settings.XamlStyler --file $xaml;   if ($LASTEXITCODE) { $failed = $true }
    npx prettier --check .;                                                if ($LASTEXITCODE) { $failed = $true }
} else {
    dotnet csharpier format .;                                             if ($LASTEXITCODE) { $failed = $true }
    dotnet xstyler --config Settings.XamlStyler --file $xaml | Out-Null;  if ($LASTEXITCODE) { $failed = $true }
    npx prettier --write --log-level warn .;                               if ($LASTEXITCODE) { $failed = $true }
}

if ($failed) { exit 1 }
