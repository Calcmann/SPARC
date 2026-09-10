# Duplo-clique (atalho no Desktop): cola o SPBREQ, gera a chave de 30 dias e copia.
# Texto 100% ASCII de proposito. Tolera quebra de linha do WhatsApp e texto extra colado junto.
param([string]$Req = "")
$ErrorActionPreference = "Stop"
$betaRoot = "C:\SPARC\beta"
$tool = Join-Path $betaRoot "tools\LicenseTool\LicenseTool.csproj"
$key = Join-Path $betaRoot "keys\private.pem"
if ([string]::IsNullOrWhiteSpace($Req)) {
    Write-Host "=== Gerador de chave SPARC Beta (30 dias) ==="
    Write-Host ""
    $Req = Read-Host "Cole o pedido SPBREQ do notebook e tecle Enter"
}
if ($Req -match "(SPBREQ\.[A-Za-z0-9\-_]+)") { $Req = $Matches[1] }
$Req = $Req -replace "\s+", ""
if (-not ($Req -match "^SPBREQ\.[A-Za-z0-9\-_]+$")) {
    Write-Host ""
    Write-Host "PEDIDO INVALIDO: nao encontrei um SPBREQ... valido no texto colado."
    Write-Host "Confira se copiou a linha inteira dos dados de ativacao."
    Write-Host ""
    pause
    exit 1
}
try {
    $b64 = $Req.Substring("SPBREQ.".Length).Replace("-", "+").Replace("_", "/")
    switch ($b64.Length % 4) { 2 { $b64 += "==" } 3 { $b64 += "=" } }
    $js = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
    Write-Host ""
    Write-Host ("Pedido da maquina: " + $js)
} catch { }
$out = & dotnet run --project $tool -- sign --key $key --req $Req --dias 30 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host ""; Write-Host ($out -join "`r`n"); Write-Host ""; pause; exit 1 }
$token = @($out | Where-Object { $_ -match "^SPB1\." })[0]
$exp = @($out | Where-Object { $_ -match "Valida ate" })[0]
if (-not $token) { Write-Host ""; Write-Host ($out -join "`r`n"); Write-Host ""; pause; exit 1 }
try { Set-Clipboard -Value $token } catch { }
Write-Host ""
Write-Host $exp
Write-Host ""
Write-Host "CHAVE (copiada para a area de transferencia):"
Write-Host $token
Write-Host ""
pause
