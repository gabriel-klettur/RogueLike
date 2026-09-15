# enforce-namespaces.ps1
# Reports (or fixes) test files whose namespace is not their path.
# Run from the workspace root: d:\Python\RogueLike
#
# Rule: namespace = "Valkur.Tests." + the folders below Assets/Tests joined with '.'
#   Assets/Tests/EditMode/Gameplay/Combat/Death/X.cs -> Valkur.Tests.EditMode.Gameplay.Combat.Death
#   Assets/Tests/Support/X.cs                         -> Valkur.Tests.Support
#
# WHERE a file belongs (root, feature folder, aliases) is not this script's job: it is enforced
# by TestLayoutConventionTests (EditMode/Project/Code), which also runs this same namespace rule.
#
# Usage:
#   .\.github\skills\unity-testing\scripts\enforce-namespaces.ps1        # report only
#   .\.github\skills\unity-testing\scripts\enforce-namespaces.ps1 -Fix   # rewrite mismatches

param(
    [switch]$Fix
)

$TestsRoot = (Resolve-Path "unity\Valkur\Assets\Tests").Path
$mismatches = 0
$fixed = 0

function Get-ExpectedNamespace([string]$fullPath) {
    $rel = $fullPath.Substring($TestsRoot.Length + 1)
    $dir = Split-Path $rel -Parent
    return "Valkur.Tests." + ($dir -replace '\\', '.')
}

$files = Get-ChildItem -Path $TestsRoot -Recurse -Filter "*.cs" | Where-Object { $_.Name -ne 'AssemblyInfo.cs' }

foreach ($file in $files) {
    $expected = Get-ExpectedNamespace $file.FullName
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $content = $content.Substring(1) }

    $m = [regex]::Match($content, '(?m)^\s*namespace\s+([\w\.]+)')
    if (-not $m.Success) {
        Write-Warning "No namespace: $($file.FullName)"
        continue
    }
    $declared = $m.Groups[1].Value
    if ($declared -eq $expected) { continue }

    $mismatches++
    Write-Host "MISMATCH  $($file.FullName.Substring($TestsRoot.Length + 1))" -ForegroundColor Yellow
    Write-Host "  expected: $expected"
    Write-Host "  declared: $declared"
    if ($Fix) {
        $newContent = $content.Substring(0, $m.Groups[1].Index) + $expected + $content.Substring($m.Groups[1].Index + $m.Groups[1].Length)
        $encoding = New-Object System.Text.UTF8Encoding($hasBom)
        [System.IO.File]::WriteAllText($file.FullName, $newContent, $encoding)
        $fixed++
        Write-Host "  fixed" -ForegroundColor Green
    }
}

Write-Host ""
if ($Fix) {
    Write-Host "Done. Mismatches: $mismatches | Fixed: $fixed" -ForegroundColor Cyan
} else {
    Write-Host "Report only. Mismatches: $mismatches  (run with -Fix to rewrite)" -ForegroundColor Cyan
}
