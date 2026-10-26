# SPARC Beta - Versão Arquivo Único (Atualizado 2026)

Variante de testes do SPARC em executável único com proteção de integridade criptográfica RSA-2048,
amarração por máquina (*node-locking*), ofuscação de código e **governança centralizada de acesso**.
**A ferramenta não possui prazo nativo de 30 dias para expiração no cliente**: o controle de acesso, validade e
permissões é gerenciado de forma centralizada através da **ficha de cadastro de cada técnico no SPARC ADMIN**.

A base em `C:\SPARC\src` permanece intacta: todo o fluxo de compilação da versão de teste vive em `C:\SPARC\beta\` e é injetado numa cópia isolada em TEMP no momento do build.

## Artefatos Gerados

| Arquivo | Tamanho | Requisito | Uso |
|---|---|---|---|
| `dist-beta\SPARC-Beta-Testes-<versao>.exe` | ~76–80 MB | Nenhum (framework embutido) | Executável único autocontido para testes em notebooks de campo |
| `dist-beta\SPARC-Mobile-v<versao>.apk` | ~49 MB | Android 7.0+ | Pacote móvel para smartphones/tablets de campo |
| Manuais PDF ao lado do executável | - | - | Abertos pelo botão de manual integrado da aplicação |

*Nota: A variante autocontida embute todas as bibliotecas nativas do WPF (`wpfgfx_cor3`, `PresentationNative_cor3`, `D3DCompiler_47`, `PenImc`, `vcruntime140`) via override de `FilesToBundle` no `.csproj` da cópia (`BetaForceBundleWpfNatives`), garantindo execução imediata em qualquer diretório sem depender de pré-instalação de runtimes.*

## Proteções e Governança de Acesso (pasta `beta\guard`, namespace `NetworkDevice.UI.Beta`)

1. **Controle Governamental via SPARC ADMIN (Sem Time-bomb Nativo de 30 Dias)**:
   A ferramenta não implementa trava rígida de 30 dias no código-fonte cliente. A liberação, prazo de vigência, prorrogação ou revogação imediata são controlados pela **ficha cadastral do técnico** gerenciada centralmente no módulo **SPARC ADMIN** (abas *Solicitações Online* e *Gestão de Cópias em Campo*).
2. **Node-locking + Chave Criptográfica RSA-2048** (`MachineId` + `LicenseCrypto`):
   ID da máquina = `SHA256(MachineGuid + MAC principal + serial do volume)`, exibido no padrão `XXXX-XXXX-XXXX-XXXX`.
   A licença gerada é assinada digitalmente com token `SPB1.<payload>.<assinatura>` contendo identificadores da máquina e perfil do técnico. Valida se o MachineGuid ou o Fingerprint conferem, garantindo tolerância a trocas de dock/placa de rede sem comprometer a segurança.
3. **Sincronização de Status Online & Bloqueio Remoto** (`BetaLicenseGuard`):
   Na inicialização, o aplicativo consulta de forma transparente o cadastro corporativo. Se o técnico tiver seu acesso revogado ou suspenso no painel administrativo, o executável bloqueia o uso imediatamente, protegendo a operação corporativa.
4. **Anti-rollback de Relógio** (`BetaClock`):
   Registra `lastseen` em `%ProgramData%\SPARC-Beta` e utiliza sincronização NTP oportunista (`time.windows.com`) quando há rede, impedindo manipulações indevidas de hora sem nunca bloquear o uso offline em campo.
5. **Ofuscação de Binário (Obfuscar)**:
   Renomeação de tipos/métodos privados e criptografia de strings com proteção de tipos WPF/XAML, garantindo segurança do código distribuído.

## Como Gerar a Versão de Testes

```powershell
# Build padrão em arquivo único autocontido (versionamento automático via csproj)
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1

# Build sem ofuscação (para diagnóstico rápido ou depuração)
powershell -ExecutionPolicy Bypass -File C:\SPARC\beta\Build-Beta.ps1 -SemOfuscacao
```

Pipeline automatizado em 6 etapas:
1. Validação/geração de chaves RSA em `beta\keys\`.
2. Cópia isolada da base `src` para pasta TEMP.
3. Injeção dos componentes do `guard`, chaves públicas e token corporativo.
4. Compilação Release x64 autocontida com hook seguro no `App.xaml.cs`.
5. Ofuscação de código com validação de símbolos.
6. Publicação single-file versionada em `C:\SPARC\dist-beta\SPARC-Beta-Testes-<versao>.exe`.

## Processo de Ativação do Técnico em Campo

1. Ao abrir o aplicativo pela primeira vez, o técnico visualiza a tela de ativação com o identificador de máquina e a aba **"Cadastro & Ativação Online"**.
2. O técnico preenche seus dados (Nome, Sobrenome, Empresa, Matrícula, WhatsApp, E-mail, Cluster, UF) e clica em **"Enviar Solicitação Online"**.
3. O administrador abre o **SPARC ADMIN** (`Iniciar_Admin.cmd` ou atalho no Desktop) e visualiza a solicitação na aba **"Solicitações Pendentes (Online)"**.
4. Pela **ficha de cadastro do técnico**, o gestor valida as informações e aprova a liberação de acesso com 1 clique (ou emite chave de ativação manual caso o notebook esteja 100% offline).
5. O status é atualizado instantaneamente na nuvem corporativa e a cópia do técnico é ativada automaticamente.

## Administração e Gestão de Técnicos

- **Interface Central**: `C:\SPARC\Iniciar_Admin.cmd` (módulo SPARC ADMIN).
- **Aba "Solicitações Pendentes"**: Avaliação e auditoria cadastral de novos técnicos solicitantes.
- **Aba "Gestão de Cópias em Campo"**: Visão completa de todas as cópias em operação (Windows e Android), com filtros por plataforma, status ativo/revogado e ferramentas de revogação imediata ou prorrogação de prazos diretamente pela ficha do usuário.
