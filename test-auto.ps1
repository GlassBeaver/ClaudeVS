# Quick automated test script for ClaudeVS terminal

$vsPath = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe"

Write-Host "Starting VS..."
$process = Start-Process -FilePath $vsPath -ArgumentList "/rootsuffix Exp" -PassThru

Write-Host "Waiting 10 seconds..."
Start-Sleep -Seconds 10

Write-Host "Checking for cmd.exe..."
$cmdProcesses = Get-Process -Name "cmd" -ErrorAction SilentlyContinue | Where-Object {
    $_.StartTime -gt (Get-Date).AddSeconds(-15)
}

if ($cmdProcesses) {
    Write-Host "SUCCESS: Found cmd.exe process(es)!"
    $cmdProcesses | Format-Table Id,StartTime -AutoSize
} else {
    Write-Host "FAIL: No cmd.exe found"
}

Write-Host "Killing VS..."
Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
