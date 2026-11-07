# Empyrion Admin Manager - Windows service management (run as administrator).
param([Parameter(Mandatory)][ValidateSet('install','uninstall')][string]$Action)
$ErrorActionPreference = 'Stop'
$name = 'EmpyrionAdminManager'
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root 'EamEngine.exe'

function Pause-End { Write-Host ''; Read-Host 'Appuyez sur Entree / Press Enter' | Out-Null }

try {
    $existing = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($Action -eq 'install') {
        if (-not (Test-Path $exe)) { throw "EamEngine.exe introuvable: $exe" }
        if ($existing) {
            Write-Host 'Le service existe deja, mise a jour du chemin... / Service exists, updating path...'
            if ($existing.Status -ne 'Stopped') { Stop-Service $name -Force; $existing.WaitForStatus('Stopped', '00:01:00') }
            sc.exe config $name binPath= "`"$exe`"" start= delayed-auto | Out-Null
        } else {
            New-Service -Name $name -BinaryPathName "`"$exe`"" -DisplayName 'Empyrion Admin Manager' `
                -Description 'Moteur Empyrion Admin Manager : gestion des serveurs Empyrion / Empyrion server management engine' `
                -StartupType Automatic | Out-Null
            sc.exe config $name start= delayed-auto | Out-Null
        }
        # Restart automatically 10 s after a crash.
        sc.exe failure $name reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null
        Start-Service $name
        Write-Host 'Service installe et demarre. / Service installed and started.' -ForegroundColor Green
        Write-Host 'Ouvrez EmpyrionAdminManager.exe ou http://127.0.0.1:8790/'
    } else {
        if (-not $existing) { Write-Host 'Service absent. / Service not installed.'; Pause-End; return }
        if ($existing.Status -ne 'Stopped') { Stop-Service $name -Force; $existing.WaitForStatus('Stopped', '00:01:00') }
        sc.exe delete $name | Out-Null
        Write-Host 'Service supprime. Les donnees sont conservees. / Service removed. Data kept.' -ForegroundColor Green
    }
} catch {
    Write-Host $_ -ForegroundColor Red
}
Pause-End
