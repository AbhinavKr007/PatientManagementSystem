# Switches Windows between "PC screen only" and "Extend" (same as Win+P).
# Usage: display-mode.ps1 extend | internal
param([Parameter(Mandatory = $true)][ValidateSet('extend', 'internal')][string]$Mode)

Add-Type -Namespace LanScreenShare -Name Display -MemberDefinition @'
[DllImport("user32.dll")]
public static extern int SetDisplayConfig(uint numPaths, IntPtr paths, uint numModes, IntPtr modes, uint flags);
'@
$SDC_APPLY = 0x80
$topology = if ($Mode -eq 'extend') { 0x04 } else { 0x01 }   # SDC_TOPOLOGY_EXTEND / _INTERNAL
exit [LanScreenShare.Display]::SetDisplayConfig(0, [IntPtr]::Zero, 0, [IntPtr]::Zero, ($SDC_APPLY -bor $topology))
