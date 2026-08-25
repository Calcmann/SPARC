# Guia de Personalização de Agentes IA para o Projeto SPARC

Este documento define as diretrizes, padrões e instruções para configurar e personalizar assistentes e agentes autônomos de IA (Antigravity, Cursor, Copilot, Roo Code, Claude Dev, etc.) que colaboram no desenvolvimento do projeto **SPARC**.

---

## 1. Visão Geral da Arquitetura do Projeto

O **SPARC** é uma solução em **.NET 8 (C#)** voltada para provisionamento, diagnóstico e recuperação de equipamentos de rede (Cisco, HPE, etc.), composta por:

* **NetworkDevice.Core**: Lógica de negócios central, parsers de ficha SAIP, cálculo de sub-redes/IPs, máquinas de estado de recuperação de boot, diagnósticos ICMP/HTTP e sessão de console (DeviceSession).
* **NetworkDevice.Protocols**: Implementações de transporte e servidores embutidos (EmbeddedTftpServer, SshTransport, ITransport).
* **NetworkDevice.UI**: Interface gráfica desktop em **WPF** (.NET 8 Windows), com design preparado para futura expansão multiplataforma (.NET MAUI / Android).
* **NetworkDevice.Cli**: Interface de linha de comando para automações e diagnósticos rápidos.
* **NetworkDevice.Tests**: Bateria de testes automatizados unitários e de integração.

---

## 2. Estrutura de Customização de Agentes (.agents / AGENTS.md)

O projeto utiliza a convenção padrão de agentes:

\\\	ext
C:\SPARC\
├── AGENTS.md                   # Regras globais de atuação do agente
├── .agents/
│   ├── rules/
│   │   ├── autonomy.md         # Regra de execução proativa e sem interrupções
│   │   ├── csharp-standards.md # Padrões de código C# e .NET 8
│   │   └── testing.md          # Diretrizes para execução e criação de testes
│   └── skills/                 # Habilidades e fluxos especializados
│       └── sparc-workflows/
│           └── SKILL.md
└── docs/
    ├── ANDROID_ARCHITECTURE.md
    └── AGENT_CUSTOMIZATION.md
\\\

---

## 3. Instruções e Regras Essenciais para o Agente

### Regra 1: Autonomia e Execução Proativa
* **Executar compilação e testes automaticamente**: Sempre que arquivos forem alterados, rodar dotnet test C:\SPARC\NetworkDevice.sln e dotnet build -c Release de forma proativa.
* **Não bloquear com perguntas triviais**: O agente tem autonomia técnica para criar classes, ajustar testes quebrados e resolver problemas de compilação sem solicitar confirmação intermediária.

### Regra 2: Padrões de Código e .NET 8
* **Nomenclatura e Idioma**: Código, métodos, propriedades e nomes de arquivos em **Inglês** (SaipCircuitData, ConnectivityService). Logs visíveis e documentações técnicas em **Português**.
* **Abstrações para Multiplataforma**:
  * Qualquer dependência de SO específico (ex: 
etsh, registro do Windows, System.IO.Ports) deve ser encapsulada através de interfaces (IHostNetworkService, ITransport).
  * Manter compatibilidade com .NET 8 Standard/Core para viabilizar compilação no Android / .NET MAUI.
* **Tratamento de Exceções e Resiliência**:
  * Métodos assíncronos (sync Task) com suporte adequado a CancellationToken.
  * Logs detalhados com timestamps nas rotinas de diagnóstico e recuperação de console.

### Regra 3: Bateria de Testes Automatizados
* Todo novo método ou parser deve possuir cobertura de testes em NetworkDevice.Tests.
* Usar dublês de teste (ScriptedTransport) para simular respostas seriais/SSH em testes de integração rápida sem hardware real conectado.
* Garantir que **100% dos testes passem** antes de encerrar qualquer ciclo de alteração.

---

## 4. Prompt do Sistema (System Prompt) para Novos Agentes

Copie o prompt abaixo para configurar o agente em ferramentas externas ou customizações de IDE:

\\\markdown
Você é um Engenheiro de Software Sênior .NET especialista em Redes e Sistemas Embarcados, atuando no projeto SPARC (C:\SPARC).

### Suas Responsabilidades:
1. Desenvolver, refatorar e testar módulos da solução SPARC (.NET 8).
2. Manter arquitetura limpa: NetworkDevice.Core (sem dependência de UI), NetworkDevice.Protocols, NetworkDevice.UI (WPF) e NetworkDevice.Tests.
3. Preservar o isolamento de plataforma: encapsular chamadas de API específicas de Windows em interfaces para garantir compatibilidade com Android/MAUI.
4. Agir de forma autônoma e proativa: rodar dotnet build e dotnet test a cada ciclo de modificação.
5. Garantir que todos os 39+ testes automatizados continuem passando com 0 falhas.
\\\

---

## 5. Como Adicionar Novas Skills (Habilidades Especializadas)

Para ensinar procedimentos complexos ao agente (ex: fluxo de recuperação de firmware Cisco IOS):
1. Crie a pasta .agents/skills/<nome-da-skill>/.
2. Adicione o arquivo SKILL.md contendo:
   - Frontmatter YAML com 
ame e description.
   - Instruções passo a passo com comandos, exemplos de entrada/saída e validações esperadas.
