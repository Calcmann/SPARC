# Script de Deploy Automatizado para o Cloudflare Worker do SPARC
# Requisitos: Node.js instalado e conta gratuita no Cloudflare (workers.dev)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   SPARC - Deploy do Cloudflare Worker (Firmware Proxy)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Le o token do GitHub local se disponivel
$token = ""
$tokenPath = "C:\SPARC\beta\github_token.txt"
if (Test-Path $tokenPath) {
    $token = (Get-Content $tokenPath -Raw).Trim()
}

if ([string]::IsNullOrWhiteSpace($token)) {
    $token = Read-Host "Informe o Token PAT do GitHub (com permissao de leitura no repositorio)"
}

Set-Location $scriptDir

# 2. Instala dependencias do Wrangler se necessario
if (-not (Test-Path "node_modules")) {
    Write-Host ""
    Write-Host "[*] Instalando dependencias locais..." -ForegroundColor Yellow
    npm.cmd install
}

# 3. Executa o login / deploy
Write-Host ""
Write-Host "[*] Iniciando publicacao no Cloudflare Workers..." -ForegroundColor Yellow
Write-Host "    (Se for a primeira vez, o navegador sera aberto para autorizar a conta gratuita Cloudflare)"
Write-Host ""

npx.cmd wrangler deploy

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "[OK] Worker publicado com sucesso!" -ForegroundColor Green

    # 4. Configura o segredo do GitHub PAT de forma segura
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        Write-Host ""
        Write-Host "[*] Configurando token do GitHub como segredo criptografado no Worker..." -ForegroundColor Yellow
        $token | npx.cmd wrangler secret put GITHUB_PAT
        Write-Host "[OK] Segredo GITHUB_PAT configurado com sucesso!" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "Tudo pronto! O endpoint agora pode ser usado pelos roteadores." -ForegroundColor Green
    Write-Host "Exemplo: copy http://<seu-worker>.workers.dev/download/c841?key=CR@PS flash:" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[ERRO] Falha no deploy. Verifique as mensagens acima." -ForegroundColor Red
}
