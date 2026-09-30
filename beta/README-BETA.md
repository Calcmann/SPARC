# SPARC Beta - variante de testes (pasta isolada, base intacta)

Tudo aqui vive em `C:\SPARC\beta\` + saida em `C:\SPARC\dist-beta\`.
Nada em `C:\SPARC\src\` e modificado por este fluxo.

## Gerar o exe unico (padrao: autocontido ~168 MB, sem dependencias)
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1 -Dias 30
Saida: `C:\SPARC\dist-beta\SPARC-Beta-Testes.exe` (single-file, roda em notebook sem .NET).

## Pular ofuscacao (debug da beta)
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1 -Dias 30 -SemOfuscacao

## Ativar num notebook de teste
1. Rode o exe - a tela mostra ID + dados de ativacao (SPBREQ...).
2. Tecnico envia o codigo SPBREQ; o gestor gera a chave de liberacao:
   - **Via SPARC Admin (Recomendado)**: Abra pelo atalho no Desktop `SPARC Admin` ou execute `C:\SPARC\Iniciar_Admin.cmd`. Cole o SPBREQ, escolha os dias e clique em "Gerar Chave" (com opcao de copiar direto formatado para WhatsApp!).
   - **Via CLI PowerShell**:
     powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Gerar-ChaveBeta.ps1 -Req "SPBREQ..." -Dias 30
3. O técnico cola a chave SPB1... na tela - licenca ativada e amarrada a maquina.

## Regras embutidas
- Time-bomb: build expira (padrao: build + 30 dias) - precisa de novo build.
- Node-locking: chave amarrada a maquina (MachineGuid + MAC + serial do volume, com tolerancia a troca de dock).
- Anti-rollback de relogio + NTP oportunista (offline nunca bloqueia por falta de WAN).
- Ofuscacao Obfuscar (rename + strings) com skip nas 4 classes XAML; resto ofuscado.
- Licenca em %ProgramData%\SPARC-Beta\license.key.

## Avisos
- beta/keys/private.pem = segredo: nunca envie junto, nunca commite.
- Protecao nivel beta (ofuscacao basica); suficiente para homologacao.
