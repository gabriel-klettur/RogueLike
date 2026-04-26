# enforce-namespaces.ps1
# Scans all Unity test .cs files and reports (or fixes) namespace mismatches.
# Run from the workspace root: d:\Python\RogueLike
#
# Usage:
#   .\\.github\\skills\\unity-testing\\scripts\\enforce-namespaces.ps1            # dry-run (report only)
#   .\\.github\\skills\\unity-testing\\scripts\\enforce-namespaces.ps1 -Fix       # auto-correct mismatches

param(
    [switch]$Fix
)

$TestsRoot = "unity\Valkur\Assets\Tests"
$mismatches = 0
$fixed = 0

# Build folder → namespace map.
# Formula: replace path separator with '.' and prepend 'Valkur.Tests.'
function Get-ExpectedNamespace {
    param([string]$filePath)
    # Get the relative path from Tests root
    $rel = $filePath -replace [regex]::Escape((Resolve-Path $TestsRoot).Path + '\'), ''
    # Drop filename
    $dir = Split-Path $rel -Parent
    # Normalise separators
    $ns = $dir -replace '\\', '.'
    return "Valkur.Tests.$ns"
}

$files = Get-ChildItem -Path $TestsRoot -Recurse -Filter "*.cs" |
         Where-Object { $_.FullName -notmatch '\\meta$' }

foreach ($file in $files) {
    $expected = Get-ExpectedNamespace -filePath $file.FullName
    $content  = Get-Content $file.FullName -Raw

    # Find declared namespace(s) — match "namespace Valkur.Tests.Something"
    $matches = [regex]::Matches($content, 'namespace\s+(Valkur\.Tests[^\s{;]+)')
    if ($matches.Count -eq 0) {
        Write-Warning "No namespace found: $($file.FullName)"
        continue
    }

    foreach ($m in $matches) {
        $declared = $m.Groups[1].Value.Trim()
        if ($declared -ne $expected) {
            $mismatches++
            Write-Host "MISMATCH  $($file.Name)" -ForegroundColor Yellow
            Write-Host "  Expected : $expected"
            Write-Host "  Declared : $declared"
            if ($Fix) {
                $newContent = $content -replace [regex]::Escape("namespace $declared"), "namespace $expected"
                Set-Content -Path $file.FullName -Value $newContent -Encoding UTF8NoBOM
                $fixed++
                Write-Host "  FIXED" -ForegroundColor Green
            }
        }
    }
}

Write-Host ""
if ($Fix) {
    Write-Host "Done. Mismatches found: $mismatches | Fixed: $fixed" -ForegroundColor Cyan
} else {
    Write-Host "Dry-run complete. Mismatches: $mismatches  (run with -Fix to auto-correct)" -ForegroundColor Cyan
}
