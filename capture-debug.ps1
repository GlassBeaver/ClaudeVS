# Capture debug output using DebugView

$vsPath = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe"
$dbgOutputFile = "c:\temp\debug-output.txt"

# Start capturing debug output to file using PowerShell's debug stream
Write-Host "Starting VS and capturing debug for 12 seconds..."

$vsProc = Start-Process -FilePath $vsPath -ArgumentList "/rootsuffix Exp /log" -PassThru

Start-Sleep -Seconds 12

Write-Host "Killing VS..."
Stop-Process -Id $vsProc.Id -Force -ErrorAction SilentlyContinue

# Check ActivityLog
$logPath = "$env:APPDATA\Microsoft\VisualStudio\18.0_*Exp\ActivityLog.xml"
$logFile = Get-ChildItem $logPath -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($logFile) {
    Write-Host "Found ActivityLog: $($logFile.FullName)"

    # Extract debug entries related to ConPTY or ClaudeTerminal
    [xml]$xml = Get-Content $logFile.FullName
    $relevant = $xml.activity.entry | Where-Object {
        $_.description -match "ConPTY|ClaudeTerminal|Claude|pseudo"
    } | Select-Object -Last 50

    if ($relevant) {
        Write-Host "`nRelevant log entries:"
        $relevant | ForEach-Object {
            Write-Host "[$($_.type)] $($_.description)"
        }
    } else {
        Write-Host "No relevant entries found in ActivityLog"
    }
} else {
    Write-Host "ActivityLog not found"
}

Write-Host "`nDone. Check c:\temp\output.txt for captured output."
