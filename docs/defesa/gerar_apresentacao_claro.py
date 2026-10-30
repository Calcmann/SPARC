# -*- coding: utf-8 -*-
"""
Gera a Apresentação Executiva SPARC formatada 100% no padrão visual oficial do PPT CLARO.
Utiliza:
- Imagem oficial de fundo de Capa Claro (slide_1_shape_5_Imagem_4.jpg)
- Imagem oficial de fundo interno Claro com marca d'água do Sol Claro (slide_4_shape_5_Imagem_4.png)
- Logo esférico 3D oficial da Claro em alta definição (slide_1_shape_9_Imagem_8.png)
- Tipografia oficial AMX / Segoe UI / Arial
- Paleta oficial Claro Red (#DA291C / #E2262B), Charcoal (#1F2937), White e Slate
- Screenshots reais da UI Desktop e do SPARC Mobile Android
"""

import os
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR

# Cores Oficiais Claro
CLARO_RED = RGBColor(0xDA, 0x29, 0x1C)       # Vermelho Claro Institucional (#DA291C)
CLARO_RED_DARK = RGBColor(0xB9, 0x1C, 0x1C)  # Vermelho Escuro (#B91C1C)
CLARO_RED_LIGHT = RGBColor(0xFE, 0xF2, 0xF2) # Vermelho Claro Fundo (#FEF2F2)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
CHARCOAL = RGBColor(0x1F, 0x29, 0x37)        # Cinza Chumbo Escuro (#1F2937)
DARK_TEXT = RGBColor(0x11, 0x18, 0x27)       # Quase Preto (#111827)
MUTED = RGBColor(0x4B, 0x55, 0x63)           # Cinza Médio (#4B5563)
LIGHT_MUTED = RGBColor(0x6B, 0x72, 0x80)     # Cinza Claro (#6B7280)
LINE_COLOR = RGBColor(0xE5, 0xE7, 0xEB)      # Borda Cinza Suave (#E5E7EB)
RED_LINE = RGBColor(0xDA, 0x29, 0x1C)        # Linha Vermelha
CARD_BG = RGBColor(0xFF, 0xFF, 0xFF)         # Branco para Cards
CARD_ALT_BG = RGBColor(0xF9, 0xFA, 0xFB)     # Fundo Suave (#F9FAFB)
GREEN = RGBColor(0x16, 0xA3, 0x4A)           # Verde Claro (#16A34A)
GREEN_BG = RGBColor(0xDC, 0xFC, 0xE7)        # Fundo Verde (#DCFCE7)
AMBER = RGBColor(0xD9, 0x77, 0x06)           # Âmbar (#D97706)
AMBER_BG = RGBColor(0xFE, 0xF3, 0xC7)        # Fundo Âmbar (#FEF3C7)
BLUE = RGBColor(0x02, 0x84, 0xC7)            # Azul (#0284C7)
BLUE_BG = RGBColor(0xE0, 0xF2, 0xFE)         # Fundo Azul (#E0F2FE)

FONT = "AMX"
FONT_FALLBACK = "Segoe UI"

# Diretórios e Assets
HERE = os.path.dirname(os.path.abspath(__file__))
CLARO_ASSETS = os.path.join(HERE, "claro_template_assets")
USER_ASSETS = os.path.join(HERE, "user_ppt_assets")

IMG_COVER_BG = os.path.join(CLARO_ASSETS, "slide_1_shape_5_Imagem_4.jpg")
IMG_RED_BG = os.path.join(CLARO_ASSETS, "slide_3_shape_5_Imagem_4.png")
IMG_WHITE_BG = os.path.join(CLARO_ASSETS, "slide_4_shape_5_Imagem_4.png")
IMG_CLARO_LOGO = os.path.join(CLARO_ASSETS, "slide_1_shape_9_Imagem_8.png")

IMG_DESKTOP_UI = os.path.join(USER_ASSETS, "slide_2_36_Imagem_35.png")
IMG_MOBILE_1 = os.path.join(USER_ASSETS, "slide_3_21_Imagem_20.png")
IMG_MOBILE_2 = os.path.join(USER_ASSETS, "slide_3_23_Imagem_22.png")

OUT_PPTX_DEFESA = os.path.join(HERE, "SPARC_Apresentacao_Executiva_Claro.pptx")
OUT_PPTX_BETA = r"C:\SPARC\dist-beta\SPARC_Apresentacao_Executiva_Claro.pptx"
OUT_PPTX_RESUMIDA = r"C:\SPARC\dist-beta\SPARC_Apresentacao_Executiva_Resumida.pptx"

# Inicializa Apresentação Widescreen 16:9
prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)
BLANK_LAYOUT = prs.slide_layouts[6]

# Utilitários de Desenho
def add_bg(slide, bg_img_path):
    if os.path.exists(bg_img_path):
        slide.shapes.add_picture(bg_img_path, Inches(0), Inches(0), width=Inches(13.333), height=Inches(7.5))

def add_claro_logo(slide):
    if os.path.exists(IMG_CLARO_LOGO):
        slide.shapes.add_picture(IMG_CLARO_LOGO, Inches(11.96), Inches(6.37), width=Inches(1.03), height=Inches(0.93))

def rect(s, x, y, w, h, fill, line=None, shape=MSO_SHAPE.RECTANGLE, lw=1):
    sh = s.shapes.add_shape(shape, Inches(x), Inches(y), Inches(w), Inches(h))
    sh.shadow.inherit = False
    if fill is None:
        sh.fill.background()
    else:
        sh.fill.solid()
        sh.fill.fore_color.rgb = fill
    if line is None:
        sh.line.fill.background()
    else:
        sh.line.color.rgb = line
        sh.line.width = Pt(lw)
    if shape == MSO_SHAPE.ROUNDED_RECTANGLE:
        sh.adjustments[0] = 0.04
    return sh

def text(s, x, y, w, h, runs, size=13, color=DARK_TEXT, bold=False, align=PP_ALIGN.LEFT,
         anchor=MSO_ANCHOR.TOP, shape=None, spacing=2):
    if shape is None:
        shape = s.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = shape.text_frame
    tf.word_wrap = True
    tf.vertical_anchor = anchor
    tf.margin_left = tf.margin_right = Inches(0.06)
    tf.margin_top = tf.margin_bottom = Inches(0.04)
    paras = runs if isinstance(runs, list) else [runs]
    for i, para in enumerate(paras):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = align
        p.space_after = Pt(spacing)
        parts = para if isinstance(para, list) else [(para, {})]
        for item in parts:
            if isinstance(item, tuple):
                t = item[0]
                o = item[1] if len(item) > 1 and isinstance(item[1], dict) else {}
            elif isinstance(item, str):
                t = item
                o = {}
            else:
                t = str(item)
                o = {}
            r = p.add_run()
            r.text = t
            f = r.font
            f.name = FONT
            f.size = Pt(o.get("size", size))
            f.bold = o.get("bold", bold)
            f.color.rgb = o.get("color", color)
            f.italic = o.get("italic", False)
    return shape

def pill(s, x, y, w, label, fill=CLARO_RED, color=WHITE, size=10.5, h=0.32):
    p = rect(s, x, y, w, h, fill, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    p.adjustments[0] = 0.5
    text(s, 0, 0, 0, 0, label, size=size, color=color, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=p)
    return p

def claro_header(s, title, subtitle=None, kicker="CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC"):
    add_bg(s, IMG_WHITE_BG)
    add_claro_logo(s)
    
    # Kicker
    text(s, 0.80, 0.22, 10.5, 0.28, "🔴  " + kicker.upper(), size=10.5, color=CLARO_RED, bold=True)
    
    # Title & Subtitle
    if subtitle:
        text(s, 0.80, 0.48, 11.0, 0.42, title, size=21, color=DARK_TEXT, bold=True)
        text(s, 0.80, 0.88, 11.0, 0.28, subtitle, size=11, color=MUTED)
    else:
        text(s, 0.80, 0.48, 11.0, 0.55, title, size=23, color=DARK_TEXT, bold=True)
        
    # Linha separadora vermelha Claro
    rect(s, 0.80, 1.20, 11.0, 0.025, CLARO_RED)
    
    # Rodapé institucional Claro
    text(s, 0.80, 7.12, 10.5, 0.28,
         "SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro  |  Regional SC  |  george.calcmann@claro.com.br",
         size=9.5, color=LIGHT_MUTED)


# ==============================================================================
# SLIDE 1: CAPA OFICIAL CLARO
# ==============================================================================
s1 = prs.slides.add_slide(BLANK_LAYOUT)
add_bg(s1, IMG_COVER_BG)
add_claro_logo(s1)

# Lado Direito: Área Vermelha Corporativa com Títulos e Destaques
rx = 6.80
rw = 5.80

pill(s1, rx, 0.65, 3.80, "🔴  CLARO S.A. — ATIVAÇÃO EMPRESARIAL", WHITE, CLARO_RED, size=11, h=0.36)

text(s1, rx, 1.15, rw, 0.95, "SPARC", size=48, color=WHITE, bold=True)

text(s1, rx, 2.10, rw, 0.35, "Sistema de Provisionamento e Ativação de Roteadores da CLARO",
     size=12, color=RGBColor(0xFE, 0xCA, 0xCA), bold=True)

text(s1, rx, 2.45, rw, 0.55, "Novas Oportunidades, Agilidade, Flexibilidade e Redução de Custos",
     size=18, color=WHITE, bold=True)

text(s1, rx, 3.05, rw, 0.45, "Solução autônoma da CLARO no apoio às Ativações de campo e Visita Única.",
     size=12, color=RGBColor(0xFE, 0xE2, 0xE2))

# 4 Cards de Destaque no Fundo da Capa (Grid 2x2 com fundo translúcido escuro Claro)
c_w = 2.80
c_h = 1.35
c_xs = [rx, rx + 2.95]
c_ys = [3.65, 5.10]

cards_s1 = [
    ("FLEXIBILIDADE OPERACIONAL",
     "Em bancada ou no campo, SPARC está pronto para preparação do equipamento. Cache de firmwares homologados embarcado no app."),
    ("BAIXO CUSTO",
     "Sem cobrança de licenças, a ativação é online mediante cadastro via módulo Administrador, cada cluster poderá administrar suas equipes."),
    ("AGILIDADE",
     "Suporte a novos modelos e implementações ágeis, sem filas de fornecedor."),
    ("OPORTUNIDADES",
     "Roteador já pode sair 100% testado da base Claro (Plug & Play), permitindo abater o item pago ao parceiro terceirizado, versatilidade com diferentes modos de operação.")
]

for idx, (ctitle, cdesc) in enumerate(cards_s1):
    col = idx % 2
    row = idx // 2
    bx = c_xs[col]
    by = c_ys[row]
    
    # Caixa com fundo semitransparente branco/vermelho
    rect(s1, bx, by, c_w, c_h, fill=RGBColor(0x8B, 0x00, 0x00), line=RGBColor(0xFE, 0x88, 0x88), shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s1, bx + 0.12, by + 0.08, c_w - 0.24, 0.30, ctitle, size=10.5, color=WHITE, bold=True)
    text(s1, bx + 0.12, by + 0.38, c_w - 0.24, 0.90, cdesc, size=9.5, color=RGBColor(0xFD, 0xEA, 0xEA))


# ==============================================================================
# SLIDE 2: COMO A FERRAMENTA FUNCIONA NA PRÁTICA (ESTEIRA AUTOMATIZADA)
# ==============================================================================
s2 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s2, "Como a Ferramenta Funciona na Prática: Esteira Automatizada",
             "Visão operacional da interface unificada que guia o técnico do diagnóstico físico ao laudo formal")

# Lado Esquerdo: Imagem da Interface SPARC Desktop
rect(s2, 0.80, 1.38, 6.70, 5.45, fill=WHITE, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s2, 0.95, 1.50, 3.40, "INTERFACE PRINCIPAL — FLUXO UNIFICADO", CLARO_RED, WHITE, size=10, h=0.30)

if os.path.exists(IMG_DESKTOP_UI):
    try:
        s2.shapes.add_picture(IMG_DESKTOP_UI, Inches(0.95), Inches(1.90), width=Inches(6.40))
    except Exception:
        pass

# Legenda abaixo da imagem
rect(s2, 0.95, 6.20, 6.40, 0.52, fill=CARD_ALT_BG, line=LINE_COLOR)
text(s2, 1.05, 6.24, 6.20, 0.44, [
    [("Tela Única Inteligente: ", {"bold": True, "size": 10.5, "color": DARK_TEXT}),
     ("Detecção COM automática, leitura de SAIP, terminal CLI integrado e esteira de automação com feedback em tempo real.", {"size": 10, "color": MUTED})]
])

# Lado Direito: Os 4 Passos Operacionais (P1 a P4)
rect(s2, 7.65, 1.38, 4.25, 5.45, fill=WHITE, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s2, 7.80, 1.50, 2.80, "GUIA VISUAL DE OPERAÇÃO", CHARCOAL, WHITE, size=10, h=0.30)

steps = [
    ("P1", "1. Detecção e Autenticação Serial",
     "Identifica automaticamente Cisco, HPE ou FortiGate. Desbloqueia e autentica o console sem intervenção manual.",
     BLUE, BLUE_BG),
    ("P2", "2. Leitura Inteligente do SAIP (PDF/TXT)",
     "Importa a ficha técnica oficial com 1 clique. Preenche WAN, LAN, BGP, rotas e hostname com 100% de precisão e zero digitação. Oportunidade: integração via API Claro com MODOS.",
     GREEN, GREEN_BG),
    ("P3", "3. Gravação de Firmware & Provisionamento",
     "Servidor TFTP/HTTP nativo grava firmware via cabo de rede na porta WAN/LAN. Aplica o script do circuito e verifica link físico.",
     AMBER, AMBER_BG),
    ("P4", "4. Teste de Banda com relatório em PDF",
     "Audita ICMP (LAN/WAN/Web), valida Telnet/SSH, afere velocidade e emite resultado em PDF com QR Code.",
     CLARO_RED, CLARO_RED_LIGHT)
]

for idx, (p_tag, p_title, p_desc, p_col, p_bg) in enumerate(steps):
    sy = 1.90 + idx * 1.20
    rect(s2, 7.80, sy, 3.95, 1.10, fill=p_bg, line=p_col, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    pill(s2, 7.90, sy + 0.08, 0.65, p_tag, p_col, WHITE, size=9.5, h=0.26)
    text(s2, 8.65, sy + 0.06, 3.00, 0.32, p_title, size=11, color=DARK_TEXT, bold=True)
    text(s2, 7.90, sy + 0.38, 3.75, 0.68, p_desc, size=9.5, color=DARK_TEXT)


# ==============================================================================
# SLIDE 3: FLEXIBILIDADE DE PLATAFORMAS (DESKTOP NA BASE + MOBILE EM CAMPO)
# ==============================================================================
s3 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s3, "Flexibilidade de Plataformas: Desktop na Base + Mobile em Campo",
             "A mesma inteligência operacional adaptada para alta produtividade com mobilidade total")

# Lado Esquerdo: 2 Telas do Mobile Android
rect(s3, 0.80, 1.38, 5.50, 5.45, fill=WHITE, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s3, 0.95, 1.50, 3.40, "SPARC MOBILE ANDROID (.APK)", CLARO_RED, WHITE, size=10, h=0.30)

mob_w = 2.45
mob_h = 4.80
if os.path.exists(IMG_MOBILE_1):
    try:
        s3.shapes.add_picture(IMG_MOBILE_1, Inches(0.95), Inches(1.90), width=Inches(mob_w), height=Inches(mob_h))
    except Exception:
        pass

if os.path.exists(IMG_MOBILE_2):
    try:
        s3.shapes.add_picture(IMG_MOBILE_2, Inches(3.65), Inches(1.90), width=Inches(mob_w), height=Inches(mob_h))
    except Exception:
        pass

# Lado Direito: Funcionalidades e Modelo SPARC Tester
rect(s3, 6.45, 1.38, 5.45, 5.45, fill=WHITE, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s3, 6.65, 1.50, 3.20, "MOBILIDADE & AGILIDADE EM CAMPO", CHARCOAL, WHITE, size=10, h=0.30)

text(s3, 6.65, 1.95, 5.05, 4.70, [
    [("SPARC MOBILE ANDROID (.APK)", {"bold": True, "size": 15, "color": CLARO_RED})],
    "",
    [("✔ 100% Portátil: ", {"bold": True, "color": GREEN, "size": 12}),
     ("Roda no celular corporativo Android do técnico de campo via cabo USB-Serial e adaptador OTG.", {"size": 11.5, "color": DARK_TEXT})],
    "",
    [("✔ Validação e Laudo na Ponta: ", {"bold": True, "color": GREEN, "size": 12}),
     ("Afere largura de banda e reporta resultado via arquivo PDF formal com QR Code.", {"size": 11.5, "color": DARK_TEXT})],
    "",
    [("✔ Modelo SPARC Tester: ", {"bold": True, "color": GREEN, "size": 12}),
     ("Ideal para parceiros terceirizados apenas validarem a entrega física e a conectividade sem mexer na configuração.", {"size": 11.5, "color": DARK_TEXT})],
    "",
    [("✔ Cache de Firmware Offline: ", {"bold": True, "color": GREEN, "size": 12}),
     ("Imagens homologadas salvas na memória interna do aparelho para uso sem internet.", {"size": 11.5, "color": DARK_TEXT})],
    "",
    [("✔ Mesma Base Tecnológica: ", {"bold": True, "color": GREEN, "size": 12}),
     ("Compartilha 100% dos algoritmos de detecção, comandos CLI e validação do Windows Desktop.", {"size": 11.5, "color": DARK_TEXT})]
], spacing=3)


# ==============================================================================
# SLIDE 4: OS 4 PILARES ESTRATÉGICOS (CONTEÚDO ESTRITAMENTE PRESERVADO)
# ==============================================================================
s4 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s4, "Os 4 Pilares Estratégicos",
             "Comparativo focado nas reais dores de campo, flexibilidade e retorno financeiro para a Claro")

p_w = 2.70
p_h = 5.45
p_xs = [0.80, 3.60, 6.40, 9.20]
p_y = 1.38

pilares = [
    ("1. FLEXIBILIDADE DE USO", "Modo Bancada", CLARO_RED, [
        [("✔ Zero Dependência do Link: ", {"bold": True, "color": GREEN}),
         ("O SPARC foi concebido sob o paradigma Offline-First, operando de forma 100% autónoma diretamente no terminal do técnico, sem necessidade do enlace de acesso durante as etapas preliminares.")],
        "",
        [("✔ Cache de Firmware Local: ", {"bold": True, "color": GREEN}),
         ("Imagens homologadas (.bin/.ipe/.out) embarcadas, com atualizações via download sempre que necessário.")],
        "",
        [("✔ Cabo Serial + Ethernet: ", {"bold": True, "color": GREEN}),
         ("Suporta recuperação de firmwares corrompidos via TFTP nativo, restaura configurações de equipamentos com senhas desconhecidas.")]
    ]),
    ("2. REDUÇÃO DE CUSTOS", "Economia Direta", CLARO_RED, [
        [("✔ R$ 0 em Licenças: ", {"bold": True, "color": GREEN}),
         ("Ativo 100% interno Claro. Sem custos com licenças, manutenções e implementações.")],
        "",
        [("✔ Flexibilidade e Abatimento por OS: ", {"bold": True, "color": GREEN}),
         ("Roteador pode sair pronto da base, configurado e testado; permite renegociar o item 'configuração de CPE’.")],
        "",
        [("✔ Fim das Visitas Perdidas: ", {"bold": True, "color": GREEN}),
         ("Triagem antecipada elimina retrabalho de técnicos por roteador defeituoso.")]
    ]),
    ("3. AGILIDADE & SUPORTE", "Soberania Técnica Claro", CLARO_RED, [
        [("✔ Sem Fila de Fornecedor: ", {"bold": True, "color": GREEN}),
         ("com SPARC dispensamos longas negociações com fornecedores, aditivos contratuais, defesas de CAPEX, prazos dilatados para ajustes e desenvolvimento.")],
        "",
        [("✔ Desenvolvimento Colaborativo (Co-design): ", {"bold": True, "color": GREEN}),
         ("alinhamento direto e contínuo com os pontos focais da operação, incorporando feedbacks práticos e evoluindo a solução a partir das dores reais vivenciadas pelas equipes de campo.")],
        "",
        [("✔ Domínio do Conhecimento: ", {"bold": True, "color": GREEN}),
         ("Know-how mantido 100% dentro da Claro; zero risco de dependência tecnológica.")]
    ]),
    ("4. TRIAGEM DE HARDWARE", "Blindagem da VU", CLARO_RED, [
        [("✔ Teste Prévio na Base: ", {"bold": True, "color": GREEN}),
         ("Portas de rede, memória Flash e boot podem ser avaliados antecipadamente com as facilidades do SPARC.")],
        "",
        [("✔ Substituição Antecipada: ", {"bold": True, "color": GREEN}),
         ("Se houver defeito físico, troca antes de sair para o cliente.")],
        "",
        [("✔ Entrega Plug & Play: ", {"bold": True, "color": GREEN}),
         ("Roteador sai da base etiquetado, com IP do circuito e firmware homologado gravado.")],
        "",
        [("✔ Produtividade em Bancada: ", {"bold": True, "color": GREEN}),
         ("Um único técnico interno prepara dezenas de aparelhos/dia em menos de 10 min cada.")]
    ])
]

for idx, (p_pill, p_sub, p_col, p_text) in enumerate(pilares):
    bx = p_xs[idx]
    rect(s4, bx, p_y, p_w, p_h, fill=WHITE, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    pill(s4, bx + 0.12, p_y + 0.15, p_w - 0.24, p_pill, p_col, WHITE, size=10, h=0.32)
    text(s4, bx + 0.12, p_y + 0.55, p_w - 0.24, 0.35, p_sub, size=13, color=p_col, bold=True)
    text(s4, bx + 0.12, p_y + 0.95, p_w - 0.24, 4.35, p_text, size=10.5, color=DARK_TEXT, spacing=2)


# ==============================================================================
# SLIDE 5: IMPACTO MENSURADO, AGILIDADE DE SUPORTE E SOBERANIA TECNOLÓGICA
# ==============================================================================
s5 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s5, "Impacto Mensurado, Agilidade de Suporte e Soberania Tecnológica",
             "Resultados consolidados em campo e comparação da governança de longo prazo")

# 4 Cards de KPIs no topo
kpi_w = 2.65
kpi_h = 1.45
kpi_xs = [0.80, 3.60, 6.40, 9.20]
kpi_y = 1.38

kpi_data = [
    ("-88%", "Tempo de Ativação", "De ~50 min com técnico experiente para 10 min com instalador por roteador", CLARO_RED),
    ("100%", "Aderência SAIP", "Zero erro de digitação de IPs, rotas e máscaras", GREEN),
    ("R$ 0", "Licenças & Servidores", "Economia contínua de Capex/Opex de fornecedor", GREEN),
    ("0%", "Visitas Perdidas por HW", "Triagem prévia na base elimina falhas em campo", CLARO_RED)
]

for idx, (kval, ktitle, kdesc, kcol) in enumerate(kpi_data):
    kx = kpi_xs[idx]
    rect(s5, kx, kpi_y, kpi_w, kpi_h, fill=WHITE, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s5, kx + 0.10, kpi_y + 0.08, kpi_w - 0.20, 0.52, kval, size=28, color=kcol, bold=True, align=PP_ALIGN.CENTER)
    text(s5, kx + 0.10, kpi_y + 0.60, kpi_w - 0.20, 0.28, ktitle, size=12, color=DARK_TEXT, bold=True, align=PP_ALIGN.CENTER)
    text(s5, kx + 0.10, kpi_y + 0.88, kpi_w - 0.20, 0.52, kdesc, size=9.5, color=MUTED, align=PP_ALIGN.CENTER)

# 2 Blocos de Sustentação
b_w = 5.45
b_h = 3.80
b_y = 3.00

# Bloco Esquerdo: SPARC Interno
rect(s5, 0.80, b_y, b_w, b_h, fill=WHITE, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s5, 0.95, b_y + 0.15, 3.50, "SPARC: SUSTENTAÇÃO ÁGIL INTERNA", GREEN, WHITE, size=10.5, h=0.32)
text(s5, 0.95, b_y + 0.60, 5.15, 3.05, [
    [("✔ Evolução Ágil e Contínua: ", {"bold": True, "color": GREEN}),
     ("Relacionamento direto com pontos focais, feedbacks colaborativos, desenvolvimento conjunto.")],
    "",
    [("✔ Suporte Multi-Fabricante: ", {"bold": True, "color": GREEN}),
     ("Agilidade na ampliação do suporte a novos modelos, podendo evoluir para outras tipos de equipamentos como switches de acesso.")],
    "",
    [("✔ Soberania Tecnológica: ", {"bold": True, "color": GREEN}),
     ("As soluções desenvolvidas aqui ficam em casa. Código, regras de negócio e inteligência 100% da Claro.")]
], size=11, color=DARK_TEXT, spacing=3)

# Bloco Direito: Modelos Externos
rect(s5, 6.40, b_y, b_w, b_h, fill=WHITE, line=CLARO_RED, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s5, 6.55, b_y + 0.15, 3.50, "RISCOS COM MODELOS EXTERNOS", CLARO_RED, WHITE, size=10.5, h=0.32)
text(s5, 6.55, b_y + 0.60, 5.15, 3.05, [
    [("✖ Dependência Crônica de Fornecedor: ", {"bold": True, "color": CLARO_RED}),
     ("Qualquer customização ou novo modelo depende de orçamento adicional e fila de backlog.")],
    "",
    [("✖ Contratos periódicos e Reajustes: ", {"bold": True, "color": CLARO_RED}),
     ("Custos recorrentes que encarecem a operação ano a ano (lock-in comercial).")],
    "",
    [("✖ Falta de Flexibilidade: ", {"bold": True, "color": CLARO_RED}),
     ("Limitação contratual que impede implementações e ajustes sem renegociações burocráticas.")]
], size=11, color=DARK_TEXT, spacing=3)


# ==============================================================================
# SLIDE 6: OPORTUNIDADES COM FORMATOS DE OPERAÇÃO
# ==============================================================================
s6 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s6, "Oportunidades com Formatos de Operação",
             "A esteira que desonera contratos terceirizados e garante zero entrega com defeito físico")

box_w = 5.45
box_h = 3.65
box_y = 1.38

# Bloco 1: SPARC Windows Full
rect(s6, 0.80, box_y, box_w, box_h, fill=WHITE, line=CLARO_RED, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s6, 0.95, box_y + 0.15, 3.20, "1. SPARC WINDOWS FULL", CLARO_RED, WHITE, size=10.5, h=0.32)
text(s6, 0.95, box_y + 0.60, 5.15, 2.90, [
    [("Quem opera: ", {"bold": True, "color": DARK_TEXT}),
     ("Todos, Assistência Técnica e Ativação própria da Claro permitindo renegociação de contratos LPU.")],
    "",
    [("Onde: ", {"bold": True, "color": DARK_TEXT}),
     ("Bancada / Laboratório na Base Regional.")],
    "",
    [("Triagem de Hardware: ", {"bold": True, "color": CLARO_RED}),
     ("Testa portas Ethernet, fonte, boot e memórias. "),
     ("Defeito físico é corrigido/substituído na base antes de ir para a rua.", {"bold": True, "color": DARK_TEXT})],
    "",
    [("Tempo Médio: ", {"bold": True, "color": DARK_TEXT}),
     ("Menos de 10 minutos por equipamento.")]
], size=11.5, color=DARK_TEXT, spacing=3)

# Bloco 2: SPARC Tester
rect(s6, 6.40, box_y, box_w, box_h, fill=WHITE, line=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
pill(s6, 6.55, box_y + 0.15, 4.00, "2. SPARC TESTER (Técnicos Terceirizados)", BLUE, WHITE, size=10.5, h=0.32)
text(s6, 6.55, box_y + 0.60, 5.15, 2.90, [
    [("Quem opera: ", {"bold": True, "color": DARK_TEXT}),
     ("Empresas parceiras terceirizadas responsáveis pela instalação física.")],
    "",
    [("O que muda: ", {"bold": True, "color": BLUE}),
     ("Retira do terceiro o acesso privilegiado e a responsabilidade de configurar.")],
    "",
    [("O que é o SPARC Tester: ", {"bold": True, "color": DARK_TEXT}),
     ("App ultraleve e blindado para celular corporativo ou notebook.")],
    "",
    [("O que faz: ", {"bold": True, "color": DARK_TEXT}),
     ("Valida link, afere largura de banda e gera relatório PDF formal.")]
], size=11.5, color=DARK_TEXT, spacing=3)

# Banner Alavanca Financeira no Rodapé
rect(s6, 0.80, 5.18, 11.05, 1.70, fill=GREEN_BG, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s6, 1.00, 5.30, 10.65, 1.45, [
    [("💰 ALAVANCA FINANCEIRA ESTRATÉGICA PARA A CLARO: ", {"bold": True, "size": 13, "color": GREEN}),
     ("REDUÇÃO DIRETA DE CONTRATO POR OS", {"bold": True, "size": 13, "color": DARK_TEXT})],
    "",
    [("Como o parceiro terceirizado ", {"size": 11.5, "color": DARK_TEXT}),
     ("NÃO precisa mais configurar o roteador no cliente ", {"bold": True, "size": 11.5, "color": DARK_TEXT}),
     ("(o equipamento já chega pronto e testado pela base Claro), a Claro ganha respaldo técnico e operacional imediato para ", {"size": 11.5, "color": DARK_TEXT}),
     ("renegociar e abater o valor do item 'Configuração de CPE' ", {"bold": True, "size": 11.5, "color": GREEN}),
     ("pago nas ordens de serviço. Em escala regional e nacional, essa economia cobre com folga qualquer investimento interno!", {"size": 11.5, "color": DARK_TEXT})]
], spacing=2)


# ==============================================================================
# SLIDE 7: SCORECARD COMPARATIVO EXECUTIVO (SPARC 10 x 1 PANTEON)
# ==============================================================================
s7 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s7, "Scorecard Comparativo: SPARC (Interno) × Panteon (Externo)",
             "Análise objetiva dos requisitos técnicos, contratuais e operacionais de implantação")

# Placar
rect(s7, 0.80, 1.38, 11.05, 0.55, fill=CHARCOAL, line=LINE_COLOR, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s7, 1.00, 1.45, 6.00, 0.40, [
    [("PLACAR ESTRATÉGICO: ", {"bold": True, "size": 12, "color": WHITE}),
     ("SPARC ", {"bold": True, "size": 16, "color": GREEN}),
     ("10  ×  1 ", {"bold": True, "size": 16, "color": WHITE}),
     ("Panteon", {"bold": True, "size": 16, "color": CLARO_RED})]
])
text(s7, 6.80, 1.48, 4.90, 0.35, "Ampla superioridade em Flexibilidade, Custo Zero e Soberania Técnica",
     size=11, color=RGBColor(0xCB, 0xD5, 0xE1), align=PP_ALIGN.RIGHT)

criteria_claro = [
    ("Investimento em Licenças de Software", "R$ 0 — Ativo proprietário interno Claro", "Alto — Licenças por usuário ou bienais", True),
    ("Infraestrutura de Servidores Novos", "Zero servidores — Processamento no endpoint", "Exige 10+ servidores dedicados (Capex/Opex)", True),
    ("Operação Offline (Sem Link no Cliente)", "100% Autônomo via Cache local de firmwares", "TRAVA — Inviabilizado sem link ativo", True),
    ("Redução de Custos em Contratos de OS", "Sim — Roteador sai pronto (abate item de CPE)", "Não — Mantém terceiro configurando no campo", True),
    ("Triagem Prévia de Hardware em Bancada", "Sim — Substituição na base antes da visita", "Não — Descobre falha na frente do cliente", True),
    ("Plataformas Operacionais Homologadas", "Windows Desktop (.exe) + Android (.apk)", "Apenas Android (sem versão Desktop)", True),
    ("Interfaces de Conexão Física em Campo", "Serial CLI + Cabo de Rede Ethernet (TFTP/HTTP)", "Somente Cabo Serial CLI", True),
    ("Agilidade para Correções e Novos Modelos", "Horas ou Dias — Equipe de desenvolvimento Claro", "Fila de Fornecedor — Depende de SLA/contrato", True),
    ("Recuperação de Firmware Corrompido / BIOS", "Automática via TFTP integrado na porta WAN", "Não recupera equipamentos sem firmware", True),
    ("Integração com Sistemas Corporativos", "Via SAIP (PDF/TXT) hoje; API em roadmap", "Homologado via API Corporativa", False),
]

row_y = 2.05
row_h = 0.45
for i, (crit, sparc_val, pant_val, sparc_win) in enumerate(criteria_claro):
    cy = row_y + i * (row_h + 0.04)
    bg_c = WHITE if i % 2 == 0 else CARD_ALT_BG
    rect(s7, 0.80, cy, 11.05, row_h, fill=bg_c, line=LINE_COLOR)
    text(s7, 0.95, cy + 0.08, 4.00, 0.35, crit, size=10.5, color=DARK_TEXT, bold=True)
    
    rect(s7, 5.05, cy + 0.05, 3.25, 0.35, fill=GREEN_BG if sparc_win else BLUE_BG)
    icon_s = "✔  " if sparc_win else "◐  "
    text(s7, 5.15, cy + 0.07, 3.10, 0.32, [
        [(icon_s, {"color": GREEN if sparc_win else BLUE, "bold": True}),
         (sparc_val, {"size": 10, "bold": True, "color": DARK_TEXT})]
    ])
    
    rect(s7, 8.45, cy + 0.05, 3.30, 0.35, fill=CLARO_RED_LIGHT if sparc_win else GREEN_BG)
    icon_p = "✖  " if sparc_win else "✔  "
    text(s7, 8.55, cy + 0.07, 3.15, 0.32, [
        [(icon_p, {"color": CLARO_RED if sparc_win else GREEN, "bold": True}),
         (pant_val, {"size": 10, "bold": False, "color": DARK_TEXT})]
    ])


# ==============================================================================
# SLIDE 8: RECOMENDAÇÕES EXECUTIVAS E PLANO DE AÇÃO IMEDIATO
# ==============================================================================
s8 = prs.slides.add_slide(BLANK_LAYOUT)
claro_header(s8, "Recomendações Executivas e Plano de Ação Imediato",
             "Passos claros e sem burocracia para consolidar a eficiência de campo na Claro")

act_w = 3.50
act_h = 3.10
act_y = 1.38
act_xs = [0.80, 4.58, 8.35]

actions = [
    ("PASSO 1", "Aprovar Modelo Modular",
     "Implantar o modelo SPARC Full na Assistência Técnica própria da Regional SC para pré-configuração e triagem, e liberar o SPARC Tester aos parceiros.",
     CLARO_RED),
    ("PASSO 2", "Renegociação Contratual",
     "Abrir estudo conjunto com Suprimentos/Contratos para abater o item 'Configuração de CPE' nas faturas de empresas terceirizadas de instalação.",
     GREEN),
    ("PASSO 3", "Oficialização Corporativa",
     "Liberar para o SPARC a mesma credencial de API Corporativa já concedida ao Panteon e oficializar a solução como padrão nacional Claro.",
     BLUE)
]

for idx, (step_num, step_title, step_desc, step_col) in enumerate(actions):
    sx = act_xs[idx]
    rect(s8, sx, act_y, act_w, act_h, fill=WHITE, line=step_col, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    pill(s8, sx + 0.15, act_y + 0.18, 1.40, step_num, step_col, WHITE, size=11, h=0.34)
    text(s8, sx + 0.15, act_y + 0.70, act_w - 0.30, 0.55, step_title, size=15, color=DARK_TEXT, bold=True)
    text(s8, sx + 0.15, act_y + 1.35, act_w - 0.30, 1.55, step_desc, size=11.5, color=MUTED)

# Box de Conclusão / Resumo Executivo
rect(s8, 0.80, 4.70, 11.05, 2.18, fill=CHARCOAL, line=CLARO_RED, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s8, 1.05, 4.88, 10.55, 1.85, [
    [("RESUMO EXECUTIVO PARA TOMADA DE DECISÃO:", {"bold": True, "size": 14, "color": RGBColor(0xFE, 0xCA, 0xCA)})],
    "",
    [("O ", {"size": 12, "color": WHITE}),
     ("SPARC ", {"bold": True, "size": 13, "color": GREEN}),
     ("entrega uma solução de ", {"size": 12, "color": WHITE}),
     ("custo zero em licenças e servidores", {"bold": True, "size": 12, "color": WHITE}),
     (", com ", {"size": 12, "color": WHITE}),
     ("máxima flexibilidade operacional (100% offline via cache de firmware)", {"bold": True, "size": 12, "color": RGBColor(0xFE, 0xCA, 0xCA)}),
     (", ", {"size": 12, "color": WHITE}),
     ("eliminação de retrabalho por triagem de hardware na base ", {"bold": True, "size": 12, "color": WHITE}),
     ("e ", {"size": 12, "color": WHITE}),
     ("agilidade total de sustentação interna", {"bold": True, "size": 12, "color": GREEN}),
     (". A adoção do SPARC protege o orçamento da Claro, valoriza a equipe técnica própria e alavanca a economia nos contratos terceirizados.", {"size": 12, "color": WHITE})],
    "",
    [("Decisão Recomendada: ", {"bold": True, "size": 12, "color": RGBColor(0xFE, 0xCA, 0xCA)}),
     ("Consolidar o SPARC como ativo estratégico prioritário da Claro.", {"bold": True, "size": 12, "color": WHITE})]
], spacing=2)


# ==============================================================================
# SLIDE 9: ENCERRAMENTO OFICIAL CLARO
# ==============================================================================
s9 = prs.slides.add_slide(BLANK_LAYOUT)
add_bg(s9, IMG_RED_BG)
add_claro_logo(s9)

text(s9, 1.50, 2.20, 10.33, 1.10, "SPARC", size=60, color=WHITE, bold=True)
text(s9, 1.50, 3.40, 10.33, 0.60, "Sistema de Provisionamento e Ativação de Roteadores da CLARO", size=22, color=RGBColor(0xFE, 0xCA, 0xCA), bold=True)
text(s9, 1.50, 4.10, 10.33, 0.60, "Obrigado!  |  Regional Santa Catarina  |  george.calcmann@claro.com.br", size=14, color=WHITE)


# Salva Apresentação PPTX
os.makedirs(os.path.dirname(OUT_PPTX_DEFESA), exist_ok=True)
prs.save(OUT_PPTX_DEFESA)
print(f"[OK] Apresentação gerada em: {OUT_PPTX_DEFESA}")

try:
    os.makedirs(os.path.dirname(OUT_PPTX_BETA), exist_ok=True)
    prs.save(OUT_PPTX_BETA)
    prs.save(OUT_PPTX_RESUMIDA)
    print(f"[OK] Cópia em dist-beta gerada em: {OUT_PPTX_BETA}")
    print(f"[OK] Cópia em dist-beta (Resumida) gerada em: {OUT_PPTX_RESUMIDA}")
except Exception as e:
    print(f"[AVISO] Falha ao copiar para dist-beta: {e}")
