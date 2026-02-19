<#
.SYNOPSIS
    Invokes Copilot CLI with timeout and async output capture.

.DESCRIPTION
    Shared helper that runs Copilot CLI as a subprocess with configurable
    timeout, captures stdout asynchronously for the return value, and
    tees stderr to the console so errors are always visible in CI logs.

    Returns $null on timeout or failure. Returns stdout (or stdout+stderr
    when IncludeStderr is set) on success.

.PARAMETER Prompt
    The prompt to send to Copilot CLI.

.PARAMETER WorkingDir
    Working directory for the Copilot CLI process.

.PARAMETER TimeoutSeconds
    Maximum time to wait for Copilot CLI to complete (default: 300).

.PARAMETER ConfigDir
    Optional config directory for session isolation.

.PARAMETER OutputFile
    Optional path to save stdout. Stderr is saved to <OutputFile>.err.

.PARAMETER IncludeStderr
    If set, return stdout + stderr combined (for stats parsing).

.PARAMETER Model
    Copilot model to use.
#>
function Invoke-CopilotCli {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [Parameter(Mandatory)]
        [string]$WorkingDir,

        [int]$TimeoutSeconds = 300,

        [string]$ConfigDir,

        [string]$OutputFile,

        [switch]$IncludeStderr,

        [Parameter(Mandatory)]
        [string]$Model
    )

    Write-Host "[RUN] Running Copilot CLI..."
    Write-Host "   Working directory: $WorkingDir"
    Write-Host "   Timeout: ${TimeoutSeconds}s"
    Write-Host "   Prompt: $Prompt"

    # Resolve copilot executable - prefer .cmd/.bat/.exe for Process.Start compatibility
    # Use -All to search across all PATH entries, not just the first match
    $copilotCmd = Get-Command copilot -All -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandType -eq 'Application' } |
        Select-Object -First 1 -ExpandProperty Source

    # Build argument list — using ArgumentList (Collection<string>) instead of the
    # Arguments string property so .NET handles escaping automatically. This avoids
    # breakage when the prompt contains quotes, special characters, or is very long.
    $argList = [System.Collections.Generic.List[string]]::new()

    if (-not $copilotCmd) {
        if ($env:OS -match 'Windows') {
            $copilotCmd = "cmd.exe"
            $argList.Add("/c")
            $argList.Add("copilot")
        } else {
            $copilotCmd = "/usr/bin/env"
            $argList.Add("copilot")
        }
    }

    $argList.Add("-p")
    $argList.Add($Prompt)
    $argList.Add("--model")
    $argList.Add($Model)
    $argList.Add("--allow-all-tools")
    $argList.Add("--allow-all-paths")
    $argList.Add("--no-ask-user")
    if ($ConfigDir) {
        $argList.Add("--config-dir")
        $argList.Add($ConfigDir)
    }

    Write-Host "   Copilot executable: $copilotCmd"

    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = $copilotCmd
    foreach ($a in $argList) { $processInfo.ArgumentList.Add($a) }
    $processInfo.WorkingDirectory = $WorkingDir
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $processInfo

    # Capture stdout asynchronously for return value.
    # Capture stderr asynchronously and tee to console so errors are visible in CI.
    $stdoutBuilder = New-Object System.Text.StringBuilder
    $stderrBuilder = New-Object System.Text.StringBuilder

    $stdoutEvent = Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -Action {
        if ($null -ne $EventArgs.Data) {
            $Event.MessageData.AppendLine($EventArgs.Data) | Out-Null
        }
    } -MessageData $stdoutBuilder

    $stderrEvent = Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -Action {
        if ($null -ne $EventArgs.Data) {
            $Event.MessageData.AppendLine($EventArgs.Data) | Out-Null
            # Tee stderr to console so it's visible in CI logs
            [Console]::Error.WriteLine($EventArgs.Data)
        }
    } -MessageData $stderrBuilder

    $startTime = Get-Date
    $process.Start() | Out-Null
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()

    try {
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        $elapsed = (Get-Date) - $startTime

        # Flush async output streams — the parameterless WaitForExit() ensures
        # all redirected stdout/stderr has been processed by the event handlers
        if ($completed) {
            $process.WaitForExit()
            $exitCode = $process.ExitCode
        }
    }
    finally {
        Unregister-Event -SourceIdentifier $stdoutEvent.Name -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier $stderrEvent.Name -ErrorAction SilentlyContinue

        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
        $process.Dispose()
    }

    $stdout = $stdoutBuilder.ToString()
    $stderr = $stderrBuilder.ToString()

    # Save outputs to files if requested
    if ($OutputFile) {
        $stdout | Out-File -FilePath $OutputFile -Encoding utf8
        if ($stderr) {
            $stderr | Out-File -FilePath "${OutputFile}.err" -Encoding utf8
        }
    }

    if (-not $completed) {
        Write-Warning "[TIMEOUT] Copilot timed out after $TimeoutSeconds seconds"
        return $null
    }

    Write-Host "   Exit code: $exitCode"
    Write-Host "   Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s"

    if ($exitCode -ne 0) {
        Write-Warning "Copilot CLI exited with code $exitCode"
        if ($stdout) {
            Write-Host "Stdout output:"
            Write-Host $stdout
        }
        return $null
    }

    if ($IncludeStderr) {
        return ($stdout + "`n" + $stderr)
    }
    return $stdout
}
