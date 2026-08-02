$projectDir = Split-Path -Parent $PSScriptRoot
Push-Location $projectDir
try {
    dotnet build Ponder.sln -c Release
    if ($LASTEXITCODE -eq 0) {
        dotnet script scripts/pack.csx
    }
} finally {
    Pop-Location
}
