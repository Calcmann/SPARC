---
trigger: always_on
---

# CONSTITUIÇÃO DE DESENVOLVIMENTO E PROTEÇÃO AO CÓDIGO (SPARC)

Esta Constituição estabelece as cláusulas pétreas que governam toda e qualquer interação, modificação, refatoração ou adição de código realizada por programadores e coprogramadores (assistentes de IA) no projeto SPARC.

---

## ARTIGO I — DA PRESERVAÇÃO DE CÓDIGO HOMOLOGADO E ANTI-REGRESSÃO

1. **Princípio da Não-Regressão**: O código existente homologado em bancada e em campo é patrimônio crítico e estável. É terminantemente proibido aplicar alterações que degradem fluxos já validados.
2. **Preservação de Testes Existentes**: Testes unitários pré-existentes nunca devem ser relaxados, comentados ou excluídos para "fazer o build passar". Se um teste existente falhar, o código novo é que deve ser corrigido.
3. **Validação Obrigatória**: Toda e qualquer alteração de código deve ser validada proativamente via `dotnet test` antes de considerar a tarefa concluída. O número de testes com falha deve ser rigorosamente zero (0).

---

## ARTIGO II — DA SEPARAÇÃO ESTRITA MULTIPLATAFORMA (WINDOWS vs ANDROID)

1. **Independência das Camadas de Apresentação**:
   - `src/NetworkDevice.UI` (Windows WPF / .NET 8 Desktop) e `src/NetworkDevice.Android` (.NET MAUI / Android) são ecossistemas independentes.
   - Ajustes visuais, comportamentais, navegação ou controle de ciclo de vida de uma plataforma NUNCA devem afetar a outra.
2. **Integridade das Bibliotecas Compartilhadas (`Core`, `Cisco`, `Fortinet`, `Protocols`)**:
   - Bibliotecas de domínio e serviços compartilhados devem ser rigorosamente neutras e compatíveis com ambas as plataformas.
   - É proibido invocar APIs nativas exclusivas do Windows (ex.: Registro do Windows, Win32 API, `System.Windows.*`, COM) diretamente nas bibliotecas compartilhadas sem a devida abstração ou guardas `OperatingSystem.IsWindows()`.
   - O canal serial físico deve ser acessado exclusivamente via a abstração `ITransport` (porta COM no Windows vs USB Serial Host CDC/FTDI/CP210x no Android).

---

## ARTIGO III — DO ISOLAMENTO ABSOLUTO ENTRE MODELOS DE EQUIPAMENTOS

O SPARC gerencia equipamentos heterogêneos de fabricantes e sistemas operacionais distintos (Cisco IOS, Cisco IOS-XE, HPE Comware 5/7 e Fortinet FortiOS). Cada modelo possui particularidades físicas, de firmware, sintaxe de CLI, modos de console e temporização.

1. **Regra de Isolamento de Perfil**:
   - Qualquer ajuste em detecção, provisionamento, recuperação de senha, detecção de portas ou upgrade de firmware de um modelo específico DEVE ser encapsulado estritamente no seu respectivo perfil (`DeviceSeries`, `DeviceManufacturer`, driver ou branch condicional).
   - É terminantemente proibido generalizar regexes de prompt, filtros de exclusão ou comandos de modo que alterem o comportamento de outros modelos.
2. **Matriz de Modelos Homologados e Suas Particularidades**:
   - **Cisco 1900 / 2900**: Portas roteadas `GigabitEthernet 0/0` (WAN) e `0/1` (LAN); filesystems `flash:` / `usbflash0:`; Break @ 9600; config-register 0x2142.
   - **Cisco ISR 841 (C841M)**: Portas `GigabitEthernet0/4` (WAN) e `GigabitEthernet0/5` / `Vlan1` (LAN); switchports `0/0..0/3`; Break @ 9600.
   - **Cisco ISR 921 (C921-4P)**: Portas `GigabitEthernet 4` (WAN) e `GigabitEthernet 5` / `Vlan1` (LAN); switchports `0..3`; Break / Ctrl+C @ 9600.
   - **Cisco Série 1100 (C1111-4P / IOS-XE)**: Portas `GigabitEthernet0/0/0` (WAN) e `Vlan1` (LAN); switchports `0/1/0..0/1/3`; filesystem `bootflash:`; versões pontuadas (`17.09.04a`); Break @ 9600.
   - **HPE Comware (MSR 930, 954, 1002)**: CLI Comware (`system-view`, `display version`, `return`, `save safely`); portas `GE0/0` e `GE0/1`; BootWare (Ctrl+B); imagens `.ipe` / `.bin`.
   - **Fortinet FortiGate 40F (FortiOS)**: CLI FortiOS (`config system interface`, `get system status`, `end`); portas `wan` e `lan1..lan3`; recuperação de senha via reinicialização/BIOS (`B\r`); prompts com submodos `(console) #`, `(root) #`, `(global) #`.
3. **Prevenção de Falsos Positivos em Regexes**:
   - Regexes de captura ou exclusão (como banners de boot, datas de BIOS ou prompts abertos) devem ser minuciosamente testados para não mascarar prompts legítimos de console aberto (ex.: `FortiGate-40F (console) #`).

---

## ARTIGO IV — DA POLÍTICA DE BUILDS, VERSIONAMENTO E DISTRIBUIÇÃO

1. **Diretório Raiz Oficial**: O desenvolvimento, compilação e teste ocorrem exclusivamente em `C:\SPARC`.
2. **Versões Beta de Teste (Arquivo Único)**:
   - Geradas exclusivamente em `C:\SPARC\dist-beta` via `C:\SPARC\beta\Build-Beta.ps1`.
   - Nome estritamente versionado: `SPARC-Beta-Testes-<versao>.exe` (Windows) e `SPARC-Mobile-v<versao>.apk` (Android), sem cópias genéricas.
3. **Ativação e Segurança Criptográfica**:
   - Obrigatório o processo de ativação na primeira execução (Node-locking RSA amarrado à máquina).
   - Sem prazo nativo de 30 dias na ferramenta; o controle de acesso e validade é centralizado na ficha de cadastro do SPARC ADMIN.
4. **Padrão de Versão**: Base `0.8`, incrementando patches sequencialmente a cada ciclo de entrega (`0.8.156`, `0.8.157`, etc.).

---

## ARTIGO V — PROTOCOLO DE INÍCIO DE SESSÃO DO COPROGRAMADOR

Ao iniciar qualquer interação ou tarefa, o coprogramador deve executar mentalmente o seguinte checklist:
1. **Identificação de Escopo**: A tarefa é específica de Windows, Android, ou atinge o Core compartilhado?
2. **Identificação de Modelos**: Quais modelos específicos (`DeviceSeries`) serão impactados?
3. **Análise de Risco de Regressão**: A alteração proposta pode colidir com regexes, comandos ou portas de outros modelos já homologados?
4. **Ciclo de Verificação**: Implementar com escopo estrito -> Executar `dotnet test` -> Garantir 0 falhas -> Gerar release versionada quando aplicável.
