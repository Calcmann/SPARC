# -*- coding: utf-8 -*-
"""
Gera a Apresentação Executiva em PDF do Módulo de Teste de Banda Larga do SPARC.
Renderização HTML5/CSS3 moderna no formato A4 Paisagem (Landscape Presentation Deck)
impressa via Chrome Headless.
"""

import os
import sys
import base64
import subprocess
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
SPARC_ROOT = os.path.abspath(os.path.join(HERE, ".."))
OUT_DOCS = os.path.join(HERE, "SPARC_Modulo_Teste_Banda_Larga_Apresentacao.pdf")
OUT_ROOT = os.path.join(SPARC_ROOT, "SPARC_Modulo_Teste_Banda_Larga_Apresentacao.pdf")

# Carrega logo do SPARC se disponível
LOGO_PATH = os.path.join(SPARC_ROOT, "src", "NetworkDevice.UI", "Assets", "sparc_logo_horizontal.png")
if not os.path.exists(LOGO_PATH):
    LOGO_PATH = os.path.join(SPARC_ROOT, "docs", "branding", "sparc_logo.png")

logo_b64 = ""
if os.path.exists(LOGO_PATH):
    with open(LOGO_PATH, "rb") as f:
        logo_b64 = base64.b64encode(f.read()).decode("utf-8")

HTML_CONTENT = f"""<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="UTF-8">
<title>SPARC — Apresentação Executiva: Módulo de Banda Larga</title>
<style>
    @page {{
        size: A4 landscape;
        margin: 0;
    }}

    * {{
        box-sizing: border-box;
        margin: 0;
        padding: 0;
    }}

    body {{
        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
        background: #080c15;
        color: #f8fafc;
        font-size: 11pt;
        line-height: 1.45;
        -webkit-print-color-adjust: exact;
        print-color-adjust: exact;
    }}

    /* Cada slide ocupa exatamente uma página A4 Paisagem */
    .slide {{
        width: 297mm;
        height: 210mm;
        page-break-after: always;
        position: relative;
        overflow: hidden;
        padding: 16mm 20mm;
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        background: #080c15;
    }}

    /* Rodapé dos slides */
    .slide-footer {{
        display: flex;
        justify-content: space-between;
        align-items: center;
        border-top: 1px solid #1e293b;
        padding-top: 4mm;
        font-size: 8.5pt;
        color: #64748b;
    }}

    /* Topo comum dos slides de conteúdo */
    .slide-header {{
        display: flex;
        justify-content: space-between;
        align-items: center;
        border-bottom: 2px solid #0284c7;
        padding-bottom: 4mm;
        margin-bottom: 6mm;
    }}
    .slide-title {{
        font-size: 18pt;
        font-weight: 900;
        color: #f8fafc;
        letter-spacing: -0.5px;
    }}
    .slide-subtitle {{
        font-size: 9.5pt;
        color: #94a3b8;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.5px;
    }}
    .badge-pill {{
        background: #1e293b;
        border: 1px solid #334155;
        padding: 4px 10px;
        border-radius: 6px;
        font-size: 8.5pt;
        font-weight: 800;
        color: #38bdf8;
    }}

    /* SLIDE 1: CAPA */
    .cover-slide {{
        background: radial-gradient(circle at 80% 20%, #1e1b4b 0%, #080c15 65%);
        justify-content: center;
        padding: 24mm 24mm;
    }}
    .cover-brand {{
        display: flex;
        align-items: center;
        gap: 16px;
        margin-bottom: 12mm;
    }}
    .cover-tag {{
        background: linear-gradient(135deg, #7c3aed, #4f46e5);
        color: #fff;
        font-weight: 900;
        font-size: 10pt;
        padding: 6px 14px;
        border-radius: 6px;
        letter-spacing: 1px;
    }}
    .cover-title {{
        font-size: 28pt;
        font-weight: 900;
        line-height: 1.15;
        letter-spacing: -0.8px;
        color: #ffffff;
        margin-bottom: 6mm;
    }}
    .cover-title span {{
        background: linear-gradient(135deg, #38bdf8, #818cf8);
        -webkit-background-clip: text;
        -webkit-text-fill-color: transparent;
    }}
    .cover-desc {{
        font-size: 13pt;
        color: #94a3b8;
        max-width: 820px;
        line-height: 1.5;
        margin-bottom: 12mm;
    }}
    .cover-chips {{
        display: flex;
        gap: 12px;
        flex-wrap: wrap;
    }}
    .chip {{
        background: #0f172a;
        border: 1px solid #1e293b;
        padding: 8px 16px;
        border-radius: 8px;
        font-size: 9.5pt;
        color: #cbd5e1;
        font-weight: 600;
    }}
    .chip b {{ color: #38bdf8; }}

    /* GRIDS E CARDS */
    .grid-2 {{
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 14px;
    }}
    .grid-3 {{
        display: grid;
        grid-template-columns: 1fr 1fr 1fr;
        gap: 12px;
    }}
    .grid-4 {{
        display: grid;
        grid-template-columns: repeat(4, 1fr);
        gap: 10px;
    }}

    .card {{
        background: #0f172a;
        border: 1px solid #1e293b;
        border-radius: 10px;
        padding: 14px 16px;
        position: relative;
        overflow: hidden;
    }}
    .card-accent-top {{
        height: 3px;
        position: absolute;
        top: 0; left: 0; right: 0;
    }}
    .card h3 {{
        font-size: 12pt;
        font-weight: 800;
        color: #f8fafc;
        margin-bottom: 6px;
        display: flex;
        align-items: center;
        gap: 6px;
    }}
    .card p {{
        font-size: 9.5pt;
        color: #94a3b8;
        line-height: 1.4;
    }}
    .card-tag {{
        display: inline-block;
        font-size: 8pt;
        font-weight: 800;
        padding: 2px 6px;
        border-radius: 4px;
        margin-top: 8px;
    }}

    /* DESTAQUES TÉCNICOS */
    .metric-hero {{
        background: #090d16;
        border: 1px solid #1e293b;
        border-radius: 8px;
        padding: 10px 14px;
        text-align: center;
    }}
    .metric-hero .val {{
        font-size: 20pt;
        font-weight: 900;
        color: #38bdf8;
    }}
    .metric-hero .lbl {{
        font-size: 8.5pt;
        font-weight: 700;
        color: #64748b;
        text-transform: uppercase;
    }}

    /* FORMULAS E CALLOUTS */
    .formula-box {{
        background: #090d16;
        border: 1px solid #334155;
        border-left: 4px solid #38bdf8;
        border-radius: 6px;
        padding: 10px 14px;
        font-family: 'Consolas', monospace;
        font-size: 10pt;
        color: #e2e8f0;
        margin: 8px 0;
    }}
</style>
</head>
<body>

<!-- ========================================================================= -->
<!-- SLIDE 1: CAPA EXECUTIVA                                                   -->
<!-- ========================================================================= -->
<div class="slide cover-slide">
  <div>
    <div class="cover-brand">
      {f'<img src="data:image/png;base64,{logo_b64}" height="42" style="vertical-align:middle;">' if logo_b64 else '<span style="font-size:24pt; font-weight:900; color:#fff;">SPARC</span>'}
      <div class="cover-tag">ENGINEERING RELEASE</div>
      <div class="badge-pill">RFC 6349 • ITU-T Y.1564</div>
    </div>

    <div class="cover-title">
      Módulo de Teste de Banda Larga &amp; <span>Qualidade de Link</span>
    </div>

    <div class="cover-desc">
      Solução corporativa multiplataforma de telemetria avançada, seleção de menor esforço geodésico,
      correção de latência física em fibra óptica e auditoria de Bufferbloat com emissão pericial de laudo oficial.
    </div>

    <div class="cover-chips">
      <div class="chip">🖥️ <b>Desktop</b> WPF Nativo</div>
      <div class="chip">📱 <b>Mobile</b> Android Gigabit</div>
      <div class="chip">🌐 <b>Web</b> GitHub Pages 100% Client-Side</div>
      <div class="chip">⚡ <b>Física da Fibra</b> RTT = d / 78ms</div>
      <div class="chip">🇧🇷 <b>Cobertura</b> 27 Capitais + Polos</div>
    </div>
  </div>

  <div class="slide-footer">
    <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores da Claro</span>
    <span>Outubro / 2026 • Documento Oficial de Apresentação Técnica</span>
  </div>
</div>

<!-- ========================================================================= -->
<!-- SLIDE 2: DESAFIO & ARQUITETURA TRÍPLICE                                   -->
<!-- ========================================================================= -->
<div class="slide">
  <div>
    <div class="slide-header">
      <div>
        <div class="slide-title">Desafio Operacional &amp; Arquitetura Tríplice</div>
        <div class="slide-subtitle">Padronização de Testes de Campo &bull; Fim das Ferramentas Informais</div>
      </div>
      <div class="badge-pill">ARQUITETURA UNIFICADA</div>
    </div>

    <div class="grid-3" style="margin-bottom: 12px;">
      <!-- Coluna 1: Desktop -->
      <div class="card">
        <div class="card-accent-top" style="background: #10b981;"></div>
        <h3>🖥️ SPARC Desktop (Windows)</h3>
        <p>
          Integrado diretamente no software principal dos técnicos. Permite isolamento rigoroso de interfaces
          (Ethernet vs Wi-Fi), testes pré e pós-ativação SAIP, e emissão imediata de relatório PDF com assinatura técnica.
        </p>
        <span class="card-tag" style="background:#064e3b; color:#6ee7b7;">WPF • .NET 8 • Single-File</span>
      </div>

      <!-- Coluna 2: Mobile -->
      <div class="card">
        <div class="card-accent-top" style="background: #3b82f6;"></div>
        <h3>📱 SPARC Mobile (Android)</h3>
        <p>
          Projetado para técnicos em campo operando com smartphones corporativos e adaptadores USB-C Gigabit Ethernet.
          Conexão direta na porta WAN/LAN da CPE para aferição de vazão nominal sem carregar notebooks pesados.
        </p>
        <span class="card-tag" style="background:#1e3a8a; color:#93c5fd;">.NET MAUI • USB-C Gigabit</span>
      </div>

      <!-- Coluna 3: Web -->
      <div class="card">
        <div class="card-accent-top" style="background: #8b5cf6;"></div>
        <h3>🌐 SPARC Web (GitHub Pages)</h3>
        <p>
          Versão 100% Client-Side hospedada sem custos no GitHub. Zero instalação, responsiva para celulares,
          tablets e computadores, permitindo homologações instantâneas em qualquer dispositivo.
        </p>
        <span class="card-tag" style="background:#581c87; color:#d8b4fe;">Zero-Backend • CORS Nativo</span>
      </div>
    </div>

    <div class="card" style="background: #090d16; border-color: #334155;">
      <h3>🎯 Proposta de Valor e Benefícios Imediatos</h3>
      <div class="grid-3" style="margin-top: 8px;">
        <div><b>✓ Validação Pericial:</b> Compara a taxa real com o perfil contratado no SAIP (Down/Up) com tolerância de engenharia.</div>
        <div><b>✓ Fim dos Falsos Chamados:</b> Distingue com precisão lentidões causadas por rota externa daquelas no enlace físico.</div>
        <div><b>✓ Laudo Executivo em PDF:</b> Emite documento formal assinado que comprova a entrega técnica da banda ao cliente.</div>
      </div>
    </div>
  </div>

  <div class="slide-footer">
    <span>SPARC Core v0.8.x &bull; Módulo de Banda Larga</span>
    <span>Slide 2 / 5</span>
  </div>
</div>

<!-- ========================================================================= -->
<!-- SLIDE 3: MOTORES DE TESTE & MENOR ESFORÇO NACIONAL                        -->
<!-- ========================================================================= -->
<div class="slide">
  <div>
    <div class="slide-header">
      <div>
        <div class="slide-title">Multi-Provedores &amp; Algoritmo de Menor Esforço</div>
        <div class="slide-subtitle">Seleção Dinâmica Nacional &bull; Eleição pelo Menor RTT e Proximidade</div>
      </div>
      <div class="badge-pill">COBERTURA DINÂMICA 27 ESTADOS</div>
    </div>

    <div class="grid-2" style="margin-bottom: 12px;">
      <!-- Motores de Teste -->
      <div class="card">
        <div class="card-accent-top" style="background: #38bdf8;"></div>
        <h3>⚙️ Motores de Teste Suportados</h3>
        <ul style="list-style: none; margin-top: 8px; font-size: 9.5pt; color: #cbd5e1; display:flex; flex-direction:column; gap:8px;">
          <li><b>⚡ Cloudflare Speed Engine (Padrão Oficial):</b> Utiliza a malha global de borda com medição progressiva de chunks (1MB a 25MB) e telemetria de headers nativos.</li>
          <li><b>🌐 M-Lab NDT (Measurement Lab / PTT.br):</b> Infraestrutura mantida pelo consórcio aberto Google / Code for Science &amp; Society com servidores em IX.br brasileiros.</li>
          <li><b>🚀 Multi-Stream Regional (IX.br CDNs):</b> Conexões paralelas concorrentes projetadas para estresse de throughput em links dedicados corporativos.</li>
        </ul>
      </div>

      <!-- Algoritmo de Menor Esforço -->
      <div class="card">
        <div class="card-accent-top" style="background: #10b981;"></div>
        <h3>🧭 Política de Menor Esforço (Pré-Teste RTT)</h3>
        <p style="margin-bottom: 8px;">
          Antes do teste pesado, o SPARC dispara uma micro-sondagem rápida concorrente para eleger o caminho ideal:
        </p>
        <div class="formula-box">
          Score = Vazão_probe * (100 / (Latência_RTT + 1))
        </div>
        <p style="font-size: 9pt;">
          O servidor de maior pontuação e menor latência física é promovido a <b>Servidor Principal</b>,
          e o segundo colocado torna-se <b>Fallback Automático</b> em caso de saturação.
        </p>
      </div>
    </div>

    <div class="card" style="background: #090d16;">
      <h3>🇧🇷 Descoberta Geodésica Adaptativa em Qualquer Ponto do Brasil</h3>
      <p style="font-size: 9.5pt; margin-top: 4px;">
        O sistema possui uma matriz com <b>todas as 27 capitais e principais polos industriais</b> do Brasil.
        Ao detectar o GPS do link (seja em Manaus, Fortaleza, Brasília, Porto Alegre ou Florianópolis),
        o SPARC calcula a distância Haversine para todos os polos e lista automaticamente os <b>8 servidores mais próximos</b>
        daquela localidade, garantindo precisão em qualquer estado brasileiro.
      </p>
    </div>
  </div>

  <div class="slide-footer">
    <span>SPARC Core v0.8.x &bull; Módulo de Banda Larga</span>
    <span>Slide 3 / 5</span>
  </div>
</div>

<!-- ========================================================================= -->
<!-- SLIDE 4: FÍSICA DA FIBRA ÓPTICA & BUFFERBLOAT                            -->
<!-- ========================================================================= -->
<div class="slide">
  <div>
    <div class="slide-header">
      <div>
        <div class="slide-title">Física da Fibra Óptica &amp; Auditoria de Bufferbloat</div>
        <div class="slide-subtitle">Piso Teórico de Propagação da Luz &bull; Identificação Precisa de Gargalos</div>
      </div>
      <div class="badge-pill">ANÁLISE PERICIAL DE ENLACE</div>
    </div>

    <div class="grid-2" style="margin-bottom: 12px;">
      <!-- Física da Fibra -->
      <div class="card">
        <div class="card-accent-top" style="background: #0284c7;"></div>
        <h3>⚡ Propagação da Luz em Fibra Monomodo</h3>
        <p>
          A velocidade da luz no núcleo de sílica da fibra óptica é \(v \approx 204.000\text{{ km/s}}\).
          Isso estabelece um piso físico inquebrável de ida e volta (RTT):
        </p>
        <div class="formula-box">
          RTT_teorico = Distancia_Geodesica (km) / 78.0 ms
        </div>
        <p style="font-size: 9pt; margin-top: 6px;">
          <b>Eficiência de Rota:</b> \(\eta = (RTT_{{teorico}} / RTT_{{medido}}) \times 100\%\)<br>
          Permite ao técnico saber instantaneamente se a latência alta decorre da distância física continental
          ou de atrasos residuais em filas de roteadores e rotas indiretas (\(\Delta\tau\)).
        </p>
      </div>

      <!-- Bufferbloat -->
      <div class="card">
        <div class="card-accent-top" style="background: #8b5cf6;"></div>
        <h3>⚖️ Auditoria de Bufferbloat (Estabilidade sob Carga)</h3>
        <p>
          Mede a degradação e o inchaço de latência nos buffers de rede sob carga máxima de tráfego simultâneo:
        </p>
        <div class="grid-3" style="margin-top: 10px;">
          <div class="metric-hero">
            <div class="val" style="color: #0284c7;">Idle</div>
            <div class="lbl">Repouso</div>
          </div>
          <div class="metric-hero">
            <div class="val" style="color: #10b981;">Down</div>
            <div class="lbl">Sob Download</div>
          </div>
          <div class="metric-hero">
            <div class="val" style="color: #8b5cf6;">Up</div>
            <div class="lbl">Sob Upload</div>
          </div>
        </div>
        <p style="font-size: 9pt; margin-top: 8px;">
          <b>Classificação Automática:</b> Notas de <b>Grade A+</b> (sem inchaço) até <b>Grade D</b> (bufferbloat severo),
          essencial para homologar links dedicados corporativos e serviços VoIP/VPN.
        </p>
      </div>
    </div>

    <div class="grid-4">
      <div class="metric-hero">
        <div class="val">~15 km</div>
        <div class="lbl">Florianópolis (Local)</div>
      </div>
      <div class="metric-hero">
        <div class="val">~91 km</div>
        <div class="lbl">Blumenau (Vale)</div>
      </div>
      <div class="metric-hero">
        <div class="val">~250 km</div>
        <div class="lbl">Curitiba (Sul)</div>
      </div>
      <div class="metric-hero">
        <div class="val">~505 km</div>
        <div class="lbl">São Paulo (Datacenter GRU)</div>
      </div>
    </div>
  </div>

  <div class="slide-footer">
    <span>SPARC Core v0.8.x &bull; Módulo de Banda Larga</span>
    <span>Slide 4 / 5</span>
  </div>
</div>

<!-- ========================================================================= -->
<!-- SLIDE 5: INTERFACE MODERNA & LAUDO PERICIAL EM PDF                        -->
<!-- ========================================================================= -->
<div class="slide">
  <div>
    <div class="slide-header">
      <div>
        <div class="slide-title">Design Moderno HUD &amp; Emissão de Laudo Oficial</div>
        <div class="slide-subtitle">Gráficos de Área em Alta Definição &bull; Certificação com Assinatura Técnica</div>
      </div>
      <div class="badge-pill">PRODUÇÃO &amp; COMPLIANCE</div>
    </div>

    <div class="grid-2" style="margin-bottom: 12px;">
      <!-- Interface HUD -->
      <div class="card">
        <div class="card-accent-top" style="background: #10b981;"></div>
        <h3>🎨 Painel Digital HUD (Cyber-Clean)</h3>
        <ul style="list-style: none; margin-top: 8px; font-size: 9.5pt; color: #cbd5e1; display:flex; flex-direction:column; gap:8px;">
          <li><b>✓ Gráfico com Preenchimento em Gradiente (Area Chart):</b> Visualização em tempo real das curvas de Download (verde) e Upload (azul) com ondas translúcidas aceleradas por GPU.</li>
          <li><b>✓ Indicadores de Pico e Jitter:</b> Painéis digitais de alto contraste com valores médios e picos de rajada.</li>
          <li><b>✓ Diagrama Topológico:</b> Ponte visual Haversine entre o Link Cliente e o Datacenter PoP com telemetria georreferenciada.</li>
        </ul>
      </div>

      <!-- Laudo Oficial PDF -->
      <div class="card">
        <div class="card-accent-top" style="background: #0284c7;"></div>
        <h3>📄 Relatório Pericial A4 com Gráficos SVG</h3>
        <ul style="list-style: none; margin-top: 8px; font-size: 9.5pt; color: #cbd5e1; display:flex; flex-direction:column; gap:8px;">
          <li><b>✓ Gráficos Vetoriais SVG Inline:</b> Throughput temporal contínuo e barras comparativas de Bufferbloat com marcadores de \(\Delta\text{{ms}}\).</li>
          <li><b>✓ Selo Criptográfico de Homologação:</b> Código único de rastreabilidade (<code>LAUDO Nº SPARC-BL-YYYYMMDD-XXXX</code>).</li>
          <li><b>✓ Assinaturas Formais:</b> Campos oficiais para o Técnico de Campo e Supervisão de Engenharia de Redes.</li>
        </ul>
      </div>
    </div>

    <!-- Link de Acesso Web -->
    <div class="card" style="background: linear-gradient(135deg, #0f172a, #1e1b4b); border-color: #4f46e5;">
      <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px;">
        <div>
          <h3 style="color: #38bdf8;">🌐 Acesso Web Imediato via GitHub Pages</h3>
          <p style="color: #cbd5e1; font-size: 10pt;">
            Disponível publicamente sem necessidade de instalação de software para técnicos, clientes e auditorias:
          </p>
          <div style="font-size: 12pt; font-weight: 900; color: #a5f3fc; font-family: monospace; margin-top: 4px;">
            https://calcmann.github.io/SPARC/
          </div>
        </div>
        <div style="background: #10b981; color: #064e3b; padding: 8px 16px; border-radius: 8px; font-weight: 900; font-size: 10pt;">
          100% OPERACIONAL
        </div>
      </div>
    </div>
  </div>

  <div class="slide-footer">
    <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores da Claro</span>
    <span>Slide 5 / 5 &bull; Documento Gerado Automaticamente</span>
  </div>
</div>

</body>
</html>
"""

def main():
    tmp_html = os.path.join(HERE, "tmp_apresentacao_banda_larga.html")
    with open(tmp_html, "w", encoding="utf-8") as f:
        f.write(HTML_CONTENT)

    chrome_paths = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    ]
    chrome_bin = next((p for p in chrome_paths if os.path.exists(p)), None)
    if not chrome_bin:
        print("[ERRO] Google Chrome ou Edge não encontrado para renderização.")
        sys.exit(1)

    print(f"[*] Renderizando Apresentação em PDF via Chrome Headless...")
    cmd = [
        chrome_bin,
        "--headless=new",
        "--no-sandbox",
        "--disable-gpu",
        "--run-all-compositor-stages-before-draw",
        f"--print-to-pdf={OUT_DOCS}",
        f"file:///{tmp_html.replace(os.sep, '/')}"
    ]

    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0 or not os.path.exists(OUT_DOCS):
        print(f"[ERRO] Falha ao gerar PDF via Chrome: {res.stderr}")
        sys.exit(1)

    try:
        shutil.copyfile(OUT_DOCS, OUT_ROOT)
    except Exception as e:
        print(f"[AVISO] Falha ao copiar para raiz: {e}")

    try:
        os.remove(tmp_html)
    except:
        pass

    size_kb = os.path.getsize(OUT_DOCS) / 1024
    print(f"[OK] Apresentação em PDF gerada com sucesso!")
    print(f"     -> {OUT_DOCS} ({size_kb:.1f} KB)")
    print(f"     -> {OUT_ROOT}")

if __name__ == "__main__":
    main()
