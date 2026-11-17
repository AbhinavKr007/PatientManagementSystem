# Installs (or with -Uninstall removes) the virtual second display used by
# LAN Screen Share. Must run as administrator; the installer does that.
#
# The display itself is the open-source "Virtual Display Driver"
# (github.com/VirtualDrivers/Virtual-Display-Driver, MIT), installed with
# nefcon (github.com/nefarius/nefcon, MIT). See the LICENSE-*.txt files here.
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$nefcon    = Join-Path $here 'nefconc.exe'
$inf       = Join-Path $here 'MttVDD.inf'
$cat       = Join-Path $here 'mttvdd.cat'
$hwid      = 'Root\MttVDD'
$classGuid = '4d36e968-e325-11ce-bfc1-08002be10318'   # Display adapters
$configDir = 'C:\VirtualDisplayDriver'                # fixed path the driver reads
$log       = Join-Path $env:ProgramData 'LanScreenShare-display-install.log'

function Log($text) {
    "$(Get-Date -Format s) $text" | Add-Content -Path $log
    Write-Host $text
}
function Run-Nefcon([string[]]$nefArgs) {
    $p = Start-Process -FilePath $nefcon -ArgumentList $nefArgs -Wait -PassThru -NoNewWindow
    Log "nefconc $($nefArgs -join ' ') -> exit $($p.ExitCode)"
    return $p.ExitCode
}
function Remove-Display {
    # Removes every matching device and, once unused, the driver package.
    Run-Nefcon @('--remove-device-node', '--hardware-id', $hwid, '--class-guid', $classGuid) | Out-Null
}

try {
    if ($Uninstall) {
        Remove-Display
        Remove-Item -Path $configDir -Recurse -Force -ErrorAction SilentlyContinue
        Log 'Virtual display removed.'
        exit 0
    }

    # Settings file: keep an existing one so the user's own edits survive upgrades.
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    $settings = Join-Path $configDir 'vdd_settings.xml'
    if (-not (Test-Path $settings)) { Copy-Item (Join-Path $here 'vdd_settings.xml') $settings }

    # Trust the driver's publisher so Windows installs it without a prompt.
    $sig = Get-AuthenticodeSignature -FilePath $cat
    Log "Driver signature: $($sig.Status) / $($sig.SignerCertificate.Subject)"
    if ($sig.Status -ne 'Valid') { throw "Driver signature is not valid: $($sig.StatusMessage)" }
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher', 'LocalMachine')
    $store.Open('ReadWrite')
    $store.Add($sig.SignerCertificate)
    $store.Close()

    Remove-Display   # avoid a duplicate display when re-installing
    $code = Run-Nefcon @('install', $inf, $hwid)
    if ($code -ne 0) { throw "Driver installation failed (exit code $code)." }
    Log 'Virtual display installed.'
    exit 0
}
catch {
    Log "ERROR: $($_.Exception.Message)"
    exit 1
}
