param(
    [string]$AdapterName = "Ethernet"
)

$results = [System.Collections.Generic.List[string]]::new()

try {
    # 1. Buffers de Recepção (descobre dinamicamente o limite máximo suportado pelo chip)
    $rxProp = Get-NetAdapterAdvancedProperty -Name $AdapterName -RegistryKeyword '*ReceiveBuffers' -ErrorAction SilentlyContinue
    if ($rxProp) {
        $maxRx = $rxProp.NumericParameterMaxValue
        if ($maxRx -and $maxRx -gt 0) {
            $targetRx = [Math]::Min(2048, $maxRx)
            Set-NetAdapterAdvancedProperty -Name $AdapterName -RegistryKeyword '*ReceiveBuffers' -RegistryValue $targetRx -ErrorAction SilentlyContinue
            $results.Add("Buffers de Recepcao: ajustado para $targetRx (limite do chip: $maxRx)")
        }
    }

    # 2. Buffers de Transmissão
    $txProp = Get-NetAdapterAdvancedProperty -Name $AdapterName -RegistryKeyword '*TransmitBuffers' -ErrorAction SilentlyContinue
    if ($txProp) {
        $maxTx = $txProp.NumericParameterMaxValue
        if ($maxTx -and $maxTx -gt 0) {
            $targetTx = [Math]::Min(2048, $maxTx)
            Set-NetAdapterAdvancedProperty -Name $AdapterName -RegistryKeyword '*TransmitBuffers' -RegistryValue $targetTx -ErrorAction SilentlyContinue
            $results.Add("Buffers de Transmissao: ajustado para $targetTx (limite do chip: $maxTx)")
        }
    }

    # 3. Economia de Energia (Intel *EEE, Realtek GreenEthernet, GigaLite, etc)
    $eeeProps = Get-NetAdapterAdvancedProperty -Name $AdapterName -ErrorAction SilentlyContinue | Where-Object { 
        $_.RegistryKeyword -match 'EEE|Green|PowerSave|GigaLite' -or $_.DisplayName -match 'energia|green' 
    }
    foreach ($p in $eeeProps) {
        $disableVal = $p.ValidDisplayValues | Where-Object { $_ -match 'Desabilitad|Disabled|Off|Desligado|0' } | Select-Object -First 1
        if ($disableVal) {
            Set-NetAdapterAdvancedProperty -Name $AdapterName -DisplayName $p.DisplayName -DisplayValue $disableVal -ErrorAction SilentlyContinue
            $results.Add("Economia de energia ($($p.DisplayName)): desativada ($disableVal)")
        }
    }

    # 4. Moderação de interrupção (tenta Baixa ou Low se o chip suportar)
    $modRate = Get-NetAdapterAdvancedProperty -Name $AdapterName -ErrorAction SilentlyContinue | Where-Object { 
        $_.DisplayName -match 'Taxa de moderação|Interrupt Moderation Rate' 
    }
    if ($modRate) {
        $lowVal = $modRate.ValidDisplayValues | Where-Object { $_ -match 'Baix|Low' } | Select-Object -First 1
        if ($lowVal) {
            Set-NetAdapterAdvancedProperty -Name $AdapterName -DisplayName $modRate.DisplayName -DisplayValue $lowVal -ErrorAction SilentlyContinue
            $results.Add("Moderacao de Interrupcoes: definida para $lowVal")
        }
    }

    # 5. Ativar RSS se suportado pelo hardware
    try {
        Enable-NetAdapterRss -Name $AdapterName -ErrorAction Stop
        $results.Add("RSS (Receive Side Scaling): Ativado com sucesso")
    } catch {
        $results.Add("RSS: Nao suportado pelo chip ou ja gerenciado pelo SO")
    }

    # 6. Ativar Checksum Offload
    try {
        Enable-NetAdapterChecksumOffload -Name $AdapterName -ErrorAction SilentlyContinue
        $results.Add("Descarga de Checksum em Hardware (Rx/Tx): Ativada")
    } catch { }

} catch {
    $results.Add("Erro durante calibracao: $($_.Exception.Message)")
}

$results
