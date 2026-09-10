# Gera chave de 30d (ou N dias) para um pedido SPBREQ... copiado da tela de ativacao.
param([Parameter(Mandatory = $true)][string]$Req, [int]$Dias = 30)
$ErrorActionPreference = "Stop"
$betaRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$tool = Join-Path $betaRoot "tools\LicenseTool\LicenseTool.csproj"
$key = Join-Path $betaRoot "keys\private.pem"
& dotnet run --project $tool -- sign --key $key --req $Req --dias $Dias
