# Reinstall extension and test

$vsixInstaller = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\VSIXInstaller.exe"
$vsPath = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe"
$vsixPath = "c:\Work\ClaudeVS\bin\Debug\ClaudeVS.vsix"

Write-Host "Uninstalling old extension..."
& $vsixInstaller /uninstall:ClaudeVS.2ca07fc7-11ad-4410-baca-b481834cd8a5 /instanceIds:68154995 2>&1 | Out-Null

Write-Host "Installing new extension..."
& $vsixInstaller /quiet $vsixPath 2>&1 | Out-Null

Write-Host "Starting VS..."
$proc = Start-Process -FilePath $vsPath -ArgumentList "/rootsuffix Exp" -PassThru

Start-Sleep -Seconds 10

Write-Host "Checking for cmd.exe..."
$cmds = Get-Process -Name "cmd" -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddSeconds(-15) }

if ($cmds) {
    Write-Host "SUCCESS: cmd.exe is running!"
    $cmds | Format-Table Id,StartTime
} else {
    Write-Host "FAIL: No cmd.exe found"
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
