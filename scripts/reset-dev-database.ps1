<#
.SYNOPSIS
  Drops and recreates the LOCAL development database, so it starts empty and fully migrated.

.DESCRIPTION
  Deletes everything in the database named by ConnectionStrings:Default in appsettings.json - jobs,
  stations, users, settings, all of it - then re-applies the migrations. On the next API start the
  seeder restores the default Settings and Shift, and (Development only) the dev admin account.

  Refuses to run unless the connection string points at LocalDB, so it cannot be aimed at a real
  server by accident.

.EXAMPLE
  ./scripts/reset-dev-database.ps1        # asks for confirmation
  ./scripts/reset-dev-database.ps1 -Yes   # no prompt
#>
param([switch]$Yes)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$settings = Get-Content (Join-Path $root 'src/ProductionScheduler.Api/appsettings.json') -Raw | ConvertFrom-Json
$connection = $settings.ConnectionStrings.Default

if ($connection -notmatch '\(localdb\)') {
    throw "Refusing to reset: the connection string is not LocalDB ($connection)."
}

if (-not $Yes) {
    $answer = Read-Host "This DELETES ALL DATA in the local database. Type YES to continue"
    if ($answer -ne 'YES') { Write-Host 'Cancelled.'; return }
}

Push-Location $root
try {
    $ef = @('--project', 'src/ProductionScheduler.Infrastructure', '--startup-project', 'src/ProductionScheduler.Api')

    dotnet ef database drop --force @ef
    if ($LASTEXITCODE -ne 0) { throw 'Dropping the database failed - is the API still running?' }

    dotnet ef database update @ef
    if ($LASTEXITCODE -ne 0) { throw 'Applying migrations failed.' }

    Write-Host ''
    Write-Host 'Database reset. Start the API to seed default settings, the default shift and the dev admin.'
}
finally {
    Pop-Location
}
