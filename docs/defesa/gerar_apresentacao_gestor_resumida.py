# -*- coding: utf-8 -*-
"""
Gera a Apresentação Executiva Ilustrada SPARC x Panteon
Total de 9 Lâminas Widescreen 16:9 de Alto Impacto Visual
- Slide 1: Capa Executiva com texto solicitado
- Slide 2: Os 4 Pilares Estratégicos (CONTEÚDO ESTRITAMENTE PRESERVADO)
- Slide 3: Como a Ferramenta Funciona na Prática (Guia Visual de Operação da UI)
- Slide 4: Flexibilidade Multiplataforma (Windows Desktop na Base + Android Mobile em Campo)
- Slide 5: Scorecard Comparativo Executivo (Placar 10 x 1)
- Slide 6: Matriz de Resiliência nos Cenários de Campo (Tolerância a Falhas)
- Slide 7: Modularidade Operacional (Full na Base + Tester no Campo & Alavanca Contratual)
- Slide 8: Impacto Mensurado e Governança de Longo Prazo (KPIs e Sustentação)
- Slide 9: Recomendações Executivas e Plano de Ação Imediato
Uso: python gerar_apresentacao_gestor_resumida.py
"""
import os
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR

# Paleta Corporativa Claro / SPARC Executiva
WINE = RGBColor(0x88, 0x13, 0x37)        # Vinho Corporativo Claro (#881337)
RED = RGBColor(0xDA, 0x29, 0x1C)         # Vermelho Claro Institucional (#DA291C)
BG = RGBColor(0xF8, 0xFA, 0xFC)          # Cinza Claro / Slate Clean (#F8FAFC)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)       # Branco
INK = RGBColor(0x0F, 0x17, 0x2A)         # Azul Ardósia Quase Preto (#0F172A)
MUTED = RGBColor(0x47, 0x55, 0x69)       # Cinza Médio (#475569)
LINE = RGBColor(0xE2, 0xE8, 0xF0)        # Borda Suave (#E2E8F0)
GREEN = RGBColor(0x16, 0xA3, 0x4A)       # Verde Sucesso (#16A34A)
GREEN_BG = RGBColor(0xDC, 0xFC, 0xE7)    # Verde Claro Fundo (#DCFCE7)
RED_ACC = RGBColor(0xDC, 0x26, 0x26)     # Vermelho Alerta (#DC2626)
RED_BG = RGBColor(0xFE, 0xE2, 0xE2)      # Vermelho Claro Fundo (#FEE2E2)
AMBER = RGBColor(0xD9, 0x77, 0x06)       # Âmbar Atenção (#D97706)
AMBER_BG = RGBColor(0xFE, 0xF3, 0xC7)    # Âmbar Claro Fundo (#FEF3C7)
BLUE = RGBColor(0x02, 0x84, 0xC7)        # Azul Destaque (#0284C7)
BLUE_BG = RGBColor(0xE0, 0xF2, 0xFE)     # Azul Claro Fundo (#E0F2FE)
PINK = RGBColor(0xFA, 0xE8, 0xEB)        # Rosa/Salmão Suave Claro (#FAE8EB)
DARK_CARD = RGBColor(0x1E, 0x29, 0x3B)   # Slate Escuro (#1E293B)
FONT = "Segoe UI"

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DEFESA = os.path.join(HERE, "SPARC_Apresentacao_Executiva_Resumida.pptx")
OUT_BETA = r"C:\SPARC\dist-beta\SPARC_Apresentacao_Executiva_Resumida.pptx"

ASSETS_DIR = os.path.join(HERE, "assets")
IMG_UI_FULL = os.path.join(ASSETS_DIR, "sparc_ui_full.png")
IMG_UI_16_9 = os.path.join(ASSETS_DIR, "sparc_ui_16_9.png")
IMG_MOBILE = os.path.join(ASSETS_DIR, "sparc_mobile.png")
LOGO = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo_full.png"
if not os.path.exists(LOGO):
    LOGO = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo.png"

prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)
BLANK = prs.slide_layouts[6]

# ----------------------------------------------------------------- Utilitários
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
        sh.adjustments[0] = 0.05
    return sh

def text(s, x, y, w, h, runs, size=14, color=INK, bold=False, align=PP_ALIGN.LEFT,
         anchor=MSO_ANCHOR.TOP, shape=None, spacing=3):
    if shape is None:
        shape = s.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = shape.text_frame
    tf.word_wrap = True
    tf.vertical_anchor = anchor
    tf.margin_left = tf.margin_right = Inches(0.08)
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

def header(s, title, subtitle=None, kicker="CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC"):
    rect(s, 0, 0, 13.333, 7.5, BG)
    rect(s, 0, 0, 13.333, 1.15, WINE)
    rect(s, 0, 1.15, 13.333, 0.05, RED)
    text(s, 0.8, 0.12, 10, 0.3, "🔴 " + kicker.upper(), size=11, color=PINK, bold=True)
    if subtitle:
        text(s, 0.8, 0.40, 11.7, 0.45, title, size=22, color=WHITE, bold=True)
        text(s, 0.8, 0.82, 11.7, 0.3, subtitle, size=11, color=PINK)
    else:
        text(s, 0.8, 0.42, 11.7, 0.65, title, size=24, color=WHITE, bold=True)
    rect(s, 0, 7.05, 13.333, 0.45, RGBColor(0xF1, 0xF5, 0xF9))
    text(s, 0.8, 7.12, 11.7, 0.3,
         "SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro  |  Regional SC  |  george.calcmann@claro.com.br",
         size=10, color=MUTED)

def pill(s, x, y, w, label, fill, color, size=11, h=0.34):
    p = rect(s, x, y, w, h, fill, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    p.adjustments[0] = 0.5
    text(s, 0, 0, 0, 0, label, size=size, color=color, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=p)
    return p

def card(s, x, y, w, h, fill=WHITE, line=LINE):
    return rect(s, x, y, w, h, fill, line, shape=MSO_SHAPE.ROUNDED_RECTANGLE)


# ==============================================================================
# SLIDE 1: CAPA EXECUTIVA (TEXTO SOLICITADO)
# ==============================================================================
s1 = prs.slides.add_slide(BLANK)
rect(s1, 0, 0, 13.333, 7.5, WINE)
rect(s1, 0, 0, 0.5, 7.5, RED)

pill(s1, 1.15, 0.6, 4.2, "🔴  CLARO S.A. — ATIVAÇÃO EMPRESARIAL", RED, WHITE, size=12, h=0.42)
text(s1, 1.1, 1.25, 11, 1.1, "SPARC", size=52, color=WHITE, bold=True)
text(s1, 1.1, 2.35, 11.5, 0.6, "Decisão Estratégica: Flexibilidade Operacional, Soberania e Redução de Custos",
     size=22, color=PINK, bold=True)

text(s1, 1.15, 3.05, 11.2, 0.7,
     "Avaliação executiva da solução autônoma interna (SPARC) frente à contratação de plataformas externas, no apoio às Ativações de campo e Visita Única.",
     size=14, color=WHITE)

# 4 Cards de Destaque na Capa (Texto exato da solicitação)
card_w = 2.65
card_h = 2.3
ys = 4.05
xs = [1.15, 4.05, 6.95, 9.85]

cards_info = [
    ("FLEXIBILIDADE TOTAL", "Modo Offline Nativo",
     "Trabalha em bancada ou campo sem depender do link do cliente. Cache de firmwares homologados embutido no app.", BLUE),
    ("CUSTO ZERO & CAPEX", "R$ 0 em Licenças / Servidores",
     "Sem cobranças de licenças, ativação via cadastro gerenciável pelo módulo Administrador, cada cluster controla sua distribuição", GREEN),
    ("REDUÇÃO CONTRATUAL", "Oportunidade de economia por OS",
     "Roteador já pode sair 100% testado da base Claro (Plug & Play), permitindo abater o item pago ao parceiro terceirizado.", RED),
    ("AGILIDADE INTERNA", "Suporte & Evolução Ágil",
     "Novos modelos (Cisco, HPE, Fortinet) e melhorias liberados em horas/dias pela equipe Claro. Sem filas de fornecedor.", AMBER)
]

for i, (kicker, title, desc, col) in enumerate(cards_info):
    c = card(s1, xs[i], ys, card_w, card_h, fill=DARK_CARD, line=col)
    pill(s1, xs[i] + 0.15, ys + 0.18, card_w - 0.3, kicker, col, WHITE, size=10, h=0.32)
    text(s1, xs[i] + 0.15, ys + 0.62, card_w - 0.3, 0.55, title, size=13, color=WHITE, bold=True)
    text(s1, xs[i] + 0.15, ys + 1.25, card_w - 0.3, 0.95, desc, size=11, color=RGBColor(0xCB, 0xD5, 0xE1))

if os.path.exists(LOGO):
    try:
        s1.shapes.add_picture(LOGO, Inches(10.5), Inches(0.6), height=Inches(0.9))
    except Exception:
        pass


# ==============================================================================
# SLIDE 2: OS 4 PILARES ESTRATÉGICOS (CONTEÚDO ESTRITAMENTE PRESERVADO)
# ==============================================================================
s2 = prs.slides.add_slide(BLANK)
header(s2, "Os 4 Pilares Estratégicos",
       "Comparativo focado nas reais dores de campo, flexibilidade e retorno financeiro para a Claro",
       kicker="CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC")

p_w = 2.80
p_h = 5.40
p_xs = [0.80, 3.85, 6.90, 9.95]
p_y = 1.40

# Coluna 1: FLEXIBILIDADE DE USO / Modo Offline
c1 = card(s2, p_xs[0], p_y, p_w, p_h, fill=WHITE, line=BLUE)
pill(s2, p_xs[0] + 0.15, p_y + 0.18, p_w - 0.3, "1. FLEXIBILIDADE DE USO", BLUE, WHITE, size=11, h=0.35)
text(s2, p_xs[0] + 0.15, p_y + 0.65, p_w - 0.3, 0.5, "Modo Offline", size=15, color=BLUE, bold=True)
text(s2, p_xs[0] + 0.15, p_y + 1.20, p_w - 0.3, 4.0, [
    [("✔ ", {"color": GREEN, "bold": True}), ("Zero Dependência do Link: ", {"bold": True}),
     ("O SPARC foi concebido sob o paradigma Offline-First, operando de forma 100% autónoma diretamente no terminal do técnico, sem necessidade do enlace de acesso durante as etapas preliminares.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Cache de Firmware Local: ", {"bold": True}),
     ("Imagens homologadas (.bin/.ipe/.out) embarcadas, com atualizações via download sempre que necessário.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Cabo Serial + Ethernet: ", {"bold": True}),
     ("Suporta recuperação de firmwares corrompidos via TFTP nativo, restaura configurações de equipamentos com senhas desconhecidas.")]
], size=11, spacing=2)

# Coluna 2: REDUÇÃO DE CUSTOS / Economia Direta
c2 = card(s2, p_xs[1], p_y, p_w, p_h, fill=WHITE, line=GREEN)
pill(s2, p_xs[1] + 0.15, p_y + 0.18, p_w - 0.3, "2. REDUÇÃO DE CUSTOS", GREEN, WHITE, size=11, h=0.35)
text(s2, p_xs[1] + 0.15, p_y + 0.65, p_w - 0.3, 0.5, "Economia Direta", size=15, color=GREEN, bold=True)
text(s2, p_xs[1] + 0.15, p_y + 1.20, p_w - 0.3, 4.0, [
    [("✔ ", {"color": GREEN, "bold": True}), ("R$ 0 em Licenças: ", {"bold": True}),
     ("Ativo 100% interno Claro. Sem custos com licenças, manutenções e implementações.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Flexibilidade e Abatimento por OS: ", {"bold": True}),
     ("Roteador pode sair pronto da base, configurado e testado; permite renegociar o item 'configuração de CPE’.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Fim das Visitas Perdidas: ", {"bold": True}),
     ("Triagem antecipada elimina retrabalho de técnicos por roteador defeituoso.")]
], size=11, spacing=2)

# Coluna 3: AGILIDADE & SUPORTE / Soberania Técnica Claro
c3 = card(s2, p_xs[2], p_y, p_w, p_h, fill=WHITE, line=AMBER)
pill(s2, p_xs[2] + 0.15, p_y + 0.18, p_w - 0.3, "3. AGILIDADE & SUPORTE", AMBER, WHITE, size=11, h=0.35)
text(s2, p_xs[2] + 0.15, p_y + 0.65, p_w - 0.3, 0.5, "Soberania Técnica Claro", size=15, color=AMBER, bold=True)
text(s2, p_xs[2] + 0.15, p_y + 1.20, p_w - 0.3, 4.0, [
    [("✔ ", {"color": GREEN, "bold": True}), ("Sem Fila de Fornecedor: ", {"bold": True}),
     ("com SPARC dispensamos longas negociações com fornecedores, aditivos contratuais, defesas de CAPEX, prazos dilatados para ajustes e desenvolvimento.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Desenvolvimento Colaborativo (Co-design): ", {"bold": True}),
     ("alinhamento direto e contínuo com os pontos focais da operação, incorporando feedbacks práticos e evoluindo a solução a partir das dores reais vivenciadas pelas equipes de campo.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Domínio do Conhecimento: ", {"bold": True}),
     ("Know-how mantido 100% dentro da Claro; zero risco de dependência tecnológica.")]
], size=11, spacing=2)

# Coluna 4: TRIAGEM DE HARDWARE / Blindagem da VU
c4 = card(s2, p_xs[3], p_y, p_w, p_h, fill=WHITE, line=RED)
pill(s2, p_xs[3] + 0.15, p_y + 0.18, p_w - 0.3, "4. TRIAGEM DE HARDWARE", RED, WHITE, size=11, h=0.35)
text(s2, p_xs[3] + 0.15, p_y + 0.65, p_w - 0.3, 0.5, "Blindagem da VU", size=15, color=RED, bold=True)
text(s2, p_xs[3] + 0.15, p_y + 1.20, p_w - 0.3, 4.0, [
    [("✔ ", {"color": GREEN, "bold": True}), ("Teste Prévio na Base: ", {"bold": True}),
     ("Portas de rede, memória Flash e boot podem ser avaliados antecipadamente com as facilidades do SPARC.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Substituição Antecipada: ", {"bold": True}),
     ("Se houver defeito físico, troca antes de sair para o cliente.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Entrega Plug & Play: ", {"bold": True}),
     ("Roteador sai da base etiquetado, com IP do circuito e firmware homologado gravado.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Produtividade em Bancada: ", {"bold": True}),
     ("Um único técnico interno prepara dezenas de aparelhos/dia em menos de 10 min cada.")]
], size=11, spacing=2)


# ==============================================================================
# SLIDE 3 (NOVO): COMO A FERRAMENTA FUNCIONA NA PRÁTICA (GUIA VISUAL DA UI)
# ==============================================================================
s3 = prs.slides.add_slide(BLANK)
header(s3, "Como a Ferramenta Funciona na Prática: Esteira Automatizada",
       "Visão operacional da interface unificada que guia o técnico do diagnóstico físico ao laudo formal")

# Painel Esquerdo: Imagem da Interface SPARC (Desktop)
card(s3, 0.8, 1.45, 6.7, 5.4, fill=WHITE, line=LINE)
pill(s3, 0.95, 1.60, 3.6, "INTERFACE PRINCIPAL — FLUXO UNIFICADO", WINE, WHITE, size=10.5, h=0.34)

img_loaded = False
if os.path.exists(IMG_UI_16_9):
    try:
        s3.shapes.add_picture(IMG_UI_16_9, Inches(0.95), Inches(2.05), width=Inches(6.4))
        img_loaded = True
    except Exception:
        pass
if not img_loaded and os.path.exists(IMG_UI_FULL):
    try:
        s3.shapes.add_picture(IMG_UI_FULL, Inches(0.95), Inches(2.05), width=Inches(6.4))
    except Exception:
        pass

# Legenda abaixo da imagem
rect(s3, 0.95, 5.95, 6.4, 0.75, fill=RGBColor(0xF1, 0xF5, 0xF9), line=LINE)
text(s3, 1.05, 6.00, 6.2, 0.65, [
    [("Tela Única Inteligente: ", {"bold": True, "size": 11, "color": INK}),
     ("Integra detecção de portas COM, terminal CLI colorido, leitor de SAIP e esteira de automação com feedback visual passo a passo.", {"size": 10.5, "color": MUTED})]
], spacing=1)

# Painel Direito: Os 4 Passos Operacionais da UI
card(s3, 7.7, 1.45, 4.833, 5.4, fill=WHITE, line=LINE)
pill(s3, 7.85, 1.60, 3.2, "GUIA VISUAL DE OPERAÇÃO", BLUE, WHITE, size=10.5, h=0.34)

step_boxes = [
    ("1a & 1b. Detecção e Autenticação Serial",
     "Identifica automaticamente Cisco, HPE ou FortiGate. Desbloqueia e autentica o console sem intervenção manual (senhas padrão Claro e fábrica).",
     BLUE, BLUE_BG),
    ("2. Leitura Inteligente do SAIP (PDF/TXT)",
     "Importa a ficha técnica oficial com 1 clique. Preenche WAN, LAN, BGP, rotas e hostname com 100% de precisão e zero digitação.",
     GREEN, GREEN_BG),
    ("3. Gravação de Firmware & Provisionamento",
     "Servidor TFTP/HTTP nativo grava firmware via cabo de rede na porta WAN/LAN. Aplica o script do circuito e verifica link físico.",
     AMBER, AMBER_BG),
    ("4. Homologação, Banda e Certidão PDF",
     "Audita ICMP (LAN/WAN/Web), valida Telnet/SSH, afere velocidade e emite laudo formal de Certidão em PDF com QR Code para a OS.",
     RED, RED_BG)
]

for i, (stitle, sdesc, scol, sbg) in enumerate(step_boxes):
    sy = 2.05 + i * 1.18
    rect(s3, 7.85, sy, 4.533, 1.05, fill=sbg, line=scol)
    pill(s3, 7.95, sy + 0.10, 0.75, f"P{i+1}", scol, WHITE, size=10, h=0.28)
    text(s3, 8.80, sy + 0.08, 3.5, 0.32, stitle, size=11.5, color=INK, bold=True)
    text(s3, 7.95, sy + 0.42, 4.35, 0.58, sdesc, size=10, color=MUTED)


# ==============================================================================
# SLIDE 4 (NOVO): MULTIPLATAFORMA (WINDOWS NA BASE + ANDROID EM CAMPO)
# ==============================================================================
s4 = prs.slides.add_slide(BLANK)
header(s4, "Flexibilidade de Plataformas: Desktop na Base + Mobile em Campo",
       "A mesma inteligência operacional adaptada para alta produtividade em bancada e mobilidade total")

half_w = 5.70
half_h = 5.40

# Card Esquerdo: Windows Desktop
card(s4, 0.8, 1.45, half_w, half_h, fill=WHITE, line=WINE)
pill(s4, 0.95, 1.60, 4.2, "SPARC WINDOWS DESKTOP (.EXE) — BASE CLARO", WINE, WHITE, size=11, h=0.35)
text(s4, 0.95, 2.05, 5.4, 0.45, "Modo Bancada / Laboratório na Regional", size=15, color=WINE, bold=True)

if os.path.exists(IMG_UI_16_9):
    try:
        s4.shapes.add_picture(IMG_UI_16_9, Inches(0.95), Inches(2.55), width=Inches(5.4))
    except Exception:
        pass

text(s4, 0.95, 5.65, 5.4, 1.1, [
    [("✔ ", {"color": GREEN, "bold": True}), ("Produtividade Máxima: ", {"bold": True}),
     ("Um único técnico prepara dezenas de aparelhos/dia em esteira fluida.")],
    [("✔ ", {"color": GREEN, "bold": True}), ("Recuperação de BIOS/ROMMON: ", {"bold": True}),
     ("TFTP gigabit nativo grava firmwares e recupera aparelhos sem SO.")],
    [("✔ ", {"color": GREEN, "bold": True}), ("Triagem Fisiológica de Hardware: ", {"bold": True}),
     ("Testa portas, memória e boot antes de despachar para o cliente.")]
], size=11, spacing=2)

# Card Direito: Android Mobile
card(s4, 6.833, 1.45, half_w, half_h, fill=WHITE, line=BLUE)
pill(s4, 6.983, 1.60, 4.2, "SPARC MOBILE ANDROID (.APK) — CAMPO", BLUE, WHITE, size=11, h=0.35)
text(s4, 6.983, 2.05, 5.4, 0.45, "Modo Portátil / Smartphone do Técnico", size=15, color=BLUE, bold=True)

mob_loaded = False
if os.path.exists(IMG_MOBILE):
    try:
        # Imagem mobile proporção vertical
        s4.shapes.add_picture(IMG_MOBILE, Inches(8.3), Inches(2.55), height=Inches(3.0))
        mob_loaded = True
    except Exception:
        pass

text(s4, 6.983, 5.65, 5.4, 1.1, [
    [("✔ ", {"color": GREEN, "bold": True}), ("100% Portátil: ", {"bold": True}),
     ("Roda no celular corporativo Android do técnico de campo via cabo USB-Serial.")],
    [("✔ ", {"color": GREEN, "bold": True}), ("Validação e Laudo na Ponta: ", {"bold": True}),
     ("Afere velocidade (Speedtest / Y.1564) e gera laudo PDF formal com QR Code.")],
    [("✔ ", {"color": GREEN, "bold": True}), ("Modelo SPARC Tester: ", {"bold": True}),
     ("Ideal para parceiros terceirizados apenas validarem a entrega sem mexer na config.")]
], size=11, spacing=2)


# ==============================================================================
# SLIDE 5: SCORECARD COMPARATIVO EXECUTIVO (TABELA GRÁFICA)
# ==============================================================================
s5 = prs.slides.add_slide(BLANK)
header(s5, "Scorecard Comparativo: SPARC (Interno) × Panteon (Externo)",
       "Análise objetiva dos requisitos técnicos, contratuais e operacionais de implantação")

# Placar no Topo
rect(s5, 0.8, 1.35, 11.733, 0.75, DARK_CARD)
text(s5, 1.0, 1.45, 6.0, 0.55, [
    [("PLACAR ESTRATÉGICO: ", {"bold": True, "size": 14, "color": PINK}),
     ("SPARC ", {"bold": True, "size": 18, "color": GREEN}),
     ("10  ×  1 ", {"bold": True, "size": 18, "color": WHITE}),
     ("Panteon", {"bold": True, "size": 18, "color": RED_ACC})]
], align=PP_ALIGN.LEFT)
text(s5, 7.2, 1.50, 5.1, 0.45,
     "Vantagem expressiva em Flexibilidade, Custo Zero e Soberania Técnica",
     size=12, color=RGBColor(0x94, 0xA3, 0xB8), align=PP_ALIGN.RIGHT)

# Tabela Comparativa em Cards / Linhas
c_y = 2.25
c_h = 0.44
gap = 0.04

criteria = [
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

for i, (crit, sparc_val, pant_val, sparc_win) in enumerate(criteria):
    row_y = c_y + i * (c_h + gap)
    bg_row = WHITE if i % 2 == 0 else RGBColor(0xF1, 0xF5, 0xF9)
    rect(s5, 0.8, row_y, 11.733, c_h, fill=bg_row, line=LINE)
    
    text(s5, 0.95, row_y + 0.06, 4.1, 0.35, crit, size=11, color=INK, bold=True)
    
    rect(s5, 5.15, row_y + 0.05, 3.45, 0.34, fill=GREEN_BG if sparc_win else BLUE_BG)
    icon_s = "✔  " if sparc_win else "◐  "
    text(s5, 5.25, row_y + 0.07, 3.3, 0.35, [
        [(icon_s, {"color": GREEN if sparc_win else BLUE, "bold": True}),
         (sparc_val, {"size": 10.5, "bold": True, "color": INK})]
    ])
    
    rect(s5, 8.75, row_y + 0.05, 3.65, 0.34, fill=RED_BG if sparc_win else GREEN_BG)
    icon_p = "✖  " if sparc_win else "✔  "
    text(s5, 8.85, row_y + 0.07, 3.5, 0.35, [
        [(icon_p, {"color": RED_ACC if sparc_win else GREEN, "bold": True}),
         (pant_val, {"size": 10.5, "bold": False, "color": INK})]
    ])


# ==============================================================================
# SLIDE 6: MATRIZ DE RESILIÊNCIA E COBERTURA DE CENÁRIOS DE CAMPO
# ==============================================================================
s6 = prs.slides.add_slide(BLANK)
header(s6, "Flexibilidade Operacional: Onde Cada Solução Funciona na Prática",
       "Análise de tolerância a falhas nos 4 principais cenários de atendimento ao cliente B2B")

c_w6 = 5.70
c_h6 = 2.45

# Cenário 1: Link Ativo
card(s6, 0.8, 1.45, c_w6, c_h6, fill=WHITE, line=LINE)
pill(s6, 0.95, 1.60, 2.2, "CENÁRIO 1: LINK ATIVO", BLUE, WHITE, size=10, h=0.32)
text(s6, 3.3, 1.62, 3.0, 0.3, "Circuito entregue e estável", size=11, color=MUTED)
text(s6, 0.95, 2.05, 5.4, 1.7, [
    [("Situação: ", {"bold": True}), ("Cliente possui conectividade disponível no ato da visita.")],
    [("SPARC: ", {"bold": True, "color": GREEN}), ("✔ FUNCIONA PERFEITAMENTE ", {"bold": True, "color": GREEN}),
     ("— Baixa parâmetros e valida direto pelo link.")],
    [("Panteon: ", {"bold": True, "color": GREEN}), ("✔ FUNCIONA ", {"bold": True, "color": GREEN}),
     ("— Este é o único fluxo previsto na ferramenta externa.")],
    "",
    [("Veredito: ", {"bold": True, "color": MUTED}), ("Ambos atendem quando tudo corre 100% bem.")]
], size=11.5, spacing=2)

# Cenário 2: Bancada na Base
card(s6, 6.833, 1.45, c_w6, c_h6, fill=WHITE, line=GREEN)
pill(s6, 6.983, 1.60, 2.7, "CENÁRIO 2: BANCADA NA BASE", GREEN, WHITE, size=10, h=0.32)
text(s6, 9.8, 1.62, 2.6, 0.3, "Sem link / Preparo prévio", size=11, color=MUTED)
text(s6, 6.983, 2.05, 5.4, 1.7, [
    [("Situação: ", {"bold": True}), ("Equipamento é preparado antes de ir ao cliente por técnicos Claro.")],
    [("SPARC: ", {"bold": True, "color": GREEN}), ("✔ 100% OPERACIONAL ", {"bold": True, "color": GREEN}),
     ("— Cache de firmware embarcado; sai pronto em < 10 min.")],
    [("Panteon: ", {"bold": True, "color": RED_ACC}), ("✖ INVIABILIZADO ", {"bold": True, "color": RED_ACC}),
     ("— Exige obrigatoriamente link online ativo para operar.")],
    "",
    [("Ganho Claro: ", {"bold": True, "color": GREEN}), ("Roteador sai Plug & Play. Terceiro só instala fisicamente.")]
], size=11.5, spacing=2)

# Cenário 3: Link Instável / Atraso
card(s6, 0.8, 4.15, c_w6, c_h6, fill=WHITE, line=AMBER)
pill(s6, 0.95, 4.30, 2.7, "CENÁRIO 3: LINK INSTÁVEL", AMBER, WHITE, size=10, h=0.32)
text(s6, 3.8, 4.32, 2.5, 0.3, "Oscilação ou atraso de rede", size=11, color=MUTED)
text(s6, 0.95, 4.75, 5.4, 1.7, [
    [("Situação: ", {"bold": True}), ("Rede oscilando ou gerência ainda não liberou o circuito.")],
    [("SPARC: ", {"bold": True, "color": GREEN}), ("✔ ZERO IMPACTO ", {"bold": True, "color": GREEN}),
     ("— Aplica firmware e configuração localmente; sobe assim que a rede normalizar.")],
    [("Panteon: ", {"bold": True, "color": RED_ACC}), ("✖ TRAVA TOTAL ", {"bold": True, "color": RED_ACC}),
     ("— Interrompe processo, prende o técnico e pode gerar visita perdida.")],
    "",
    [("Ganho Claro: ", {"bold": True, "color": GREEN}), ("Blindagem do cronograma e proteção da Visita Única.")]
], size=11.5, spacing=2)

# Cenário 4: Firmware Corrompido / BIOS
card(s6, 6.833, 4.15, c_w6, c_h6, fill=WHITE, line=RED)
pill(s6, 6.983, 4.30, 2.9, "CENÁRIO 4: RECUPERAÇÃO / BIOS", RED, WHITE, size=10, h=0.32)
text(s6, 10.0, 4.32, 2.4, 0.3, "Sem SO / ROMMON / BootWare", size=11, color=MUTED)
text(s6, 6.983, 4.75, 5.4, 1.7, [
    [("Situação: ", {"bold": True}), ("Roteador reutilizado sem imagem ou bloqueado em bootloader.")],
    [("SPARC: ", {"bold": True, "color": GREEN}), ("✔ RECUPERAÇÃO TOTAL ", {"bold": True, "color": GREEN}),
     ("— Servidor TFTP Ethernet nativo grava SO via porta WAN.")],
    [("Panteon: ", {"bold": True, "color": RED_ACC}), ("✖ NÃO SUPORTA ", {"bold": True, "color": RED_ACC}),
     ("— Opera apenas via CLI Serial; incapaz de transferir imagem TFTP.")],
    "",
    [("Ganho Claro: ", {"bold": True, "color": GREEN}), ("Reaproveitamento de centenas de roteadores parados em estoque.")]
], size=11.5, spacing=2)


# ==============================================================================
# SLIDE 7: MODELO MODULAR: SPARC FULL (BASE) + SPARC TESTER (CAMPO)
# ==============================================================================
s7 = prs.slides.add_slide(BLANK)
header(s7, "Modularidade Operacional: O Modelo SPARC Full × SPARC Tester",
       "A esteira que desonera contratos terceirizados e garante zero entrega com defeito físico")

box_w = 5.70
box_h = 3.65

# Bloco 1: Base Claro
card(s7, 0.8, 1.45, box_w, box_h, fill=WHITE, line=WINE)
pill(s7, 0.95, 1.60, 3.8, "1. BASE CLARO: SPARC FULL (Técnicos Próprios)", WINE, WHITE, size=11, h=0.35)
text(s7, 0.95, 2.10, 5.4, 2.8, [
    [("Quem opera: ", {"bold": True}), ("Assistência Técnica e Ativação própria da Claro.")],
    [("Onde: ", {"bold": True}), ("Bancada / Laboratório na Base Regional.")],
    [("Triagem de Hardware: ", {"bold": True, "color": RED_ACC}),
     ("Testa portas Ethernet, fonte, boot e memórias. "),
     ("Defeito físico é corrigido/substituído na base antes de ir para a rua.", {"bold": True})],
    [("O que faz: ", {"bold": True}), ("Lê o SAIP (PDF/TXT) sem erro, grava firmware homologado e aplica script.")],
    [("Tempo Médio: ", {"bold": True}), ("Menos de 10 minutos por equipamento.")],
    [("Resultado: ", {"bold": True, "color": GREEN}),
     ("Roteador sai 100% pronto, testado e etiquetado ('Plug & Play').", {"bold": True})]
], size=11.5, spacing=3)

# Bloco 2: Campo Parceiro
card(s7, 6.833, 1.45, box_w, box_h, fill=WHITE, line=BLUE)
pill(s7, 6.983, 1.60, 4.0, "2. CAMPO: SPARC TESTER (Técnicos Terceirizados)", BLUE, WHITE, size=11, h=0.35)
text(s7, 6.983, 2.10, 5.4, 2.8, [
    [("Quem opera: ", {"bold": True}), ("Empresas parceiras terceirizadas responsáveis pela instalação física.")],
    [("O que muda: ", {"bold": True, "color": BLUE}),
     ("Retira do terceiro o acesso privilegiado e a responsabilidade de configurar.")],
    [("O que é o SPARC Tester: ", {"bold": True}), ("App ultraleve e blindado para celular corporativo ou notebook.")],
    [("O que faz: ", {"bold": True}), ("Valida link, afere velocidade com Speedtest e gera Certidão PDF formal.")],
    [("Versatilidade Híbrida: ", {"bold": True}), ("Permite também ativar online se a base não preparou o roteador.")],
    [("Resultado: ", {"bold": True, "color": GREEN}),
     ("Zero erro de configuração em campo e entrega homologada com laudo.", {"bold": True})]
], size=11.5, spacing=3)

# Alavanca Financeira em Destaque
rect(s7, 0.8, 5.30, 11.733, 1.55, fill=GREEN_BG, line=GREEN)
text(s7, 1.05, 5.45, 11.2, 1.25, [
    [("💰 ALAVANCA FINANCEIRA ESTRATÉGICA PARA A CLARO: ", {"bold": True, "size": 14, "color": GREEN}),
     ("REDUÇÃO DIRETA DE CONTRATO POR OS", {"bold": True, "size": 14, "color": INK})],
    "",
    [("Como o parceiro terceirizado ", {"size": 12}),
     ("NÃO precisa mais configurar o roteador no cliente ", {"bold": True, "size": 12, "color": INK}),
     ("(o equipamento já chega pronto e testado pela base Claro), a Claro ganha respaldo técnico e operacional imediato para ", {"size": 12}),
     ("renegociar e abater o valor do item 'Configuração de CPE' ", {"bold": True, "size": 12, "color": GREEN}),
     ("pago nas ordens de serviço. Em escala regional e nacional, essa economia cobre com folga qualquer investimento interno!", {"size": 12})]
], spacing=2)


# ==============================================================================
# SLIDE 8: GANHOS MENSURADOS, AGILIDADE DE SUPORTE E SOBERANIA
# ==============================================================================
s8 = prs.slides.add_slide(BLANK)
header(s8, "Impacto Mensurado, Agilidade de Suporte e Soberania Tecnológica",
       "Resultados consolidados em campo e comparação da governança de longo prazo")

kpi_w = 2.80
kpi_h = 1.65
kpi_y = 1.45

kpis = [
    ("-88%", "Tempo de Ativação", "De ~50 min para < 10 min por roteador", GREEN),
    ("100%", "Aderência SAIP", "Zero erro de digitação de IPs, rotas e máscaras", BLUE),
    ("R$ 0", "Licenças & Servidores", "Economia contínua de Capex/Opex de fornecedor", GREEN),
    ("0%", "Visitas Perdidas por HW", "Triagem prévia na base elimina falhas em campo", RED)
]

for i, (val, title, sub, col) in enumerate(kpis):
    card(s8, p_xs[i], kpi_y, kpi_w, kpi_h, fill=WHITE, line=col)
    text(s8, p_xs[i] + 0.1, kpi_y + 0.12, kpi_w - 0.2, 0.55, val, size=32, color=col, bold=True, align=PP_ALIGN.CENTER)
    text(s8, p_xs[i] + 0.1, kpi_y + 0.72, kpi_w - 0.2, 0.35, title, size=13, color=INK, bold=True, align=PP_ALIGN.CENTER)
    text(s8, p_xs[i] + 0.1, kpi_y + 1.10, kpi_w - 0.2, 0.45, sub, size=10.5, color=MUTED, align=PP_ALIGN.CENTER)

col_w = 5.70
col_h = 3.50
col_y = 3.35

card(s8, 0.8, col_y, col_w, col_h, fill=WHITE, line=GREEN)
pill(s8, 0.95, col_y + 0.15, 3.5, "SPARC: SUSTENTAÇÃO ÁGIL INTERNA", GREEN, WHITE, size=11, h=0.35)
text(s8, 0.95, col_y + 0.60, 5.4, 2.7, [
    [("✔ ", {"color": GREEN, "bold": True}), ("Evolução Ágil e Contínua: ", {"bold": True}),
     ("Ajustes de firmware, regexes e novos modelos liberados em horas/dias pela equipe Claro.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Multi-Fabricante Real: ", {"bold": True}),
     ("Cisco (1100, 841, 921, 1900, 2900), HPE MSR (930, 954, 1002) e Fortinet 40F já ativos.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Zero SLA de Terceiros: ", {"bold": True}),
     ("Qualquer necessidade técnica é resolvida diretamente sem dependência de fornecedor.")],
    "",
    [("✔ ", {"color": GREEN, "bold": True}), ("Soberania Tecnológica: ", {"bold": True}),
     ("O código, as regras de negócio e o know-how pertencem 100% à Claro.")]
], size=11.5, spacing=2)

card(s8, 6.833, col_y, col_w, col_h, fill=WHITE, line=RED)
pill(s8, 6.983, col_y + 0.15, 3.8, "PANTEON: RISCOS DO MODELO EXTERNO", RED, WHITE, size=11, h=0.35)
text(s8, 6.983, col_y + 0.60, 5.4, 2.7, [
    [("✖ ", {"color": RED_ACC, "bold": True}), ("Dependência Crônica de Fornecedor: ", {"bold": True}),
     ("Qualquer customização ou novo modelo depende de orçamento adicional e fila de backlog.")],
    "",
    [("✖ ", {"color": RED_ACC, "bold": True}), ("Contratos Bienais e Reajustes: ", {"bold": True}),
     ("Custos recorrentes que encarecem a operação ano a ano (lock-in comercial).")],
    "",
    [("✖ ", {"color": RED_ACC, "bold": True}), ("Falta de Suporte a Ethernet / TFTP: ", {"bold": True}),
     ("Limitação arquitetural que impede recuperação de roteadores sem SO / em modo BIOS.")],
    "",
    [("✖ ", {"color": RED_ACC, "bold": True}), ("Exigência de 10+ Servidores: ", {"bold": True}),
     ("Complexidade de manutenção e alto custo de infraestrutura centralizada.")]
], size=11.5, spacing=2)


# ==============================================================================
# SLIDE 9: PLANO DE AÇÃO E CONCLUSÃO EXECUTIVA
# ==============================================================================
s9 = prs.slides.add_slide(BLANK)
header(s9, "Recomendações Executivas e Plano de Ação Imediato",
       "Passos claros e sem burocracia para consolidar a eficiência de campo na Claro")

act_w = 3.65
act_h = 3.10
act_y = 1.45
act_xs = [0.80, 4.84, 8.88]

actions = [
    ("PASSO 1", "Aprovar Modelo Modular",
     "Implantar o modelo SPARC Full na Assistência Técnica própria da Regional SC para pré-configuração e triagem, e liberar o SPARC Tester aos parceiros.",
     WINE),
    ("PASSO 2", "Renegociação Contratual",
     "Abrir estudo conjunto com Suprimentos/Contratos para abater o item 'Configuração de CPE' nas faturas de empresas terceirizadas de instalação.",
     GREEN),
    ("PASSO 3", "Oficialização Corporativa",
     "Liberar para o SPARC a mesma credencial de API Corporativa já concedida ao Panteon e oficializar a solução como padrão nacional Claro.",
     BLUE)
]

for i, (step, title, desc, col) in enumerate(actions):
    card(s9, act_xs[i], act_y, act_w, act_h, fill=WHITE, line=col)
    pill(s9, act_xs[i] + 0.15, act_y + 0.18, 1.4, step, col, WHITE, size=11, h=0.35)
    text(s9, act_xs[i] + 0.15, act_y + 0.70, act_w - 0.3, 0.6, title, size=16, color=INK, bold=True)
    text(s9, act_xs[i] + 0.15, act_y + 1.40, act_w - 0.3, 1.5, desc, size=12, color=MUTED)

# Box de Conclusão / Frase de Fechamento
rect(s9, 0.8, 4.80, 11.733, 2.05, fill=DARK_CARD, line=RED)
text(s9, 1.1, 5.00, 11.1, 1.65, [
    [("RESUMO EXECUTIVO PARA TOMADA DE DECISÃO:", {"bold": True, "size": 15, "color": PINK})],
    "",
    [("O ", {"size": 13, "color": WHITE}),
     ("SPARC ", {"bold": True, "size": 14, "color": GREEN}),
     ("entrega uma solução de ", {"size": 13, "color": WHITE}),
     ("custo zero em licenças e servidores", {"bold": True, "size": 13, "color": WHITE}),
     (", com ", {"size": 13, "color": WHITE}),
     ("máxima flexibilidade operacional (100% offline via cache de firmware)", {"bold": True, "size": 13, "color": PINK}),
     (", ", {"size": 13, "color": WHITE}),
     ("eliminação de retrabalho por triagem de hardware na base ", {"bold": True, "size": 13, "color": WHITE}),
     ("e ", {"size": 13, "color": WHITE}),
     ("agilidade total de sustentação interna", {"bold": True, "size": 13, "color": GREEN}),
     (". A adoção do SPARC protege o orçamento da Claro, valoriza a equipe técnica própria e alavanca a economia nos contratos terceirizados.", {"size": 13, "color": WHITE})],
    "",
    [("Decisão Recomendada: ", {"bold": True, "size": 13, "color": PINK}),
     ("Consolidar o SPARC como ativo estratégico prioritário da Claro.", {"bold": True, "size": 13, "color": WHITE})]
], spacing=2)

# Salva arquivo
os.makedirs(os.path.dirname(OUT_DEFESA), exist_ok=True)
prs.save(OUT_DEFESA)
print(f"[OK] Apresentação gerada com sucesso em: {OUT_DEFESA}")

try:
    os.makedirs(os.path.dirname(OUT_BETA), exist_ok=True)
    prs.save(OUT_BETA)
    print(f"[OK] Cópia em dist-beta gerada com sucesso em: {OUT_BETA}")
except Exception as e:
    print(f"[AVISO] Falha ao copiar para dist-beta: {e}")
