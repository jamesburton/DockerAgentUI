# AgentHub Host Daemon
# Receives HostCommandProtocol JSON via SSH_ORIGINAL_COMMAND or stdin,
# processes commands, and returns JSON responses on stdout.
#
# Install: Set as ForceCommand in sshd_config for the agent user, or
# call directly: powershell -NoProfile -File agenthub-daemon.ps1

param(
    [string]$Command
)

$ErrorActionPreference = "Stop"

# Session tracking (in-memory for this process lifetime)
$script:Sessions = @{}

function Write-JsonResponse {
    param(
        [bool]$Success,
        [string]$CommandName,
        [string]$SessionId = $null,
        [string]$Error = $null,
        [object]$Data = $null
    )
    $response = @{ success = $Success; command = $CommandName }
    if ($SessionId) { $response.sessionId = $SessionId }
    if ($Error) { $response.error = $Error }
    if ($Data) { $response.data = $Data }
    $json = $response | ConvertTo-Json -Compress -Depth 10
    Write-Output $json
}

function Handle-Ping {
    param($cmd)
    Write-JsonResponse -Success $true -CommandName "ping"
}

function Handle-ReportStatus {
    param($cmd)
    $os = Get-CimInstance Win32_OperatingSystem
    $cpu = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
    $memUsed = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1024)
    $memTotal = [math]::Round($os.TotalVisibleMemorySize / 1024)

    $statusData = @{
        hostId = $env:COMPUTERNAME
        uptimeSeconds = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime |
            ForEach-Object { ((Get-Date) - $_).TotalSeconds }
        activeSessions = $script:Sessions.Count
        cpuPercent = $cpu
        memoryUsedMb = $memUsed
        memoryTotalMb = $memTotal
        agentsAvailable = @()
    }

    # Detect available agents
    $agents = @()
    if (Get-Command claude -ErrorAction SilentlyContinue) { $agents += "claude-code" }
    if (Get-Command codex -ErrorAction SilentlyContinue) { $agents += "codex" }
    if (Get-Command aider -ErrorAction SilentlyContinue) { $agents += "aider" }
    $statusData.agentsAvailable = $agents

    Write-JsonResponse -Success $true -CommandName "report-status" -Data $statusData
}

function Handle-StartSession {
    param($cmd)
    $sessionId = $cmd.sessionId
    $payload = $cmd.payload

    if (-not $sessionId) {
        Write-JsonResponse -Success $false -CommandName "start-session" -Error "Missing sessionId"
        return
    }

    $agentType = $payload.agentType
    $prompt = $payload.prompt
    $workDir = $payload.workingDirectory
    $skipPrompts = $false
    if ($payload.permissions) {
        $skipPrompts = $payload.permissions.skipPermissionPrompts
    }

    # Ensure working directory exists
    if ($workDir -and -not (Test-Path $workDir)) {
        New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    }

    # Resolve agent command
    $agentCmd = switch ($agentType) {
        "ClaudeCode" { "claude" }
        "claude-code" { "claude" }
        "Codex" { "codex" }
        "Aider" { "aider" }
        default { $agentType.ToLower() }
    }

    # Check agent is available
    if (-not (Get-Command $agentCmd -ErrorAction SilentlyContinue)) {
        Write-JsonResponse -Success $false -CommandName "start-session" -SessionId $sessionId `
            -Error "Agent '$agentCmd' not found on this host"
        return
    }

    # Build agent arguments
    $agentArgs = @()
    switch ($agentCmd) {
        "claude" {
            $agentArgs += "--print"
            if ($skipPrompts) {
                $agentArgs += "--dangerously-skip-permissions"
            }
            if ($prompt) {
                $agentArgs += $prompt
            }
        }
    }

    # Launch agent process in background
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $agentCmd
        $psi.Arguments = $agentArgs -join " "
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        if ($workDir) { $psi.WorkingDirectory = $workDir }

        # Set environment variables
        if ($payload.environment) {
            foreach ($key in $payload.environment.PSObject.Properties.Name) {
                $psi.Environment[$key] = $payload.environment.$key
            }
        }

        $process = [System.Diagnostics.Process]::Start($psi)
        $script:Sessions[$sessionId] = @{
            Process = $process
            AgentType = $agentType
            StartedAt = Get-Date
            WorkDir = $workDir
        }

        Write-JsonResponse -Success $true -CommandName "start-session" -SessionId $sessionId
    }
    catch {
        Write-JsonResponse -Success $false -CommandName "start-session" -SessionId $sessionId `
            -Error $_.Exception.Message
    }
}

function Handle-StopSession {
    param($cmd)
    $sessionId = $cmd.sessionId
    if ($script:Sessions.ContainsKey($sessionId)) {
        $session = $script:Sessions[$sessionId]
        $proc = $session.Process
        if ($proc -and -not $proc.HasExited) {
            # Graceful: send Ctrl+C via GenerateConsoleCtrlEvent or just close stdin
            $proc.CloseMainWindow() | Out-Null
            $proc.WaitForExit(5000) | Out-Null
            if (-not $proc.HasExited) {
                $proc.Kill()
            }
        }
        $script:Sessions.Remove($sessionId)
        Write-JsonResponse -Success $true -CommandName "stop-session" -SessionId $sessionId
    }
    else {
        Write-JsonResponse -Success $true -CommandName "stop-session" -SessionId $sessionId
    }
}

function Handle-ForceKill {
    param($cmd)
    $sessionId = $cmd.sessionId
    if ($script:Sessions.ContainsKey($sessionId)) {
        $session = $script:Sessions[$sessionId]
        $proc = $session.Process
        if ($proc -and -not $proc.HasExited) {
            $proc.Kill()
        }
        $script:Sessions.Remove($sessionId)
    }
    Write-JsonResponse -Success $true -CommandName "force-kill" -SessionId $sessionId
}

function Handle-SendInput {
    param($cmd)
    $sessionId = $cmd.sessionId
    # Input delivery is a placeholder - real implementation would write to agent stdin
    Write-JsonResponse -Success $true -CommandName "send-input" -SessionId $sessionId
}

function Process-Command {
    param([string]$JsonInput)

    try {
        $cmd = $JsonInput | ConvertFrom-Json
    }
    catch {
        Write-JsonResponse -Success $false -CommandName "unknown" -Error "Invalid JSON: $($_.Exception.Message)"
        return
    }

    switch ($cmd.command) {
        "ping"             { Handle-Ping $cmd }
        "report-status"    { Handle-ReportStatus $cmd }
        "start-session"    { Handle-StartSession $cmd }
        "stop-session"     { Handle-StopSession $cmd }
        "force-kill"       { Handle-ForceKill $cmd }
        "send-input"       { Handle-SendInput $cmd }
        "approval-response" { Write-JsonResponse -Success $true -CommandName "approval-response" -SessionId $cmd.sessionId }
        default {
            Write-JsonResponse -Success $false -CommandName $cmd.command -Error "Unknown command: $($cmd.command)"
        }
    }
}

# --- Entry point ---

# Priority 1: SSH_ORIGINAL_COMMAND (set by ForceCommand in sshd_config)
$input_cmd = $env:SSH_ORIGINAL_COMMAND

# Priority 2: Command parameter
if (-not $input_cmd -and $Command) {
    $input_cmd = $Command
}

# Priority 3: Read from stdin (interactive/piped mode)
if (-not $input_cmd) {
    $input_cmd = [Console]::In.ReadLine()
}

if ($input_cmd) {
    Process-Command $input_cmd
}
else {
    Write-JsonResponse -Success $false -CommandName "unknown" -Error "No command received"
}
