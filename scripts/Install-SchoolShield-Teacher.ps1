<#
.SYNOPSIS
    SchoolShield, редакция «Учитель»: включает ТОЛЬКО фильтр материалов 18+ (порно, эротика).

.DESCRIPTION
    Windows остаётся обычной: рабочий стол, установка программ, USB, командная строка, диспетчер задач - всё как было.
    Скрипт делает пять вещей (повторный запуск безопасен):
      1. настраивает папку C:\ProgramData\SchoolShield (редакция «teacher», файлы «мои добавки»);
      2. убирает следы ученической редакции, если она стояла на этом ПК (оболочка Windows, SRP, запреты, автовход);
      3. задаёт Chrome / Edge / Firefox: SafeSearch и список закрытых 18+ сайтов (без запрета инкогнито, скачиваний, расширений);
      4. включает семейный DNS-фильтр (по умолчанию AdGuard Family);
      5. регистрирует автозапуск охраны окон и экрана (значок в области уведомлений).
    Запускать от имени администратора. Установщик и панель управления вызывают его сами.

.PARAMETER Dns            AdGuardFamily (по умолчанию) | YandexCloudflare | Yandex | Cloudflare | CleanBrowsing | None
.PARAMETER DnsFirewall    Дополнительно закрыть обход DNS брандмауэром (по умолчанию выключено: ломает корпоративный VPN и DoH).
.PARAMETER NoAutostart    Не регистрировать автозапуск охраны.
.PARAMETER NonInteractive Не задавать вопросов (запуск из установщика и панели).
.PARAMETER DryRun         Только показать, что будет сделано.
#>
[CmdletBinding()]
param(
    [ValidateSet('AdGuardFamily', 'YandexCloudflare', 'Yandex', 'Cloudflare', 'CleanBrowsing', 'None')][string]$Dns = 'AdGuardFamily',
    [switch]$DnsFirewall,
    [switch]$NoAutostart,
    [switch]$NonInteractive,
    [switch]$DryRun
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ErrorActionPreference = 'Stop'

$script:DryRun = [bool]$DryRun
$InstallDir = Join-Path $env:ProgramFiles 'SchoolShield'
$DataDir    = Join-Path $env:ProgramData 'SchoolShield'
$ExeInstalled = Join-Path $InstallDir 'SchoolShield.exe'
$Winlogon   = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$OwnKey     = 'HKLM:\SOFTWARE\SchoolShield'
$RunKey     = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'

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
    if ($script:DryRun) { return }
    if (Test-Path -LiteralPath $Path) { Remove-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue }
}

function Remove-RegKey {
    param([string]$Path)
    if ($script:DryRun) { return }
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue }
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

function Read-DomainList([string]$File, [bool]$StopAtStudentOnly) {
    if (-not (Test-Path -LiteralPath $File)) { return @() }
    $list = @()
    foreach ($line in (Get-Content -LiteralPath $File -Encoding UTF8)) {
        $t = $line.Trim()
        if ($StopAtStudentOnly -and $t.StartsWith('# @student-only')) { break }
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $list += $t.ToLowerInvariant()
    }
    return $list
}

function Set-JsonField([string]$Json, [string]$Name, [string]$Value, [bool]$IsString) {
    $pattern = '"' + $Name + '"\s*:\s*(?:"[^"]*"|-?[0-9.]+)'
    if ($IsString) { $repl = '"' + $Name + '": "' + $Value + '"' } else { $repl = '"' + $Name + '": ' + $Value }
    $re = New-Object System.Text.RegularExpressions.Regex($pattern)
    if ($re.IsMatch($Json)) { return $re.Replace($Json, $repl, 1) }
    $i = $Json.IndexOf('{')
    return $Json.Substring(0, $i + 1) + "`n  " + $repl + "," + $Json.Substring($i + 1)
}

# ---------------------------------------------------------------- 0. preconditions

Write-Host ''
Write-Host 'SchoolShield (редакция «Учитель») - включение фильтра 18+' -ForegroundColor White
if ($script:DryRun) { Write-Warn2 'РЕЖИМ DryRun: ничего не будет изменено.' }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Нужны права администратора.'
}
if ([Environment]::OSVersion.Version.Major -lt 10) { throw 'Нужна Windows 10 или новее.' }
if (-not (Test-Path -LiteralPath $ExeInstalled)) { throw "Не найден $ExeInstalled - сначала установите SchoolShield." }

# ---------------------------------------------------------------- 1. files

Write-Step '1/5  Настройки'
$cfgDir = Join-Path $InstallDir 'config'
if ($script:DryRun) {
    Write-Dry "настройки в $DataDir, редакция teacher"
} else {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $DataDir 'logs') -Force | Out-Null
    $cfgPath = Join-Path $DataDir 'config.json'
    if (-not (Test-Path -LiteralPath $cfgPath)) { Copy-Item -LiteralPath (Join-Path $cfgDir 'config.json') -Destination $cfgPath -Force }

    $json = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8
    $json = Set-JsonField $json 'Edition' 'teacher' $true
    [IO.File]::WriteAllText($cfgPath, $json, (New-Object System.Text.UTF8Encoding($false)))

    # Student copies of the lists are replaced by empty "my additions" files; the originals are kept next to them.
    $blockData = Join-Path $DataDir 'blocklist.txt'
    if ((Test-Path -LiteralPath $blockData) -and ((Get-Item -LiteralPath $blockData).Length -gt 20000)) {
        $bak = Join-Path $DataDir 'blocklist.student-backup.txt'
        if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Force }
        Move-Item -LiteralPath $blockData -Destination $bak
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    if (-not (Test-Path -LiteralPath $blockData)) {
        [IO.File]::WriteAllText($blockData, "# Ваши дополнительные закрытые сайты, по одному в строке (поддомены закрываются тоже).`n# Основной список идёт с программой.`n", $utf8)
    }
    $wordsExtra = Join-Path $DataDir 'badwords-extra.txt'
    if (-not (Test-Path -LiteralPath $wordsExtra)) {
        [IO.File]::WriteAllText($wordsExtra, "# Ваши дополнительные запрещённые слова в названиях окон: одно регулярное выражение в строке.`n# Основной список идёт с программой.`n", $utf8)
    }

    # ProgramData\SchoolShield: everybody reads, only administrators write; logs: everybody may append.
    & icacls.exe $DataDir /inheritance:r | Out-Null
    & icacls.exe $DataDir /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    & icacls.exe (Join-Path $DataDir 'logs') /grant '*S-1-5-32-545:(OI)(CI)(RX,WD,AD)' | Out-Null
}
Write-Ok "Настройки: $DataDir"

# ---------------------------------------------------------------- 2. leftovers of the student edition

Write-Step '2/5  Следы ученической редакции'
$wasStudent = $false
if (-not $script:DryRun) {
    $shell = (Get-ItemProperty -LiteralPath $Winlogon -Name 'Shell' -ErrorAction SilentlyContinue).Shell
    if ($shell -and $shell -like '*SchoolShield*') {
        $original = 'explorer.exe'
        $o = Get-ItemProperty -LiteralPath $OwnKey -Name 'OriginalShell' -ErrorAction SilentlyContinue
        if ($o -and $o.OriginalShell) { $original = $o.OriginalShell }
        Set-ItemProperty -LiteralPath $Winlogon -Name 'Shell' -Value $original -Type String
        Set-ItemProperty -LiteralPath $Winlogon -Name 'AutoAdminLogon' -Value '0' -Type String
        Remove-ItemProperty -LiteralPath $Winlogon -Name 'DefaultPassword' -ErrorAction SilentlyContinue
        $wasStudent = $true
        Write-Ok "Оболочка Windows возвращена: $original"
    }
    $srp = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers'
    if (Test-Path -LiteralPath $srp) { Remove-Item -LiteralPath $srp -Recurse -Force; $wasStudent = $true; Write-Ok 'Запрет запуска программ (SRP) снят.' }
    if ($wasStudent) {
        foreach ($sv in @(
            @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer', 'DisableUserInstalls'),
            @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer', 'AlwaysInstallElevated'),
            @('HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore', 'RemoveWindowsStore'),
            @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableWindowsConsumerFeatures'),
            @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableSoftLanding'),
            @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive', 'DisableFileSyncNGSC'),
            @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoDriveTypeAutoRun'),
            @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', 'HideFastUserSwitching'))) {
            Remove-RegValue $sv[0] $sv[1]
        }
        $studentUser = 'Student'
        $v = Get-ItemProperty -LiteralPath $OwnKey -Name 'StudentUser' -ErrorAction SilentlyContinue
        if ($v -and $v.StudentUser) { $studentUser = $v.StudentUser }
        foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
            if ($drive.DriveType -eq 'Fixed' -and $drive.IsReady) { & icacls.exe $drive.RootDirectory.FullName /remove:d $studentUser 2>$null | Out-Null }
        }
        & icacls.exe (Join-Path $env:SystemDrive 'Users') /remove:d $studentUser 2>$null | Out-Null
        Write-Ok 'Запреты ученической редакции убраны.'
    }
}
if (-not $wasStudent) { Write-Host '  не требуется' }

# ---------------------------------------------------------------- 3. browsers

Write-Step '3/5  Браузеры: Chrome, Edge, Firefox'
# The 18+ lists that ship with the program (read up to the "student-only" marker) plus the teacher's own additions.
$blocklist = @((Read-DomainList (Join-Path $cfgDir 'blocklist.txt') $true) + (Read-DomainList (Join-Path $DataDir 'blocklist.txt') $false)) |
    Where-Object { $_ -notmatch '\*' } | Select-Object -Unique
# Chrome and Edge accept about 1000 entries in URLBlocklist; the rest is covered by the DNS filter and the guard.
$policyLimit = 900
if (@($blocklist).Count -gt $policyLimit) {
    Write-Warn2 ("В списке {0} сайтов; Chrome/Edge получат первые {1}, остальное закроют DNS-фильтр и охрана." -f @($blocklist).Count, $policyLimit)
    $blocklist = @($blocklist | Select-Object -First $policyLimit)
}
$blocklist = @($blocklist)

# Values that the student edition set and the teacher edition must not leave behind.
$chromiumStudentOnly = @('ForceYouTubeRestrict', 'IncognitoModeAvailability', 'InPrivateModeAvailability', 'BrowserAddPersonEnabled',
    'BrowserAddProfileEnabled', 'BrowserGuestModeEnabled', 'DeveloperToolsAvailability', 'DnsOverHttpsMode',
    'AllowDeletingBrowserHistory', 'SyncDisabled', 'BrowserSignin', 'DownloadRestrictions')

function Set-TeacherChromium {
    param([string]$Root, [bool]$IsEdge)
    foreach ($n in $chromiumStudentOnly) { Remove-RegValue $Root $n }
    Remove-RegKey ($Root + '\ExtensionInstallBlocklist')
    Remove-RegKey ($Root + '\URLAllowlist')
    Set-Reg $Root 'ForceGoogleSafeSearch' 1
    if ($IsEdge) { Set-Reg $Root 'ForceBingSafeSearch' 2 }
    Set-RegList ($Root + '\URLBlocklist') $blocklist
}
Set-TeacherChromium 'HKLM:\SOFTWARE\Policies\Google\Chrome' $false
Set-TeacherChromium 'HKLM:\SOFTWARE\Policies\Microsoft\Edge' $true

$ff = 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox'
foreach ($n in 'DisablePrivateBrowsing', 'DisableDeveloperTools', 'BlockAboutConfig', 'ExtensionSettings') { Remove-RegValue $ff $n }
Remove-RegKey ($ff + '\DNSOverHTTPS')
Remove-RegKey ($ff + '\WebsiteFilter\Exceptions')
$ffBlock = @()
foreach ($d in $blocklist) { if ($d -notmatch '/') { $ffBlock += ('*://*.' + $d + '/*') } }   # '*.' also matches the bare domain
Set-RegList ($ff + '\WebsiteFilter\Block') $ffBlock
Write-Ok ("Закрыто сайтов 18+ в браузерах: {0}; SafeSearch включён. Инкогнито, скачивания, расширения - без ограничений." -f @($blocklist).Count)

# ---------------------------------------------------------------- 4. DNS

Write-Step '4/5  Семейный DNS-фильтр'
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

if (-not $script:DryRun) { Get-NetFirewallRule -Group 'SchoolShield' -ErrorAction SilentlyContinue | Remove-NetFirewallRule }

if ($Dns -eq 'None') {
    Write-Warn2 'DNS-фильтр пропущен (-Dns None). Возвращаю автоматическое получение DNS.'
    if (-not $script:DryRun) {
        foreach ($nic in @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue)) {
            try { Set-DnsClientServerAddress -InterfaceIndex $nic.ifIndex -ResetServerAddresses } catch { }
        }
    }
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
    if ($DnsFirewall) {
        $ranges = Get-ComplementRanges (@($v4) + '127.0.0.1')
        if (-not $script:DryRun) {
            New-NetFirewallRule -DisplayName 'SchoolShield: DNS UDP' -Group 'SchoolShield' -Direction Outbound -Action Block `
                -Protocol UDP -RemotePort 53 -RemoteAddress $ranges -Profile Any | Out-Null
            New-NetFirewallRule -DisplayName 'SchoolShield: DNS TCP' -Group 'SchoolShield' -Direction Outbound -Action Block `
                -Protocol TCP -RemotePort 53 -RemoteAddress $ranges -Profile Any | Out-Null
            Write-Ok 'Брандмауэр: обычный DNS разрешён только на фильтрующие серверы.'
        }
    } else {
        Write-Host '  правила брандмауэра против обхода DNS не создаются (корпоративный VPN и DoH продолжат работать)'
    }
}

# ---------------------------------------------------------------- 5. autostart

Write-Step '5/5  Автозапуск охраны'
if ($NoAutostart) {
    Write-Warn2 'Автозапуск пропущен (-NoAutostart).'
} else {
    Set-Reg $RunKey 'SchoolShield' ('"' + $ExeInstalled + '" --teacher-guard') 'String'
    Write-Ok 'Охрана окон и экрана стартует при входе в Windows (значок SchoolShield в области уведомлений).'
}

if (-not $script:DryRun) {
    if (-not (Test-Path -LiteralPath $OwnKey)) { New-Item -Path $OwnKey -Force | Out-Null }
    Set-Reg $OwnKey 'InstallDir' $InstallDir 'String'
    Set-Reg $OwnKey 'Edition' 'teacher' 'String'
    Set-Reg $OwnKey 'Version' '2.3.0' 'String'
    Set-Reg $OwnKey 'Configured' 1
}

Write-Host ''
Write-Host '================ ГОТОВО ================' -ForegroundColor Green
if ($script:DryRun) { Write-Host 'Это был DryRun: ничего не изменено.' -ForegroundColor Yellow; return }
Write-Host 'Закрыт только материал 18+. Установка программ и остальное - без ограничений.'
Write-Host 'Браузеры подхватят политики после перезапуска. Охрана запустится при следующем входе или через «Центр учителя».'
