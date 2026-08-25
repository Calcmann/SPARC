# Diretrizes de Autonomia e Execução Automática (SPARC)

## 1. Política de Autonomia e Execução de Comandos
- **Execução Proativa**: O agente tem permissão total para executar comandos de build (`dotnet build`), testes automatizados (`dotnet test`), inspeção de processos e gerenciamento de arquivos diretamente, sem solicitar confirmações prévias para operações seguras de desenvolvimento.
- **Edição e Criação Direta de Arquivos**: Aplicar alterações diretamente nos arquivos do projeto e manter testes automatizados atualizados.
- **Manutenção de Integridade**: Preservar testes existentes, compatibilidade com .NET 8 e arquitetura multiplataforma (Windows e Android).

## 2. Modos de Operação
- **Modo Agente / Piloto Automático**: Tomar decisões técnicas completas de arquitetura, testes e código, reportando os resultados ao final de cada ciclo através do terminal e dos artefatos de walkthrough.
