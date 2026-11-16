# Removes the Guardrail Server Windows service installed by Install-Service.ps1.
# Your data in C:\ProgramData\Guardrail Server is kept unless you run it with -Purge.
param([switch]$Purge)
$ErrorActionPreference = 'SilentlyContinue'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $a = "-ExecutionPolicy Bypass -File `"$PSCommandPath`""; if ($Purge) { $a += ' -Purge' }
    Start-Process powershell -Verb RunAs -ArgumentList $a
    exit
}
Stop-Service GuardrailServer -Force
sc.exe delete GuardrailServer | Out-Null
Remove-NetFirewallRule -DisplayName 'Guardrail Server'
Remove-Item (Join-Path $env:ProgramFiles 'Guardrail Server') -Recurse -Force
if ($Purge) {
    Remove-Item (Join-Path $env:ProgramData 'Guardrail Server') -Recurse -Force
    Write-Host "Guardrail Server and all of its data were removed."
} else {
    Write-Host "Guardrail Server removed. Your data is still in C:\ProgramData\Guardrail Server (run with -Purge to delete it)."
}
