param(
    [switch]$Apply
)

$arguments = @(
    'run',
    '--no-restore',
    '--project',
    'GachaBot.Web.csproj',
    '--',
    '--collect-media-garbage'
)

if ($Apply) {
    $arguments += '--apply'
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
