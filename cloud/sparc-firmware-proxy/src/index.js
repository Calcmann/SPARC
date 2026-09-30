/**
 * SPARC Firmware Proxy • Cloudflare Worker
 * 
 * Permite que roteadores em campo (Cisco IOS, HPE Comware, Fortinet) realizem
 * o download de firmwares homologados diretamente pela porta WAN via HTTP simples,
 * enquanto a autenticação privada com o GitHub Releases é tratada nos bastidores.
 */

// Mapeamento de apelidos de modelos para busca inteligente de arquivos
const MODEL_ALIASES = {
  "c841": ["c800", "c841", "841"],
  "c921": ["c900", "c921", "921"],
  "c1900": ["c1900", "1921", "1941", "1900"],
  "fortigate-40f": ["fgt", "forti", "40f"],
  "fgt40f": ["fgt", "forti", "40f"],
  "msr954": ["msr954", "954", "958", "95x"],
  "msr930": ["msr930", "930", "931", "935", "93x"],
  "msr1002": ["msr100", "1002", "1003", "1000", "100x"]
};

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    // Rota Raiz: Status do serviço e lista de modelos homologados
    if (url.pathname === "/" || url.pathname === "/status") {
      return handleStatus(env);
    }

    // Rota de Download: /download/:modelo ou /:modelo
    const pathParts = url.pathname.replace(/^\/+|\/+$/g, "").split("/");
    let requestedModel = pathParts[0] === "download" && pathParts.length > 1 ? pathParts[1] : pathParts[0];
    requestedModel = requestedModel.toLowerCase();

    // 1. Validação de Chave Secreta
    const expectedKey = env.SPARC_SECRET_KEY || "CR@PS";
    const providedKey = url.searchParams.get("key") || request.headers.get("x-sparc-key");

    if (!providedKey || providedKey !== expectedKey) {
      return new Response("403 Proibido: Chave de autorização SPARC ausente ou inválida.\nUso: ?key=" + expectedKey + "\n", {
        status: 403,
        headers: { "Content-Type": "text/plain; charset=utf-8" }
      });
    }

    // 2. Validação do Token do GitHub
    const rawToken = env.GITHUB_PAT || env.GITHUB_TOKEN || "";
    const githubToken = rawToken.trim();
    if (!githubToken) {
      return new Response("500 Erro de Configuração: Token GITHUB_PAT não configurado no Worker.\n", {
        status: 500,
        headers: { "Content-Type": "text/plain; charset=utf-8" }
      });
    }

    try {
      const owner = env.REPO_OWNER || "Calcmann";
      const repo = env.REPO_NAME || "repo";
      const tag = env.RELEASE_TAG || "homologados";

      // 3. Consulta a lista de assets na Release 'homologados'
      const releaseApiUrl = `https://api.github.com/repos/${owner}/${repo}/releases/tags/${tag}`;
      const releaseResp = await fetch(releaseApiUrl, {
        headers: {
          "Authorization": `Bearer ${githubToken}`,
          "User-Agent": "SPARC-Firmware-Proxy",
          "Accept": "application/vnd.github+json"
        }
      });

      if (!releaseResp.ok) {
        const errText = await releaseResp.text();
        return new Response(`Erro ao consultar GitHub Release (${releaseResp.status}): ${errText}\n`, {
          status: 502,
          headers: { "Content-Type": "text/plain; charset=utf-8" }
        });
      }

      const releaseData = await releaseResp.json();
      const assets = releaseData.assets || [];

      // 4. Localiza o asset correspondente ao modelo solicitado
      const matchedAsset = findAssetForModel(requestedModel, assets);
      if (!matchedAsset) {
        const available = assets.map(a => a.name).join("\n - ");
        return new Response(`Modelo '${requestedModel}' não localizado na Release '${tag}'.\nArquivos disponíveis:\n - ${available}\n`, {
          status: 404,
          headers: { "Content-Type": "text/plain; charset=utf-8" }
        });
      }

      // 5. Requisita o download do asset binário da API do GitHub
      const assetUrl = `https://api.github.com/repos/${owner}/${repo}/releases/assets/${matchedAsset.id}`;
      const assetResp = await fetch(assetUrl, {
        headers: {
          "Authorization": `Bearer ${githubToken}`,
          "User-Agent": "SPARC-Firmware-Proxy",
          "Accept": "application/octet-stream"
        },
        redirect: "follow"
      });

      if (!assetResp.ok && assetResp.status !== 302 && assetResp.status !== 301) {
        const err = await assetResp.text();
        return new Response(`Erro ao baixar asset do GitHub (${assetResp.status}): ${err}\n`, {
          status: 502,
          headers: { "Content-Type": "text/plain; charset=utf-8" }
        });
      }

      // Se for método HEAD, retorna apenas os cabeçalhos sem transferir o corpo
      if (request.method === "HEAD") {
        return new Response(null, {
          status: 200,
          headers: {
            "Content-Type": "application/octet-stream",
            "Content-Length": matchedAsset.size.toString(),
            "Content-Disposition": `attachment; filename="${matchedAsset.name}"`,
            "X-Sparc-Model": requestedModel,
            "X-Sparc-Firmware": matchedAsset.name
          }
        });
      }

      // 6. Faz o stream direto para o roteador com Content-Length exato
      const responseHeaders = new Headers();
      responseHeaders.set("Content-Type", "application/octet-stream");
      responseHeaders.set("Content-Length", matchedAsset.size.toString());
      responseHeaders.set("Content-Disposition", `attachment; filename="${matchedAsset.name}"`);
      responseHeaders.set("X-Sparc-Model", requestedModel);
      responseHeaders.set("X-Sparc-Firmware", matchedAsset.name);
      responseHeaders.set("Cache-Control", "public, max-age=86400");

      return new Response(assetResp.body, {
        status: 200,
        headers: responseHeaders
      });

    } catch (ex) {
      return new Response(`Erro interno no proxy: ${ex.message}\n`, {
        status: 500,
        headers: { "Content-Type": "text/plain; charset=utf-8" }
      });
    }
  }
};

function findAssetForModel(model, assets) {
  const clean = model.toLowerCase();
  const aliases = MODEL_ALIASES[clean] || [clean];

  for (const asset of assets) {
    const name = asset.name.toLowerCase();
    for (const alias of aliases) {
      if (name.includes(alias)) {
        return asset;
      }
    }
  }

  // Busca exata pelo nome do arquivo caso passado direto
  for (const asset of assets) {
    if (asset.name.toLowerCase() === clean) {
      return asset;
    }
  }

  return null;
}

async function handleStatus(env) {
  const html = `
<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <title>SPARC Firmware Proxy • Ativo</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #0F172A; color: #F8FAFC; padding: 30px; line-height: 1.6; }
    .card { background: #1E293B; border-radius: 12px; padding: 24px; max-width: 650px; margin: 0 auto; box-shadow: 0 4px 20px rgba(0,0,0,0.4); border: 1px solid #334155; }
    h1 { color: #38BDF8; font-size: 22px; margin-top: 0; }
    code { background: #0B132B; padding: 3px 8px; border-radius: 6px; color: #4ADE80; font-size: 13px; font-family: monospace; }
    pre { background: #0B132B; padding: 14px; border-radius: 8px; color: #38BDF8; overflow-x: auto; font-size: 12.5px; }
    .tag { display: inline-block; background: #059669; color: white; padding: 2px 10px; border-radius: 9999px; font-size: 11px; font-weight: bold; }
  </style>
</head>
<body>
  <div class="card">
    <div style="display:flex; justify-content:space-between; align-items:center;">
      <h1>🌐 SPARC Firmware Proxy</h1>
      <span class="tag">ONLINE</span>
    </div>
    <p>Proxy de entrega direta de firmwares homologados para roteadores (Cisco IOS, HPE Comware, Fortinet) via porta WAN sem certificados complexos.</p>
    
    <h3>Exemplo de Comando para Cisco IOS:</h3>
    <pre>copy http://SEU-WORKER.workers.dev/download/c841?key=CR@PS flash:c800m-universalk9-mz.SPA.159-3.M12.bin</pre>

    <h3>Modelos Disponíveis:</h3>
    <ul>
      <li><code>c841</code> &bull; Cisco Série 800 / C841M</li>
      <li><code>c921</code> &bull; Cisco Série 900 / C921</li>
      <li><code>c1900</code> &bull; Cisco Série 1900 / 1941</li>
      <li><code>fortigate-40f</code> &bull; Fortinet FortiGate 40F</li>
      <li><code>msr954</code> &bull; HPE MSR 954 / 958</li>
      <li><code>msr930</code> &bull; HPE MSR 930</li>
      <li><code>msr1002</code> &bull; HPE MSR 1002</li>
    </ul>
  </div>
</body>
</html>
`;
  return new Response(html, {
    status: 200,
    headers: { "Content-Type": "text/html; charset=utf-8" }
  });
}
