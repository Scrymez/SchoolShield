<#
.SYNOPSIS
    Установка SchoolShield на школьный компьютер с Windows 10.

.DESCRIPTION
    Делает 10 вещей (каждая отдельным шагом, с выводом в консоль):
      1. копирует лаунчер в C:\Program Files\SchoolShield и настройки в C:\ProgramData\SchoolShield;
      2. задаёт код администратора (хранится только хэш);
      3. создаёт обычную (не админ) учётную запись ученика и включает в неё автовход;
      4. запрещает установку программ (Software Restriction Policies: запуск только из Windows и Program Files);
      5. отключает Microsoft Store, пользовательские установки MSI, автозапуск носителей;
      6. настраивает Chrome / Edge / Firefox: SafeSearch, без инкогнито, без расширений, без скачиваний, без DoH;
      7. включает семейный DNS-фильтр (Яндекс / Cloudflare / CleanBrowsing) и закрывает обходы DNS в брандмауэре;
      8. (по желанию) повышает защиту загрузки;
      9. регистрирует лаунчер как оболочку Windows (Winlogon\Shell) - ПОСЛЕДНИМ шагом;
     10. печатает итог и порядок действий для проверки.

    Запускать от имени администратора (Install.cmd делает это сам). Повторный запуск безопасен.

.PARAMETER AdminCode     Код администратора (если не указан - спросит; минимум 8 символов).
.PARAMETER StudentUser   Имя учётной записи ученика (по умолчанию Student).
.PARAMETER Dns           AdGuardFamily (по умолчанию: блокирует рекламу, взрослые сайты, включает SafeSearch) | YandexCloudflare | Yandex | Cloudflare | CleanBrowsing | None
.PARAMETER WebMode       Filter (по умолчанию: интернет с фильтром) | Whitelist (только сайты из whitelist.txt)
.PARAMETER Downloads     BlockAll (по умолчанию) | Dangerous | Off - скачивания в браузерах.
.PARAMETER Usb           block - флешки закрыты | readonly (по умолчанию) - с флешки можно только читать и копировать | allow - чтение и запись.
.PARAMETER NoShell       Не менять оболочку Windows. Для первой проверки: всё настроено, но рабочий стол остаётся обычным.
.PARAMETER NoAutoLogon   Не включать автовход ученика.
.PARAMETER NoDnsFirewall Не создавать правила брандмауэра против обхода DNS.
.PARAMETER NoDialogLock  Не запрещать ученику просмотр корней дисков (окна «Сохранить как» показывают всё, как в Windows).
.PARAMETER HardenBoot    Отключить меню автовосстановления Windows при сбоях загрузки.
.PARAMETER ResetConfig   Перезаписать config.json, badwords.txt и т.д. значениями по умолчанию.
.PARAMETER NonInteractive Не задавать вопросов (запуск из панели администратора). Код берётся из -AdminCode или переменной SS_ADMIN_CODE;
                       если кода нет, а в config.json он уже есть - остаётся прежним.
.PARAMETER DryRun        Только показать, что будет сделано. Ничего не менять.
#>
[CmdletBinding()]
param(
    [string]$AdminCode,
    [string]$StudentUser = 'Student',
    [ValidateSet('AdGuardFamily', 'YandexCloudflare', 'Yandex', 'Cloudflare', 'CleanBrowsing', 'None')][string]$Dns = 'AdGuardFamily',
    [ValidateSet('Filter', 'Whitelist')][string]$WebMode = 'Filter',
    [ValidateSet('BlockAll', 'Dangerous', 'Off')][string]$Downloads = 'BlockAll',
    [ValidateSet('block', 'readonly', 'allow')][string]$Usb = 'readonly',
    [switch]$NoShell,
    [switch]$NoAutoLogon,
    [switch]$NoDnsFirewall,
    [switch]$NoDialogLock,
    [switch]$HardenBoot,
    [switch]$ResetConfig,
    [switch]$NonInteractive,
    [switch]$DryRun
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
if (-not $AdminCode -and $env:SS_ADMIN_CODE) { $AdminCode = $env:SS_ADMIN_CODE }

$ErrorActionPreference = 'Stop'

$script:DryRun = [bool]$DryRun
$PackageRoot = Split-Path -Parent $PSScriptRoot
$InstallDir  = Join-Path $env:ProgramFiles 'SchoolShield'
$DataDir     = Join-Path $env:ProgramData 'SchoolShield'
$ExeName     = 'SchoolShield.exe'
$Winlogon    = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$OwnKey      = 'HKLM:\SOFTWARE\SchoolShield'

# ---------------------------------------------------------------- helpers

function Write-Step([string]$text) { Write-Host ''; Write-Host ('=== ' + $text) -ForegroundColor Cyan }
function Write-Ok([string]$text)   { Write-Host ('  [ok] ' + $text) -ForegroundColor Green }
function Write-Warn2([string]$text) { Write-Host ('  [!]  ' + $text) -ForegroundColor Yellow }
function Write-Dry([string]$text)  { Write-Host ('  [dry] ' + $text) -ForegroundColor DarkGray }

function Set-Reg {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord')
    if ($script:DryRun) { Write-Dry ("{0}\{1} = {2}" -f $Path, $Name, $Value); return }
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType $Type -Force | Out-Null
}

function Remove-RegValue {
    param([string]$Path, [string]$Name)
    if ($script:DryRun) { Write-Dry ("удалить {0}\{1}" -f $Path, $Name); return }
    if (Test-Path -LiteralPath $Path) {
        Remove-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
    }
}

function Set-RegList {
    # Chrome/Edge/Firefox list policies are sub-keys with values named 1, 2, 3...
    param([string]$Path, [string[]]$Items)
    if ($script:DryRun) { Write-Dry ("{0}: {1} элементов" -f $Path, @($Items).Count); return }
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
    if (@($Items).Count -eq 0) { return }
    New-Item -Path $Path -Force | Out-Null
    $i = 1
    foreach ($item in $Items) {
        New-ItemProperty -LiteralPath $Path -Name ([string]$i) -Value $item -PropertyType String -Force | Out-Null
        $i++
    }
}

function Read-DomainList([string]$File) {
    if (-not (Test-Path -LiteralPath $File)) { return @() }
    $list = @()
    foreach ($line in (Get-Content -LiteralPath $File -Encoding UTF8)) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $list += $t.ToLowerInvariant()
    }
    return $list
}

# ---------------------------------------------------------------- 0. preconditions

Write-Host ''
Write-Host 'SchoolShield - установка' -ForegroundColor White
if ($script:DryRun) { Write-Warn2 'РЕЖИМ DryRun: ничего не будет изменено.' }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Нужны права администратора. Запустите Install.cmd (он запросит их сам).'
}
if ([Environment]::OSVersion.Version.Major -lt 10) { throw 'Нужна Windows 10 или новее.' }

$exeSource = Join-Path $PackageRoot $ExeName
if (-not (Test-Path -LiteralPath $exeSource)) { throw "Не найден $exeSource - распакуйте пакет целиком." }
$inPlace = ([IO.Path]::GetFullPath($PackageRoot).TrimEnd('\') -ieq [IO.Path]::GetFullPath($InstallDir).TrimEnd('\'))

$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
Write-Host ("Редакция Windows: {0}" -f $edition)
if ($edition -like 'Core*') {
    Write-Warn2 'Windows 10 Home: политики запрета запуска (SRP) НЕ действуют. Защиту обеспечат лаунчер и его охрана,'
    Write-Warn2 'но надёжнее Pro / Education / Enterprise. Установка продолжится.'
}

# Another admin must exist: the student account is a standard user and the launcher replaces the desktop.
try {
    $adminGroup = Get-LocalGroup -SID 'S-1-5-32-544'
    $admins = @(Get-LocalGroupMember -Group $adminGroup.Name -ErrorAction Stop | Where-Object { $_.ObjectClass -eq 'User' })
    Write-Host ('Администраторы этого ПК: ' + (($admins | ForEach-Object { $_.Name }) -join ', '))
    $otherAdmins = @($admins | Where-Object { $_.Name -notlike "*\$StudentUser" })
    if ($otherAdmins.Count -eq 0) { throw 'Нет ни одной учётной записи администратора, кроме ученика.' }
} catch {
    Write-Warn2 ('Не удалось проверить список администраторов: ' + $_.Exception.Message)
}
Write-Warn2 'Убедитесь, что у администратора Windows ЕСТЬ ПАРОЛЬ. Без него защита бессмысленна.'

# ---------------------------------------------------------------- 1. admin code

Write-Step '1/9  Код администратора'
$keepExistingCode = $false
if (-not $AdminCode) {
    $existingCfg = Join-Path $DataDir 'config.json'
    $hasCode = $false
    if (Test-Path -LiteralPath $existingCfg) {
        $hasCode = [regex]::IsMatch((Get-Content -LiteralPath $existingCfg -Raw -Encoding UTF8), '"AdminHash"\s*:\s*"[^"]+"')
    }
    if ($NonInteractive -or $script:DryRun) {
        if ($hasCode -or $script:DryRun) { $keepExistingCode = $true; Write-Ok 'Оставляю прежний код администратора.' }
        else { throw 'Не задан код администратора (-AdminCode или переменная SS_ADMIN_CODE).' }
    } else {
        while ($true) {
            $s1 = Read-Host 'Придумайте код администратора (минимум 8 символов)' -AsSecureString
            $s2 = Read-Host 'Повторите код' -AsSecureString
            $p1 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s1))
            $p2 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s2))
            if ($p1.Length -lt 8) { Write-Warn2 'Слишком короткий код.'; continue }
            if ($p1 -ne $p2)      { Write-Warn2 'Коды не совпадают.'; continue }
            $AdminCode = $p1
            break
        }
    }
}

$iterations = 100000
if (-not $keepExistingCode) {
    if ($AdminCode.Length -lt 8) { throw 'Код администратора должен быть не короче 8 символов.' }
    $saltBytes = New-Object byte[] 16
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    $rng.GetBytes($saltBytes)
    $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($AdminCode, $saltBytes, $iterations)
    $adminSalt = [Convert]::ToBase64String($saltBytes)
    $adminHash = [Convert]::ToBase64String($kdf.GetBytes(32))
    $kdf.Dispose()
    Write-Ok 'Хэш кода вычислен (сам код нигде не хранится).'
}

# ---------------------------------------------------------------- 2. files

Write-Step '2/9  Файлы и права доступа'
if ($script:DryRun) {
    Write-Dry "копирование в $InstallDir и $DataDir"
} else {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $DataDir 'logs') -Force | Out-Null

    if (-not $inPlace) {
        # Portable package (unzipped folder): copy everything the program needs. Already-installed copies are left alone.
        foreach ($item in Get-ChildItem -LiteralPath $PackageRoot -Force | Where-Object { $_.Name -notin 'src', 'dist', '.git', '.github', 'tools' }) {
            Copy-Item -LiteralPath $item.FullName -Destination $InstallDir -Recurse -Force
        }
    }
    $cfgDir = Join-Path $InstallDir 'config'
    foreach ($f in 'badwords.txt', 'blocklist.txt', 'whitelist.txt') {
        $dst = Join-Path $DataDir $f
        if ($ResetConfig -or -not (Test-Path -LiteralPath $dst)) {
            Copy-Item -LiteralPath (Join-Path $cfgDir $f) -Destination $dst -Force
        }
    }
    $cfgPath = Join-Path $DataDir 'config.json'
    if ($ResetConfig -or -not (Test-Path -LiteralPath $cfgPath)) {
        Copy-Item -LiteralPath (Join-Path $cfgDir 'config.json') -Destination $cfgPath -Force
    }

    # Patch only the fields we own (admin code, USB mode); everything else the admin edited stays untouched.
    function Set-JsonField([string]$Json, [string]$Name, [string]$Value, [bool]$IsString) {
        $pattern = '"' + $Name + '"\s*:\s*(?:"[^"]*"|-?[0-9.]+)'
        if ($IsString) { $repl = '"' + $Name + '": "' + $Value + '"' } else { $repl = '"' + $Name + '": ' + $Value }
        $re = New-Object System.Text.RegularExpressions.Regex($pattern)
        if ($re.IsMatch($Json)) { return $re.Replace($Json, $repl, 1) }
        $i = $Json.IndexOf('{')
        return $Json.Substring(0, $i + 1) + "`n  " + $repl + "," + $Json.Substring($i + 1)
    }
    $json = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8
    if (-not $keepExistingCode) {
        $json = Set-JsonField $json 'AdminSalt' $adminSalt $true
        $json = Set-JsonField $json 'AdminHash' $adminHash $true
        $json = Set-JsonField $json 'Iterations' ([string]$iterations) $false
    }
    $json = Set-JsonField $json 'UsbMode' $Usb $true
    [IO.File]::WriteAllText($cfgPath, $json, (New-Object System.Text.UTF8Encoding($false)))

    # ProgramData\SchoolShield: students may read, only admins may write; logs: students may append only.
    & icacls.exe $DataDir /inheritance:r | Out-Null
    & icacls.exe $DataDir /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    & icacls.exe (Join-Path $DataDir 'logs') /grant '*S-1-5-32-545:(OI)(CI)(RX,WD,AD)' | Out-Null
}
Write-Ok "Лаунчер: $InstallDir ; настройки и журнал: $DataDir"

# ---------------------------------------------------------------- 3. student account

Write-Step '3/9  Учётная запись ученика'
if ($script:DryRun) {
    Write-Dry "создать обычного пользователя $StudentUser без пароля, автовход"
} else {
    $usersGroup = Get-LocalGroup -SID 'S-1-5-32-545'
    if (-not (Get-LocalUser -Name $StudentUser -ErrorAction SilentlyContinue)) {
        New-LocalUser -Name $StudentUser -NoPassword -FullName 'Ученик' -Description 'SchoolShield: учётная запись ученика' `
            -AccountNeverExpires -UserMayNotChangePassword | Out-Null
        Add-LocalGroupMember -Group $usersGroup.Name -Member $StudentUser
        Write-Ok "Создан пользователь $StudentUser (обычный, без прав администратора)."
    } else {
        Write-Ok "Пользователь $StudentUser уже существует."
    }
    # Password changes are forbidden for the student (also blocked in the Ctrl+Alt+Del screen by the launcher).
    try { Set-LocalUser -Name $StudentUser -UserMayChangePassword $false -PasswordNeverExpires $true } catch { Write-Warn2 ('Запрет смены пароля: ' + $_.Exception.Message) }
    # The student must never be an administrator.
    try {
        $adminGroup = Get-LocalGroup -SID 'S-1-5-32-544'
        $isAdmin = Get-LocalGroupMember -Group $adminGroup.Name -ErrorAction Stop | Where-Object { $_.Name -like "*\$StudentUser" }
        if ($isAdmin) {
            Remove-LocalGroupMember -Group $adminGroup.Name -Member $StudentUser
            Write-Warn2 "$StudentUser был администратором - права сняты."
        }
    } catch { Write-Warn2 ('Проверка прав ученика: ' + $_.Exception.Message) }

    if (-not $NoAutoLogon) {
        Set-Reg $Winlogon 'AutoAdminLogon' '1' 'String'
        Set-Reg $Winlogon 'DefaultUserName' $StudentUser 'String'
        Set-Reg $Winlogon 'DefaultPassword' '' 'String'
        Set-Reg $Winlogon 'DefaultDomainName' $env:COMPUTERNAME 'String'
        Remove-RegValue $Winlogon 'AutoLogonCount'
        Write-Ok "Автовход под $StudentUser включён."
    }
}

# ---------------------------------------------------------------- 4. software restriction policies

Write-Step '4/9  Запрет установки и запуска программ (SRP)'
$srp = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers'
if (-not $script:DryRun -and (Test-Path -LiteralPath $srp)) { Remove-Item -LiteralPath $srp -Recurse -Force }

# Default = Disallowed; Windows and Program Files = Unrestricted; administrators are exempt (PolicyScope=1).
Set-Reg $srp 'DefaultLevel' 0
Set-Reg $srp 'TransparentEnabled' 1
Set-Reg $srp 'PolicyScope' 1
Set-Reg $srp 'AuthenticodeEnabled' 0
Set-Reg $srp 'ExecutableTypes' ([string[]]@(
    'ADE', 'ADP', 'BAS', 'BAT', 'CHM', 'CMD', 'COM', 'CPL', 'CRT', 'EXE', 'HTA', 'INF', 'INS', 'ISP', 'JOB',
    'JS', 'JSE', 'MDB', 'MDE', 'MSC', 'MSI', 'MSP', 'MST', 'OCX', 'PCD', 'PIF', 'PS1', 'REG', 'SCR', 'SHS',
    'VB', 'VBE', 'VBS', 'WSC', 'WSF', 'WSH')) 'MultiString'

function Add-SrpRule {
    param([string]$Level, [string]$RulePath, [string]$Description)
    if ($script:DryRun) { Write-Dry ("SRP {0}: {1}" -f $Level, $RulePath); return }
    $key = '{0}\{1}\Paths\{2}' -f $srp, $Level, ([guid]::NewGuid().ToString('B'))
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name 'ItemData' -Value $RulePath -PropertyType ExpandString -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name 'SaferFlags' -Value 0 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name 'Description' -Value $Description -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name 'LastModified' -Value ([long](Get-Date).ToFileTime()) -PropertyType QWord -Force | Out-Null
}

$allow = '262144'   # Unrestricted
$deny  = '0'        # Disallowed
Add-SrpRule $allow '%windir%' 'SchoolShield: Windows'
Add-SrpRule $allow '%ProgramFiles%' 'SchoolShield: Program Files'
Add-SrpRule $allow '%ProgramFiles(x86)%' 'SchoolShield: Program Files (x86)'

# Folders inside Windows that ordinary users can write to (classic allow-list bypasses).
foreach ($d in @(
    '%windir%\Temp', '%windir%\Tasks', '%windir%\tracing', '%windir%\Registration\CRMLog',
    '%windir%\System32\spool\drivers\color', '%windir%\System32\spool\PRINTERS', '%windir%\System32\Tasks',
    '%windir%\System32\Com\dmp', '%windir%\System32\FxsTmp', '%windir%\SysWOW64\Tasks',
    '%windir%\SysWOW64\Com\dmp', '%windir%\SysWOW64\FxsTmp')) {
    Add-SrpRule $deny $d 'SchoolShield: writable folder inside Windows'
}

# System tools a student must not be able to start (admins are exempt, so they keep working).
$tools = @('cmd.exe', 'regedit.exe', 'reg.exe', 'mmc.exe', 'taskmgr.exe', 'wscript.exe', 'cscript.exe', 'mshta.exe',
    'msiexec.exe', 'msconfig.exe', 'control.exe', 'schtasks.exe', 'sc.exe', 'net.exe', 'net1.exe', 'bitsadmin.exe',
    'certutil.exe', 'wmic.exe', 'runas.exe', 'wsl.exe', 'bash.exe', 'taskkill.exe', 'at.exe', 'cmstp.exe',
    'regsvr32.exe', 'OneDriveSetup.exe')
foreach ($dir in '%windir%\System32', '%windir%\SysWOW64') {
    foreach ($t in $tools) { Add-SrpRule $deny ($dir + '\' + $t) ('SchoolShield: block ' + $t) }
    Add-SrpRule $deny ($dir + '\WindowsPowerShell\v1.0\powershell.exe') 'SchoolShield: block PowerShell'
    Add-SrpRule $deny ($dir + '\WindowsPowerShell\v1.0\powershell_ise.exe') 'SchoolShield: block PowerShell ISE'
}
foreach ($fw in '%windir%\Microsoft.NET\Framework', '%windir%\Microsoft.NET\Framework64') {
    foreach ($t in 'msbuild.exe', 'csc.exe', 'vbc.exe', 'jsc.exe', 'InstallUtil.exe', 'RegAsm.exe', 'RegSvcs.exe') {
        Add-SrpRule $deny ($fw + '\v4.0.30319\' + $t) ('SchoolShield: block ' + $t)
    }
}
Add-SrpRule $deny '%windir%\ImmersiveControlPanel\SystemSettings.exe' 'SchoolShield: block Settings'
Add-SrpRule $deny '%ProgramFiles%\PowerShell' 'SchoolShield: block PowerShell 7'
Add-SrpRule $deny '%ProgramFiles%\WindowsApps\Microsoft.DesktopAppInstaller*' 'SchoolShield: block winget'
Add-SrpRule $deny '%ProgramFiles%\WindowsApps\Microsoft.WindowsStore*' 'SchoolShield: block Microsoft Store'
Write-Ok 'Ученик может запускать программы только из Windows и Program Files; cmd, regedit, PowerShell и т.п. закрыты.'

# ---------------------------------------------------------------- 5. system policies

Write-Step '5/9  Системные политики (Store, MSI, автозапуск, OneDrive)'
Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer' 'DisableUserInstalls' 1
Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer' 'AlwaysInstallElevated' 0
Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore' 'RemoveWindowsStore' 1
Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1
Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableSoftLanding' 1
Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' 'DisableFileSyncNGSC' 1
Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoDriveTypeAutoRun' 255
Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' 'HideFastUserSwitching' 1
Write-Ok 'Готово.'

# ---------------------------------------------------------------- 5b. file dialogs

Write-Step '5б  Окна «Сохранить как»: запрет обзора дисков для ученика'
if ($NoDialogLock) {
    Write-Host '  пропущено (-NoDialogLock)'
} else {
    # NTFS "list folder" is denied to the student on drive roots and C:\Users (this folder only, not inherited): programs still
    # open and save files by full path, but the Save As / Open dialogs show nothing outside the allowed folders.
    $denyTargets = @()
    foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
        if ($drive.DriveType -eq 'Fixed' -and $drive.IsReady) { $denyTargets += $drive.RootDirectory.FullName }
    }
    $denyTargets += (Join-Path $env:SystemDrive 'Users')
    foreach ($t in ($denyTargets | Select-Object -Unique)) {
        if ($script:DryRun) { Write-Dry ("icacls {0} /deny {1}:(RD)" -f $t, $StudentUser); continue }
        try {
            & icacls.exe $t /deny ("{0}:(RD)" -f $StudentUser) | Out-Null
            Write-Ok ("Просмотр запрещён: {0}" -f $t)
        } catch { Write-Warn2 ("{0}: {1}" -f $t, $_.Exception.Message) }
    }
}

# ---------------------------------------------------------------- 6. browsers

Write-Step '6/9  Браузеры: Chrome, Edge, Firefox'
# The lists that ship with the program (refreshed by every update) plus the administrator's own additions.
$shippedCfg = Join-Path $InstallDir 'config'
if (-not (Test-Path -LiteralPath $shippedCfg)) { $shippedCfg = Join-Path $PackageRoot 'config' }
$blocklist = @((Read-DomainList (Join-Path $shippedCfg 'blocklist.txt')) + (Read-DomainList (Join-Path $DataDir 'blocklist.txt'))) |
    Where-Object { $_ -notmatch '\*' } | Select-Object -Unique
$whitelist = @((Read-DomainList (Join-Path $shippedCfg 'whitelist.txt')) + (Read-DomainList (Join-Path $DataDir 'whitelist.txt'))) |
    Where-Object { $_ -notmatch '\*' } | Select-Object -Unique
# Games: while "AllowGames" is false in config.json the game sites (Yandex Games first) join the block list; the administrator's
# exceptions become an allow list (they win over the block list in Chrome/Edge/Firefox).
$allowGames = $false
$gameExceptions = @()
try {
    $cfgObj = Get-Content -LiteralPath (Join-Path $DataDir 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($cfgObj.AllowGames -eq $true) { $allowGames = $true }
    if ($cfgObj.GameExceptions) { $gameExceptions = @($cfgObj.GameExceptions | ForEach-Object { ([string]$_).Trim().ToLowerInvariant() } | Where-Object { $_ -and $_ -notmatch '\*' }) }
} catch { Write-Warn2 ('Не прочитан config.json (игры считаются запрещёнными): ' + $_.Exception.Message) }
$games = @()
if (-not $allowGames) {
    $games = @((Read-DomainList (Join-Path $shippedCfg 'games.txt')) + (Read-DomainList (Join-Path $DataDir 'games.txt'))) |
        Where-Object { $_ -notmatch '\*' } | Select-Object -Unique
}
# Chrome and Edge accept about 1000 entries in URLBlocklist. The built-in browser reads the full lists itself.
# The 18+ list goes first, then the games (Yandex Games sits at the top of games.txt), so the most important entries always fit.
$policyLimit = 980
$blocklist = @(@($blocklist) + @($games) | Select-Object -Unique)
if (@($blocklist).Count -gt $policyLimit) {
    Write-Warn2 ("В списках {0} сайтов; Chrome/Edge получат первые {1} (встроенный браузер использует все)." -f @($blocklist).Count, $policyLimit)
    $blocklist = @($blocklist | Select-Object -First $policyLimit)
}
$blocklist = @($blocklist)
$whitelist = @($whitelist)
$allowEntries = @(@($whitelist) + @($gameExceptions) | Select-Object -Unique)

function Set-ChromiumPolicy {
    param([string]$Root, [bool]$IsEdge)
    $prefix = if ($IsEdge) { 'edge' } else { 'chrome' }
    Set-Reg $Root 'ForceGoogleSafeSearch' 1
    Set-Reg $Root 'ForceYouTubeRestrict' 2
    if ($IsEdge) {
        Set-Reg $Root 'ForceBingSafeSearch' 2
        Set-Reg $Root 'InPrivateModeAvailability' 1
        Set-Reg $Root 'BrowserAddProfileEnabled' 0
    } else {
        Set-Reg $Root 'IncognitoModeAvailability' 1
        Set-Reg $Root 'BrowserAddPersonEnabled' 0
    }
    Set-Reg $Root 'BrowserGuestModeEnabled' 0
    Set-Reg $Root 'DeveloperToolsAvailability' 2
    Set-Reg $Root 'DnsOverHttpsMode' 'off' 'String'
    Set-Reg $Root 'AllowDeletingBrowserHistory' 0
    Set-Reg $Root 'SyncDisabled' 1
    Set-Reg $Root 'BrowserSignin' 0
    switch ($Downloads) {
        'BlockAll'  { Set-Reg $Root 'DownloadRestrictions' 3 }
        'Dangerous' { Set-Reg $Root 'DownloadRestrictions' 1 }
        'Off'       { Remove-RegValue $Root 'DownloadRestrictions' }
    }
    Set-RegList ($Root + '\ExtensionInstallBlocklist') @('*')

    $block = @('file://*', 'view-source:*', 'javascript:*',
        ($prefix + '://settings'), ($prefix + '://flags'), ($prefix + '://extensions'), ($prefix + '://inspect'),
        ($prefix + '://net-internals'), ($prefix + '://policy'))
    $block += $blocklist
    if ($WebMode -eq 'Whitelist') {
        $block += '*'
        Set-RegList ($Root + '\URLAllowlist') $allowEntries
    } else {
        Set-RegList ($Root + '\URLAllowlist') $gameExceptions
    }
    Set-RegList ($Root + '\URLBlocklist') $block
}
Set-ChromiumPolicy 'HKLM:\SOFTWARE\Policies\Google\Chrome' $false
Set-ChromiumPolicy 'HKLM:\SOFTWARE\Policies\Microsoft\Edge' $true

$ff = 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox'
Set-Reg $ff 'DisablePrivateBrowsing' 1
Set-Reg $ff 'DisableDeveloperTools' 1
Set-Reg $ff 'BlockAboutConfig' 1
Set-Reg ($ff + '\DNSOverHTTPS') 'Enabled' 0
Set-Reg ($ff + '\DNSOverHTTPS') 'Locked' 1
Set-Reg $ff 'ExtensionSettings' '{"*":{"installation_mode":"blocked"}}' 'String'
function ConvertTo-FirefoxPattern([string]$Entry) {
    # "domain" -> any scheme, the domain and its sub-domains ('*.' also matches the bare domain); "domain/path" -> that section of the site
    $i = $Entry.IndexOf('/')
    if ($i -lt 0) { return ('*://*.' + $Entry + '/*') }
    return ('*://*.' + $Entry.Substring(0, $i) + $Entry.Substring($i).TrimEnd('/') + '*')
}
$ffBlock = @('file:///*')
foreach ($d in $blocklist) { $ffBlock += (ConvertTo-FirefoxPattern $d) }
$ffGameAllow = @()
foreach ($d in $gameExceptions) { $ffGameAllow += (ConvertTo-FirefoxPattern $d) }
if ($WebMode -eq 'Whitelist') {
    Set-RegList ($ff + '\WebsiteFilter\Block') @('<all_urls>')
    $ffAllow = @()
    foreach ($d in $allowEntries) { $ffAllow += (ConvertTo-FirefoxPattern $d) }
    Set-RegList ($ff + '\WebsiteFilter\Exceptions') $ffAllow
} else {
    Set-RegList ($ff + '\WebsiteFilter\Block') $ffBlock
    Set-RegList ($ff + '\WebsiteFilter\Exceptions') $ffGameAllow
}
Write-Ok ("Режим интернета: {0}; скачивания: {1}; инкогнито, расширения, DoH, инструменты разработчика выключены." -f $WebMode, $Downloads)
if ($allowGames) { Write-Ok 'Игры разрешены: игровые сайты в политики не вошли.' } else { Write-Ok ("Игры запрещены: в политики вошло игровых сайтов - {0} (Яндекс Игры первыми); исключений: {1}." -f @($games).Count, @($gameExceptions).Count) }

# ---------------------------------------------------------------- 7. DNS

Write-Step '7/9  Семейный DNS-фильтр'
$dnsV4 = @{
    AdGuardFamily    = @('94.140.14.15', '94.140.15.16')
    Yandex           = @('77.88.8.7', '77.88.8.3')
    Cloudflare       = @('1.1.1.3', '1.0.0.3')
    CleanBrowsing    = @('185.228.168.168', '185.228.169.168')
    YandexCloudflare = @('77.88.8.7', '1.1.1.3')
}
$dnsV6 = @{
    AdGuardFamily    = @('2a10:50c0::bad1:ff', '2a10:50c0::bad2:ff')
    Yandex           = @('2a02:6b8::feed:a11', '2a02:6b8:0:1::feed:a11')
    Cloudflare       = @('2606:4700:4700::1113', '2606:4700:4700::1003')
    CleanBrowsing    = @('2a0d:2a00:1::', '2a0d:2a00:2::')
    YandexCloudflare = @('2a02:6b8::feed:a11', '2606:4700:4700::1113')
}

function ConvertTo-IpString([uint64]$n) {
    $a = [int]([math]::Floor($n / 16777216))
    $b = [int]([math]::Floor($n / 65536)) % 256
    $c = [int]([math]::Floor($n / 256)) % 256
    $d = [int]($n % 256)
    return ('{0}.{1}.{2}.{3}' -f $a, $b, $c, $d)
}

function Get-ComplementRanges([string[]]$Addresses) {
    # "Everything except these IPv4 addresses" as a list of ranges (Windows Firewall cannot negate an address).
    $nums = @()
    foreach ($ip in $Addresses) {
        $by = [System.Net.IPAddress]::Parse($ip).GetAddressBytes()
        $nums += ([uint64]$by[0] * 16777216 + [uint64]$by[1] * 65536 + [uint64]$by[2] * 256 + [uint64]$by[3])
    }
    $nums = @($nums | Sort-Object -Unique)
    $ranges = @()
    [uint64]$start = 0
    foreach ($n in $nums) {
        if ($n -gt $start) { $ranges += ((ConvertTo-IpString $start) + '-' + (ConvertTo-IpString ($n - 1))) }
        $start = $n + 1
    }
    if ($start -le 4294967295) { $ranges += ((ConvertTo-IpString $start) + '-255.255.255.255') }
    return $ranges
}

if ($Dns -eq 'None') {
    Write-Warn2 'DNS-фильтр пропущен (-Dns None).'
} else {
    $v4 = $dnsV4[$Dns]
    $v6 = $dnsV6[$Dns]
    if ($script:DryRun) {
        Write-Dry ("DNS на всех физических адаптерах: {0} / {1}" -f ($v4 -join ', '), ($v6 -join ', '))
    } else {
        foreach ($nic in @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue)) {
            try { Set-DnsClientServerAddress -InterfaceIndex $nic.ifIndex -ServerAddresses $v4 } catch { Write-Warn2 ("IPv4 DNS на {0}: {1}" -f $nic.Name, $_.Exception.Message) }
            try { Set-DnsClientServerAddress -InterfaceIndex $nic.ifIndex -ServerAddresses ($v4 + $v6) } catch { }
            Write-Ok ("DNS на адаптере '{0}'" -f $nic.Name)
        }
    }

    if (-not $NoDnsFirewall) {
        $keep = @($v4) + '127.0.0.1'
        $ranges = Get-ComplementRanges $keep
        $dohIps = @('1.1.1.1', '1.0.0.1', '8.8.8.8', '8.8.4.4', '9.9.9.9', '149.112.112.112', '208.67.222.222',
            '208.67.220.220', '94.140.14.14', '94.140.15.15', '76.76.2.0', '76.76.10.0', '45.90.28.0', '45.90.30.0')
        $dohIps = @($dohIps | Where-Object { $v4 -notcontains $_ })
        if ($script:DryRun) {
            Write-Dry ('брандмауэр: блок DNS (53) кроме ' + ($keep -join ', ') + ', блок DoT (853) и DoH-серверов')
        } else {
            Get-NetFirewallRule -Group 'SchoolShield' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
            New-NetFirewallRule -DisplayName 'SchoolShield: DNS UDP' -Group 'SchoolShield' -Direction Outbound -Action Block `
                -Protocol UDP -RemotePort 53 -RemoteAddress $ranges -Profile Any | Out-Null
            New-NetFirewallRule -DisplayName 'SchoolShield: DNS TCP' -Group 'SchoolShield' -Direction Outbound -Action Block `
                -Protocol TCP -RemotePort 53 -RemoteAddress $ranges -Profile Any | Out-Null
            New-NetFirewallRule -DisplayName 'SchoolShield: DNS-over-TLS' -Group 'SchoolShield' -Direction Outbound -Action Block `
                -Protocol TCP -RemotePort 853 -Profile Any | Out-Null
            New-NetFirewallRule -DisplayName 'SchoolShield: public DoH servers' -Group 'SchoolShield' -Direction Outbound -Action Block `
                -Protocol TCP -RemotePort 443 -RemoteAddress $dohIps -Profile Any | Out-Null
            Write-Ok 'Брандмауэр: DNS разрешён только на фильтрующие серверы; DoT и публичные DoH закрыты.'
        }
    } else {
        Write-Warn2 'Правила брандмауэра против обхода DNS пропущены (-NoDnsFirewall).'
    }
}

# ---------------------------------------------------------------- 8. boot hardening

Write-Step '8/9  Защита загрузки'
if ($HardenBoot) {
    if ($script:DryRun) { Write-Dry 'bcdedit: recoveryenabled No, bootstatuspolicy IgnoreAllFailures' }
    else {
        & bcdedit.exe /set '{current}' recoveryenabled No | Out-Null
        & bcdedit.exe /set '{current}' bootstatuspolicy IgnoreAllFailures | Out-Null
        Write-Ok 'Меню автовосстановления при сбоях питания отключено.'
    }
} else {
    Write-Host '  пропущено (включается ключом -HardenBoot)'
}
Write-Warn2 'Вручную: пароль на BIOS/UEFI и запрет загрузки с USB - иначе ученик может загрузить свою систему.'

# ---------------------------------------------------------------- 9. shell

Write-Step '9/9  Оболочка Windows'
$exeInstalled = Join-Path $InstallDir $ExeName
if ($NoShell) {
    Write-Warn2 'Оболочка НЕ изменена (-NoShell). Проверьте лаунчер: SchoolShield.exe --preview'
} elseif ($script:DryRun) {
    Write-Dry ("Winlogon\Shell = `"{0}`"" -f $exeInstalled)
} else {
    $current = (Get-ItemProperty -LiteralPath $Winlogon -Name 'Shell' -ErrorAction SilentlyContinue).Shell
    if (-not (Test-Path -LiteralPath $OwnKey)) { New-Item -Path $OwnKey -Force | Out-Null }
    if ($current -and $current -notlike '*SchoolShield*') {
        Set-Reg $OwnKey 'OriginalShell' $current 'String'
    } elseif (-not (Get-ItemProperty -LiteralPath $OwnKey -Name 'OriginalShell' -ErrorAction SilentlyContinue)) {
        Set-Reg $OwnKey 'OriginalShell' 'explorer.exe' 'String'
    }
    Set-Reg $OwnKey 'InstallDir' $InstallDir 'String'
    Set-Reg $OwnKey 'StudentUser' $StudentUser 'String'
    Set-Reg $OwnKey 'Version' '2.5.2' 'String'
    Set-Reg $Winlogon 'Shell' ('"' + $exeInstalled + '"') 'String'
    Write-Ok 'Лаунчер зарегистрирован как оболочка. Администраторам он откроет обычный рабочий стол.'
}

if (-not $script:DryRun) {
    if (-not (Test-Path -LiteralPath $OwnKey)) { New-Item -Path $OwnKey -Force | Out-Null }
    Set-Reg $OwnKey 'Configured' 1
}

# ---------------------------------------------------------------- summary

Write-Host ''
Write-Host '================ ГОТОВО ================' -ForegroundColor Green
if ($script:DryRun) { Write-Host 'Это был DryRun: ничего не изменено.' -ForegroundColor Yellow; return }
Write-Host 'Дальше:'
Write-Host '  1. Перезагрузите ПК. Он сам войдёт под учеником и откроет лаунчер.'
Write-Host '  2. Скрытая комбинация открывает терминал администратора: Ctrl+Shift+Alt+F12, затем ваш код.'
Write-Host '  3. Команда  logoff  в терминале - выход на экран входа, где выберите свою учётную запись администратора.'
Write-Host ''
Write-Host 'Если что-то пошло не так (чёрный экран, нет входа): см. docs\README-RU.md, раздел «Аварийное восстановление».' -ForegroundColor Yellow
Write-Host 'Отключить защиту можно в панели администратора (значок SchoolShield) или через «Установка и удаление программ».'
