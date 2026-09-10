# SPARC Beta - Versao Arquivo Unico (build 10/09/2026)

Variante de testes do SPARC em executavel unico, com licenciamento por chave de 30 dias,
travamento por maquina, time-bomb de build e ofuscacao. A base em `C:\SPARC\src` nao foi alterada:
todo o fluxo vive em `C:\SPARC\beta\` e e injetado numa copia isolada em TEMP no momento do build.

## Artefatos

| Arquivo | Tamanho | Requisito | Uso |
|---|---|---|---|
| `dist-beta\SPARC-Beta-Testes.exe` (padrao) | ~13,7 MB | .NET 8 Desktop Runtime instalado | Notebooks que ja rodam o SPARC normal (16 MB) |
| `dist-beta\SPARC-Beta-Testes.exe` (`-SelfContained`) | ~168 MB | Nenhum (framework embutido) | Notebook de campo/teste sem .NET |
| Manuais PDF ao lado do exe | - | - | Abertos pelo botao de manual do app |

Nota: a variante autocontida embute tambem as nativas do WPF (`wpfgfx_cor3`, `PresentationNative_cor3`,
`D3DCompiler_47`, `PenImc`, `vcruntime140`) via override de `FilesToBundle` no `.csproj` da copia
(`BetaForceBundleWpfNatives`). Sem isso o exe abre na pasta do build mas morre com `DllNotFoundException`
(HwndSubclass) em qualquer outra pasta.

## Protecoes implementadas (pasta `beta\guard`, namespace `NetworkDevice.UI.Beta`)

1. **Time-bomb de build** (`BetaConfig.ExpiresUtc`, injetado no build = hoje + 30 dias).
   Apos expirar, nem chave valida abre: precisa de novo build.
2. **Node-locking + chave RSA 2048 de 30 dias** (`MachineId` + `LicenseCrypto`).
   ID da maquina = SHA256(MachineGuid + MAC principal + serial do volume), exibido como
   `XXXX-XXXX-XXXX-XXXX`. Licenca = token `SPB1.<payload>.<assinatura>` com `{guid, fingerprint, exp}`.
   Aceita se o MachineGuid OU o fingerprint baterem (tolerancia a troca de dock/placa).
3. **Anti-rollback de relogio** (`BetaClock`): grava `lastseen` em `%ProgramData%\SPARC-Beta` e
   recusa abrir se o relogio voltar. NTP (`time.windows.com`) oportunista: so corrige se houver rede
   e divergencia > 10 min; offline nunca bloqueia.
4. **Bloqueio total** (`BetaLicenseGuard.Enforce` no inicio do `OnStartup` da copia): sem chave valida,
   so abre a tela de ativacao. Detalhe WPF: o guard fixa `ShutdownMode.OnExplicitShutdown` enquanto o
   dialogo existe, senao o app se encerra sozinho ao clicar Ativar (bug encontrado em 10/09/2026).
5. **Ofuscacao Obfuscar** (rename de privados + criptografia de strings), com `SkipType` total nas
   4 classes XAML (`App`, `MainWindow`, `CliDiagnosticWindow`, `PasswordAuthDialog`). Auditoria:
   nenhum acesso a membro por nome via reflexao no codigo (so delegates e JSON externo), entao o
   rename e seguro. Verificacao automatica no build: `HandlePaginationAsync` some do Core,
   `MainWindow` permanece.

## Gerar o exe

```powershell
# Padrao: autocontido single-file (~168 MB, sem dependencias)
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1 -Dias 30
# Sem ofuscacao (debug)
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1 -Dias 30 -SemOfuscacao
```
(A variante enxuta framework-dependent foi descontinuada para simplificacao.)

Pipeline (6 etapas): chaves RSA -> copia `src` p/ TEMP (sem `bin/obj`) -> injeta guard + config
(tag/expiracao/chave publica) + hook no `OnStartup` -> `dotnet build -r win-x64` -> Obfuscar
(GlobalTool, com `AssemblySearchPath` do runtime p/ resolver `PresentationFramework`) com
checagem de rename -> `publish --no-build` single-file + rename p/ `SPARC-Beta-Testes.exe`.
Se a beta estiver rodando, o script aborta no inicio (arquivo em uso).

## Ativacao e chaves

1. No notebook de teste, o exe mostra ID (`XXXX-...`) + dados `SPBREQ...` (botao copia).
2. Responsavel gera a chave (atalho do Desktop `Gerar Chave SPARC Beta` ou direto):
   `powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Gerar-ChaveBeta.ps1 -Req "SPBREQ..." -Dias 30`
3. Tecnico cola a `SPB1...` e ativa. Licenca em `%ProgramData%\SPARC-Beta\license.key`.
4. Validade padrao 30 dias por maquina. Erros: `INVALIDO` = assinatura nao confere (copia parcial);
   `outro computador` = pedido trocado entre tecnicos.
5. Chave privada: `beta\keys\private.pem` (nunca distribuir/commits; `beta\keys\.gitignore` a ignora).

## Correcoes do dia incluidas na copia beta

* Retry da avaliacao pos-login (3 tentativas, `ConnectTimeout` 6s -> 10s): 1o clique nao fica mais
  so no `Avaliado` sem modelo/popup; falha real vira `Avaliacao incompleta - testar novamente`.
* Textos do guard 100% ASCII (o `Set-Content` padrao corrompia acentos em `?` na tela de ativacao).

## Teste full em lab com link 4G (sem WAN Claro)

Topologia: notebook(fixo) <-> LAN roteador | WAN roteador <-> LAN do 4G (ex: `192.168.10.1`).
Ficha lab `SAIP_LAB_4G.txt` (UTF-8, Passo 2):
`IP Serial Usuario (IPv4): 192.168.10.2/30` (gateway derivado = `.1` = 4G),
`Blocos IPv4: 10.10.10.0/29` (LAN `10.10.10.1`, notebook `10.10.10.2` configurado na Fase B).
Esperado: 5a/5b/5c OK, Telnet OK, banda mede o 4G (funcional). Reprovisionar com SAIP real antes do campo.

## Limitacoes conhecidas

* Protecao nivel beta (ofuscacao basica, sem virtualizacao); app exige admin (`requireAdministrator`).
* Chave por maquina, mas sem cadastro central: gestao manual dos pedidos.
* Relogio/NTP e licenca dependem de `%ProgramData%\SPARC-Beta` gravavel.
