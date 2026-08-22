$arguments = @(
    'run',
    '--no-restore',
    '--project',
    'GachaBot.Web.csproj',
    '--',
    '--seed-game-banners'
)

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
