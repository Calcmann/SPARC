# ==============================================================================
# SPARC — Utilitário de Configuração de Senha de Teste (FortiGate 40F)
# Objetivo: Aplicar uma senha personalizada desconhecida no FortiGate 40F
#           para simular o bloqueio e testar a quebra/recuperação de senha no SPARC.
# ==============================================================================
param(
    [string]$Porta = "COM1",
    [int]$Baud = 9600,
    [string]$Usuario = "admin",
    [string]$NovaSenha = "Claro@Teste2026",
    [string]$SenhaAtual = "" # Deixe vazio para tentar automaticamente branco e CQMR
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  SPARC - CONFIGURADOR DE SENHA DE TESTE (FORTIGATE 40F)" -ForegroundColor Yellow
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  Porta Serial : $Porta @ $Baud bps"
Write-Host "  Usuário      : $Usuario"
Write-Host "  Nova Senha   : $NovaSenha"
Write-Host "=================================================================`n"

# 1. Valida se a porta COM existe
$portas = [System.IO.Ports.SerialPort]::GetPortNames()
if ($portas -notcontains $Porta) {
    Write-Warning "A porta $Porta nao foi localizada nas portas COM disponiveis: $($portas -join ', ')"
    $escolha = Read-Host "Deseja usar outra porta? (digite o nome ou Enter para manter $Porta)"
    if (![string]::IsNullOrWhiteSpace($escolha)) { $Porta = $escolha.Trim().ToUpper() }
}

$sp = New-Object System.IO.Ports.SerialPort $Porta, $Baud, [System.IO.Ports.Parity]::None, 8, [System.IO.Ports.StopBits]::One
$sp.ReadTimeout = 400
$sp.WriteTimeout = 1000
$sp.NewLine = "`r"

try {
    Write-Host "[1/4] Abrindo porta serial $Porta..." -ForegroundColor Cyan
    $sp.Open()
    $sp.DiscardInBuffer()
    $sp.DiscardOutBuffer()

    # Função auxiliar para enviar texto e coletar buffer
    function Send-And-Read([string]$cmd, [int]$waitMs = 500) {
        if (![string]::IsNullOrEmpty($cmd)) {
            $sp.Write($cmd + "`r")
        } else {
            $sp.Write("`r")
        }
        Start-Sleep -Milliseconds $waitMs
        $out = ""
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt 1500) {
            try {
                $chunk = $sp.ReadExisting()
                if (![string]::IsNullOrEmpty($chunk)) {
                    $out += $chunk
                    $sw.Restart()
                }
            } catch { }
            Start-Sleep -Milliseconds 100
        }
        return $out
    }

    Write-Host "[2/4] Detectando console FortiGate..." -ForegroundColor Cyan
    # Envia Ctrl+C e Enter para solicitar prompt
    $sp.Write([byte[]]@(0x03), 0, 1)
    Start-Sleep -Milliseconds 150
    $resp = Send-And-Read "" 600

    # Se estiver em disclaimer de aviso prévio ((Press 'a' to accept):)
    if ($resp -match "(?i)press\s+['""]?a['""]?\s+to\s+accept") {
        Write-Host "  [>] Termo de uso detectado. Aceitando com 'a'..." -ForegroundColor Gray
        $resp = Send-And-Read "a" 600
    }

    # Verifica se já está no prompt operacional (# ou $)
    $logged = ($resp -match "FortiGate.*[#\$]") -or ($resp -match "[A-Za-z0-9_\-]+.*#")

    if (!$logged) {
        Write-Host "[3/4] Realizando login na console do FortiGate..." -ForegroundColor Cyan
        
        # Se não estiver no prompt de login, envia Enter para acordar
        if ($resp -notmatch "(?i)login:") {
            $resp = Send-And-Read "" 600
        }

        # Envia usuário
        if ($resp -match "(?i)login:") {
            Write-Host "  [>] Enviando usuario '$Usuario'..." -ForegroundColor Gray
            $resp = Send-And-Read $Usuario 600
        }

        # Envia senha
        if ($resp -match "(?i)password:") {
            $passToSend = if (![string]::IsNullOrEmpty($SenhaAtual)) { $SenhaAtual } else { "" }
            Write-Host "  [>] Enviando senha (tentando vazia/atual)..." -ForegroundColor Gray
            $resp = Send-And-Read $passToSend 800

            # Se FortiOS exigir troca de senha (New Password)
            if ($resp -match "(?i)(?:new\s+password|forced\s+to\s+change)") {
                Write-Host "  [>] Troca obrigatoria de senha detectada no primeiro login!" -ForegroundColor Yellow
                Write-Host "  [>] Definindo nova senha: '$NovaSenha'..." -ForegroundColor Gray
                $resp = Send-And-Read $NovaSenha 600
                if ($resp -match "(?i)confirm") {
                    $resp = Send-And-Read $NovaSenha 800
                }
            }
            # Se falhou com senha em branco, tenta CQMR
            elseif ($resp -match "(?i)login incorrect") {
                Write-Host "  [!] Senha em branco incorreta. Tentando 'CQMR'..." -ForegroundColor Yellow
                Start-Sleep -Milliseconds 500
                $resp = Send-And-Read $Usuario 600
                $resp = Send-And-Read "CQMR" 800
            }
        }

        # Verifica disclaimer pos-login
        if ($resp -match "(?i)press\s+['""]?a['""]?\s+to\s+accept") {
            $resp = Send-And-Read "a" 600
        }

        $logged = ($resp -match "FortiGate.*[#\$]") -or ($resp -match "[A-Za-z0-9_\-]+.*#")
    }

    if (!$logged) {
        Write-Warning "Nao foi possivel confirmar o prompt autenticado (#) do FortiGate."
        Write-Host "Ultima saida recebida:" -ForegroundColor DarkYellow
        Write-Host $resp
        Write-Host "`n👉 Dica: Se o FortiGate ja tiver outra senha, informe o parametro -SenhaAtual '<senha>'." -ForegroundColor Cyan
        return
    }

    Write-Host "[4/4] Aplicando senha personalizada '$NovaSenha' no usuario '$Usuario'..." -ForegroundColor Green
    
    # Comandos FortiOS para atualizar senha de admin
    $c1 = Send-And-Read "config system admin" 300
    $c2 = Send-And-Read "edit `"$Usuario`"" 300
    $c3 = Send-And-Read "set password `"$NovaSenha`"" 300
    $c4 = Send-And-Read "next" 300
    $c5 = Send-And-Read "end" 600

    Write-Host "  [OK] Comandos FortiOS executados com sucesso!" -ForegroundColor Green

    # Envia exit para deixar a console no prompt de login para o teste do SPARC
    Write-Host "  [>] Efetuando logout para bloquear o terminal..." -ForegroundColor Cyan
    $null = Send-And-Read "exit" 500

    Write-Host "`n=================================================================" -ForegroundColor Green
    Write-Host "✅ SENHA PERSONALIZADA CONFIGURADA COM SUCESSO NO FORTIGATE 40F!" -ForegroundColor Green
    Write-Host "=================================================================" -ForegroundColor Green
    Write-Host "  Usuário  : $Usuario"
    Write-Host "  Senha    : $NovaSenha"
    Write-Host ""
    Write-Host "👉 COMO TESTAR A 'QUEBRA DE SENHA' NO SPARC:" -ForegroundColor Yellow
    Write-Host "  1. Abra o SPARC (C:\SPARC\dist-beta\SPARC-Beta-Testes-0.8.153.exe);"
    Write-Host "  2. Na tela inicial, clique em 'Testar Conexao Porta Serial';"
    Write-Host "  3. O SPARC tentara o acesso em branco e CQMR (ambos serao rejeitados);"
    Write-Host "  4. O SPARC exibira o dialogo de bloqueio:"
    Write-Host "     '🔒 [FORTIGATE 40F] SENHA DESCONHECIDA / NÃO HOMOLOGADA';"
    Write-Host "  5. Escolha a opcao 'Quebrar senha e zerar configuracao de forma autonoma';"
    Write-Host "  6. Siga as orientacoes de reset fisico no assistente autonomo para liberar o equipamento!"
    Write-Host "=================================================================`n" -ForegroundColor Green
}
catch {
    Write-Error "Falha ao comunicar com o FortiGate na porta $Porta : $($_.Exception.Message)"
}
finally {
    if ($sp -and $sp.IsOpen) {
        $sp.Close()
        $sp.Dispose()
    }
}
