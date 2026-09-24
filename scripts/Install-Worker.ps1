#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory)][string]$PublishedDirectory,
    [Parameter(Mandatory)][string]$SettingsPath
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishedDirectory).Path
$settingsFile = (Resolve-Path -LiteralPath $SettingsPath).Path
$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
$mode = $settings.REMOTE_WAKE_MODE
if ($mode -notin @('agent','gateway')) { throw 'Mode must be agent or gateway.' }
if (!(Test-Path -LiteralPath (Join-Path $source 'RemoteWake.Worker.exe'))) { throw 'Publish a self-contained win-x64 worker first.' }
if (!$settings.REMOTE_WAKE_KEY -or $settings.REMOTE_WAKE_KEY -like 'REPLACE*') { throw 'Configure the worker key first.' }
$uri = [Uri]$settings.REMOTE_WAKE_API_URL
if (!$uri.IsAbsoluteUri -or $uri.Scheme -notin @('http','https')) { throw 'Invalid API URL.' }
if ($mode -eq 'agent') {
    $machineId = [Guid]::Empty
    if (![Guid]::TryParse($settings.REMOTE_WAKE_MACHINE_ID, [ref]$machineId)) { throw 'Invalid machine ID.' }
}
if ($mode -eq 'gateway' -and !$settings.REMOTE_WAKE_ALLOWED_BROADCASTS) { throw 'Configure allowed broadcasts.' }
$serviceName = if ($mode -eq 'agent') { 'RemoteWakeAgent' } else { 'RemoteWakeGateway' }
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw 'Service exists. Stop and update it explicitly; this installer does not overwrite an installation.' }
$destination = Join-Path $env:ProgramFiles "RemoteWake\$mode"
if (Test-Path -LiteralPath $destination) { throw "Destination already exists: $destination" }
New-Item -ItemType Directory -Path $destination | Out-Null
# Only administrators and SYSTEM may change the executable/configuration.
& icacls $destination /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Failed to protect service directory.' }
if ($mode -eq 'gateway') {
    & icacls $destination /grant '*S-1-5-19:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to grant gateway read access.' }
}
Get-ChildItem -LiteralPath $source | Copy-Item -Destination $destination -Recurse
Copy-Item -LiteralPath $settingsFile -Destination (Join-Path $destination 'worker.json')
$binary = '"' + (Join-Path $destination 'RemoteWake.Worker.exe') + '" --settings "' + (Join-Path $destination 'worker.json') + '"'
$parameters = @{Name=$serviceName; BinaryPathName=$binary; DisplayName="Remote Wake $mode"; StartupType='Automatic'}
if ($mode -eq 'gateway') {
    $parameters.Credential = [PSCredential]::new('NT AUTHORITY\LocalService', [Security.SecureString]::new())
}
# The agent runs as SYSTEM to schedule shutdown; the gateway uses LocalService.
New-Service @parameters | Out-Null
# Restart after crashes (10 s, 30 s, then every 60 s; counters reset after a day). A clean
# stop with an error, such as a revoked key, is not retried: it needs a new configuration.
& sc.exe failure $serviceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Failed to configure automatic service recovery.' }
Start-Service -Name $serviceName
Write-Host "Installed $serviceName. Protect or remove the original settings file: $settingsFile"
Write-Host 'The example configuration uses dry-run. Validate before enabling real power actions.'
