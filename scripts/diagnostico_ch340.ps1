# ==============================================================================
# SPARC - Script de Diagnostico e Resolucao para USB-SERIAL CH340 / CH341
# ==============================================================================
# Execucao: powershell -ExecutionPolicy Bypass -File .\diagnostico_ch340.ps1
# ==============================================================================

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "   🔍 SPARC — DIAGNOSTICO DO ADAPTADOR USB-SERIAL CH340 / CH341" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Identificar Placa-Mae e Chipset
Write-Host "[1/4] Verificando Placa-Mae e Controladora USB..." -ForegroundColor Yellow
try {
    $mb = Get-CimInstance Win32_BaseBoard | Select-Object -First 1 Manufacturer, Product
    Write-Host "  • Placa-Mae Detectada: $($mb.Manufacturer) $($mb.Product)" -ForegroundColor White
    if ($mb.Product -match "G41" -or $mb.Manufacturer -match "ECS") {
        Write-Host "  ⚠️ ATENCAO: Placa-mae com chipset Intel G41 / ICH7 detectada." -ForegroundColor Yellow
        Write-Host "     Recomendacao: Utilize SEMPRE as portas USB traseiras soldadas na placa-mae." -ForegroundColor Yellow
    }
} catch {
    Write-Host "  • Nao foi possivel consultar a placa-mae via WMI." -ForegroundColor Gray
}

# 2. Verificar Dispositivos WCH (CH340/CH341)
Write-Host "`n[2/4] Verificando Dispositivos WCH (VID: 1A86)..." -ForegroundColor Yellow
$wchDevices = Get-PnpDevice | Where-Object { $_.InstanceId -like '*1A86*' -or $_.FriendlyName -like '*CH34*' -or $_.FriendlyName -like '*USB-SERIAL*' }

if (-not $wchDevices) {
    Write-Host "  ❌ Nenhum adaptador CH340/CH341 (VID 1A86) foi encontrado no sistema." -ForegroundColor Red
    Write-Host "     • Verifique se o cabo USB esta conectado firmemente na porta USB traseira." -ForegroundColor Gray
} else {
    foreach ($dev in $wchDevices) {
        $statusColor = if ($dev.Status -eq "OK") { "Green" } else { "Red" }
        Write-Host "  -----------------------------------------------------------------" -ForegroundColor Gray
        Write-Host "  • Nome Amigavel : $($dev.FriendlyName)" -ForegroundColor White
        Write-Host "  • Classe Windows: $($dev.Class)" -ForegroundColor White
        Write-Host "  • ID Instancia  : $($dev.InstanceId)" -ForegroundColor White
        Write-Host "  • Status Atual  : $($dev.Status) (Presente: $($dev.Present))" -ForegroundColor $statusColor

        # Verificar modo do CH341
        if ($dev.InstanceId -match 'PID_5512') {
            Write-Host "  ⚠️ ALERTA CRITICO: Dispositivo detectado em modo PARALELO / GRAVADOR / I2C (PID 5512)!" -ForegroundColor Red
            Write-Host "     👉 Se o seu adaptador for o CH341A (placa preta/verde com jumper):" -ForegroundColor Yellow
            Write-Host "        - O jumper DEVE estar nos pinos 2-3 (Modo Serial TTL / UART)." -ForegroundColor Yellow
            Write-Host "        - Se estiver nos pinos 1-2 (Modo Gravador SPI/I2C), ele NAO criara porta COM serial." -ForegroundColor Yellow
            Write-Host "     👉 Se voce instalou o pacote 'CH341PAR.EXE', desinstale-o e instale o 'CH341SER.EXE'." -ForegroundColor Yellow
        }
        elseif ($dev.InstanceId -match 'PID_7523') {
            Write-Host "  ✅ Modo Serial padrao identificado (PID 7523 - Virtual COM Port)." -ForegroundColor Green
        }

        # Verificar se possui codigo de erro no Gerenciador
        if ($dev.Problem -and $dev.Problem -ne "CM_PROB_PHANTOM") {
            Write-Host "  ❌ Codigo de Problema Windows: $($dev.Problem)" -ForegroundColor Red
            Write-Host "     👉 Solucao: Instale o driver estavel CH341SER.EXE v3.4 ou v3.5." -ForegroundColor Yellow
        }
    }
}

# 3. Listar Portas COM Ativas no Sistema
Write-Host "`n[3/4] Verificando Portas COM Seriais Disponiveis..." -ForegroundColor Yellow
$portas = [System.IO.Ports.SerialPort]::GetPortNames()
if ($portas.Count -eq 0) {
    Write-Host "  ⚠️ Nenhuma porta serial COM ativa foi detectada pelo Windows." -ForegroundColor Yellow
} else {
    Write-Host "  • Portas detectadas: $($portas -join ', ')" -ForegroundColor Green
    foreach ($p in $portas) {
        $num = 0
        if ([int]::TryParse($p.Replace("COM", ""), [ref]$num) -and $num -ge 10) {
            Write-Host "  ⚠️ A porta $p possui dois digitos ($p >= COM10)." -ForegroundColor Yellow
            Write-Host "     👉 Remapeie para COM2, COM3, COM4 ou COM5 no Gerenciador de Dispositivos (devmgmt.msc)." -ForegroundColor Yellow
        }
    }
}

# 4. Resumo de Acoes para o Operador
Write-Host "`n[4/4] Guia Rapido de Solucao:" -ForegroundColor Cyan
Write-Host "  1. Remapeamento: Gerenciador de Dispositivos -> Portas (COM e LPT) -> Propriedades -> Avancado -> Mudar para COM2 a COM5." -ForegroundColor White
Write-Host "  2. Porta USB: Conecte exclusivamente na porta traseira da placa-mae ECS G41T-M5." -ForegroundColor White
Write-Host "  3. Driver Oficial: Utilize o instalador CH341SER.EXE (VCP Serial Driver)." -ForegroundColor White
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""
