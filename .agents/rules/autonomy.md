---
trigger: always_on
---

# Regra de Execução Autônoma e Sem Interrupções

- `C:\SPARC` é o diretório padrão de desenvolvimento.
- Versões beta testes em executável único (single-file) devem ser geradas exclusivamente em `C:\SPARC\dist-beta` em formato único versionado (`SPARC-Beta-Testes-<versao>.exe` para Windows e `SPARC-Mobile-v<versao>.apk` para Android, sem duplicatas com nome genérico), com o processo de ativação na primeira execução obrigatório e sem prazo nativo de 30 dias na ferramenta para que novas releases não venham com essa limitação — o controle de validade e acesso é feito pela ficha de cadastro de cada usuário gerenciada no **SPARC ADMIN** (Node-locking RSA via `C:\SPARC\beta\Build-Beta.ps1`).
- Padronização de versões: base `0.8`, incrementando patches a cada ciclo de teste (`0.8.1`, `0.8.2`, etc.) até mudança de escopo significativo que justifique versão decimal nova (`0.9`, `1.0`).
- Execute compilações (`dotnet build`) e testes (`dotnet test`) de forma proativa sempre que arquivos forem modificados.
- Não solicite permissão ou confirmação para tarefas técnicas de rotina (edições, criação de arquivos de código, execução de testes unitários e atualização de atalhos).
- Priorize soluções completas e funcionais com validação automatizada.
