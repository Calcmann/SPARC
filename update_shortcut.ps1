$wsh = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = "$desktop\NetworkDevice.lnk"
$uiExe = "C:\Killtech\src\NetworkDevice.UI\bin\Release\net8.0-windows\NetworkDevice.UI.exe"

$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\Killtech\scripts\launch_with_update.ps1"'
$shortcut.WorkingDirectory = "C:\Killtech"
$shortcut.IconLocation = if (Test-Path $uiExe) { "$uiExe,0" } else { "shell32.dll,21" }
$shortcut.Description = "Killtech Network Device (auto-atualizado ao iniciar - sempre carrega ultima versao)"
$shortcut.Save()

Write-Host "[OK] Atalho na Area de Trabalho configurado com sucesso!" -ForegroundColor Green
Write-Host "  Caminho: $shortcutPath" -ForegroundColor Cyan
Write-Host "  Alvo: powershell.exe $($shortcut.Arguments)" -ForegroundColor DarkGray
Write-Host "  O atalho agora sempre executa launch_with_update.ps1 que faz git pull + dotnet build Release antes de abrir a UI." -ForegroundColor Yellow
