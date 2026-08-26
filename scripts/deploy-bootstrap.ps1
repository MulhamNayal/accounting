# Runs ON the target Windows server after the deploy workflow downloads and extracts the
# bundle there. $PSScriptRoot is <bundleDir>\scripts, so the bundle root (containing app\)
# is one level up.
#
# Unlike a split API/SPA deployment this provisions ONE IIS Application. The API serves the
# built frontend out of its own wwwroot, so the whole product is one origin -- which is what
# frontend/src/api/client.ts assumes and what avoids ever configuring CORS in production.
#
# Self-provisioning: creates the app pool and the IIS Application under $env:IIS_SITE_NAME
# if they don't already exist, so no manual IIS Manager setup is needed beyond the site.
#
# Expects these environment variables, set by winrm_deploy.py before running this:
#   IIS_SITE_NAME                    the parent IIS site (must already exist)
#   DB_CONNECTION_STRING             application role -- cannot run DDL, cannot alter the ledger
#   DB_MIGRATION_CONNECTION_STRING   owner role -- used ONLY to apply migrations, never at runtime
#   JWT_SIGNING_KEY                  at least 32 bytes
#   SEED_DEMO_PASSWORD               password for the seeded demo account

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$siteName   = $env:IIS_SITE_NAME
$appName    = "accounting"
$poolName   = "accounting"
$root       = "C:\AspNetCoreWebApps"
$appRoot    = Join-Path $root $appName
$bundleRoot = Split-Path -Parent $PSScriptRoot

foreach ($required in @('IIS_SITE_NAME', 'DB_CONNECTION_STRING', 'DB_MIGRATION_CONNECTION_STRING', 'JWT_SIGNING_KEY', 'SEED_DEMO_PASSWORD')) {
    if (-not (Get-Item "Env:$required" -ErrorAction SilentlyContinue).Value) {
        # SEED_DEMO_PASSWORD is required rather than optional on purpose. Without it the app
        # would fall back to the value committed in appsettings.Development.json, which is
        # published in a public repository -- so a forgotten secret would silently become the
        # sign-in credential of this instance.
        throw "$required is not set. Add it to the repository secrets and re-run the deploy."
    }
}

if (-not (Test-Path "IIS:\Sites\$siteName")) {
    throw "IIS site '$siteName' does not exist on this server. This script provisions an Application under an existing site, not the site itself."
}

Write-Host "=== ensure app pool exists ==="
if (-not (Test-Path "IIS:\AppPools\$poolName")) {
    Write-Host "Creating app pool '$poolName'..."
    New-WebAppPool -Name $poolName | Out-Null
    # ASP.NET Core runs via the ASP.NET Core Module, not the classic CLR pipeline, so the
    # pool needs "No Managed Code".
    Set-ItemProperty "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ""
} else {
    Write-Host "App pool '$poolName' already exists."
}

Write-Host "=== ensure IIS Application exists ==="
New-Item -ItemType Directory -Force -Path $appRoot | Out-Null
if (-not (Test-Path "IIS:\Sites\$siteName\$appName")) {
    Write-Host "Creating IIS Application '$appName' at '$appRoot'..."
    New-WebApplication -Site $siteName -Name $appName -PhysicalPath $appRoot -ApplicationPool $poolName | Out-Null
} else {
    Write-Host "IIS Application '$appName' already exists."
}

Write-Host "=== apply database migrations ==="
# Deliberately BEFORE the pool stops and the files are overwritten. Migrations are additive,
# so the currently-running build tolerates the new schema; if this fails the deploy aborts
# with the old build still serving, rather than shipping code that asks for columns which do
# not exist. Nothing applies migrations at startup.
#
# This uses the OWNER connection. The application role has no DDL rights and, from Layer 1
# onward, cannot UPDATE or DELETE ledger rows -- that is the guarantee, so it must not be the
# role that runs schema changes.
$migrationScript = Join-Path $PSScriptRoot "sql\migrate-accounting.sql"
if (-not (Test-Path $migrationScript)) {
    throw "Bundle is missing scripts\sql\migrate-accounting.sql -- the deploy workflow generates it from the Migrations folder, so an absent file means that step failed or was skipped."
}
& (Join-Path $PSScriptRoot "apply-migration.ps1") `
    -ScriptPath $migrationScript `
    -ConnectionString $env:DB_MIGRATION_CONNECTION_STRING

Write-Host "=== stop app pool (before overwriting the files it is serving) ==="
if ((Get-WebAppPoolState -Name $poolName).Value -eq 'Started') {
    Stop-WebAppPool -Name $poolName
    while ((Get-WebAppPoolState -Name $poolName).Value -ne 'Stopped') { Start-Sleep -Milliseconds 500 }
}
Write-Host "Pool '$poolName' stopped."

Write-Host "=== copy application files ==="
# /MIR mirrors, so files removed from a build are removed from the server too. wwwroot is
# inside the published output and carries the frontend, so one copy covers both.
robocopy "$bundleRoot\app" $appRoot /MIR /NFL /NDL /NJH /NJS /R:3 /W:5
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "=== configure environment (IIS app pool level, never written to disk as a file) ==="
function Set-AppEnvVar([string]$name, [string]$value) {
    $filter = "/system.webServer/aspNetCore/environmentVariables"
    $psPath = "IIS:\Sites\$siteName\$appName"
    $existing = Get-WebConfigurationProperty -Filter $filter -PSPath $psPath -Name Collection -ErrorAction SilentlyContinue |
        Where-Object { $_.name -eq $name }
    if ($existing) {
        Set-WebConfigurationProperty -Filter "$filter/add[@name='$name']" -PSPath $psPath -Name "value" -Value $value
    } else {
        Add-WebConfigurationProperty -Filter $filter -PSPath $psPath -Name Collection -Value @{ name = $name; value = $value }
    }
    Write-Host "Set '$name'."
}

# Only the low-privilege application connection is given to the running app. The owner
# connection string is used above and deliberately not persisted anywhere on the box.
Set-AppEnvVar "ConnectionStrings__AccountingDatabase" $env:DB_CONNECTION_STRING
Set-AppEnvVar "Jwt__SigningKey" $env:JWT_SIGNING_KEY
Set-AppEnvVar "Seed__DemoPassword" $env:SEED_DEMO_PASSWORD
# This box is a development server, not a production instance. Development is what it
# actually is, and it is what exposes the OpenAPI document for poking at the API.
Set-AppEnvVar "ASPNETCORE_ENVIRONMENT" "Development"

Write-Host "=== start app pool ==="
Start-WebAppPool -Name $poolName
Write-Host "Pool '$poolName' started."

Write-Host "Deploy complete."
# robocopy's own success exit code is 1 (0 means "nothing needed copying"), and nothing after
# the copy otherwise touches $LASTEXITCODE -- without this the script inherits robocopy's
# leftover value, which a caller checking $LASTEXITCODE -ne 0 misreads as a failure.
exit 0
