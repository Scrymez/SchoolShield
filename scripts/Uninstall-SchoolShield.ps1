<#
.SYNOPSIS
    Полное удаление SchoolShield: возвращает обычный рабочий стол и снимает все ограничения.

.PARAMETER RemoveStudent  Удалить и учётную запись ученика (вместе с её профилем).
.PARAMETER KeepData       Не удалять C:\ProgramData\SchoolShield (настройки и журнал).
.PARAMETER KeepProgram    Не удалять саму программу (только снять защиту). Так вызывает панель администратора.
.PARAMETER DryRun         Только показать, что будет сделано.

.NOTES
    Браузерные политики Chrome/Edge/Firefox удаляются целиком (HKLM\SOFTWARE\Policies\...),
    включая те, что были заданы не нами.
#>
[CmdletBinding()]
param(
    [switch]$RemoveStudent,
    [switch]$KeepData,
    [switch]$KeepProgram,
    [switch]$DryRun
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$ErrorActionPreference = 'Continue'
$InstallDir = Join-Path $env:ProgramFiles 'SchoolShield'
$DataDir    = Join-Path $env:ProgramData 'SchoolShield'
$Winlogon   = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$OwnKey     = 'HKLM:\SOFTWARE\SchoolShield'

function Write-Step([string]$t) { Write-Host ''; Write-Host ('=== ' + $t) -ForegroundColor Cyan }
function Act([string]$what, [scriptblock]$do) {
    if ($DryRun) { Write-Host ('  [dry] ' + $what) -ForegroundColor DarkGray; return }
    try { & $do; Write-Host ('  [ok] ' + $what) -ForegroundColor Green }
    catch { Write-Host ('  [!]  ' + $what + ': ' + $_.Exception.Message) -ForegroundColor Yellow }
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Нужны права администратора. Запустите Uninstall.cmd.'
}

$studentUser = 'Student'
if (Test-Path -LiteralPath $OwnKey) {
    $v = (Get-ItemProperty -LiteralPath $OwnKey -Name 'StudentUser' -ErrorAction SilentlyContinue)
    if ($v -and $v.StudentUser) { $studentUser = $v.StudentUser }
}

Write-Step '1. Оболочка Windows -> explorer.exe (самое важное)'
$original = 'explorer.exe'
if (Test-Path -LiteralPath $OwnKey) {
    $o = (Get-ItemProperty -LiteralPath $OwnKey -Name 'OriginalShell' -ErrorAction SilentlyContinue)
    if ($o -and $o.OriginalShell) { $original = $o.OriginalShell }
}
Act "Winlogon\Shell = $original" {
    Set-ItemProperty -LiteralPath $Winlogon -Name 'Shell' -Value $original -Type String
}

Write-Step '2. Автовход'
Act 'отключить автовход' {
    Set-ItemProperty -LiteralPath $Winlogon -Name 'AutoAdminLogon' -Value '0' -Type String
    Remove-ItemProperty -LiteralPath $Winlogon -Name 'DefaultPassword' -ErrorAction SilentlyContinue
}

Write-Step '3. Политики запуска (SRP) и системные политики'
Act 'удалить SRP' {
    Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers' -Recurse -Force -ErrorAction SilentlyContinue
}
$sysValues = @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer', 'DisableUserInstalls'),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer', 'AlwaysInstallElevated'),
    @('HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore', 'RemoveWindowsStore'),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableWindowsConsumerFeatures'),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableSoftLanding'),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive', 'DisableFileSyncNGSC'),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoDriveTypeAutoRun'),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', 'HideFastUserSwitching')
)
foreach ($sv in $sysValues) {
    Act ('значение ' + $sv[1]) { Remove-ItemProperty -LiteralPath $sv[0] -Name $sv[1] -ErrorAction SilentlyContinue }
}

Write-Step '4. Политики браузеров'
foreach ($k in 'HKLM:\SOFTWARE\Policies\Google\Chrome', 'HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox') {
    Act ('удалить ' + $k) { Remove-Item -LiteralPath $k -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Step '5. DNS и брандмауэр'
Act 'правила брандмауэра SchoolShield' {
    Get-NetFirewallRule -Group 'SchoolShield' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
Act 'DNS: вернуть автоматическое получение' {
    foreach ($nic in @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue)) {
        Set-DnsClientServerAddress -InterfaceIndex $nic.ifIndex -ResetServerAddresses
    }
}

Write-Step '6. Пользовательские ограничения в профиле ученика'
# The launcher wrote these into the student's HKCU on every start; take them out.
$userValues = @(
    @('Software\Microsoft\Windows\CurrentVersion\Policies\System', 'DisableTaskMgr'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\System', 'DisableRegistryTools'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\System', 'DisableChangePassword'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoRun'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoControlPanel'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoWinKeys'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoDriveTypeAutoRun'),
    @('Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoNetConnectDisconnect'),
    @('Software\Policies\Microsoft\Windows\System', 'DisableCMD')
)
function Clear-UserHive([string]$HiveRoot) {
    foreach ($uv in $userValues) {
        Remove-ItemProperty -LiteralPath ($HiveRoot + '\' + $uv[0]) -Name $uv[1] -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath ($HiveRoot + '\Software\Policies\Microsoft\Windows\RemovableStorageDevices') -Recurse -Force -ErrorAction SilentlyContinue
}
$profilePath = $null
$sid = $null
try {
    $acct = New-Object System.Security.Principal.NTAccount($studentUser)
    $sid = $acct.Translate([System.Security.Principal.SecurityIdentifier]).Value
    $profilePath = (Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\' + $sid) -ErrorAction Stop).ProfileImagePath
} catch { }
if ($sid) {
    if (-not (Test-Path -LiteralPath 'HKU:\')) { New-PSDrive -Name HKU -PSProvider Registry -Root HKEY_USERS | Out-Null }
    if (Test-Path -LiteralPath ('HKU:\' + $sid)) {
        Act 'очистить профиль ученика (он сейчас открыт)' { Clear-UserHive ('HKU:\' + $sid) }
    } elseif ($profilePath -and (Test-Path -LiteralPath (Join-Path $profilePath 'NTUSER.DAT'))) {
        Act 'очистить профиль ученика' {
            & reg.exe load 'HKU\SS_Cleanup' (Join-Path $profilePath 'NTUSER.DAT') | Out-Null
            try { Clear-UserHive 'HKU:\SS_Cleanup' }
            finally {
                [GC]::Collect(); [GC]::WaitForPendingFinalizers()
                & reg.exe unload 'HKU\SS_Cleanup' | Out-Null
            }
        }
    } else { Write-Host '  профиль ученика не найден - пропуск' }
}

if ($RemoveStudent) {
    Write-Step '7. Удаление учётной записи ученика'
    Act "удалить пользователя $studentUser" { Remove-LocalUser -Name $studentUser }
    if ($profilePath -and (Test-Path -LiteralPath $profilePath)) {
        Act "удалить профиль $profilePath" { Remove-Item -LiteralPath $profilePath -Recurse -Force }
    }
}

Write-Step '8. Файлы'
if ($KeepProgram) {
    Act 'флаг «защита настроена» сброшен' { Remove-ItemProperty -LiteralPath $OwnKey -Name 'Configured' -ErrorAction SilentlyContinue }
} else {
    Act 'ключ реестра HKLM\SOFTWARE\SchoolShield' { Remove-Item -LiteralPath $OwnKey -Recurse -Force -ErrorAction SilentlyContinue }
}
if (-not $KeepData) {
    Act "удалить $DataDir" { Remove-Item -LiteralPath $DataDir -Recurse -Force }
}
if (-not $KeepProgram) {
    Act "удалить $InstallDir" {
        # This script may be running from inside $InstallDir; remove what we can, the rest goes after the reboot.
        Get-ChildItem -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -ne $PSCommandPath } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host 'Готово. Перезагрузите ПК: загрузится обычный рабочий стол.' -ForegroundColor Green
