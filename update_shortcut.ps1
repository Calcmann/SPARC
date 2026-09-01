$wsh = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = "$desktop\SPARC.lnk"
$uiExe = "C:\SPARC\src\NetworkDevice.UI\bin\Release\net8.0-windows\NetworkDevice.UI.exe"
$iconPath = "C:\SPARC\src\NetworkDevice.UI\Assets\sparc.ico"

$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\SPARC\scripts\launch_with_update.ps1"'
$shortcut.WorkingDirectory = "C:\SPARC"
$shortcut.IconLocation = if (Test-Path $iconPath) { $iconPath } elseif (Test-Path $uiExe) { "$uiExe,0" } else { "shell32.dll,21" }
$shortcut.Description = "SPARC - Sistema de Provisionamento e Ativacao de Roteadores Claro"
$shortcut.Save()

# Remove atalho legado
$legacy = "$desktop\NetworkDevice.lnk"
if (Test-Path $legacy) { Remove-Item $legacy -Force -ErrorAction SilentlyContinue }

Write-Host "[OK] Atalho SPARC configurado com icone oficial!" -ForegroundColor Green
Write-Host "  Caminho: $shortcutPath" -ForegroundColor Cyan
Write-Host "  Icone:   $($shortcut.IconLocation)" -ForegroundColor Yellow
