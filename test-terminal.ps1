# Script to test the ClaudeVS terminal and capture debug output

$vsPath = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe"
$outputFile = "c:\temp\terminal-debug.txt"

# Clear previous output
if (Test-Path $outputFile) {
    Remove-Item $outputFile
}

Write-Host "Starting Visual Studio with experimental instance..."
Write-Host "Debug output will be captured to: $outputFile"

# Start VS in experimental instance
$process = Start-Process -FilePath $vsPath -ArgumentList "/rootsuffix Exp /log" -PassThru

# Wait for VS to start (15 seconds)
Write-Host "Waiting 15 seconds for VS to fully load..."
Start-Sleep -Seconds 15

# Use DebugView to capture debug output (if available)
# Otherwise, check VS ActivityLog
Write-Host "Checking for debug output..."

# Give it 5 more seconds for any terminal initialization
Start-Sleep -Seconds 5

# Try to get the ActivityLog.xml
$logPath = "$env:APPDATA\Microsoft\VisualStudio\18.0_*Exp\ActivityLog.xml"
$logFiles = Get-ChildItem $logPath -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($logFiles) {
    Write-Host "Copying ActivityLog from: $($logFiles.FullName)"
    Copy-Item $logFiles.FullName $outputFile
} else {
    Write-Host "No ActivityLog.xml found"
}

# Kill VS
Write-Host "Terminating Visual Studio..."
Stop-Process -Id $process.Id -Force

Write-Host "Done. Check output file: $outputFile"
