# 🌐 SPARC Firmware Proxy • Cloudflare Worker

Micro-serviço serverless em Cloudflare Workers para possibilitar o download direto de firmwares homologados pelos roteadores em campo (**Cisco IOS**, **HPE Comware** e **Fortinet FortiOS**) diretamente pela sua porta WAN/Internet, sem exigir servidores locais TFTP/HTTP no notebook ou smartphone do técnico.

---

### 🚀 Como Funciona

1. O técnico em campo provisiona o roteador via **SPARC Mobile** (cabo serial USB OTG).
2. Com o link WAN ativo, o SPARC Mobile ou o técnico dispara no console serial:
   ```cisco
   copy http://sparc-firmware-proxy.<seu-subdominio>.workers.dev/download/c841?key=CR@PS flash:c800m-universalk9-mz.SPA.159-3.M12.bin
   ```
3. O Cloudflare Worker recebe a requisição simples via **HTTP (porta 80)**:
   - Valida a chave de autorização `?key=CR@PS`.
   - Consulta a Release `homologados` no repositório privado `Calcmann/repo` no GitHub com o Token PAT embutido como segredo criptografado.
   - Faz o streaming direto dos bytes do arquivo para o roteador.
4. O roteador grava o arquivo na flash, valida o MD5 e programa o comando de boot.

---

### 📦 Publicação Rápida (Deploy em 2 Minutos)

#### Opção A: Via PowerShell (Recomendada)
Abra o PowerShell na pasta `C:\SPARC\cloud\sparc-firmware-proxy` e execute:
```powershell
.\Deploy-Worker.ps1
```
> O script cuidará do `npm install`, chamará a autorização gratuita do Cloudflare no navegador e salvará o segredo criptografado `GITHUB_PAT`.

#### Opção B: Manual via Terminal
1. Entre na pasta:
   ```bash
   cd C:\SPARC\cloud\sparc-firmware-proxy
   npm install
   ```
2. Faça o login e o deploy:
   ```bash
   npx wrangler deploy
   ```
3. Adicione o Token do GitHub como segredo:
   ```bash
   npx wrangler secret put GITHUB_PAT
   # Cole o token PAT do GitHub quando solicitado
   ```

---

### 🛠️ Parâmetros e Endpoints

| Rota | Descrição | Exemplo de Modelo |
|---|---|---|
| `GET /` | Página de status e lista de firmwares disponíveis | - |
| `GET /download/c841?key=CR@PS` | Download do Cisco C841M | `c800m-universalk9-mz.SPA.159-3.M12.bin` |
| `GET /download/c921?key=CR@PS` | Download do Cisco C921 | `c900-universalk9-mz.SPA.159-3.M12.bin` |
| `GET /download/c1900?key=CR@PS` | Download do Cisco 1900/1941 | `c1900-universalk9-mz.SPA.157-3.M9.bin` |
| `GET /download/fgt40f?key=CR@PS` | Download do Fortinet 40F | `FGT_40F-v7.2.11.M-build1740-FORTINET.out` |
| `GET /download/msr954?key=CR@PS` | Download do HPE MSR 954 | `MSR954-CMW710-R6749P43.ipe` |
| `GET /download/msr930?key=CR@PS` | Download do HPE MSR 930 | `MSR93X-CMW520-R2512P04.BIN` |
| `GET /download/msr1002?key=CR@PS` | Download do HPE MSR 1002 | `MSR100X-CMW710-R6749P43.ipe` |

---

### 🔒 Segurança
- O repositório no GitHub continua **100% privado**.
- O token `GITHUB_PAT` nunca é exposto na resposta ou para o roteador.
- Somente requisições com o parâmetro `?key=CR@PS` (ou o valor definido em `SPARC_SECRET_KEY`) têm a autorização de download liberada.
