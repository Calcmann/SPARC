# NetworkDevice — Quebra de Senha Cisco (ROMMON)

Documentação de referência e progresso para co-programadores.

## Visão geral

Aplicação .NET 8 que conecta em equipamentos Cisco IOS por serial (RS-232) ou SSH,
faz identificação, backup de configuração e recuperação de senha via ROMMON.

Solução: `NetworkDevice.sln`

| Projeto | Papel |
| --- | --- |
| `src/NetworkDevice.Core` | Sessão de dispositivo, transportes (abstração), backup, controle de energia |
| `src/NetworkDevice.Protocols` | Implementações de transporte: Serial (RS-232) e SSH |
| `src/NetworkDevice.Cisco` | Adapter IOS e orquestração de recuperação de senha |
| `src/NetworkDevice.Cli` | CLI (`serial`, `ssh`, `ports`, `mock`, `recover`) |
| `src/NetworkDevice.UI` | WPF (backup, identificação, quebra de senha) |
| `tests/NetworkDevice.Tests` | Testes xUnit (sessão, backup, adapter, recovery) |

## Comandos

```
dotnet build NetworkDevice.sln
dotnet test NetworkDevice.sln
dotnet run --project src/NetworkDevice.Cli -- recover COM1 9600
```

## Fluxo de quebra de senha (fluxo atual)

Orquestrado em `src/NetworkDevice.Cisco/CiscoIOSRecovery.cs` → `RecoverAndResetAsync`:

1. **Verificar RS-232 ativa** — `VerifyTerminalAsync`
   - Envia uma linha em branco e aguarda resposta.
   - **Sem resposta dentro de `verifyTimeout` (10s por padrão) → aborta** com
     `DeviceSessionException` (conexão inativa: cabo, porta COM ou equipamento desligado).
   - Detecta também se o equipamento **já está em ROMMON** e, nesse caso, pula o reload.
2. **Iniciar loop de interrupção** — `WaitForRommonAsync` (task em background)
   - Envia break (ou Ctrl+C) repetidamente até capturar o prompt ROMMON
     (`rommon N >` / `switch:`) dentro de `bootWait` (5 min por padrão).
   - **Método de interrupção configurável** (`InterruptMethod.Break` / `CtrlC`).
3. **Solicitar reload e confirmação do técnico** — delegado `requestReload`
   - O aplicativo **solicita o reload do equipamento** (power-cycle) e aguarda o
     técnico confirmar (botão na UI / ENTER no CLI). O break já está ativo.
4. **Monitorar terminal até ROMMON** — o loop de break captura o prompt ROMMON.
5. **Executar recuperação** — `RunRecoveryStepsAsync`
   - **Roteador:** `confreg 0x2142` → `reset` (ignora startup-config)
   - **Switch:** `flash_init` → `load_helper` → `dir flash:` →
     `rename flash:config.text flash:config.text.old` → `boot`
   - Ignora o diálogo de configuração inicial (`no`)
   - `enable` (sem senha) → `write erase` → `config-register 0x2102` →
     `write memory` → `reload`

## Contratos principais

- `NetworkDevice.Core/Session/ITransport.cs` — abstração de transporte
  (`OpenAsync`, `ReadAsync`, `WriteAsync`, `SendBreakAsync`, `CloseAsync`).
- `NetworkDevice.Core/Session/DeviceSession.cs` — sessão com matcher de prompt,
  paginação (`--More--`), login e envio de comandos.
- `NetworkDevice.Core/Session/StopCondition.cs` — condições de parada
  (`Prompt`, `LineRegex`, `Contains`).
- `NetworkDevice.Core/Power/IPowerController.cs` — controle de energia
  (mantido; o fluxo de recovery atual usa o delegado `requestReload` no lugar).

## Progresso

### Concluído
- Fluxo de recovery reestruturado: **verificação de RS-232 obrigatória** (aborta se inativa),
  **solicitação de reload com confirmação do técnico**, monitoramento do terminal via
  break até o ROMMON e execução dos passos de zeramento/recuperação.
- Delegado `requestReload` substitui o uso direto de `IPowerController` no recovery.
- UI: botão de confirmação renomeado para **"Reload confirmado (continuar)"**; texto do
  diálogo de confirmação atualizado para "reload".
- CLI `recover`: solicita reload e aguarda ENTER do operador.
- `VerifyTerminalAsync` detecta ROMMON já ativo e pula o reload.
- Testes adicionados em `tests/NetworkDevice.Tests/CiscoIOSRecoveryTests.cs`:
  - aborta quando o terminal não responde;
  - fluxo completo de roteador (reload → ROMMON → confreg/write erase/config-register);
  - já em ROMMON pula o reload.

### Feedback de progresso (evita "parece travado")
- `WaitForRommonAsync` reporta a cada 5s: "Break enviado X vezes — ainda aguardando o ROMMON
  durante o boot...", mantendo o técnico informado durante o power-cycle.
- Após confirmação do reload: "Reload confirmado — monitorando o boot do equipamento...".
- Quando o ROMMON é capturado: "ROMMON capturado. Executando os passos de recuperação...".

### Verificação
- `dotnet build` (Debug e Release): **0 avisos, 0 erros**.
- `dotnet test`: **20/20 aprovados**.

### Nova Arquitetura de Interrupção de Boot e Recuperação
- **Separação de Responsabilidades**:
  - `RecoveryStateMachine`: Única autoridade sobre ciclo de vida, transições de estado (`Connecting` $\to$ `VerifyingTerminal` $\to$ `WaitingReload` $\to$ `WaitingBoot` $\to$ `Interrupting` $\to$ `RommonDetected` $\to$ `ExecutingRecovery` $\to$ `Completed` ou `Failed`).
  - `BootInterruptScheduler`: Focado exclusivamente em **TX** (quando enviar, quantas vezes, respeitando fases e limites de perfil, sem flood na console).
  - `BootMonitor`: Focado exclusivamente em **RX** (escuta contínua e assíncrona, detecção em tempo real de `RommonDetected` e `OsBootDetected`).
  - `ITransport` / `SerialTransport`: Primitivas puras de I/O (`Open`, `Close`, `Read`, `Write`, `SendBreak`), sem regras de negócio ou de marcas embutidas.
- **Perfis de Interrupção (`BootInterruptProfile`)**:
  - Identificador estável `Id` (ex: `cisco.c900.ctrl-c`, `cisco.standard.break`, `cisco.catalyst.mode`, `generic.manual`).
  - `Method`: `CtrlC`, `Break`, `CtrlBreak`, `Esc`, `None`.
  - Suporte a intervenção manual (`RequiresManualIntervention`, `ManualInterventionPrompt` para ações como segurar botão `MODE`).
  - Política de boot do SO (`OsBootPolicy.TerminalFail`, `Warning`, `Ignore`).
- **Resolução do Deadlock e Bloqueio de Leitura**:
  - Desacoplamento do loop TX e RX garante que o silêncio de serial durante o power-cycle não bloqueie o envio escalonado de interrupções.
  - `SerialTransport.ReadAsync` com cancelamento por inatividade evita travas no .NET `BaseStream`.
- **Verificação e Testes**:
  - `dotnet build`: **0 avisos, 0 erros**.
  - `dotnet test`: **24/24 aprovados**. Testes determinísticos cobrindo detecção de ROMMON, detecção e falha rápida de OS Boot, silêncio de serial e ausência de flood.