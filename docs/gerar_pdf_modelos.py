# -*- coding: utf-8 -*-
"""
Gera o PDF executivo oficial da Matriz de Roteadores Homologados no SPARC.
Utiliza renderização HTML5/CSS3 moderna impressa via Chrome Headless ajustada
para caber com perfeição milimétrica em 1 PÁGINA ÚNICA A4.
"""

import os
import sys
import base64
import subprocess
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
SPARC_ROOT = os.path.abspath(os.path.join(HERE, ".."))
OUT_DOCS = os.path.join(HERE, "SPARC_Modelos_Roteadores_Homologados.pdf")
OUT_ROOT = os.path.join(SPARC_ROOT, "SPARC_Modelos_Roteadores_Homologados.pdf")
OUT_BETA = os.path.join(SPARC_ROOT, "dist-beta", "SPARC_Modelos_Roteadores_Homologados.pdf")

# Carrega logos em base64 se disponíveis
LOGO_PATH = os.path.join(SPARC_ROOT, "src", "NetworkDevice.UI", "Assets", "sparc_logo_full.png")
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
<title>SPARC — Matriz de Roteadores Homologados</title>
<style>
    @page {{
        size: A4 portrait;
        margin: 7mm 9mm 7mm 9mm;
    }}

    * {{
        box-sizing: border-box;
        margin: 0;
        padding: 0;
    }}

    body {{
        font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
        color: #0f172a;
        background: #ffffff;
        font-size: 8.8pt;
        line-height: 1.35;
    }}

    .header-card {{
        background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
        border-radius: 6px;
        padding: 12px 18px;
        color: #ffffff;
        margin-bottom: 10px;
        border-left: 5px solid #ee1c25;
        display: flex;
        align-items: center;
        justify-content: space-between;
    }}

    .header-titles h1 {{
        font-size: 15pt;
        font-weight: 800;
        letter-spacing: -0.3px;
        color: #ffffff;
        margin-bottom: 2px;
        display: flex;
        align-items: center;
        gap: 8px;
    }}

    .header-titles h1 span.sparc-tag {{
        background: #ee1c25;
        color: #ffffff;
        font-size: 8pt;
        padding: 2px 7px;
        border-radius: 3px;
        font-weight: 700;
        letter-spacing: 0.5px;
    }}

    .header-titles h2 {{
        font-size: 10pt;
        font-weight: 600;
        color: #94a3b8;
    }}

    .header-meta {{
        display: flex;
        gap: 6px;
        margin-top: 6px;
        flex-wrap: wrap;
    }}

    .badge {{
        display: inline-flex;
        align-items: center;
        padding: 2px 7px;
        border-radius: 3px;
        font-size: 7.5pt;
        font-weight: 600;
    }}

    .badge-red {{ background: rgba(238, 28, 37, 0.25); color: #fca5a5; border: 1px solid rgba(238, 28, 37, 0.4); }}
    .badge-blue {{ background: rgba(56, 189, 248, 0.18); color: #7dd3fc; border: 1px solid rgba(56, 189, 248, 0.3); }}
    .badge-slate {{ background: rgba(148, 163, 184, 0.18); color: #cbd5e1; border: 1px solid rgba(148, 163, 184, 0.3); }}

    .header-logo {{
        max-height: 44px;
        max-width: 150px;
        object-fit: contain;
    }}

    .section-title {{
        font-size: 10.5pt;
        font-weight: 700;
        color: #0f172a;
        margin: 9px 0 5px 0;
        padding-bottom: 3px;
        border-bottom: 2px solid #e2e8f0;
        display: flex;
        align-items: center;
        gap: 6px;
    }}

    .section-title.cisco {{ color: #0284c7; border-bottom-color: #0284c7; }}
    .section-title.hpe {{ color: #059669; border-bottom-color: #059669; }}
    .section-title.fortinet {{ color: #b91c1c; border-bottom-color: #b91c1c; }}

    .section-title .icon {{
        font-size: 11pt;
    }}

    table.spec-table {{
        width: 100%;
        border-collapse: collapse;
        margin-bottom: 8px;
        background: #ffffff;
        border: 1px solid #cbd5e1;
        border-radius: 5px;
        overflow: hidden;
        font-size: 8pt;
    }}

    table.spec-table thead th {{
        background: #f1f5f9;
        color: #334155;
        font-weight: 700;
        text-align: left;
        padding: 5px 8px;
        border-bottom: 1.5px solid #cbd5e1;
        text-transform: uppercase;
        font-size: 7.2pt;
        letter-spacing: 0.3px;
    }}

    table.spec-table tbody td {{
        padding: 5px 8px;
        border-bottom: 1px solid #e2e8f0;
        vertical-align: middle;
    }}

    table.spec-table tbody tr:last-child td {{
        border-bottom: none;
    }}

    table.spec-table tbody tr:nth-child(even) {{
        background: #f8fafc;
    }}

    .serie-name {{
        font-weight: 700;
        color: #0f172a;
    }}

    .models-list {{
        color: #334155;
        font-weight: 600;
    }}

    .mono-tag {{
        font-family: 'Consolas', 'Courier New', monospace;
        background: #f1f5f9;
        padding: 1px 4px;
        border-radius: 3px;
        font-size: 7.5pt;
        color: #0f172a;
        border: 1px solid #e2e8f0;
        display: inline-block;
    }}

    .mono-port {{
        font-family: 'Consolas', 'Courier New', monospace;
        font-weight: 700;
        color: #0369a1;
        background: #e0f2fe;
        padding: 1px 5px;
        border-radius: 3px;
        border: 1px solid #bae6fd;
        display: inline-block;
        font-size: 7.5pt;
    }}

    .mono-port.wan {{
        color: #b91c1c;
        background: #fee2e2;
        border-color: #fca5a5;
    }}

    .mono-fw {{
        font-family: 'Consolas', 'Courier New', monospace;
        font-size: 7.2pt;
        color: #475569;
        display: block;
        margin-top: 1px;
    }}

    .bullet-list {{
        list-style: none;
        padding-left: 0;
    }}

    .bullet-list li {{
        position: relative;
        padding-left: 12px;
        margin-bottom: 1px;
    }}

    .bullet-list li::before {{
        content: "•";
        position: absolute;
        left: 0;
        color: #ef4444;
        font-weight: bold;
    }}

    .notes-box {{
        background: #f8fafc;
        border: 1px solid #e2e8f0;
        border-left: 4px solid #3b82f6;
        border-radius: 5px;
        padding: 7px 12px;
        margin-top: 6px;
        font-size: 7.6pt;
        color: #334155;
    }}

    .notes-box h4 {{
        font-size: 8.2pt;
        font-weight: 700;
        color: #1e3a8a;
        margin-bottom: 3px;
        display: flex;
        align-items: center;
        gap: 4px;
    }}

    .notes-grid {{
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 4px 12px;
    }}

    .footer {{
        margin-top: 8px;
        padding-top: 5px;
        border-top: 1px solid #e2e8f0;
        display: flex;
        justify-content: space-between;
        align-items: center;
        font-size: 7pt;
        color: #64748b;
    }}

    .footer strong {{
        color: #0f172a;
    }}
</style>
</head>
<body>

<div class="header-card">
    <div class="header-titles">
        <h1>
            SPARC
            <span class="sparc-tag">OFICIAL</span>
        </h1>
        <h2>Matriz de Roteadores e Equipamentos Homologados</h2>
        <div class="header-meta">
            <span class="badge badge-red">Versão 0.8.100 (Estável)</span>
            <span class="badge badge-blue">Windows 10/11 x64 & Android 11+ OTG</span>
            <span class="badge badge-slate">Claro Brasil • Assistência Técnica & Campo</span>
        </div>
    </div>
    {"<img class='header-logo' src='data:image/png;base64," + logo_b64 + "' alt='SPARC Logo'>" if logo_b64 else ""}
</div>

<!-- ========================================== -->
<!-- 1. CISCO SYSTEMS                            -->
<!-- ========================================== -->
<div class="section-title cisco">
    <span class="icon">🖧</span>
    1. Cisco Systems (Cisco IOS)
</div>

<table class="spec-table">
    <thead>
        <tr>
            <th style="width: 20%;">Série / Família</th>
            <th style="width: 26%;">Modelos Suportados</th>
            <th style="width: 24%;">Método de Interrupção</th>
            <th style="width: 14%;">Porta TFTP / Staging</th>
            <th style="width: 16%;">Formato Firmware</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td class="serie-name">Cisco Série 1900</td>
            <td class="models-list">Cisco 1905, 1921, 1941</td>
            <td><span class="mono-tag">Break</span> (ROMMON @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 0/0</span></td>
            <td><strong>.bin</strong><span class="mono-fw">c1900-universalk9-mz*.bin</span></td>
        </tr>
        <tr>
            <td class="serie-name">Cisco Série 2900</td>
            <td class="models-list">Cisco 2901, 2911, 2921, 2951</td>
            <td><span class="mono-tag">Break</span> (ROMMON @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 0/0</span></td>
            <td><strong>.bin</strong><span class="mono-fw">c2900-universalk9-mz*.bin</span></td>
        </tr>
        <tr>
            <td class="serie-name">Cisco Série 900</td>
            <td class="models-list">Cisco C921-4P, C921, ISR 900</td>
            <td><span class="mono-tag">Ctrl+C puro</span> (ROMMON @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 4</span></td>
            <td><strong>.bin</strong><span class="mono-fw">c900-universalk9-mz*.bin</span></td>
        </tr>
        <tr>
            <td class="serie-name">Cisco Série 800</td>
            <td class="models-list">Cisco C841M-4X, C841M-8X, C841, C800M</td>
            <td><span class="mono-tag">Break</span> (ROMMON @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 4</span></td>
            <td><strong>.bin</strong><span class="mono-fw">c841-universalk9-mz*.bin</span></td>
        </tr>
    </tbody>
</table>

<!-- ========================================== -->
<!-- 2. HEWLETT PACKARD ENTERPRISE              -->
<!-- ========================================== -->
<div class="section-title hpe">
    <span class="icon">🖧</span>
    2. Hewlett Packard Enterprise (HPE / H3C Comware 7)
</div>

<table class="spec-table">
    <thead>
        <tr>
            <th style="width: 20%;">Série / Família</th>
            <th style="width: 26%;">Modelos Suportados</th>
            <th style="width: 24%;">Método de Interrupção</th>
            <th style="width: 14%;">Porta TFTP / Staging</th>
            <th style="width: 16%;">Formato Firmware</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td class="serie-name">HPE MSR 954 / 958</td>
            <td class="models-list">MSR 954, MSR 954-W, MSR 958</td>
            <td><span class="mono-tag">Ctrl+B</span> (BootWare @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 0/0</span></td>
            <td><strong>.ipe / .bin</strong><span class="mono-fw">msr954-cmw710-*.ipe</span></td>
        </tr>
        <tr>
            <td class="serie-name">HPE MSR 930 / 931</td>
            <td class="models-list">MSR 930, MSR 931, MSR 935</td>
            <td><span class="mono-tag">Ctrl+B</span> (BootWare @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 0/0</span></td>
            <td><strong>.ipe</strong><span class="mono-fw">msr930-cmw710-*.ipe</span></td>
        </tr>
        <tr>
            <td class="serie-name">HPE MSR 1002 / 1003</td>
            <td class="models-list">MSR 1002, MSR 1003, MSR 1004</td>
            <td><span class="mono-tag">Ctrl+B</span> (BootWare @ 9600 bps)</td>
            <td><span class="mono-port">GigabitEthernet 0/0</span></td>
            <td><strong>.ipe / .bin</strong><span class="mono-fw">msr1000-cmw710-*.ipe</span></td>
        </tr>
    </tbody>
</table>

<!-- ========================================== -->
<!-- 3. FORTINET (FORTIOS)                       -->
<!-- ========================================== -->
<div class="section-title fortinet">
    <span class="icon">🛡️</span>
    3. Fortinet (FortiOS)
</div>

<table class="spec-table">
    <thead>
        <tr>
            <th style="width: 20%;">Série / Família</th>
            <th style="width: 26%;">Modelos Suportados</th>
            <th style="width: 26%;">Modo de Operação</th>
            <th style="width: 14%;">Porta TFTP / Staging</th>
            <th style="width: 14%;">Formato Firmware</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td class="serie-name">FortiGate 40F</td>
            <td class="models-list">Fortinet FortiGate 40F<br><small style="color:#64748b;">(FG-40F / FGT-40F)</small></td>
            <td>
                <span class="mono-tag">Console Serial @ 9600 / 115200 bps</span>
                <ul class="bullet-list" style="margin-top: 3px; font-size: 7.5pt;">
                    <li>BIOS Recovery (FortiBootLoader TFTP)</li>
                    <li>Reset / Quebra Autônoma de Senha</li>
                    <li>Auto-aceite de Termo (PRE WARNING)</li>
                </ul>
            </td>
            <td>
                <span class="mono-port wan">Porta WAN (BIOS TFTP)</span>
                <span class="mono-port" style="margin-top: 3px;">Porta LAN1 (Testes)</span>
            </td>
            <td><strong>.out</strong><span class="mono-fw">FGT_40F-*.out</span></td>
        </tr>
    </tbody>
</table>

<!-- ========================================== -->
<!-- NOTAS TÉCNICAS E OPERACIONAIS              -->
<!-- ========================================== -->
<div class="notes-box">
    <h4>ℹ️ Diretrizes Técnicas e Operacionais de Bancada e Campo</h4>
    <div class="notes-grid">
        <div>
            • <strong>Detecção Automática:</strong> O motor analítico do SPARC identifica o fabricante, arquitetura e estado operacional (Normal, ROMMON, BootWare, BIOS Halt ou Bloqueado por Senha) diretamente pela serial console.<br>
            • <strong>Staging LAN Dinâmico:</strong> Em roteadores Cisco, o SPARC aplica IP temporário via console para carga HTTP/TFTP de bancada, validando a interface antes para prevenir conflitos de sub-rede.
        </div>
        <div>
            • <strong>Multiplataforma Nativa:</strong> Suporte completo no Windows (desktop/notebook na Base Claro) e no Android (smartphone/tablet via OTG no cliente), com os mesmos algoritmos de provisionamento.<br>
            • <strong>Aceite de Avisos Legais:</strong> O diálogo inicial pós-recuperação do FortiGate 40F (PRE WARNING) é aceito automaticamente com "a" sem intervenção manual do operador.
        </div>
    </div>
</div>

<div class="footer">
    <div><strong>SPARC</strong> — Sistema de Provisionamento e Automação de Roteadores Claro</div>
    <div>Documento Gerado em 15/10/2026 • Classificação: Uso Interno Claro • Página 1 de 1</div>
</div>

</body>
</html>
"""

def main():
    tmp_html = os.path.join(HERE, "temp_modelos.html")
    with open(tmp_html, "w", encoding="utf-8") as f:
        f.write(HTML_CONTENT)

    chrome_paths = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ]
    chrome_bin = next((p for p in chrome_paths if os.path.exists(p)), None)
    if not chrome_bin:
        print("[ERRO] Google Chrome não encontrado para renderização.")
        sys.exit(1)

    print(f"[*] Renderizando PDF via Chrome Headless...")
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
        shutil.copyfile(OUT_DOCS, OUT_BETA)
    except Exception as e:
        print(f"[AVISO] Falha ao copiar para raízes adicionais: {e}")

    try:
        os.remove(tmp_html)
    except:
        pass

    size_kb = os.path.getsize(OUT_DOCS) / 1024
    print(f"[OK] PDF gerado com sucesso!")
    print(f"     -> {OUT_DOCS} ({size_kb:.1f} KB)")
    print(f"     -> {OUT_ROOT}")
    print(f"     -> {OUT_BETA}")

if __name__ == "__main__":
    main()
