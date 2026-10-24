Set-StrictMode -Version Latest

function Get-NevmaRepositoryRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function New-NevmaSecret {
    param([int]$Length = 32)

    $bytes = New-Object byte[] $Length
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return ([System.BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}

function Initialize-NevmaEnvironment {
    $root = Get-NevmaRepositoryRoot
    $path = Join-Path $root '.env'

    if (-not (Test-Path -LiteralPath $path)) {
        $lines = @(
            "POSTGRES_ADMIN_PASSWORD=$(New-NevmaSecret)"
            "NEVMA_IDENTITY_DB_PASSWORD=$(New-NevmaSecret)"
            "NEVMA_PLANNING_DB_PASSWORD=$(New-NevmaSecret)"
            "NEVMA_MESSAGING_DB_PASSWORD=$(New-NevmaSecret)"
            "NEVMA_NOTIFICATIONS_DB_PASSWORD=$(New-NevmaSecret)"
            "NEVMA_FILES_DB_PASSWORD=$(New-NevmaSecret)"
            "NEVMA_COMMANDS_DB_PASSWORD=$(New-NevmaSecret)"
            'RABBITMQ_DEFAULT_USER=nevma'
            "RABBITMQ_DEFAULT_PASS=$(New-NevmaSecret)"
            "REDIS_PASSWORD=$(New-NevmaSecret)"
        )

        [System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host 'Created .env with local random credentials.' -ForegroundColor Green
    }

    return Import-NevmaEnvironment -Path $path
}

function Import-NevmaEnvironment {
    param([Parameter(Mandatory = $true)][string]$Path)

    $values = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $separator = $trimmed.IndexOf('=')
        if ($separator -lt 1) {
            throw "Invalid environment entry in ${Path}: $line"
        }

        $name = $trimmed.Substring(0, $separator)
        $value = $trimmed.Substring($separator + 1)
        $values[$name] = $value
    }

    return $values
}

function Set-NevmaProcessEnvironment {
    param([Parameter(Mandatory = $true)][hashtable]$Values)

    $connections = @{
        IdentityDatabase = @('nevma_identity', 'nevma_identity', 'NEVMA_IDENTITY_DB_PASSWORD')
        PlanningDatabase = @('nevma_planning', 'nevma_planning', 'NEVMA_PLANNING_DB_PASSWORD')
        MessagingDatabase = @('nevma_messaging', 'nevma_messaging', 'NEVMA_MESSAGING_DB_PASSWORD')
        NotificationsDatabase = @('nevma_notifications', 'nevma_notifications', 'NEVMA_NOTIFICATIONS_DB_PASSWORD')
        FilesDatabase = @('nevma_files', 'nevma_files', 'NEVMA_FILES_DB_PASSWORD')
        CommandsDatabase = @('nevma_commands', 'nevma_commands', 'NEVMA_COMMANDS_DB_PASSWORD')
    }

    foreach ($entry in $connections.GetEnumerator()) {
        $database, $username, $passwordName = $entry.Value
        $connectionString = "Host=localhost;Port=5432;Database=$database;Username=$username;Password=$($Values[$passwordName])"
        [System.Environment]::SetEnvironmentVariable("ConnectionStrings__$($entry.Key)", $connectionString, 'Process')
    }

    $rabbitUser = [System.Uri]::EscapeDataString($Values['RABBITMQ_DEFAULT_USER'])
    $rabbitPassword = [System.Uri]::EscapeDataString($Values['RABBITMQ_DEFAULT_PASS'])
    $rabbitUri = "amqp://${rabbitUser}:${rabbitPassword}@localhost:5672/"

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Authentication__Issuer = 'https://localhost:7293/'
    $env:MessageBroker__Enabled = 'true'
    $env:MessageBroker__Uri = $rabbitUri
    $env:OpenTelemetry__Otlp__Enabled = 'true'
    $env:OpenTelemetry__Otlp__Endpoint = 'http://localhost:4317'
    $env:Redis__Enabled = 'true'
    $env:Redis__ConnectionString = "localhost:6379,password=$($Values['REDIS_PASSWORD']),ssl=False,abortConnect=False"

    foreach ($name in @(
        'CommandAI__Enabled',
        'CommandAI__Provider',
        'CommandAI__Endpoint',
        'CommandAI__ApiKey',
        'CommandAI__Model',
        'CommandAI__TranscriptionEndpoint',
        'CommandAI__TranscriptionModel'
        'SpeechToText__Enabled'
        'SpeechToText__Provider'
        'SpeechToText__Endpoint'
        'PushDelivery__Enabled'
        'PushDelivery__WebPush__Enabled'
        'PushDelivery__WebPush__PublicKey'
        'PushDelivery__WebPush__PrivateKey'
        'PushDelivery__WebPush__Subject'
    )) {
        if ($Values.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace($Values[$name])) {
            [System.Environment]::SetEnvironmentVariable($name, $Values[$name], 'Process')
        }
    }
}
