# Applies an EF-generated PostgreSQL migration script.
#
# Runs psql rather than talking to Postgres from .NET: the generated script is a sequence of
# DO $$ ... $$ blocks and dollar-quoted function bodies, and splitting that correctly is
# psql's job, not ours. psql.exe ships with the PostgreSQL server install that is already on
# the target box, so this adds no dependency.
#
# The connection string is Npgsql format (the same value the app uses), and must be the
# OWNER role -- migrations run DDL, which the application role deliberately cannot.

param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [Parameter(Mandatory = $true)][string]$ConnectionString
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ScriptPath)) {
    throw "Migration script '$ScriptPath' does not exist."
}

$psql = Get-Command psql.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $psql) {
    # Newest major version first, so a box with several installs uses the one most likely to
    # match the server.
    $psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $psql) {
    throw "psql.exe was not found on PATH or under C:\Program Files\PostgreSQL. Install the PostgreSQL client tools on this server."
}

# Npgsql keywords -> the libpq environment variables psql reads. Passing the password via
# PGPASSWORD keeps it off the command line, where it would be visible to any user able to
# list processes.
$map = @{
    'host' = 'PGHOST'; 'server' = 'PGHOST'
    'port' = 'PGPORT'
    'database' = 'PGDATABASE'; 'db' = 'PGDATABASE'
    'username' = 'PGUSER'; 'user id' = 'PGUSER'; 'userid' = 'PGUSER'; 'user' = 'PGUSER'
    'password' = 'PGPASSWORD'; 'pwd' = 'PGPASSWORD'
}

$applied = @{}
foreach ($pair in $ConnectionString.Split(';')) {
    if (-not $pair.Trim()) { continue }
    $eq = $pair.IndexOf('=')
    if ($eq -lt 1) { continue }

    $key = $pair.Substring(0, $eq).Trim().ToLowerInvariant()
    $value = $pair.Substring($eq + 1).Trim()

    if ($map.ContainsKey($key)) {
        $name = $map[$key]
        Set-Item -Path "Env:$name" -Value $value
        $applied[$name] = $value
    }
}

foreach ($required in @('PGHOST', 'PGDATABASE', 'PGUSER')) {
    if (-not $applied.ContainsKey($required)) {
        throw "The connection string is missing the value needed for $required."
    }
}

Write-Host "Applying $(Split-Path -Leaf $ScriptPath) to $($applied['PGDATABASE']) on $($applied['PGHOST']) as $($applied['PGUSER'])..."

# ON_ERROR_STOP is what makes this abort rather than plough on leaving the schema half
# migrated. --single-transaction means a failure rolls the whole thing back.
& $psql --no-psqlrc --quiet -v ON_ERROR_STOP=1 --single-transaction -f $ScriptPath
$code = $LASTEXITCODE

Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue

if ($code -ne 0) {
    throw "psql exited with code $code -- the migration was rolled back and nothing was changed."
}

Write-Host "Migrations applied."
