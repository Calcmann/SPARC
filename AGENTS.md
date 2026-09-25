# Diretrizes de Autonomia e Execução Automática (SPARC)

## 0. Política de Projeto e Versionamento
- **Raiz do projeto é o diretório local `C:\SPARC`** — fonte da verdade e diretório padrão para desenvolvimento, build e atalhos.
- **Versões Beta Testes (Arquivo Único)**: Geradas e armazenadas exclusivamente em `C:\SPARC\dist-beta` (ex.: `SPARC-Beta-Testes.exe` e `SPARC-Beta-Testes-<versao>.exe`).
- **Ativação Obrigatória em Arquivo Único**: Toda versão gerada em arquivo único deve OBRIGATORIAMENTE incluir o processo de ativação na primeira execução, com proteção criptográfica RSA (Node-locking amarrado à máquina) e chave válida por 30 dias (`C:\SPARC\beta\Build-Beta.ps1 -Dias 30`). A liberação do técnico em campo é gerada via `C:\SPARC\beta\Gerar-ChaveBeta.ps1`.
- **Padrão de Versionamento Incremental**: Base atual `0.8`. Incrementa-se sequencialmente a cada entrega/correção de testes (`0.8.1`, `0.8.2`, etc.) até que ocorra uma mudança arquitetural ou de escopo significativo que justifique uma nova versão decimal (`0.9`, `1.0`, etc.).
- **Git é apenas backup eventual**: não fazer `git pull` automático no launcher/atalho; sincronização com GitHub só quando o usuário solicitar explicitamente (`git add -A && git commit -m "backup" && git push`).
- **Atalho/Script**: `C:\SPARC\Iniciar_SPARC.cmd` e `C:\SPARC\scripts\launch_with_update.ps1` compilam localmente (`dotnet build -c Release`) sem depender do remoto.

## 1. Política de Autonomia e Execução de Comandos
- **Execução Proativa**: O agente tem permissão total para executar comandos de build (`dotnet build`), testes automatizados (`dotnet test`), inspeção de processos e gerenciamento de arquivos diretamente, sem solicitar confirmações prévias para operações seguras de desenvolvimento.
- **Edição e Criação Direta de Arquivos**: Aplicar alterações diretamente nos arquivos do projeto e manter testes automatizados atualizados.
- **Manutenção de Integridade**: Preservar testes existentes, compatibilidade com .NET 8 e arquitetura multiplataforma (Windows e Android).

## 2. Modos de Operação
- **Modo Agente / Piloto Automático**: Tomar decisões técnicas completas de arquitetura, testes e código, reportando os resultados ao final de cada ciclo através do terminal e dos artefatos de walkthrough.
