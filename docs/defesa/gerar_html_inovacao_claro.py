import os, base64

OUT_HTML = r'C:\SPARC\docs\defesa\SPARC_Apresentacao_Inovacao_Claro.html'
DIST_HTML = r'C:\SPARC\dist-beta\SPARC_Apresentacao_Inovacao_Claro.html'

IMG_DESKTOP_UI = r'C:\SPARC\docs\defesa\user_ppt_assets\slide_2_36_Imagem_35.png'
IMG_DESKTOP_16_9 = r'C:\SPARC\docs\defesa\assets\sparc_ui_16_9.png'
IMG_MOBILE_PROV = r'C:\SPARC\docs\defesa\user_ppt_assets\slide_3_21_Imagem_20.png'
IMG_MOBILE_TEST = r'C:\SPARC\docs\defesa\user_ppt_assets\slide_3_23_Imagem_22.png'

def to_base64(path):
    if os.path.exists(path):
        with open(path, 'rb') as f:
            return "data:image/png;base64," + base64.b64encode(f.read()).decode('utf-8')
    return ""

b64_desktop_ui = to_base64(IMG_DESKTOP_UI)
b64_desktop_16_9 = to_base64(IMG_DESKTOP_16_9)
b64_mobile_prov = to_base64(IMG_MOBILE_PROV)
b64_mobile_test = to_base64(IMG_MOBILE_TEST)

html_content = f"""<!DOCTYPE html>
<html lang="pt-BR">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>SPARC — Premiação Inovação Claro | George Luís Calcmann</title>
    <link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@400;600;700;800;900&family=Open+Sans:wght@400;600;700&display=swap" rel="stylesheet">
    <style>
        :root {{
            --claro-red: #DA291C;
            --claro-dark-red: #B01E15;
            --claro-dark: #262626;
            --claro-gray: #555555;
            --claro-light-bg: #F8F9FA;
            --claro-border: #E2E8F0;
            --claro-orange: #F5842B;
        }}
        * {{ margin: 0; padding: 0; box-sizing: border-box; font-family: 'Open Sans', sans-serif; }}
        body {{
            background: #111;
            color: #333;
            display: flex;
            flex-direction: column;
            align-items: center;
            min-height: 100vh;
            padding: 20px;
        }}
        .controls {{
            position: fixed;
            bottom: 20px;
            background: rgba(0,0,0,0.85);
            padding: 10px 25px;
            border-radius: 30px;
            display: flex;
            gap: 15px;
            align-items: center;
            z-index: 1000;
            box-shadow: 0 4px 20px rgba(0,0,0,0.5);
            backdrop-filter: blur(10px);
        }}
        .controls button {{
            background: var(--claro-red);
            border: none;
            color: #fff;
            padding: 8px 18px;
            border-radius: 20px;
            font-weight: 700;
            font-size: 14px;
            cursor: pointer;
            transition: all 0.2s;
        }}
        .controls button:hover {{ background: #ff3b30; transform: scale(1.05); }}
        .controls span {{ color: #fff; font-weight: 600; font-size: 14px; }}

        .deck {{
            width: 100%;
            max-width: 1200px;
            aspect-ratio: 16 / 9;
            position: relative;
            background: #fff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 15px 40px rgba(0,0,0,0.6);
        }}
        .slide {{
            position: absolute;
            top: 0; left: 0; width: 100%; height: 100%;
            padding: 40px 60px;
            display: none;
            flex-direction: column;
            background: #fff;
            animation: fadeIn 0.3s ease-in-out;
        }}
        .slide.active {{ display: flex; }}
        @keyframes fadeIn {{ from {{ opacity: 0.4; }} to {{ opacity: 1; }} }}

        /* Slide Capa & Fechamento */
        .slide-cover {{
            background: radial-gradient(circle at 80% 20%, #ff4b3e 0%, var(--claro-red) 50%, var(--claro-dark-red) 100%);
            color: #fff;
            justify-content: center;
            align-items: flex-start;
        }}
        .cover-tag {{
            background: rgba(255,255,255,0.2);
            color: #fff;
            padding: 6px 16px;
            border-radius: 20px;
            font-size: 13px;
            font-weight: 700;
            letter-spacing: 1px;
            text-transform: uppercase;
            margin-bottom: 25px;
            backdrop-filter: blur(5px);
        }}
        .cover-title {{
            font-family: 'Montserrat', sans-serif;
            font-size: 38px;
            font-weight: 900;
            line-height: 1.2;
            margin-bottom: 20px;
        }}
        .cover-category {{
            font-size: 20px;
            color: #ffdedb;
            margin-bottom: 40px;
        }}
        .cover-author {{
            font-size: 16px;
            border-left: 4px solid #fff;
            padding-left: 15px;
            line-height: 1.5;
        }}

        /* Header Padrão Claro */
        .slide-header {{
            margin-bottom: 25px;
            border-bottom: 2px solid var(--claro-border);
            padding-bottom: 15px;
            display: flex;
            justify-content: space-between;
            align-items: flex-end;
        }}
        .slide-header h2 {{
            font-family: 'Montserrat', sans-serif;
            font-size: 26px;
            font-weight: 800;
            color: var(--claro-red);
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        .slide-header .tagline {{
            font-size: 13px;
            color: #888;
            font-weight: 600;
        }}

        /* Grid & Cards */
        .grid-2 {{ display: grid; grid-template-columns: 1.1fr 0.9fr; gap: 30px; height: 100%; }}
        .grid-3 {{ display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }}
        .grid-4 {{ display: grid; grid-template-columns: repeat(2, 1fr); gap: 20px; }}
        .grid-5 {{ display: flex; flex-direction: column; gap: 12px; }}

        .card {{
            background: var(--claro-light-bg);
            border: 1px solid var(--claro-border);
            border-radius: 8px;
            padding: 16px 20px;
            transition: transform 0.2s;
        }}
        .card h4 {{
            font-family: 'Montserrat', sans-serif;
            font-size: 15px;
            font-weight: 800;
            color: var(--claro-red);
            margin-bottom: 6px;
        }}
        .card p {{ font-size: 13px; color: var(--claro-dark); line-height: 1.45; }}

        .desafio-item {{
            display: flex;
            gap: 15px;
            align-items: flex-start;
            background: #fafafa;
            border-left: 4px solid var(--claro-red);
            padding: 10px 15px;
            border-radius: 0 6px 6px 0;
        }}
        .desafio-num {{
            font-family: 'Montserrat', sans-serif;
            font-size: 24px;
            font-weight: 900;
            color: var(--claro-orange);
            line-height: 1;
        }}
        .desafio-txt h4 {{ font-size: 14px; font-weight: 700; color: var(--claro-red); }}
        .desafio-txt p {{ font-size: 12.5px; color: #444; }}

        .metric-badge {{
            background: #fff;
            border: 2px solid var(--claro-red);
            border-radius: 12px;
            padding: 15px;
            text-align: center;
            box-shadow: 0 4px 12px rgba(218,41,28,0.1);
        }}
        .metric-val {{
            font-family: 'Montserrat', sans-serif;
            font-size: 36px;
            font-weight: 900;
            color: var(--claro-red);
            margin-bottom: 4px;
        }}
        .metric-label {{ font-size: 12px; font-weight: 600; color: #555; }}

        .img-container {{
            width: 100%;
            height: 100%;
            display: flex;
            align-items: center;
            justify-content: center;
            overflow: hidden;
            border-radius: 8px;
            border: 1px solid var(--claro-border);
            background: #000;
        }}
        .img-container img {{
            max-width: 100%;
            max-height: 100%;
            object-fit: contain;
        }}
    </style>
</head>
<body>

    <div class="deck" id="deck">

        <!-- SLIDE 1: Capa -->
        <div class="slide slide-cover active">
            <div class="cover-tag">Inovação Claro 2026</div>
            <div class="cover-title">SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro</div>
            <div class="cover-category">Categoria: Ideia que melhora a experiência do cliente</div>
            <div class="cover-author">
                <strong>George Luís Calcmann</strong><br>
                Analista em Serviços de Telecomunicações II<br>
                Regional Sul — Santa Catarina | Claro Brasil
            </div>
        </div>

        <!-- SLIDE 2: Integrantes -->
        <div class="slide slide-cover" style="align-items: center; text-align: center;">
            <div class="cover-tag">Integrante do Projeto</div>
            <div style="font-family: 'Montserrat'; font-size: 40px; font-weight: 900; margin-bottom: 15px;">GEORGE LUÍS CALCMANN</div>
            <div style="font-size: 20px; color: #ffdedb; font-weight: 600;">ANALISTA EM SERVIÇOS DE TELECOMUNICAÇÕES II</div>
            <div style="margin-top: 40px; font-size: 14px; opacity: 0.85;">Diretoria de Operações | Regional Sul</div>
        </div>

        <!-- SLIDE 3: Objetivo e Justificativa -->
        <div class="slide">
            <div class="slide-header">
                <h2>Objetivo e Justificativa do Projeto</h2>
                <span class="tagline">Garantia de Visita Única e Eficiência Operacional</span>
            </div>
            <div class="grid-2">
                <div style="display: flex; flex-direction: column; justify-content: space-between;">
                    <div style="font-size: 14px; color: var(--claro-dark); line-height: 1.6;">
                        <p style="margin-bottom: 12px;"><strong>Automatização Determinística:</strong> Eliminação integral dos processos manuais em linha de comando que levavam até 50 minutos por equipamento nos roteadores corporativos B2B (Cisco, HPE e Fortinet).</p>
                        <p style="margin-bottom: 12px;"><strong>Cumprimento da Visita Única (VU):</strong> Redução do tempo de ativação para menos de 10 minutos na ponta, eliminando erros humanos de transcrição do SAIP e expurgando configurações residuais que bloqueavam circuitos.</p>
                        <p><strong>Mudança Estratégica:</strong> Transferência do esforço técnico para a base Claro (modelo Plug & Play), assegurando satisfação e confiabilidade desde o primeiro minuto de ativação.</p>
                    </div>
                    <div class="grid-3" style="margin-top: 15px;">
                        <div class="card" style="padding: 10px;">
                            <h4 style="font-size: 12px;">Visita Única (VU)</h4>
                            <p style="font-size: 11px;">Ativação em <10 min e laudo digital com QR Code.</p>
                        </div>
                        <div class="card" style="padding: 10px;">
                            <h4 style="font-size: 12px;">Plug & Play na Base</h4>
                            <p style="font-size: 11px;">Roteador 100% testado e homologado antes de sair.</p>
                        </div>
                        <div class="card" style="padding: 10px;">
                            <h4 style="font-size: 12px;">Custo Zero</h4>
                            <p style="font-size: 11px;">100% interno, sem licenças e Offline-First.</p>
                        </div>
                    </div>
                </div>
                <div class="img-container">
                    <img src="{b64_mobile_prov}" alt="SPARC Mobile">
                </div>
            </div>
        </div>

        <!-- SLIDE 4: Desafios -->
        <div class="slide">
            <div class="slide-header">
                <h2>Desafios | Cenário da Problemática</h2>
                <span class="tagline">5 Obstáculos Críticos Superados</span>
            </div>
            <div class="grid-5">
                <div class="desafio-item">
                    <div class="desafio-num">1</div>
                    <div class="desafio-txt">
                        <h4>Tempo excessivo e ativação manual (até 50 min/roteador)</h4>
                        <p>Processo artesanal linha por linha em terminal serial, gerando filas de espera e ociosidade da equipe técnica de campo.</p>
                    </div>
                </div>
                <div class="desafio-item">
                    <div class="desafio-num">2</div>
                    <div class="desafio-txt">
                        <h4>Erros humanos de digitação e falhas no SAIP</h4>
                        <p>Transcrição manual de IPs de WAN, máscaras e rotas estáticas; erro de 1 único dígito travava a ativação e exigia retrabalho.</p>
                    </div>
                </div>
                <div class="desafio-item">
                    <div class="desafio-num">3</div>
                    <div class="desafio-txt">
                        <h4>Visitas perdidas por defeitos físicos não detectados</h4>
                        <p>Roteadores do estoque com portas queimadas ou memórias corrompidas só eram descobertos na frente do cliente B2B.</p>
                    </div>
                </div>
                <div class="desafio-item">
                    <div class="desafio-num">4</div>
                    <div class="desafio-txt">
                        <h4>Contaminação por configurações residuais (VLANs antigas)</h4>
                        <p>Equipamentos reutilizados retinham senhas e VLANs de clientes anteriores, gerando falsos diagnósticos de "link down".</p>
                    </div>
                </div>
                <div class="desafio-item">
                    <div class="desafio-num">5</div>
                    <div class="desafio-txt">
                        <h4>Dependência de conectividade externa e custos com terceiros</h4>
                        <p>Ferramentas de mercado (ex: Panteon) exigiam link de internet ativo e servidores centrais, inviáveis em subsolos e obras.</p>
                    </div>
                </div>
            </div>
        </div>

        <!-- SLIDE 5: Ações Desenvolvidas -->
        <div class="slide">
            <div class="slide-header">
                <h2>Ações Desenvolvidas</h2>
                <span class="tagline">Pilares da Construção e Engenharia do SPARC</span>
            </div>
            <div style="display: grid; grid-template-columns: repeat(5, 1fr); gap: 15px; height: 75%;">
                <div class="card" style="display: flex; flex-direction: column;">
                    <h4>1. Automação SAIP</h4>
                    <p style="margin-top: 10px; font-size: 12px;">Digitalização total dos fluxos e leitor inteligente do SAIP (PDF/TXT), eliminando digitação manual de parâmetros.</p>
                </div>
                <div class="card" style="display: flex; flex-direction: column;">
                    <h4>2. TFTP & Offline</h4>
                    <p style="margin-top: 10px; font-size: 12px;">Servidor gigabit embutido com cache local de imagens; 100% autônomo sem internet.</p>
                </div>
                <div class="card" style="display: flex; flex-direction: column;">
                    <h4>3. Multi-Marca</h4>
                    <p style="margin-top: 10px; font-size: 12px;">Isolamento por modelo (Cisco IOS/IOS-XE, HPE Comware, Fortinet) com quebra de senha.</p>
                </div>
                <div class="card" style="display: flex; flex-direction: column;">
                    <h4>4. Multiplataforma</h4>
                    <p style="margin-top: 10px; font-size: 12px;">SPARC Desktop para bancada e SPARC Mobile Android via cabo OTG para técnicos na ponta com laudo em PDF.</p>
                </div>
                <div class="card" style="display: flex; flex-direction: column;">
                    <h4>5. 789 Testes</h4>
                    <p style="margin-top: 10px; font-size: 12px;">Burn-in de portas e memórias na base e qualidade blindada por 789 testes unitários automatizados (0 falhas).</p>
                </div>
            </div>
        </div>

        <!-- SLIDE 6: A Solução: Esteira Automatizada -->
        <div class="slide">
            <div class="slide-header">
                <h2>A Solução: Esteira Automatizada SPARC</h2>
                <span class="tagline">Visão Completa da Interface Desktop de Bancada</span>
            </div>
            <div style="height: 78%; display: flex; flex-direction: column; align-items: center; justify-content: center;">
                <div class="img-container" style="background: transparent; border: none;">
                    <img src="{b64_desktop_ui}" alt="SPARC Desktop Esteira UI">
                </div>
                <div style="margin-top: 10px; font-size: 12.5px; color: #666; font-weight: 600;">
                    Esteira determinística: detecção serial, leitura SAIP, higienização, firmware e testes fisiológicos em tela única.
                </div>
            </div>
        </div>

        <!-- SLIDE 7: SPARC Desktop Base Claro -->
        <div class="slide">
            <div class="slide-header">
                <h2>SPARC Desktop: Preparação e Triagem na Base Claro</h2>
                <span class="tagline">Modelo Operacional de Bancada e Laboratório</span>
            </div>
            <div class="grid-2">
                <div class="img-container">
                    <img src="{b64_desktop_16_9}" alt="SPARC Desktop 16:9">
                </div>
                <div style="display: flex; flex-direction: column; gap: 10px; font-size: 13px;">
                    <div class="card">
                        <h4>• Detecção Automática Plug & Play</h4>
                        <p>Identificação instantânea do modelo (Cisco, HPE, Fortinet), portas e versão via console serial.</p>
                    </div>
                    <div class="card">
                        <h4>• Carga de Firmware Gigabit</h4>
                        <p>Servidor TFTP nativo grava 500 MB em ~3 min (substituindo transferências lentas de 35 min).</p>
                    </div>
                    <div class="card">
                        <h4>• Higienização Profunda (Clean State)</h4>
                        <p>Expurgo de configurações residuais, senhas antigas e VLANs de clientes anteriores.</p>
                    </div>
                    <div class="card">
                        <h4>• Testes Fisiológicos de Hardware</h4>
                        <p>Teste de memória RAM, integridade de Flash e conferência de loopback de portas Ethernet.</p>
                    </div>
                    <div class="card">
                        <h4>• Saída Homologada para Campo</h4>
                        <p>Roteador sai 100% testado, etiquetado e Plug & Play para instalação imediata no cliente.</p>
                    </div>
                </div>
            </div>
        </div>

        <!-- SLIDE 8: SPARC Mobile Android -->
        <div class="slide">
            <div class="slide-header">
                <h2>SPARC Mobile Android: Validação e Laudo na Ponta</h2>
                <span class="tagline">Mobilidade via Cabo OTG e Homologação Instantânea</span>
            </div>
            <div style="display: grid; grid-template-columns: 0.8fr 0.8fr 1.4fr; gap: 20px; height: 80%;">
                <div class="img-container">
                    <img src="{b64_mobile_prov}" alt="SPARC Mobile Status">
                </div>
                <div class="img-container">
                    <img src="{b64_mobile_test}" alt="SPARC Mobile Speedtest">
                </div>
                <div style="display: flex; flex-direction: column; gap: 12px; font-size: 13px;">
                    <div class="card">
                        <h4>• Mobilidade Total via USB-Serial OTG</h4>
                        <p>Funciona no celular Android comum via cabo OTG, sem requerer notebook ou tomada no rack.</p>
                    </div>
                    <div class="card">
                        <h4>• Operação 100% Offline-First</h4>
                        <p>Validação completa e aplicação de scripts mesmo em locais sem sinal 4G/5G ou sem internet.</p>
                    </div>
                    <div class="card">
                        <h4>• Conferência Física Prévia</h4>
                        <p>Auditoria ativa de link que impede reloads prematuros se o cabo físico não estiver engatado.</p>
                    </div>
                    <div class="card">
                        <h4>• Certidão Digital com QR Code</h4>
                        <p>Laudo formal em PDF assinado digitalmente com data, hora, IPs, testes de banda e foto da instalação.</p>
                    </div>
                </div>
            </div>
        </div>

        <!-- SLIDE 9: Os 4 Pilares Estratégicos -->
        <div class="slide">
            <div class="slide-header">
                <h2>Os 4 Pilares Estratégicos & Modelo Operacional</h2>
                <span class="tagline">Soberania, Eficiência e Redução de Custos</span>
            </div>
            <div class="grid-4" style="height: 78%;">
                <div class="card">
                    <h4>1. Flexibilidade de Uso (Offline-First)</h4>
                    <p style="margin-top: 8px;">• Operação Híbrida: Triagem na Base (Desktop) + Validação ágil na Ponta (Mobile OTG).<br>• Cache local de firmwares: ativação garantida mesmo em áreas de sombra ou sem link de internet.</p>
                </div>
                <div class="card">
                    <h4>2. Redução Drástica de Custos (Soberania)</h4>
                    <p style="margin-top: 8px;">• R$ 0 em licenciamento de software (vs custos recorrentes do Panteon).<br>• Zero novos servidores: roda na infraestrutura existente de PCs e celulares.<br>• Alavanca para renegociar contratos de empreiteiras (menor valor por OS).</p>
                </div>
                <div class="card">
                    <h4>3. Agilidade & Suporte Interno</h4>
                    <p style="margin-top: 8px;">• Evolução contínua pela equipe interna da Claro Regional SC.<br>• Autonomia total para homologar novos modelos e versões sem depender de terceiros.<br>• Resolução imediata de chamados e adaptação a demandas operacionais locais.</p>
                </div>
                <div class="card">
                    <h4>4. Triagem Fisiológica & Visita Única</h4>
                    <p style="margin-top: 8px;">• Roteador sai da base com portas, memórias e boot 100% testados.<br>• Elimina 100% das visitas perdidas por roteadores com defeito físico.<br>• Instalação Plug & Play no rack concluída em menos de 10 minutos.</p>
                </div>
            </div>
        </div>

        <!-- SLIDE 10: Resultados Alcançados -->
        <div class="slide">
            <div class="slide-header">
                <h2>Resultados Alcançados & Eficiência</h2>
                <span class="tagline">Ganhos Quantitativos e Qualitativos Comprovados</span>
            </div>
            <div class="grid-2">
                <div class="card" style="display: flex; flex-direction: column; justify-content: space-around;">
                    <div>
                        <h4 style="font-size: 16px; margin-bottom: 12px;">Ganhos Operacionais Homologados:</h4>
                        <p style="font-size: 13.5px; line-height: 1.8;">
                            <strong>• Tempo de Ativação:</strong> Redução de ~50 min para <10 min (-88%).<br>
                            <strong>• Aderência SAIP:</strong> 100% de precisão, zero erros de digitação.<br>
                            <strong>• Visita Única (VU):</strong> Eliminação de retrabalho por portas queimadas.<br>
                            <strong>• Produtividade:</strong> Capacidade de atendimento ampliada em até 4x.<br>
                            <strong>• Custos Recorrentes:</strong> R$ 0 em licenças ou servidores adicionais.
                        </p>
                    </div>
                </div>
                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 15px;">
                    <div class="metric-badge">
                        <div class="metric-val">-88%</div>
                        <div class="metric-label">Tempo de Ativação<br>(de 50 min para <10 min)</div>
                    </div>
                    <div class="metric-badge">
                        <div class="metric-val">100%</div>
                        <div class="metric-label">Aderência SAIP e<br>Zero Erro de Digitação</div>
                    </div>
                    <div class="metric-badge">
                        <div class="metric-val">R$ 0</div>
                        <div class="metric-label">Custo com Licenças<br>ou Novos Servidores</div>
                    </div>
                    <div class="metric-badge">
                        <div class="metric-val">0%</div>
                        <div class="metric-label">Visitas Perdidas por<br>Falhas de Hardware</div>
                    </div>
                </div>
            </div>
        </div>

        <!-- SLIDE 11: Impacto no Cliente -->
        <div class="slide">
            <div class="slide-header">
                <h2>Impacto no Cliente (Experiência B2B & Satisfação)</h2>
                <span class="tagline">A Primeira Impressão Como Alavanca de NPS</span>
            </div>
            <div class="grid-2">
                <div class="img-container">
                    <img src="{b64_mobile_test}" alt="Certidão Digital Claro">
                </div>
                <div style="display: flex; flex-direction: column; justify-content: space-between; font-size: 13px;">
                    <p style="font-size: 13.5px; color: var(--claro-red); font-weight: 800;">A entrega do circuito é o primeiro e mais crítico contato do cliente corporativo com a Claro:</p>
                    <div class="card">
                        <h4>1. Agilidade e Respeito ao Tempo</h4>
                        <p>Ativação em <10 min no rack, sem paralisações na operação da empresa do cliente.</p>
                    </div>
                    <div class="card">
                        <h4>2. Garantia Real da Visita Única (VU)</h4>
                        <p>Cliente nunca é surpreendido com roteador queimado ou configuração errada; zero reagendamentos.</p>
                    </div>
                    <div class="card">
                        <h4>3. Transparência e Confiança Imediata</h4>
                        <p>Certidão Digital formal em PDF com QR Code entregue na hora atestando banda, latência e IP.</p>
                    </div>
                    <div class="card">
                        <h4>4. Elevação Direta do NPS B2B</h4>
                        <p>A excelência na primeira entrega transforma a ativação em um momento de alto encantamento.</p>
                    </div>
                </div>
            </div>
        </div>

        <!-- SLIDE 12: Encerramento -->
        <div class="slide slide-cover" style="align-items: center; text-align: center;">
            <div class="cover-tag">Obrigado!</div>
            <div style="font-family: 'Montserrat'; font-size: 38px; font-weight: 900; margin-bottom: 20px;">SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro</div>
            <div style="font-size: 18px; color: #ffdedb; margin-bottom: 30px;">Ideia que melhora a experiência do cliente</div>
            <div class="cover-author" style="border: none; padding: 0;">
                <strong>George Luís Calcmann</strong> — Analista em Serviços de Telecomunicações II<br>
                Regional Sul — Santa Catarina | Claro Brasil
            </div>
        </div>

    </div>

    <!-- Navegação -->
    <div class="controls">
        <button onclick="prevSlide()">❮ Anterior</button>
        <span id="slideIndicator">Slide 1 / 12</span>
        <button onclick="nextSlide()">Próximo ❯</button>
        <button onclick="toggleFullScreen()" style="background: #333;">⛶ Tela Cheia</button>
    </div>

    <script>
        let currentSlide = 0;
        const slides = document.querySelectorAll('.slide');
        const indicator = document.getElementById('slideIndicator');

        function showSlide(index) {{
            slides.forEach((s, i) => {{
                s.classList.toggle('active', i === index);
            }});
            indicator.textContent = `Slide ${{index + 1}} / ${{slides.length}}`;
        }}

        function nextSlide() {{
            if (currentSlide < slides.length - 1) {{
                currentSlide++;
                showSlide(currentSlide);
            }}
        }}

        function prevSlide() {{
            if (currentSlide > 0) {{
                currentSlide--;
                showSlide(currentSlide);
            }}
        }}

        function toggleFullScreen() {{
            if (!document.fullscreenElement) {{
                document.documentElement.requestFullscreen();
            }} else {{
                if (document.exitFullscreen) {{
                    document.exitFullscreen();
                }}
            }}
        }}

        document.addEventListener('keydown', (e) => {{
            if (e.key === 'ArrowRight' || e.key === ' ') nextSlide();
            if (e.key === 'ArrowLeft') prevSlide();
        }});
    </script>
</body>
</html>
"""

with open(OUT_HTML, 'w', encoding='utf-8') as f:
    f.write(html_content)

os.makedirs(os.path.dirname(DIST_HTML), exist_ok=True)
with open(DIST_HTML, 'w', encoding='utf-8') as f:
    f.write(html_content)

print(f"HTML gerado com sucesso em:")
print(f"  {OUT_HTML}")
print(f"  {DIST_HTML}")
