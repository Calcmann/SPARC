# SPARC NetworkDevice Launcher & Auto-Updater - sempre carrega versao atualizada ao iniciar
param ([switch]$ForceRebuild)

$ErrorActionPreference = "Continue"
$sparcDir = if (Test-Path "C:\SPARC") { "C:\SPARC" } else { (Resolve-Path "$PSScriptRoot\..").Path }
$uiExe = "$sparcDir\src\NetworkDevice.UI\bin\Release\net8.0-windows\NetworkDevice.UI.exe"
$logFile = "$sparcDir\scripts\launcher.log"
Set-Location $sparcDir

function Log($msg, $color="Gray") { $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"; "$ts $msg" | Out-File -Append -FilePath $logFile -Encoding utf8; Write-Host $msg -ForegroundColor $color }

# 0. Auto-repara atalho no Desktop a cada inicio (garante que o duplo-clique sempre passa por este launcher)
try {
    $wsh = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = "$desktop\NetworkDevice.lnk"
    $launcherArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$sparcDir\scripts\launch_with_update.ps1`""
    $needsFix = $true
    if (Test-Path $shortcutPath) {
        $sc = $wsh.CreateShortcut($shortcutPath)
        if ($sc.Arguments -eq $launcherArgs -and (Test-Path $sc.TargetPath)) { $needsFix = $false }
    }
    if ($needsFix) {
        $sc2 = $wsh.CreateShortcut($shortcutPath)
        $sc2.TargetPath = "powershell.exe"
        $sc2.Arguments = $launcherArgs
        $sc2.WorkingDirectory = $sparcDir
        $sc2.IconLocation = if (Test-Path $uiExe) { "$uiExe,0" } else { "shell32.dll,21" }
        $sc2.Description = "SPARC Network Device (auto-atualizado ao iniciar)"
        $sc2.Save()
        Log "[*] Atalho do Desktop recriado/corrigido: $shortcutPath" Cyan
    }
} catch { Log "[WARN] Falha ao verificar atalho: $_" Yellow }

# 1. Git pull --ff-only se houver remoto (traz ultima versao commitada)
$pulled = $false
if (Test-Path "$sparcDir\.git") {
    try {
        $hasRemote = git remote 2>$null
        if ($hasRemote) {
            Log "[*] Verificando atualizacoes no Git remoto..." Cyan
            git fetch --all --prune 2>&1 | Out-Null
            $before = git rev-parse HEAD 2>$null
            git pull --ff-only 2>&1 | Tee-Object -Variable pullOut | Out-Null
            $after = git rev-parse HEAD 2>$null
            if ($before -ne $after) { $pulled = $true; Log "[*] Novos commits baixados: $before -> $after" Green }
            elseif ($pullOut -match "Already up to date") { Log "[*] Git ja atualizado." Gray }
        }
    } catch { Log "[WARN] Git pull falhou: $_" Yellow }
}

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
    # Encerra qualquer instancia aberta para liberar arquivos binarios (trata processos elevados)
    taskkill /F /IM NetworkDevice.UI.exe 2>$null | Out-Null
    Start-Sleep -Milliseconds 500
    $runningUis = Get-Process -Name "NetworkDevice.UI" -ErrorAction SilentlyContinue
    if ($runningUis) {
        try {
            $runningUis | ForEach-Object { $_.Kill(); $_.WaitForExit(2000) }
        } catch {
            Log "[*] Encerrando processo elevado via RunAs..." Yellow
            Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command Stop-Process -Name NetworkDevice.UI -Force" -Wait -WindowStyle Hidden
        }
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
