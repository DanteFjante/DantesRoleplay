#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ruleName = 'DantesRoleplay-Web-6217'
$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
if ($null -eq $rule) {
    New-NetFirewallRule -Name $ruleName -DisplayName 'DantesRoleplay website (TCP 6217)' `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort 6217 -Profile Any | Out-Null
} else {
    $rule | Set-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -Profile Any
    $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort 6217
}
Write-Host 'Inbound TCP 6217 is allowed. Router forwarding should map external TCP 80 to this device on TCP 6217.'
