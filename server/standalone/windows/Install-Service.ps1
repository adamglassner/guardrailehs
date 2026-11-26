# Installs GuardHawk Server as a Windows service that starts with Windows.
# Right-click → Run with PowerShell (it asks for administrator rights). Data: C:\ProgramData\Guardrail Server
# Uninstall later with Uninstall-Service.ps1.
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}
$dest = Join-Path $env:ProgramFiles 'Guardrail Server'
$data = Join-Path $env:ProgramData 'Guardrail Server'
New-Item -ItemType Directory -Force $dest, $data | Out-Null
if (Get-Service GuardrailServer -ErrorAction SilentlyContinue) { Stop-Service GuardrailServer -Force; Start-Sleep 2 }
Copy-Item (Join-Path $PSScriptRoot 'GuardrailServer.exe') $dest -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-Service.ps1') $dest -Force
Copy-Item (Join-Path $PSScriptRoot 'wwwroot') $dest -Recurse -Force
$settings = Join-Path $dest 'appsettings.json'
if (-not (Test-Path $settings)) {
    @{ Guardrail = @{ Port = 5080; DataFolder = $data } } | ConvertTo-Json | Set-Content $settings -Encoding UTF8
}
if (-not (Get-Service GuardrailServer -ErrorAction SilentlyContinue)) {
    sc.exe create GuardrailServer binPath= "`"$dest\GuardrailServer.exe`"" start= delayed-auto DisplayName= "GuardHawk Server" | Out-Null
    sc.exe description GuardrailServer "Sync server for GuardHawk (web panel on port 5080)." | Out-Null
}
# Restart it if it stops unexpectedly (and when restarted from the web panel); "Shut down" in the panel stays stopped
sc.exe failure GuardrailServer reset= 86400 actions= restart/3000/restart/3000/restart/10000 | Out-Null
sc.exe failureflag GuardrailServer 1 | Out-Null
if (-not (Get-NetFirewallRule -DisplayName 'GuardHawk Server' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'GuardHawk Server' -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Private,Domain | Out-Null
}
Start-Service GuardrailServer
Write-Host "GuardHawk Server is installed and running. Opening the web panel..."
Start-Process 'http://localhost:5080'
