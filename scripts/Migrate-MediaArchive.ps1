param(
    [switch]$Apply,
    [switch]$DeleteLegacy,
    [string]$SourceKey
)

$arguments = @(
    'run',
    '--no-restore',
    '--project',
    'GachaBot.Web.csproj',
    '--',
    '--migrate-media'
)

if ($Apply) {
    $arguments += '--apply'
}

if ($DeleteLegacy) {
    if (-not $Apply) {
        throw '-DeleteLegacy requires -Apply.'
    }

    $arguments += '--delete-legacy'
}

if ($SourceKey) {
    $arguments += "--source-key=$SourceKey"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot 'src/GachaBot.Web'
Push-Location $webRoot
try {
    & dotnet @arguments
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

exit $exitCode
