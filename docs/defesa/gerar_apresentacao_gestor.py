# -*- coding: utf-8 -*-
"""Gera a apresentação executiva e estratégica SPARC x Panteon para o gestor da regional Claro.
Enfatiza o impacto financeiro, flexibilidade, modularidade (SPARC Full vs SPARC Tester),
a redução de custos contratuais com terceiros, o preparo 100% offline na base (triagem prévia de hardware)
e a eliminação de gargalos quando o link do cliente está inoperante.
Uso: python gerar_apresentacao_gestor.py
"""
import os
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR

WINE = RGBColor(0x88, 0x13, 0x37)
RED = RGBColor(0xDA, 0x29, 0x1C)
BG = RGBColor(0xF8, 0xFA, 0xFC)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
INK = RGBColor(0x1E, 0x29, 0x3B)
MUTED = RGBColor(0x64, 0x74, 0x8B)
LINE = RGBColor(0xE2, 0xE8, 0xF0)
GREEN = RGBColor(0x16, 0xA3, 0x4A)
GREEN_BG = RGBColor(0xDC, 0xFC, 0xE7)
RED_BG = RGBColor(0xFE, 0xE2, 0xE2)
AMBER = RGBColor(0xD9, 0x77, 0x06)
AMBER_BG = RGBColor(0xFE, 0xF3, 0xC7)
BLUE = RGBColor(0x02, 0x84, 0xC7)
BLUE_BG = RGBColor(0xE0, 0xF2, 0xFE)
PINK = RGBColor(0xF9, 0xD5, 0xDC)
FONT = "Segoe UI"

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "SPARC_vs_Panteon_Gestor_Regional.pptx")
LOGO = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo_full.png"

prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)
BLANK = prs.slide_layouts[6]


# ----------------------------------------------------------------- helpers
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
        sh.adjustments[0] = 0.08
    return sh


def text(s, x, y, w, h, runs, size=14, color=INK, bold=False, align=PP_ALIGN.LEFT,
         anchor=MSO_ANCHOR.TOP, shape=None, spacing=4):
    """runs: str | list of paragraphs; each paragraph str or list of (txt, {opts})."""
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
        for t, o in parts:
            r = p.add_run()
            r.text = t
            f = r.font
            f.name = FONT
            f.size = Pt(o.get("size", size))
            f.bold = o.get("bold", bold)
            f.color.rgb = o.get("color", color)
            f.italic = o.get("italic", False)
    return shape


def header(s, title, kicker="CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON"):
    rect(s, 0, 0, 13.333, 7.5, BG)
    rect(s, 0, 0, 13.333, 1.15, WINE)
    rect(s, 0, 1.15, 13.333, 0.06, RED)
    text(s, 0.8, 0.12, 9, 0.3, "🔴 " + kicker, size=11, color=PINK, bold=True)
    text(s, 0.8, 0.42, 11.7, 0.65, title, size=24, color=WHITE, bold=True)
    rect(s, 0, 7.05, 13.333, 0.45, RGBColor(0xF1, 0xF5, 0xF9))
    text(s, 0.8, 7.1, 11.7, 0.3,
         "SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro  |  Contato: george.calcmann@claro.com.br",
         size=10, color=MUTED)


def card(s, x, y, w, h, title, title_fill, body, body_size=13):
    rect(s, x, y, w, h, WHITE, line=PINK, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.25)
    t = rect(s, x, y, w, 0.6, title_fill)
    text(s, 0, 0, 0, 0, title, size=15, color=WHITE, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=t)
    text(s, x + 0.2, y + 0.72, w - 0.4, h - 0.85, body, size=body_size, spacing=7)


def pill(s, x, y, w, label, fill, color, size=11, h=0.32):
    p = rect(s, x, y, w, h, fill, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    p.adjustments[0] = 0.5
    text(s, 0, 0, 0, 0, label, size=size, color=color, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=p)
    return p


def node(s, x, y, w, h, label, fill=WHITE, color=INK, line=LINE, size=12):
    n = rect(s, x, y, w, h, fill, line=line, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.25)
    text(s, 0, 0, 0, 0, label, size=size, color=color, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=n, spacing=0)
    return n


def arrow(s, x, y, w=0.45, color=MUTED):
    a = rect(s, x, y, w, 0.28, color, shape=MSO_SHAPE.RIGHT_ARROW)
    return a


def notes(s, txt):
    s.notes_slide.notes_text_frame.text = txt


def bullets(items, bold_color=INK):
    """items: list of (bold, rest)"""
    return [[(b + " ", {"bold": True, "color": bold_color}), (r, {})] for b, r in items]


# ================================================================ 1. CAPA
s = prs.slides.add_slide(BLANK)
rect(s, 0, 0, 13.333, 7.5, WINE)
rect(s, 0, 0, 0.5, 7.5, RED)
pill(s, 1.15, 0.5, 3.4, "🔴  CLARO S.A. — ATIVAÇÃO SC", RED, WHITE, size=13, h=0.42)
text(s, 1.1, 1.05, 9, 1.0, "SPARC × Panteon", size=54, color=WHITE, bold=True)
text(s, 1.1, 2.15, 11, 0.6, "Decisão Estratégica: Eficiência Operacional, Modularidade e Redução de Custos",
     size=24, color=WHITE, bold=True)
text(s, 1.15, 2.95, 7.4, 3.3, [
    "Como simplificar a operação de campo, economizar recursos contratuais e blindar a entrega contra falhas?",
    "",
    [("✔ ", {"color": PINK, "bold": True}), ("Redução de Custos Contratuais: ", {"bold": True, "color": WHITE}),
     ("Roteador sai pronto da base Claro, permitindo abater item pago a terceiros.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Triagem Prévia de Hardware: ", {"bold": True, "color": WHITE}),
     ("Equipamento já sai testado na base; defeitos são substituídos antes da visita.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Operação 100% Offline (Sem Dependência de Link): ", {"bold": True, "color": WHITE}),
     ("Panteon trava se o link oscilar; SPARC entrega pronto e suporta ambos os modos.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Zero Investimento em Licenças e Servidores: ", {"bold": True, "color": WHITE}),
     ("Ativo 100% interno Claro, sem Capex/Opex de fornecedor externo engessado.", {})],
], size=14.5, color=WHITE, spacing=5)
if os.path.exists(LOGO):
    b = rect(s, 9.0, 3.1, 3.4, 2.3, WHITE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    s.shapes.add_picture(LOGO, Inches(9.35), Inches(3.35), width=Inches(2.7))
text(s, 1.15, 6.7, 11, 0.35,
     "Instalação Empresarial - Ativação  |  Regional Santa Catarina  |  george.calcmann@claro.com.br",
     size=12, color=PINK, bold=True)
notes(s, "Objetivo da apresentação: demonstrar à liderança executiva que o SPARC resolve o processo de ponta a ponta. "
         "Além de zerar licenças e servidores, traz triagem prévia de hardware, funciona 100% offline e permite desonerar contratos de terceiros.")


# ================================================================ 2. RESUMO EXECUTIVO
s = prs.slides.add_slide(BLANK)
header(s, "Resumo Executivo: A Decisão Estratégica em 30 Segundos")
cols = [
    ("ECONOMIA EM CONTRATOS",
     "Ao pré-configurar o roteador na base com a Assistência Técnica própria, retiramos a complexidade do terceiro e renegociamos o item de configuração pago por OS.",
     GREEN),
    ("HARDWARE TESTADO & OFFLINE",
     "Testa hardware na base (troca prévia se houver defeito). Opera 100% offline em bancada sem travar se o link do cliente estiver fora (e também opera online se desejado).",
     GREEN),
    ("ZERO LICENÇAS E SERVIDORES",
     "Sem taxas de licença por usuário, sem renovações bienais e sem compra de servidores dedicados. Código de propriedade da Claro pronto para escala imediata.",
     GREEN),
]
for i, (t, d, c) in enumerate(cols):
    x = 0.8 + i * 3.95
    rect(s, x, 1.55, 3.7, 2.25, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    rect(s, x, 1.55, 0.12, 2.25, c)
    text(s, x + 0.3, 1.7, 3.3, 0.45, t, size=16.5, color=WINE, bold=True)
    text(s, x + 0.3, 2.22, 3.25, 1.48, d, size=12.5)

rect(s, 0.8, 4.05, 5.75, 2.75, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s, 1.05, 4.15, 5.3, 0.4, "O Gargalo da Solução Externa (Panteon)", size=16, color=AMBER, bold=True)
text(s, 1.05, 4.65, 5.3, 2.05, bullets([
    ("Totalmente Dependente de Link Online:", "se a WAN/fibra tiver qualquer oscilação ou atraso, o Panteon trava e não consegue sequer iniciar o processo."),
    ("Sem Triagem Prévia de Hardware:", "descobre defeito físico do roteador na frente do cliente, gerando visita improdutiva."),
    ("Custo Recorrente e Servidores:", "licenças proprietárias por usuário ou renovações bienais, além de exigir +10 servidores internos."),
    ("Engessamento Operacional:", "obriga o técnico terceiro a configurar no campo, encarecendo o contrato de instalação."),
]), size=12, spacing=5)

r = rect(s, 6.8, 4.05, 5.73, 2.75, WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s, 7.05, 4.15, 5.3, 0.4, "Nossa Recomendação Prática", size=16, color=PINK, bold=True)
text(s, 7.05, 4.62, 5.3, 2.05, [
    [("1. ", {"bold": True}), ("Adotar o modelo SPARC Full na Assistência Técnica própria para entregar roteadores 100% testados.", {})],
    [("2. ", {"bold": True}), ("Disponibilizar o 'SPARC Tester' para os parceiros terceirizados apenas validarem a entrega física/velocidade.", {})],
    [("3. ", {"bold": True}), ("Renegociar o valor do item de configuração de roteador nos contratos das prestadoras de serviço.", {})],
    [("Resultado: ", {"bold": True, "color": PINK}), ("zero visita perdida por defeito de hardware, ativação ágil e economia recorrente.", {})],
], size=12, color=WHITE, spacing=5)
notes(s, "Mensagem executiva: o Panteon fica de mãos atadas quando a rede externa oscila. O SPARC desacopla a preparação: "
         "faz tudo offline na base com hardware testado, e ainda permite rodar online pelo link se for a opção escolhida.")


# ================================================================ 3. MODELO MODULAR (SPARC FULL vs SPARC TESTER)
s = prs.slides.add_slide(BLANK)
header(s, "Modularidade Operacional: O Modelo SPARC Full × SPARC Tester")

# Card Base Claro
rect(s, 0.8, 1.45, 5.75, 4.25, WHITE, line=WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t1 = rect(s, 0.8, 1.45, 5.75, 0.6, WINE)
text(s, 0, 0, 0, 0, "1. BASE CLARO: SPARC Full (Técnicos Próprios)", size=15, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t1)
text(s, 1.05, 2.15, 5.25, 3.45, [
    [("Quem opera: ", {"bold": True, "color": WINE}), ("Técnicos da Assistência Técnica interna da Claro.", {})],
    [("Onde: ", {"bold": True}), ("Bancada / Laboratório na base regional, 100% OFFLINE (sem depender de link).", {})],
    [("Triagem de Hardware: ", {"bold": True, "color": RED}), ("Testa portas, boot e placa. Se houver falha, substitui o equipamento na base antes de ir para a rua.", {})],
    [("O que faz: ", {"bold": True}), ("Lê o SAIP automaticamente, atualiza o firmware homologado e aplica a configuração completa.", {})],
    [("Tempo: ", {"bold": True}), ("Menos de 10 minutos por lote de roteadores.", {})],
    [("Resultado: ", {"bold": True, "color": GREEN}), ("Roteador sai 100% pronto, testado e etiquetado ('Plug & Play').", {})],
], size=12.5, spacing=7)

# Card Terceiros Campo
rect(s, 6.78, 1.45, 5.75, 4.25, WHITE, line=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t2 = rect(s, 6.78, 1.45, 5.75, 0.6, BLUE)
text(s, 0, 0, 0, 0, "2. CAMPO: SPARC Tester (Técnicos Terceirizados)", size=15, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t2)
text(s, 7.03, 2.15, 5.25, 3.45, [
    [("Quem opera: ", {"bold": True, "color": BLUE}), ("Empresas terceiras responsáveis pela fixação e instalação do acesso.", {})],
    [("O que muda: ", {"bold": True, "color": RED}), ("Retira do terceiro o acesso e a responsabilidade de configurar o roteador.", {})],
    [("O que é o SPARC Tester: ", {"bold": True}), ("Aplicativo simplificado e blindado para celular ou notebook, focado na entrega.", {})],
    [("O que ele faz: ", {"bold": True}), ("Checa porta de rede, valida link quando o circuito sobe, afere velocidade com Speedtest e gera laudo PDF.", {})],
    [("Versatilidade Híbrida: ", {"bold": True, "color": GREEN}), ("Permite também ativar online pelo link do cliente exatamente como o Panteon, se desejado.", {})],
    [("Impacto: ", {"bold": True, "color": GREEN}), ("Zero visita perdida por equipamento defeituoso e zero erro de configuração.", {})],
], size=12.5, spacing=7)

# Faixa inferior de ganho de negócio
c_gain = rect(s, 0.8, 5.85, 11.73, 1.05, GREEN_BG, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
text(s, 1.05, 5.95, 11.23, 0.85, [
    [("💰 Vantagem Financeira Estratégica: ", {"bold": True, "color": GREEN, "size": 13.5}),
     ("Como o parceiro NÃO precisa mais configurar o roteador no cliente, a Claro ganha respaldo direto para ", {"size": 12.5}),
     ("renegociar e abater o item contratual de configuração de CPE", {"bold": True, "color": INK, "size": 12.5}),
     (". Além disso, a substituição prévia na base elimina o custo de visitas improdutivas por hardware defeituoso!", {"size": 12.5})],
], size=12.5, anchor=MSO_ANCHOR.MIDDLE)

notes(s, "Explicar como a modularidade elimina o risco de campo: equipamento já sai testado (troca prévia de hardware) "
         "e o terceiro não mexe em configuração sensível, permitindo abater o valor do item de instalação no contrato.")


# ================================================================ 4. IMPACTO FINANCEIRO CONSOLIDADO
s = prs.slides.add_slide(BLANK)
header(s, "Impacto Financeiro Direto: Onde o SPARC Gera Economia para a Claro")

savings = [
    ("1. Redução Contratual de Terceiros",
     "Eliminação do custo de 'configuração de CPE' nas ordens de serviço executadas por parceiras externas.",
     "Economia por OS em escala nacional", GREEN),
    ("2. Custo Zero com Licenças",
     "Panteon exige licenças por usuário ou contratos bienais caros. O SPARC é ativo de propriedade da Claro: R$ 0.",
     "100% de economia em licenças de software", GREEN),
    ("3. Zero Investimento em Servidores",
     "Panteon exige estrutura própria e expansão para 10+ servidores dedicados. O SPARC roda localmente sem novos servidores.",
     "Evita Capex e Opex de infraestrutura", GREEN),
    ("4. Eliminação de Visitas Improdutivas",
     "Como o equipamento já é testado na base, elimina visitas perdidas por defeito de hardware ou link instável.",
     "Queda drástica de retrabalho e revisitas", GREEN),
]

for i, (t, d, sub, col) in enumerate(savings):
    x = 0.8 + (i % 2) * 5.98
    y = 1.5 + (i // 2) * 2.6
    rect(s, x, y, 5.75, 2.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    rect(s, x, y, 5.75, 0.12, col)
    text(s, x + 0.3, y + 0.25, 5.15, 0.45, t, size=16, color=WINE, bold=True)
    text(s, x + 0.3, y + 0.75, 5.15, 0.85, d, size=13)
    pill(s, x + 0.3, y + 1.7, 5.15, "✔  " + sub, GREEN_BG, GREEN, size=11, h=0.38)

notes(s, "Quatro alavancas concretas de economia: redução do contrato de terceiros, zero licenças, zero servidores novos "
         "e corte de visitas perdidas por hardware com defeito ou atraso de link.")


# ================================================================ 5. SPARC HOJE (FUNCIONALIDADES DESCOMPLICADAS)
s = prs.slides.add_slide(BLANK)
header(s, "SPARC Hoje: A Ferramenta Prática que Já Transforma o Campo")
kpis = [("-88%", "Tempo de Ativação", "De ~50 min para < 10 min"),
        ("100%", "Leitura Automática", "Zero digitação manual do SAIP"),
        ("100%", "Operação Offline", "Bancada sem depender de link"),
        ("2", "Plataformas Nativas", "Windows (.exe) + Android (.apk)")]
for i, (v, l, d) in enumerate(kpis):
    x = 0.8 + i * 2.97
    rect(s, x, 1.5, 2.75, 1.75, WHITE, line=PINK, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.25)
    text(s, x, 1.6, 2.75, 0.7, v, size=34, color=RED, bold=True, align=PP_ALIGN.CENTER)
    text(s, x, 2.32, 2.75, 0.35, l, size=13, bold=True, align=PP_ALIGN.CENTER)
    text(s, x, 2.68, 2.75, 0.35, d, size=11, color=MUTED, align=PP_ALIGN.CENTER)

feats = [
    ("📄 Leitor Inteligente do SAIP", "Extrai parâmetros diretamente de PDFs/TXTs da Claro. Sem digitação e sem margem para erro humano."),
    ("🧰 Bancada & Triagem de Hardware", "Testa portas, memória e boot na base Claro. Substitui roteador com defeito antes de ir ao cliente."),
    ("📦 Repositório Offline Embarcado", "Mantém firmwares homologados acessíveis localmente para gravação imediata sem depender de internet."),
    ("🧪 Testes e Certidão em PDF", "Valida internet (Speedtest) e circuito (Y.1564), gerando laudo formal na hora para anexar à OS."),
    ("🔌 Conexão Serial + Cabo de Rede", "Trabalha com cabo console e porta Ethernet simultaneamente pelo celular ou notebook."),
    ("🛡️ Gestão e Controle Seguro", "Ativação com proteção criptográfica individual vinculada ao dispositivo do técnico."),
]
for i, (t, d) in enumerate(feats):
    c_, r_ = i % 3, i // 3
    x, y = 0.8 + c_ * 3.95, 3.45 + r_ * 1.65
    rect(s, x, y, 3.7, 1.48, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, x + 0.2, y + 0.12, 3.3, 0.4, t, size=14, color=WINE, bold=True)
    text(s, x + 0.2, y + 0.55, 3.3, 0.85, d, size=12)
notes(s, "Funcionalidades descritas em termos de valor: leitura automática, triagem de hardware na base, "
         "repositório offline, laudo PDF e suporte multiplataforma.")


# ================================================================ 6. COMPARATIVO LADO A LADO
s = prs.slides.add_slide(BLANK)
header(s, "Comparativo Objetivo: SPARC × Panteon")
rows = [
    ("Investimento em Licenças", "Zero — Propriedade interna Claro", "g", "Alto — Licenças vitalícias ou bienais", "r"),
    ("Redução de Custo Contratual", "Sim — Permite modelo SPARC Tester", "g", "Não — Mantém terceiro configurando", "r"),
    ("Tolerância a Link Fora do Ar", "100% Offline — Roteador preparado na base", "g", "Trava — Exige link 100% online", "r"),
    ("Triagem Prévia de Hardware", "Sim — Substituição na base antes da visita", "g", "Não — Descobre falha na frente do cliente", "r"),
    ("Modo Online pelo Link do Cliente", "Sim — Funciona igual ao Panteon se desejado", "g", "Sim — Único modo suportado", "g"),
    ("Necessidade de Servidores Novos", "Zero — Processamento no dispositivo", "g", "Exige 1 + expansão para 10 servidores", "r"),
    ("Plataformas Suportadas", "Windows + Android", "g", "Somente Android", "r"),
    ("Interface de Teste de Campo", "Serial CLI + Cabo de Rede (Ethernet)", "g", "Somente Serial CLI", "r"),
    ("Homologação da Entrega", "Speedtest + Y.1564 com Laudo PDF", "g", "Exige servidores internos dedicados", "a"),
    ("Disponibilidade Operacional", "Imediata (Beta em uso na Regional)", "g", "Piloto travado em aprovação financeira", "r"),
    ("Integração com APIs Claro", "Em roadmap (tecnologia já modular)", "a", "Sim (já homologado)", "g"),
]
fills = {"g": (GREEN_BG, GREEN, "✔"), "r": (RED_BG, RED, "✖"), "a": (AMBER_BG, AMBER, "◐")}
x0, y0, rh = 0.8, 1.38, 0.45
cw = [3.9, 4.3, 3.53]
for j, h in enumerate(["Critério", "SPARC (Inovação Interna)", "Panteon (Fornecedor Externo)"]):
    xx = x0 + sum(cw[:j])
    c = rect(s, xx, y0, cw[j], 0.38, WINE if j != 1 else RED)
    text(s, 0, 0, 0, 0, h, size=12.5, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.LEFT if j == 0 else PP_ALIGN.CENTER)
for i, (crit, sv, sk, pv, pk) in enumerate(rows):
    y = y0 + 0.38 + i * rh
    rect(s, x0, y, sum(cw), rh, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s, x0 + 0.1, y, cw[0] - 0.15, rh, crit, size=11, bold=True, anchor=MSO_ANCHOR.MIDDLE)
    for k, (val, key) in enumerate([(sv, sk), (pv, pk)]):
        bg, fg, ic = fills[key]
        xx = x0 + sum(cw[:k + 1]) + 0.08
        c = rect(s, xx, y + 0.03, cw[k + 1] - 0.16, rh - 0.06, bg, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        text(s, 0, 0, 0, 0, f"{ic}  {val}", size=10.5, color=fg, bold=True, anchor=MSO_ANCHOR.MIDDLE,
             align=PP_ALIGN.CENTER, shape=c, spacing=0)

sg = sum(1 for r_ in rows if r_[2] == "g")
pg = sum(1 for r_ in rows if r_[4] == "g")
pill(s, 0.8, 6.45, 4.8, f"Vantagens Comerciais/Operacionais:  SPARC {sg}  ×  {pg}  Panteon", WINE, WHITE, size=11.5, h=0.38)
text(s, 5.8, 6.45, 6.8, 0.38, "✔ Atende com excelência   ◐ Em andamento/Depende de insumo   ✖ Não atende", size=11, color=MUTED)
notes(s, "Destacar: o SPARC não perde nada para o Panteon (faz o modo online pelo link igualzinho se necessário), "
         "mas oferece o que o Panteon não consegue: operação offline, triagem de hardware na base e desoneração contratual.")


# ================================================================ 7. CENÁRIOS DE CAMPO
s = prs.slides.add_slide(BLANK)
header(s, "Flexibilidade de Ativação: Onde Cada Solução Funciona na Prática")
scen = [
    ("🌐 Link Ativo no Cliente", "Circuito online: baixa firmware e valida direto pelo link (igual ao Panteon).", "✔", "✔"),
    ("🧰 Bancada na Base (Sem Link)", "Equipamento testado e preparado antes da visita por técnicos Claro. Roteador sai pronto.", "✔", "✖"),
    ("📲 Atualização pelo Dispositivo", "Técnico transfere firmware do próprio celular/notebook quando cliente não tem rede.", "✔", "✖"),
    ("🔴 Link Instável ou com Atraso", "Panteon TRAVA totalmente. O SPARC entrega o roteador pronto; sobe assim que a rede normalizar.", "✔", "✖"),
]
for i, (t, d, sp, pa) in enumerate(scen):
    x = 0.8 + i * 2.97
    rect(s, x, 1.5, 2.75, 2.9, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, x + 0.15, 1.62, 2.5, 0.45, t, size=14.5, color=WINE, bold=True)
    text(s, x + 0.15, 2.15, 2.5, 0.95, d, size=12)
    for k, (nm, v) in enumerate([("SPARC", sp), ("Panteon", pa)]):
        ok = v == "✔"
        pill(s, x + 0.2, 3.25 + k * 0.52, 2.35, f"{v}  {nm}",
             GREEN_BG if ok else RED_BG, GREEN if ok else RED, size=11.5, h=0.4)

text(s, 0.8, 4.65, 6, 0.35, "Compatibilidade e Meios de Conexão", size=16, color=WINE, bold=True)
matrix = [
    ("Cabo Console (Serial)", "✔ SPARC", "✔ Panteon", True, True),
    ("Cabo de Rede (Ethernet)", "✔ SPARC", "✖ Panteon", True, False),
    ("Celular / Tablet (Android)", "✔ SPARC", "✔ Panteon", True, True),
    ("Notebook / PC (Windows)", "✔ SPARC", "✖ Panteon", True, False),
]
for i, (nm, sp, pa, spok, paok) in enumerate(matrix):
    x = 0.8 + i * 2.97
    rect(s, x, 5.1, 2.75, 1.7, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, x, 5.2, 2.75, 0.35, nm, size=13.5, bold=True, align=PP_ALIGN.CENTER)
    pill(s, x + 0.2, 5.65, 2.35, sp, GREEN_BG if spok else RED_BG, GREEN if spok else RED, size=11, h=0.32)
    pill(s, x + 0.2, 6.05, 2.35, pa, GREEN_BG if paok else RED_BG, GREEN if paok else RED, size=11, h=0.32)
notes(s, "Se o link tiver qualquer atraso na entrega ou falha de porta na central, o Panteon trava o técnico no cliente. "
         "O SPARC permite que a visita física ocorra sem perda de tempo porque o roteador já está 100% preparado.")


# ================================================================ 8. MATRIZ ESTRATÉGICA
s = prs.slides.add_slide(BLANK)
header(s, "Matriz Estratégica: Inovação Interna (SPARC) vs. Fornecedor Externo")

dims = [
    ("Velocidade & Resiliência",
     "Funciona 100% offline em bancada ou online pelo link. Evolução contínua sem depender de fornecedor externo.",
     "Depende obrigatoriamente de link ativo na visita e fila de suporte do fornecedor externo.",
     "Zero paralisação por oscilação de rede"),
    ("Flexibilidade Orçamentária",
     "Zero licenças e zero servidores dedicados. Capacidade de modular (SPARC Full vs SPARC Tester) sob demanda.",
     "Cobrança recorrente por usuário ou pacotes bienais caros; qualquer mudança gera cobrança extra.",
     "Previsibilidade e economia contínua"),
    ("Redução Contratual",
     "Equipamento testado e pronto de base; desonera terceirizados e viabiliza redução do valor de OS.",
     "Mantém o modelo antigo, com terceiro cobrando caro pela configuração em campo.",
     "Redução direta do custo unitário por OS"),
    ("Domínio do Conhecimento",
     "Know-how retido 100% dentro da Claro. Triagem prévia de hardware feita pelos técnicos próprios.",
     "Dependência crônica de fornecedor terceiro, com risco de descontinuidade ou reajustes.",
     "Soberania técnica e controle de processos"),
]

cw = [2.2, 3.6, 3.6, 2.33]
x0, y0 = 0.8, 1.45
for j, h in enumerate(["Dimensão", "SPARC (Interno Claro)", "Fornecedor Externo (Panteon)", "Impacto no Negócio"]):
    xx = x0 + sum(cw[:j])
    c = rect(s, xx, y0, cw[j], 0.42, WINE if j == 0 else (GREEN if j == 1 else (RED if j == 2 else BLUE)))
    text(s, 0, 0, 0, 0, h, size=12.5, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.CENTER)

for i, (dm, sp, ext, imp) in enumerate(dims):
    y = y0 + 0.42 + i * 1.25
    rect(s, x0, y, sum(cw), 1.25, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s, x0 + 0.1, y, cw[0] - 0.2, 1.25, dm, size=12.5, bold=True, color=WINE, anchor=MSO_ANCHOR.MIDDLE)
    text(s, x0 + cw[0] + 0.1, y, cw[1] - 0.2, 1.25, sp, size=11, color=INK, anchor=MSO_ANCHOR.MIDDLE)
    text(s, x0 + sum(cw[:2]) + 0.1, y, cw[2] - 0.2, 1.25, ext, size=11, color=MUTED, anchor=MSO_ANCHOR.MIDDLE)
    text(s, x0 + sum(cw[:3]) + 0.1, y, cw[3] - 0.2, 1.25, imp, size=11, bold=True, color=GREEN, anchor=MSO_ANCHOR.MIDDLE)

notes(s, "Visão estratégica para liderança: capacidade de trabalhar offline, triagem antecipada de hardware, "
         "custo zero de licenças e alavancagem para renegociação de contratos de terceiros.")


# ================================================================ 9. LACUNAS E TRANSPARÊNCIA
s = prs.slides.add_slide(BLANK)
header(s, "Transparência Total: O Que Falta e Como Fechar Rapidamente")
gaps = [
    ("Script Completo da GER CPE",
     "Hoje o SPARC aplica o script base de conectividade e acesso remoto, e a GER CPE conclui remotamente (já funcional).",
     "Receber da GER CPE o layout padrão do script. O motor de automação do SPARC já está pronto.",
     "Rápido (dias)", GREEN),
    ("Acesso à API Corporativa",
     "Hoje o SPARC lê as ordens e parâmetros diretamente da ficha SAIP (PDF/TXT) sem erro humano.",
     "Liberar para o SPARC a mesma credencial de API já concedida ao Panteon para leitura direta de sistema.",
     "Médio", AMBER),
    ("Governança e Oficialização",
     "Hoje o SPARC opera como projeto inovador da Regional SC com versão beta estável.",
     "Oficializar em repositório corporativo Claro, revisão de Segurança da Informação e 2º mantenedor.",
     "Médio", AMBER),
    ("Perfil Modular 'SPARC Tester'",
     "Hoje a versão atual possui todas as ferramentas avançadas de bancada e engenharia.",
     "Gerar o perfil restrito/simplificado para distribuição segura e padronizada às parceiras terceirizadas.",
     "Em curso", BLUE),
]
cw = [2.4, 3.9, 3.9, 1.53]
for j, h in enumerate(["Item", "Situação Atual", "Plano de Fechamento", "Prazo / Esforço"]):
    xx = 0.8 + sum(cw[:j])
    c = rect(s, xx, y0, cw[j], 0.42, WINE)
    text(s, 0, 0, 0, 0, h, size=13, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c)
for i, (g, a, b, e, col) in enumerate(gaps):
    y = y0 + 0.42 + i * 1.22
    rect(s, 0.8, y, sum(cw), 1.22, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s, 0.85, y, cw[0] - 0.1, 1.22, g, size=12.5, bold=True, color=WINE, anchor=MSO_ANCHOR.MIDDLE)
    text(s, 0.8 + cw[0], y, cw[1] - 0.1, 1.22, a, size=11, anchor=MSO_ANCHOR.MIDDLE)
    text(s, 0.8 + cw[0] + cw[1], y, cw[2] - 0.1, 1.22, b, size=11, anchor=MSO_ANCHOR.MIDDLE)
    pill(s, 0.8 + sum(cw[:3]) + 0.12, y + 0.43, cw[3] - 0.24, e, col, WHITE, size=11, h=0.36)
notes(s, "As lacunas não são limitações da arquitetura. São liberações operacionais e de insumo interno.")


# ================================================================ 10. PLANO DE AÇÃO E PRÓXIMOS PASSOS
s = prs.slides.add_slide(BLANK)
header(s, "Plano de Ação: O Que Propomos para a Regional e a Claro")
asks = [
    ("1", "Aprovar o Modelo Modular (Full + Tester)",
     "Implementar a triagem de hardware e preparo prévio na Assistência Técnica da Regional SC e distribuir o SPARC Tester aos parceiros."),
    ("2", "Intermediar Insumos com a GER CPE",
     "Obter o layout do script completo e a liberação da API corporativa já compartilhada com o Panteon."),
    ("3", "Revisão e Renegociação Contratual",
     "Abrir estudo com Suprimentos/Contratos para abatimento do item de configuração de roteador nas faturas de parceiras terceirizadas."),
    ("4", "Oficialização Corporativa do SPARC",
     "Integrar aos canais oficiais da Claro com homologação de Segurança da Informação e expansão para as demais regionais."),
]
for i, (n, t, d) in enumerate(asks):
    y = 1.45 + i * 1.02
    circ = rect(s, 0.8, y + 0.08, 0.7, 0.7, RED, shape=MSO_SHAPE.OVAL)
    text(s, 0, 0, 0, 0, n, size=22, color=WHITE, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=circ)
    rect(s, 1.7, y, 6.3, 0.88, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s, 1.85, y + 0.04, 6.0, 0.38, t, size=14.5, bold=True, color=WINE)
    text(s, 1.85, y + 0.42, 6.0, 0.45, d, size=11.5)

rect(s, 8.3, 1.45, 4.23, 3.94, WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s, 8.5, 1.58, 3.9, 0.4, "Ganhos Imediatos para a Claro", size=16, color=PINK, bold=True)
tl = [
    ("Economia Contratual", "Abatimento de valor por OS nas faturas terceirizadas."),
    ("Zero Equipamento Defeituoso", "Triagem prévia na base Claro substitui hardware antes do campo."),
    ("Zero Dependência de Link", "Opera 100% offline em bancada ou online pelo link do cliente."),
    ("Zero Capex / Licenças", "Ativo próprio da Claro sem taxas de fornecedor externo."),
]
for i, (w, d) in enumerate(tl):
    y = 2.05 + i * 0.82
    text(s, 8.5, y, 3.9, 0.32, "✔ " + w, size=13, color=WHITE, bold=True)
    text(s, 8.5, y + 0.3, 3.9, 0.45, d, size=11, color=PINK)

c = rect(s, 0.8, 5.65, 11.73, 1.15, RED, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s, 0, 0, 0, 0, [
    [("Decisão Estratégica Inteligente: ", {"bold": True, "size": 14}),
     ("O SPARC faz tudo o que o Panteon faz (inclusive ativação online pelo link), mas entrega muito mais: ", {"size": 13}),
     ("preparo 100% offline, triagem prévia de hardware, eliminação de licenças e redução direta de custos nos contratos de instalação terceirizados.", {"bold": True, "size": 13})]],
    size=13, color=WHITE, anchor=MSO_ANCHOR.MIDDLE, align=PP_ALIGN.CENTER, shape=c)
notes(s, "Encerramento reforçando a soberania técnica e a economia em escala nacional.")

prs.save(OUT)
print("Apresentação atualizada com sucesso em:", OUT)
