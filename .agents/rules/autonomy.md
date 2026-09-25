---
trigger: always_on
---

# Regra de Execução Autônoma e Sem Interrupções

- `C:\SPARC` é o diretório padrão de desenvolvimento.
- Versões beta testes em executável único (single-file) devem ser geradas exclusivamente em `C:\SPARC\dist-beta` com o processo de ativação na primeira execução obrigatório e chave válida por 30 dias (Node-locking RSA via `C:\SPARC\beta\Build-Beta.ps1 -Dias 30`).
- Padronização de versões: base `0.8`, incrementando patches a cada ciclo de teste (`0.8.1`, `0.8.2`, etc.) até mudança de escopo significativo que justifique versão decimal nova (`0.9`, `1.0`).
- Execute compilações (`dotnet build`) e testes (`dotnet test`) de forma proativa sempre que arquivos forem modificados.
- Não solicite permissão ou confirmação para tarefas técnicas de rotina (edições, criação de arquivos de código, execução de testes unitários e atualização de atalhos).
- Priorize soluções completas e funcionais com validação automatizada.
