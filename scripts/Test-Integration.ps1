param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$prefix = 'rw-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$containers = @()
function DockerRun([string[]]$Arguments) {
    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker failed: $($Arguments[0])" }
    return $output
}
function Check($Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    Write-Host "PASS: $Message"
}
function Request([string]$Method, [string]$Path, $Body = $null, $Headers = @{}, [int]$Expected = 200) {
    $parameters = @{ Method=$Method; Uri="$script:base$Path"; Headers=$Headers; UseBasicParsing=$true; TimeoutSec=35 }
    if ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json; $parameters.ContentType='application/json' }
    try { $response = Invoke-WebRequest @parameters; $status = [int]$response.StatusCode }
    catch {
        if (!$_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
        if ($status -ne $Expected) { throw }
        return $null
    }
    if ($status -ne $Expected) { throw "$Method $Path returned $status, expected $Expected" }
    if ($response.Content) { $parsed = $response.Content | ConvertFrom-Json; return $parsed }
}
function WaitOnline([string]$Property) {
    for ($i=0; $i -lt 30; $i++) {
        $items = @(Request GET /api/machines/ -Headers $script:auth)
        if ($items[0].$Property) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "$Property did not become true"
}
try {
    Push-Location $repo
    # Explicit tags: independent of the Compose project name and of the local .env.
    if (!$SkipBuild) {
        DockerRun @('build','-q','-f','src/api/Dockerfile','-t','remote-wake-test-api','.') | Out-Host
        DockerRun @('build','-q','-f','src/worker/Dockerfile','-t','remote-wake-test-worker','.') | Out-Host
    }
    DockerRun @('network','create',$prefix) | Out-Null
    $db = "$prefix-db"; $api = "$prefix-api"
    $containers += $db
    DockerRun @('run','-d','--name',$db,'--network',$prefix,'-e','POSTGRES_PASSWORD=test-only','-e','POSTGRES_DB=test','postgres:18-alpine') | Out-Null
    for ($i=0; $i -lt 40; $i++) {
        & docker exec $db pg_isready -U postgres -d test *> $null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Milliseconds 500
    }
    $gatewayKey = [Guid]::NewGuid().ToString('N')
    $containers += $api
    DockerRun @('run','-d','--name',$api,'--network',$prefix,'-p','127.0.0.1::8080',
        '-e',"ConnectionStrings__Database=Host=$db;Database=test;Username=postgres;Password=test-only",
        '-e','Jwt__Key=integration-only-key-at-least-thirty-two-characters',
        '-e','Jwt__Issuer=RemoteWake','-e','Jwt__Audience=RemoteWake.Web',
        '-e',"Gateway__Key=$gatewayKey",'-e','Gateway__OwnerEmail=owner@example.test',
        '-e','Agent__MasterKey=integration-only-agent-master-key','-e','Registration__Open=true','remote-wake-test-api') | Out-Null
    $port = (DockerRun @('port',$api,'8080/tcp')).Trim().Split(':')[-1]
    $script:base = "http://127.0.0.1:$port"
    for ($i=0; $i -lt 50; $i++) {
        try { Invoke-WebRequest "$script:base/health" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 }
    }
    $owner = Request POST /api/auth/register @{name='Test owner';email='owner@example.test';password='Test-only-pass123!'}
    $other = Request POST /api/auth/register @{name='Other owner';email='other@example.test';password='Test-only-pass123!'}
    $script:auth = @{Authorization="Bearer $($owner.token)"}
    $otherAuth = @{Authorization="Bearer $($other.token)"}
    $inputMachine = @{name='Test PC';macAddress='02:00:00:00:00:01';hostname='test';broadcastAddress='127.0.0.1';wolPort=9;wakeMethod='TailscaleGateway'}
    $machine = Request POST /api/machines/ $inputMachine $auth 201
    $id = $machine.id
    $inputMachine.name = 'Edited PC'
    $edited = Request PUT "/api/machines/$id" $inputMachine $auth
    Check ($edited.name -eq 'Edited PC') 'Machine editing'
    Request PUT "/api/machines/$id" $inputMachine $otherAuth 404 | Out-Null
    Request POST "/api/machines/$id/agent-key" -Headers $otherAuth -Expected 404 | Out-Null
    Check (@(Request GET /api/activity -Headers $otherAuth).Count -eq 0) 'Owner isolation'
    $key = (Request POST "/api/machines/$id/agent-key" -Headers $auth).key
    $gateway = "$prefix-gateway"; $agent = "$prefix-agent"
    $containers += $gateway
    DockerRun @('run','-d','--name',$gateway,'--network',$prefix,'-e','REMOTE_WAKE_MODE=gateway',
        '-e',"REMOTE_WAKE_API_URL=http://${api}:8080/",'-e',"REMOTE_WAKE_KEY=$gatewayKey",
        '-e','REMOTE_WAKE_ALLOWED_BROADCASTS=127.0.0.1','remote-wake-test-worker') | Out-Null
    $containers += $agent
    DockerRun @('run','-d','--name',$agent,'--network',$prefix,'-e','REMOTE_WAKE_MODE=agent',
        '-e',"REMOTE_WAKE_API_URL=http://${api}:8080/",'-e',"REMOTE_WAKE_KEY=$key",
        '-e',"REMOTE_WAKE_MACHINE_ID=$id",'-e','REMOTE_WAKE_DRY_RUN=true','remote-wake-test-worker') | Out-Null
    WaitOnline gatewayOnline
    WaitOnline agentOnline
    Check ((Request POST "/api/machines/$id/wake" -Headers $auth).succeeded) 'Gateway wake (loopback only)'
    foreach ($action in @('restart','shutdown')) {
        $result = Request POST "/api/machines/$id/actions" @{action=$action} $auth
        Check ($result.succeeded -and $result.message -match 'Simula') "$action dry run (no power action)"
    }
    Request POST "/api/machines/$id/actions" @{action='arbitrary-script'} $auth 400 | Out-Null
    $inputMachine.broadcastAddress='192.0.2.255'
    Request PUT "/api/machines/$id" $inputMachine $auth | Out-Null
    $denied = Request POST "/api/machines/$id/wake" -Headers $auth -Expected 400
    Check (@(Request GET /api/activity -Headers $auth).Count -eq 4) 'Persistent wake/action history including failures'
    Check (@(Request GET /api/activity -Headers $otherAuth).Count -eq 0) 'History is private to owner'
    Request DELETE "/api/machines/$id/agent-key" -Headers $auth -Expected 204 | Out-Null
    Request GET "/api/agent/$id/poll" -Headers @{'X-Remote-Wake-Key'=$key} -Expected 401 | Out-Null
    $newKey = (Request POST "/api/machines/$id/agent-key" -Headers $auth).key
    Check ($key -ne $newKey) 'Revocation invalidates old key and generates a new key'
    Request GET /api/gateway/poll -Headers @{'X-Remote-Wake-Key'='invalid'} -Expected 401 | Out-Null
    # Exercise additive upgrade against the previous schema in this disposable database only.
    DockerRun @('stop',$api) | Out-Null
    # Without the migration history the API treats the database as created by version 0.1 (EnsureCreated).
    'ALTER TABLE "Machines" DROP COLUMN "AgentKeyVersion"; ALTER TABLE "WakeAttempts" DROP COLUMN "Action"; DROP TABLE "__EFMigrationsHistory";' |
        & docker exec -i $db psql -v ON_ERROR_STOP=1 -U postgres -d test | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare legacy schema fixture.' }
    DockerRun @('start',$api) | Out-Null
    $port = (DockerRun @('port',$api,'8080/tcp')).Trim().Split(':')[-1]
    $script:base = "http://127.0.0.1:$port"
    for ($i=0; $i -lt 50; $i++) {
        try { $history = @(Request GET /api/activity -Headers $auth); break } catch { Start-Sleep -Milliseconds 500 }
    }
    Check ($history.Count -eq 4) 'Legacy schema upgrade preserves records'
    Check (@(Request GET /api/machines/ -Headers $auth)[0].name -eq 'Edited PC') 'Restart preserves machines'
    Write-Host 'All integration checks passed.'
}
finally {
    foreach ($container in $containers) { & docker rm -f -v $container *> $null }
    & docker network rm $prefix *> $null
    Pop-Location
}
