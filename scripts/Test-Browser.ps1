# Runs the Playwright tests of tests/browser in Chromium (desktop and phone) against a
# disposable instance: database, API, web and the demo gateway. Nothing on the host is
# reused: own project name, free ports and an env file, all removed at the end.
param([switch]$SkipBuild, [switch]$KeepRunning)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$tests = Join-Path $repo 'tests/browser'
$project = 'rw-browser-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$version = (Get-Content -LiteralPath (Join-Path $tests 'package.json') -Raw | ConvertFrom-Json).devDependencies.'@playwright/test'
$setupToken = 'BROWSER-SETUP-CODE'
$owner = 'dona@e2e.test'

function FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}

$envFile = Join-Path ([IO.Path]::GetTempPath()) "$project.env"
@(
    'POSTGRES_PASSWORD=browser-tests-only'
    "SETUP_TOKEN=$setupToken"
    'ALLOW_REGISTRATION=true'
    'GATEWAY_KEY=browser-tests-gateway-key'
    "GATEWAY_OWNER_EMAIL=$owner"
    'AGENT_MASTER_KEY=browser-tests-agent-master-key'
    'ASPNETCORE_ENVIRONMENT=Development'
    "WEB_PORT=$(FreePort)"
    "API_PORT=$(FreePort)"
) | Set-Content -LiteralPath $envFile -Encoding ascii

$compose = @('compose', '--project-name', $project, '--env-file', $envFile,
    '-f', (Join-Path $repo 'compose.yaml'), '-f', (Join-Path $tests 'compose.browser.yaml'), '--profile', 'demo')
try {
    $up = $compose + @('up', '-d', '--wait') + $(if ($SkipBuild) { @() } else { @('--build') }) + @('database', 'api', 'web', 'demo-gateway')
    & docker @up
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the application.' }

    $run = @('run', '--rm', '--network', "${project}_default", '-v', "${tests}:/tests", '-w', '/tests',
        '-e', 'E2E_BASE_URL=http://web', '-e', "E2E_SETUP_TOKEN=$setupToken", '-e', "E2E_OWNER_EMAIL=$owner", '-e', 'CI', '-e', 'HOME=/tmp')
    # Keep test outputs owned by the current user on Linux ($IsLinux does not exist in Windows PowerShell).
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) {
        $run += @('-u', "$(id -u):$(id -g)")
    }
    $run += @("mcr.microsoft.com/playwright:v$version-noble", 'sh', '-c', 'npm ci --no-audit --no-fund && npx playwright test')
    & docker @run
    if ($LASTEXITCODE -ne 0) {
        & docker @($compose + @('logs', '--no-color', '--tail', '80', 'api'))
        throw 'Browser tests failed. See tests/browser/playwright-report.'
    }
    Write-Host 'All browser tests passed.'
}
finally {
    if ($KeepRunning) { Write-Host "Instance kept running: docker $($compose -join ' ') down --volumes" }
    else {
        & docker @($compose + @('down', '--volumes', '--remove-orphans')) *> $null
        Remove-Item -LiteralPath $envFile -ErrorAction SilentlyContinue
    }
}
