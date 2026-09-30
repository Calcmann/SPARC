# Script de Deploy Automatizado para o Cloudflare Worker do SPARC
# Requisitos: Node.js instalado e conta gratuita no Cloudflare (workers.dev)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   SPARC • Deploy do Cloudflare Worker (Firmware Proxy)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Lê o token do GitHub local se disponível
$token = ""
$tokenPath = "C:\SPARC\beta\github_token.txt"
if (Test-Path $tokenPath) {
    $token = (Get-Content $tokenPath).Trim()
}

if ([string]::IsNullOrWhiteSpace($token)) {
    $token = Read-Host "Informe o Token PAT do GitHub (com permissão de leitura no repositório)"
}

Set-Location $scriptDir

# 2. Instala dependências do Wrangler se necessário
if (-not (Test-Path "node_modules")) {
    Write-Host "`n[*] Instalando Wrangler..." -ForegroundColor Yellow
    & npm install
}

# 3. Executa o login / deploy
Write-Host "`n[*] Iniciando publicação no Cloudflare Workers..." -ForegroundColor Yellow
Write-Host "    (Se for a primeira vez, o navegador será aberto para autorizar a conta gratuita Cloudflare)`n"

& npx wrangler deploy

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[✓] Worker publicado com sucesso!" -ForegroundColor Green

    # 4. Configura o segredo do GitHub PAT de forma segura
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        Write-Host "`n[*] Configurando token do GitHub como segredo criptografado no Worker..." -ForegroundColor Yellow
        $token | & npx wrangler secret put GITHUB_PAT
        Write-Host "[✓] Segredo GITHUB_PAT configurado com sucesso!" -ForegroundColor Green
    }

    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host "Tudo pronto! O endpoint agora pode ser usado pelos roteadores." -ForegroundColor Green
    Write-Host "Exemplo: copy http://<seu-worker>.workers.dev/download/c841?key=CR@PS flash:" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Host "`n[X] Falha no deploy. Verifique as mensagens acima." -ForegroundColor Red
}
