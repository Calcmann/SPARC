# SPARC Beta - Variante de Testes (Pasta Isolada, Base Intacta)

Todo o processo de empacotamento de teste vive em `C:\SPARC\beta\` e gera executáveis em `C:\SPARC\dist-beta\`.
Nenhum arquivo em `C:\SPARC\src\` é modificado por este fluxo.

## Como Gerar o Executável Único (Autocontido, Sem Dependências)
```powershell
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1
```
Saída gerada exclusivamente em: `C:\SPARC\dist-beta\SPARC-Beta-Testes-<versao>.exe` (executável único, roda diretamente sem requerer instalação do runtime .NET).

## Pular Ofuscação (Debug / Diagnóstico)
```powershell
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1 -SemOfuscacao
```

## Ativação e Governança de Usuários
1. Ao executar o aplicativo pela primeira vez, a tela exibe o identificador único da máquina e as opções de ativação.
2. O técnico realiza a solicitação de acesso:
   - **Online (Recomendado)**: Preenche a ficha na aba *"Cadastro & Ativação Online"* e clica em enviar. O gestor recebe a solicitação em tempo real no **SPARC ADMIN**.
   - **Manual / Offline**: Copia o código `SPBREQ...` e envia para o gestor.
3. Liberação pelo Gestor:
   - **Via SPARC Admin**: Abra pelo atalho no Desktop `SPARC Admin` ou execute `C:\SPARC\Iniciar_Admin.cmd`. Na aba *"Solicitações Pendentes"*, o gestor aprova diretamente pela ficha de cadastro do técnico com 1 clique (ou gera a chave manual para WhatsApp na aba *"Chaves de Ativação"*).
   - **Via CLI PowerShell**:
     ```powershell
     powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Gerar-ChaveBeta.ps1 -Req "SPBREQ..."
     ```
4. A liberação ocorre com a assinatura digital RSA amarrada à máquina do técnico.

## Regras de Governança
- **Sem Vencimento Nativo de 30 Dias**: O aplicativo não possui expiração forçada por calendário local no cliente. O controle de validade e permissões é governado centralmente pela ficha de cadastro de cada usuário no **SPARC ADMIN**.
- **Gestão Centralizada**: Suspensões, revogações imediatas e auditorias de cópias em campo são feitas via módulo **SPARC ADMIN**.
- **Node-locking**: Chave amarrada à identidade criptográfica da máquina (MachineGuid + MAC + serial do volume, com tolerância a docks).
- **Anti-rollback de Relógio + NTP Oportunista**: Não bloqueia técnicos em campo sem conectividade.
- **Ofuscação de Código**: Símbolos privados renomeados e strings criptografadas.
- **Armazenamento de Licença Local**: Em `%ProgramData%\SPARC-Beta\license.key`.

## Avisos de Segurança
- `beta/keys/private.pem` é a chave privada mestra de assinatura: nunca distribuir ou commitar.
