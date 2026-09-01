# SPARC NetworkDevice Launcher & Auto-Updater - sempre carrega versao atualizada ao iniciar
param ([switch]$ForceRebuild)

$ErrorActionPreference = "Continue"
$sparcDir = if (Test-Path "C:\SPARC") { "C:\SPARC" } else { (Resolve-Path "$PSScriptRoot\..").Path }
$uiExe = "$sparcDir\src\NetworkDevice.UI\bin\Release\net8.0-windows\NetworkDevice.UI.exe"
$logFile = "$sparcDir\scripts\launcher.log"
Set-Location $sparcDir

function Log($msg, $color="Gray") { $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"; "$ts $msg" | Out-File -Append -FilePath $logFile -Encoding utf8; Write-Host $msg -ForegroundColor $color }

# 0. Auto-repara atalho SPARC no Desktop e remove legado NetworkDevice.lnk
try {
    $wsh = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath("Desktop")
    $sparcLink = "$desktop\SPARC.lnk"
    $legacyLink = "$desktop\NetworkDevice.lnk"
    if (Test-Path $legacyLink) { Remove-Item $legacyLink -Force -ErrorAction SilentlyContinue; Log "[*] Atalho legado removido: $legacyLink" Yellow }
    $launcherArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$sparcDir\scripts\launch_with_update.ps1`""
    $iconFile = "$sparcDir\src\NetworkDevice.UI\Assets\sparc.ico"
    $needsFix = $true
    if (Test-Path $sparcLink) {
        $sc = $wsh.CreateShortcut($sparcLink)
        if ($sc.Arguments -eq $launcherArgs -and (Test-Path $sc.TargetPath) -and $sc.IconLocation -eq $iconFile) { $needsFix = $false }
    }
    if ($needsFix) {
        $sc2 = $wsh.CreateShortcut($sparcLink)
        $sc2.TargetPath = "powershell.exe"
        $sc2.Arguments = $launcherArgs
        $sc2.WorkingDirectory = $sparcDir
        $sc2.IconLocation = if (Test-Path $iconFile) { $iconFile } elseif (Test-Path $uiExe) { "$uiExe,0" } else { "shell32.dll,21" }
        $sc2.Description = "SPARC - Sistema de Provisionamento e Ativacao de Roteadores Claro"
        $sc2.Save()
        Log "[*] Atalho SPARC recriado/corrigido com icone oficial: $sparcLink" Cyan
    }
} catch { Log "[WARN] Falha ao verificar atalho: $_" Yellow }

# 1. Git é apenas backup eventual - NÃO faz pull automático. Diretório local é a raiz do projeto.
# Backup para Git deve ser feito manualmente via: git add -A; git commit -m "backup"; git push
$pulled = $false
Log "[*] Modo local: Git não sincronizado automaticamente (backup eventual apenas)." Gray

# 2. Decide se precisa recompilar Release
$needBuild = $ForceRebuild -or -not (Test-Path $uiExe) -or $pulled
if (-not $needBuild) {
    $exeTime = (Get-Item $uiExe).LastWriteTime
    $coreDll = "$sparcDir\src\NetworkDevice.UI\bin\Release\net8.0-windows\NetworkDevice.Core.dll"
    $dllTime = if (Test-Path $coreDll) { (Get-Item $coreDll).LastWriteTime } else { [DateTime]::MinValue }
    $minBinTime = if ($exeTime -lt $dllTime) { $exeTime } else { $dllTime }

    $newestSource = Get-ChildItem -Path "$sparcDir\src" -Recurse -Include *.cs,*.xaml,*.csproj -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSource -and $newestSource.LastWriteTime -gt $minBinTime) {
        Log "[*] Codigo fonte mais recente que os binarios ($($newestSource.Name) $($newestSource.LastWriteTime) > $($minBinTime)). Recompilando..." Yellow
        $needBuild = $true
    } else {
        Log "[*] Nenhuma alteracao detectada. Usando build Release existente." Gray
    }
}

# 3. Compilacao Release automatica
if ($needBuild) {
    Log "[*] Compilando Release (dotnet build -c Release)..." Cyan
    # Encerra qualquer instancia aberta para liberar arquivos binarios — SEMPRE via taskkill elevado (processo roda como Admin)
    try { Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command taskkill /F /IM NetworkDevice.UI.exe 2>`$null; Start-Sleep 1; Get-Process -Name NetworkDevice.UI -ErrorAction SilentlyContinue | Stop-Process -Force" -Wait -WindowStyle Hidden } catch { }
    taskkill /F /IM NetworkDevice.UI.exe 2>$null | Out-Null
    Start-Sleep -Milliseconds 800
    $runningUis = Get-Process -Name "NetworkDevice.UI" -ErrorAction SilentlyContinue
    if ($runningUis) {
        try {
            $runningUis | ForEach-Object { $_.Kill(); $_.WaitForExit(2000) }
        } catch {
            Log "[*] Encerrando processo elevado via RunAs..." Yellow
            Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command Stop-Process -Name NetworkDevice.UI -Force" -Wait -WindowStyle Hidden
            Start-Sleep 1500
        }
    }
    # Aguarda liberação real do lock da DLL antes de compilar
    for ($i=0; $i -lt 5; $i++) {
        $stillRunning = Get-Process -Name "NetworkDevice.UI" -ErrorAction SilentlyContinue
        if (-not $stillRunning) { break }
        Log "[*] Aguardando encerramento do SPARC antigo (tentativa $($i+1)/5)..." Yellow
        Start-Sleep 1000
    }
    
    dotnet build "$sparcDir\NetworkDevice.sln" -c Release -v m --nologo 2>&1 | Out-File -Append -FilePath $logFile -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        Log "[*] Nova tentativa de compilacao Release..." Yellow
        taskkill /F /IM NetworkDevice.UI.exe 2>$null | Out-Null
        Start-Sleep -Seconds 1
        dotnet build "$sparcDir\NetworkDevice.sln" -c Release -v m --nologo 2>&1 | Out-File -Append -FilePath $logFile -Encoding utf8
    }

    if ($LASTEXITCODE -ne 0) {
        Log "[ERRO] Falha na compilacao Release (exit $LASTEXITCODE)." Red
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("Falha ao compilar a versao atualizada do SPARC.`n`nPor favor feche a janela do SPARC que esta aberta e tente novamente.`nLog: scripts/launcher.log","SPARC Launcher",0,16) | Out-Null
        exit $LASTEXITCODE
    } else {
        Log "[OK] Build Release concluido com sucesso." Green
    }
}

# 4. Inicia UI elevada como Administrador (necessário para netsh configurar IPs)
if (Test-Path $uiExe) {
    Log "[*] Iniciando $uiExe" Cyan
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        Start-Process -FilePath $uiExe -WorkingDirectory $sparcDir
    } else {
        try { Start-Process -FilePath $uiExe -WorkingDirectory $sparcDir -Verb RunAs } catch {
            Log "[WARN] UAC negado, iniciando sem elevação (IP pode falhar)" Yellow
            Start-Process -FilePath $uiExe -WorkingDirectory $sparcDir
        }
    }
} else {
    Log "[ERRO] Executavel nao encontrado: $uiExe" Red
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("Executavel nao encontrado:`n$uiExe`n`nExecute dotnet build -c Release manualmente.","SPARC Launcher",0,16) | Out-Null
    exit 1
}
