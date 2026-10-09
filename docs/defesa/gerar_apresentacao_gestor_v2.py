# -*- coding: utf-8 -*-
"""
Gera a Apresentação Executiva SPARC x Panteon (11 Slides Unificados)
Voltada para apresentação a Diretores, Gerentes Executivos e Liderança Corporativa da Claro.
Tom: Diplomático, objetivo, técnico e focado em valor estratégico, governança e eficiência financeira.

Uso: python gerar_apresentacao_gestor_v2.py
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
INK = RGBColor(0x0F, 0x17, 0x2A)         # Azul Ardósia Muito Escuro / Quase Preto (#0F172A)
MUTED = RGBColor(0x47, 0x55, 0x69)       # Cinza Médio (#475569)
LINE = RGBColor(0xE2, 0xE8, 0xF0)        # Borda Suave (#E2E8F0)
GREEN = RGBColor(0x16, 0xA3, 0x4A)       # Verde Sucesso (#16A34A)
GREEN_BG = RGBColor(0xDC, 0xFC, 0xE7)    # Verde Claro Fundo (#DCFCE7)
AMBER = RGBColor(0xD9, 0x77, 0x06)       # Âmbar Atenção (#D97706)
AMBER_BG = RGBColor(0xFE, 0xF3, 0xC7)    # Âmbar Claro Fundo (#FEF3C7)
BLUE = RGBColor(0x02, 0x84, 0xC7)        # Azul Destaque (#0284C7)
BLUE_BG = RGBColor(0xE0, 0xF2, 0xFE)     # Azul Claro Fundo (#E0F2FE)
PINK = RGBColor(0xFA, 0xE8, 0xEB)        # Rosa/Salmão Suave Claro (#FAE8EB)
FONT = "Segoe UI"

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "SPARC_vs_Panteon_Apresentacao_Executiva.pptx")
LOGO = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo_full.png"
if not os.path.exists(LOGO):
    LOGO = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo.png"

prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)
BLANK = prs.slide_layouts[6]

# ----------------------------------------------------------------- Funções Utilitárias
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
        sh.adjustments[0] = 0.06
    return sh

def text(s, x, y, w, h, runs, size=14, color=INK, bold=False, align=PP_ALIGN.LEFT,
         anchor=MSO_ANCHOR.TOP, shape=None, spacing=4):
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

def header(s, title, kicker="CLARO S.A. — ATIVAÇÃO SC · INOVAÇÃO OPERACIONAL"):
    rect(s, 0, 0, 13.333, 7.5, BG)
    rect(s, 0, 0, 13.333, 1.15, WINE)
    rect(s, 0, 1.15, 13.333, 0.06, RED)
    text(s, 0.8, 0.12, 10, 0.3, "🔴 " + kicker.upper(), size=11, color=PINK, bold=True)
    text(s, 0.8, 0.42, 11.7, 0.65, title, size=24, color=WHITE, bold=True)
    rect(s, 0, 7.05, 13.333, 0.45, RGBColor(0xF1, 0xF5, 0xF9))
    text(s, 0.8, 7.1, 11.7, 0.3,
         "SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro  |  george.calcmann@claro.com.br",
         size=10, color=MUTED)

def pill(s, x, y, w, label, fill, color, size=11, h=0.34):
    p = rect(s, x, y, w, h, fill, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    p.adjustments[0] = 0.5
    text(s, 0, 0, 0, 0, label, size=size, color=color, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=p)
    return p

def notes(s, txt):
    s.notes_slide.notes_text_frame.text = txt


# ==============================================================================
# SLIDE 1: CAPA EXECUTIVA — SPARC
# ==============================================================================
s1 = prs.slides.add_slide(BLANK)
rect(s1, 0, 0, 13.333, 7.5, WINE)
rect(s1, 0, 0, 0.45, 7.5, RED)

pill(s1, 1.15, 0.5, 3.8, "🔴  CLARO S.A. — ATIVAÇÃO SC", RED, WHITE, size=13, h=0.42)
text(s1, 1.1, 1.05, 10, 1.0, "SPARC", size=54, color=WHITE, bold=True)
text(s1, 1.1, 2.15, 11, 0.5, "Sistema de Provisionamento e Ativação de Roteadores Claro",
     size=22, color=PINK, bold=True)

text(s1, 1.15, 2.8, 7.6, 3.6, [
    [("Plataforma autônoma para apoio técnico em processos de Ativações Empresariais B2B de campo, ", {"size": 14}),
     ("focada em eficiência operacional, custo marginal zero, alta adaptabilidade e escalabilidade.", {"bold": True, "size": 14})],
    "",
    [("✔ ", {"color": PINK, "bold": True}), ("Recuperação de roteadores: ", {"bold": True, "color": WHITE}),
     ("Restauração ágil de senhas, configs legadas e firmwares corrompidos.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Upgrades online e locais de firmwares: ", {"bold": True, "color": WHITE}),
     ("Servidores TFTP/HTTP integrados sem softwares adicionais.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Parametrização automatizada: ", {"bold": True, "color": WHITE}),
     ("Importação direta de dados técnicos do SAIP (PDF/TXT) com zero erro manual.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Homologação técnica de serviços: ", {"bold": True, "color": WHITE}),
     ("Auditoria automática de conectividade e emissão de laudo formal.", {})],
    "",
    [("Foco Estratégico: ", {"bold": True, "color": PINK}),
     ("Preenchimento de lacunas técnicas em campo, forte sinergia com o projeto ", {}),
     ("Visita Única", {"bold": True, "color": WHITE}),
     (" e flexibilização contínua de capacitação.", {})]
], size=13, color=WHITE, spacing=4)

if os.path.exists(LOGO):
    rect(s1, 9.1, 3.0, 3.4, 2.4, WHITE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    s1.shapes.add_picture(LOGO, Inches(9.35), Inches(3.25), width=Inches(2.9))

text(s1, 1.15, 6.7, 11, 0.35,
     "Instalação Empresarial - Ativação  |  Regional Santa Catarina  |  george.calcmann@claro.com.br",
     size=12, color=PINK, bold=True)

notes(s1, "Apresentação Executiva do SPARC: Destacar à liderança que se trata de uma plataforma in-house da Claro, "
          "já funcional em Santa Catarina, construída para mitigar a dependência de capacitação intermediária de campo, "
          "agilizar a entrega do projeto Visita Única e preparar o terreno para a economia em escala.")


# ==============================================================================
# SLIDE 2: CONTEXTO OPERACIONAL (MANUAL VS ESTEIRA SPARC)
# ==============================================================================
s2 = prs.slides.add_slide(BLANK)
header(s2, "Contexto Operacional: Transformando a Ativação de Circuitos B2B",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC BETA")

# Card Esquerdo: Processo Manual Anterior
rect(s2, 0.8, 1.45, 5.75, 5.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
t_m = rect(s2, 0.8, 1.45, 5.75, 0.55, RGBColor(0x99, 0x1B, 0x1B))
text(s2, 0, 0, 0, 0, "❌ Processo Manual Anterior", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_m)

text(s2, 1.05, 2.15, 5.25, 4.45, [
    [("• Diálogo Técnico Complexo: ", {"bold": True, "color": RGBColor(0x99, 0x1B, 0x1B)}),
     ("Fricção e tempo elevado na comunicação entre o suporte CLARO e o técnico de campo.", {})],
    [("• Tempo Médio Elevado: ", {"bold": True, "color": INK}),
     ("Procedimentos manuais de atualização e testes consumiam ~50 minutos por roteador.", {})],
    [("• Fricção com Senhas Desconhecidas: ", {"bold": True, "color": INK}),
     ("Necessidade de resets forçados via sequências manuais complexas em bootloader.", {})],
    [("• Dispersão de Ferramentas: ", {"bold": True, "color": INK}),
     ("Operador dependia de PuTTY, TFTPD32, calculadoras de IP, scripts soltos e múltiplos arquivos.", {})],
    [("• Risco de Inconsistência Manual: ", {"bold": True, "color": INK}),
     ("Digitação de máscaras, rotas e IPs WAN/LAN sujeita a erro humano e revisitas.", {})],
    [("• Validação e Ajustes Manuais: ", {"bold": True, "color": INK}),
     ("Ajustes manuais no Windows, testes manuais de banda e validação empírica de conectividade.", {})],
], size=12.5, spacing=7)

# Card Direito: Solução com a Esteira SPARC
rect(s2, 6.78, 1.45, 5.75, 5.35, WHITE, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t_s = rect(s2, 6.78, 1.45, 5.75, 0.55, GREEN)
text(s2, 0, 0, 0, 0, "⚡ Solução com a Esteira SPARC", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_s)

text(s2, 7.03, 2.15, 5.25, 4.45, [
    [("• Intermediação Eficiente: ", {"bold": True, "color": GREEN}),
     ("Automação guiada que simplifica a interface entre o analista Claro e o operador de campo.", {})],
    [("• Agilidade Comprovada: ", {"bold": True, "color": INK}),
     ("Provisionamento completo em bancada ou campo em menos de 10 minutos com poucos cliques.", {})],
    [("• Recuperação Automatizada: ", {"bold": True, "color": INK}),
     ("Desbloqueio de roteadores, limpeza de configurações residuais e suporte a firmwares corrompidos.", {})],
    [("• Servidor TFTP/HTTP 100% Integrado: ", {"bold": True, "color": INK}),
     ("Streaming nativo embarcado no aplicativo, eliminando a dependência de softwares paralelos.", {})],
    [("• Importação Direta do SAIP: ", {"bold": True, "color": INK}),
     ("Extração automática de parâmetros a partir de PDF/TXT, eliminando erros de digitação.", {})],
    [("• Auditoria e Testes Automáticos: ", {"bold": True, "color": INK}),
     ("Configura placa de rede automaticamente, testa ICMP, Telnet/SSH e homologa a banda.", {})],
], size=12.5, spacing=7)

notes(s2, "Mostrar o contraste operacional: o processo anterior era lento, fragmentado e gerava chamados de apoio. "
          "A esteira SPARC unifica todo o ciclo em um fluxo digital automatizado com menos de 10 minutos.")


# ==============================================================================
# SLIDE 3: IMPACTO OPERACIONAL E MÉTRICAS
# ==============================================================================
s3 = prs.slides.add_slide(BLANK)
header(s3, "Impacto Operacional e Métricas de Produtividade",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC BETA")

kpis_s3 = [
    ("-88%", "Redução de Tempo", "De ~50 min para < 10 min por roteador", RED),
    ("100%", "Aderência Padrão Claro", "Zero falhas de digitação em IPs, rotas e dados", GREEN),
    ("1 App", "Solução Portátil", "Substitui PuTTY, TFTPD32, scripts e calculadoras", BLUE),
    ("2", "Plataformas Nativas", "Operação híbrida em Windows (.exe) e Android (.apk)", WINE),
]
for i, (val, tit, desc, cor) in enumerate(kpis_s3):
    x = 0.8 + i * 2.98
    rect(s3, x, 1.45, 2.76, 2.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    rect(s3, x, 1.45, 2.76, 0.1, cor)
    text(s3, x, 1.6, 2.76, 0.75, val, size=38, color=cor, bold=True, align=PP_ALIGN.CENTER)
    text(s3, x + 0.1, 2.4, 2.56, 0.35, tit, size=13.5, bold=True, align=PP_ALIGN.CENTER)
    text(s3, x + 0.1, 2.8, 2.56, 0.85, desc, size=11, color=MUTED, align=PP_ALIGN.CENTER)

# Card Benefícios Estratégicos
rect(s3, 0.8, 4.05, 11.733, 2.75, WHITE, line=WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.25)
t_b = rect(s3, 0.8, 4.05, 11.733, 0.5, WINE)
text(s3, 0, 0, 0, 0, "💡 Benefícios Estratégicos para a Operação Claro S.A.", size=14, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_b)

text(s3, 1.05, 4.7, 11.23, 1.95, [
    [("• Escalabilidade de Ativação (Modo Bancada): ", {"bold": True, "color": WINE}),
     ("Possibilidade de trabalho em laboratório regional, onde um único técnico prepara dezenas de roteadores diariamente com alto padrão de qualidade, entregando os equipamentos pré-configurados para as equipes de campo, ou realizando o processo completo in loco conforme a necessidade.", {})],
    "",
    [("• Facilidade de Uso e Mitigação de Treinamento: ", {"bold": True, "color": WINE}),
     ("Interface visual intuitiva com indicação em tempo real de progresso e status, permitindo que operadores executem procedimentos de alta complexidade com segurança e rapidez.", {})],
    "",
    [("• Maximização do Projeto Visita Única: ", {"bold": True, "color": WINE}),
     ("Padronização técnica rigorosa que elimina retrabalho, reduz cancelamentos de ativação e garante conformidade na entrega inicial.", {})],
], size=12.5, spacing=4)

notes(s3, "Métricas concretas: 88% de redução de tempo, conformidade total do SAIP e eliminação de ferramentas dispersas. "
          "O modo bancada é a grande chave para a desoneração operacional.")


# ==============================================================================
# SLIDE 4: ROADMAP DE EVOLUÇÃO
# ==============================================================================
s4 = prs.slides.add_slide(BLANK)
header(s4, "Roadmap de Evolução: Da Ativação Local à Integração Corporativa",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC BETA")

# Card Esquerdo: Andamento Atual
rect(s4, 0.8, 1.45, 5.75, 5.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
t_and = rect(s4, 0.8, 1.45, 5.75, 0.55, BLUE)
text(s4, 0, 0, 0, 0, "🚀 Roadmap de Evolução — Entregas em Andamento", size=14, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_and)

text(s4, 1.05, 2.15, 5.25, 4.45, [
    [("📱 SPARC Móvel Android (75% concluído): ", {"bold": True, "color": BLUE}),
     ("Em processo de refinamento operacional. Permite atuação completa com smartphone corporativo.", {})],
    "",
    [("☁️ Repositório Online Homologado: ", {"bold": True, "color": BLUE}),
     ("Elimina a gestão manual de arquivos pelos técnicos. Totalmente funcional e em fase de homologação.", {})],
    "",
    [("🧪 Aferições Avançadas & Certidões (80% concluído): ", {"bold": True, "color": BLUE}),
     ("Certidões de entrega sem necessidade de aparelhos dedicados caros (JDSU/Viavi), com loop lógico e testes via LAN com um clique.", {})],
    "",
    [("🔧 Funções de Assistência e Manutenção (70% concluído): ", {"bold": True, "color": BLUE}),
     ("Rotinas adicionais para equipamentos ativos, atualizações periódicas e recuperação de equipamentos em laboratório.", {})],
], size=12.5, spacing=6)

# Card Direito: Projeção Futura
rect(s4, 6.78, 1.45, 5.75, 5.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
t_proj = rect(s4, 6.78, 1.45, 5.75, 0.55, WINE)
text(s4, 0, 0, 0, 0, "🎯 Roadmap de Evolução — Projeção & Integrações", size=14, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_proj)

text(s4, 7.03, 2.15, 5.25, 4.45, [
    [("🔗 Integração com Sistemas Corporativos Claro: ", {"bold": True, "color": WINE}),
     ("A partir do identificador da Ordem de Serviço (OS), consulta automática de parâmetros técnicos, baixa de status e atualização sistêmica em tempo real.", {})],
    "",
    [("🔐 Acesso a Banco de Credenciais Protegido: ", {"bold": True, "color": WINE}),
     ("Consulta segura a cofres de senhas corporativos sem necessidade de divulgação ao operador de campo.", {})],
    "",
    [("🌐 Expansão para Equipamentos de Acesso: ", {"bold": True, "color": WINE}),
     ("Inclusão de suporte automatizado para switches e dispositivos de rede de diferentes portes e fabricantes.", {})],
    "",
    [("🏢 Adoção Institucional em Escala: ", {"bold": True, "color": WINE}),
     ("Disponibilização da plataforma para as demais regionais Claro sob diretrizes de governança corporativa.", {})],
], size=12.5, spacing=6)

notes(s4, "Apresentar a maturidade do roadmap: 75% a 80% dos módulos avançados já estão desenvolvidos, "
          "e as projeções futuras contemplam a integração com os sistemas corporativos Claro e a governança central.")


# ==============================================================================
# SLIDE 5: CAPA DE TRANSIÇÃO COMPARATIVA — SPARC X PANTEON
# ==============================================================================
s5 = prs.slides.add_slide(BLANK)
rect(s5, 0, 0, 13.333, 7.5, WINE)
rect(s5, 0, 0, 0.45, 7.5, RED)

pill(s5, 1.15, 0.5, 3.8, "🔴  CLARO S.A. — ATIVAÇÃO SC", RED, WHITE, size=13, h=0.42)
text(s5, 1.1, 1.05, 10, 1.0, "SPARC × Panteon", size=54, color=WHITE, bold=True)
text(s5, 1.1, 2.15, 11, 0.5, "Decisão Estratégica: Eficiência Operacional, Modularidade e Racionalização de Custos",
     size=22, color=PINK, bold=True)

text(s5, 1.15, 2.8, 11.2, 3.6, [
    [("Como simplificar a operação de campo, economizar recursos contratuais e blindar a entrega contra falhas?", {"bold": True, "size": 15})],
    "",
    [("✔ ", {"color": PINK, "bold": True}), ("Racionalização de Custos Contratuais: ", {"bold": True, "color": WHITE}),
     ("Roteador já sai preparado da base Claro, permitindo rever a necessidade e o valor de remuneração de configuração nas ordens de serviço terceirizadas.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Triagem Preventiva de Hardware: ", {"bold": True, "color": WHITE}),
     ("O equipamento é testado previamente na base; falhas físicas de portas e boot são identificadas e substituídas antes do deslocamento ao cliente.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Operação Híbrida e Resiliente (Modo Offline e Online): ", {"bold": True, "color": WHITE}),
     ("Soluções de mercado dependem de link ativo em campo; o SPARC entrega pronto em bancada (100% offline) e também suporta ativação online pelo link se desejado.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Eficiência em Licenciamento e Infraestrutura: ", {"bold": True, "color": WHITE}),
     ("Ativo desenvolvido internamente na Claro, sem custos recorrentes de licenças de terceiros e sem necessidade de alocação de servidores dedicados de grande porte.", {})],
], size=13.5, color=WHITE, spacing=6)

text(s5, 1.15, 6.7, 11, 0.35,
     "Instalação Empresarial - Ativação  |  Regional Santa Catarina  |  george.calcmann@claro.com.br",
     size=12, color=PINK, bold=True)

notes(s5, "Transição para o comparativo com a solução de mercado: usar tom equilibrado. "
          "O Panteon atende ao modelo tradicional em campo com link, mas o SPARC oferece uma abordagem operacional "
          "superior: triagem prévia na base, preparo offline e desoneração contratual de terceiros.")


# ==============================================================================
# SLIDE 6: MODULARIDADE OPERACIONAL (FULL VS TESTER)
# ==============================================================================
s6 = prs.slides.add_slide(BLANK)
header(s6, "Modularidade Operacional: O Modelo SPARC Full × SPARC Tester",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

# Card Base Claro
rect(s6, 0.8, 1.45, 5.75, 4.3, WHITE, line=WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t_full = rect(s6, 0.8, 1.45, 5.75, 0.55, WINE)
text(s6, 0, 0, 0, 0, "1. BASE CLARO: SPARC Full (Técnicos Próprios)", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_full)

text(s6, 1.05, 2.15, 5.25, 3.45, [
    [("Quem opera: ", {"bold": True, "color": WINE}), ("Técnicos da Assistência Técnica interna da Claro.", {})],
    [("Onde: ", {"bold": True}), ("Bancada / Laboratório na base regional (100% offline, sem depender de link externo).", {})],
    [("Triagem de Hardware: ", {"bold": True, "color": RED}), ("Testa portas, boot e integridade da placa. Falhas são substituídas na base antes da visita ao cliente.", {})],
    [("O que faz: ", {"bold": True}), ("Lê os dados técnicos do SAIP automaticamente, aplica firmware homologado e grava a configuração completa.", {})],
    [("Tempo: ", {"bold": True}), ("Menos de 10 minutos por equipamento.", {})],
    [("Resultado: ", {"bold": True, "color": GREEN}), ("Roteador sai 100% pronto, testado e etiquetado ('Plug & Play').", {})],
], size=12.5, spacing=7)

# Card Parceiros Campo
rect(s6, 6.78, 1.45, 5.75, 4.3, WHITE, line=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t_tst = rect(s6, 6.78, 1.45, 5.75, 0.55, BLUE)
text(s6, 0, 0, 0, 0, "2. CAMPO: SPARC Tester (Técnicos Parceiros)", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_tst)

text(s6, 7.03, 2.15, 5.25, 3.45, [
    [("Quem opera: ", {"bold": True, "color": BLUE}), ("Empresas parceiras responsáveis pela fixação física e conexão do acesso.", {})],
    [("O que muda: ", {"bold": True, "color": INK}), ("Desonera o técnico terceiro da tarefa complexa de configuração do roteador.", {})],
    [("O que é o SPARC Tester: ", {"bold": True}), ("Aplicativo simplificado e orientado à entrega para celular ou notebook.", {})],
    [("O que ele faz: ", {"bold": True}), ("Checa porta de rede, valida link quando o circuito sobe, afere velocidade com Speedtest e gera laudo PDF.", {})],
    [("Versatilidade Híbrida: ", {"bold": True, "color": GREEN}), ("Permite também ativar online pelo link do cliente exatamente como o modelo tradicional, se desejado.", {})],
    [("Impacto: ", {"bold": True, "color": GREEN}), ("Zero visita perdida por equipamento defeituoso e zero erro de configuração.", {})],
], size=12.5, spacing=7)

# Faixa de Vantagem Financeira
c_vf = rect(s6, 0.8, 5.9, 11.733, 1.0, GREEN_BG, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
text(s6, 1.05, 5.95, 11.23, 0.9, [
    [("💰 Vantagem Financeira Estratégica: ", {"bold": True, "color": GREEN, "size": 13}),
     ("Como o parceiro de campo NÃO precisa executar a configuração do roteador no cliente, a Claro ganha respaldo técnico direto para ", {"size": 12.5}),
     ("renegociar e abater o item contratual de configuração de CPE por OS", {"bold": True, "color": INK, "size": 12.5}),
     (". Além disso, a substituição prévia na base elimina o custo operacional de visitas improdutivas por hardware danificado!", {"size": 12.5})],
], size=12.5, anchor=MSO_ANCHOR.MIDDLE)

notes(s6, "Este é o slide de maior peso para a liderança: explicar que o modelo modular (Full + Tester) cria uma alavanca "
          "financeira direta com terceiros e blinda a operação de campo.")


# ==============================================================================
# SLIDE 7: IMPACTO FINANCEIRO DIRETO
# ==============================================================================
s7 = prs.slides.add_slide(BLANK)
header(s7, "Impacto Financeiro Direto: Onde o SPARC Gera Otimização para a Claro",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

fin_cards = [
    ("1. Otimização em Contratos de Terceiros",
     "Readequação do item de 'configuração de CPE' nas ordens de serviço executadas por parceiras externas, simplificando o escopo de campo.",
     "Economia escalável por OS em âmbito nacional", GREEN),
    ("2. Custo Marginal Zero de Licenças",
     "Soluções de fornecedores externos envolvem taxas periódicas de licenciamento de software. O SPARC é um ativo proprietário da Claro.",
     "100% de previsibilidade sem custos de licença", GREEN),
    ("3. Eficiência em Infraestrutura & Servidores",
     "Modelos centralizados demandam expansão contínua de clusters de servidores dedicados. O SPARC processa no dispositivo local (Edge Computing).",
     "Evita novos custos de Capex e Opex de infraestrutura", GREEN),
    ("4. Mitigação de Visitas Improdutivas",
     "Com a pré-triagem de hardware na base Claro, elimina-se o retrabalho decorrente de falha física do equipamento no momento da visita.",
     "Queda expressiva de cancelamentos e reatendimentos", GREEN),
]

for i, (tit, desc, sub, cor) in enumerate(fin_cards):
    x = 0.8 + (i % 2) * 5.98
    y = 1.45 + (i // 2) * 2.65
    rect(s7, x, y, 5.75, 2.45, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    rect(s7, x, y, 5.75, 0.1, cor)
    text(s7, x + 0.3, y + 0.22, 5.15, 0.45, tit, size=15.5, color=WINE, bold=True)
    text(s7, x + 0.3, y + 0.72, 5.15, 0.95, desc, size=12.5)
    pill(s7, x + 0.3, y + 1.85, 5.15, "✔  " + sub, GREEN_BG, GREEN, size=11, h=0.38)

notes(s7, "Apresentar as 4 alavancas de eficiência de forma clara: contratos de terceiros, ausência de novas licenças, "
          "aproveitamento da infraestrutura existente e eliminação de visitas perdidas.")


# ==============================================================================
# SLIDE 8: SPARC HOJE (FERRAMENTA PRÁTICA)
# ==============================================================================
s8 = prs.slides.add_slide(BLANK)
header(s8, "SPARC Hoje: A Ferramenta Prática que Já Transforma o Campo",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

kpis_s8 = [
    ("-88%", "Tempo de Ativação", "De ~50 min para < 10 min por CPE"),
    ("100%", "Leitura Automática", "Zero digitação manual do SAIP"),
    ("100%", "Operação Offline", "Bancada sem depender de link externo"),
    ("2", "Plataformas Nativas", "Windows (.exe) + Android (.apk)")
]
for i, (v, l, d) in enumerate(kpis_s8):
    x = 0.8 + i * 2.97
    rect(s8, x, 1.45, 2.75, 1.75, WHITE, line=PINK, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.25)
    text(s8, x, 1.55, 2.75, 0.7, v, size=34, color=RED, bold=True, align=PP_ALIGN.CENTER)
    text(s8, x, 2.27, 2.75, 0.35, l, size=13, bold=True, align=PP_ALIGN.CENTER)
    text(s8, x, 2.62, 2.75, 0.35, d, size=11, color=MUTED, align=PP_ALIGN.CENTER)

feats_s8 = [
    ("📄 Leitor Inteligente do SAIP", "Extrai parâmetros diretamente de PDFs/TXTs da Claro. Sem digitação e sem margem para erro humano."),
    ("🧰 Bancada & Triagem de Hardware", "Testa portas, memória e boot na base Claro. Substitui roteador com defeito antes de ir ao cliente."),
    ("📦 Repositório Offline Embarcado", "Mantém firmwares homologados acessíveis localmente para gravação imediata sem depender de internet."),
    ("🧪 Testes e Certidão em PDF", "Valida internet (Speedtest) e circuito (Y.1564), gerando laudo formal na hora para anexar à OS."),
    ("🔌 Conexão Serial + Cabo de Rede", "Trabalha com cabo console e porta Ethernet simultaneamente pelo celular ou notebook."),
    ("🛡️ Gestão e Controle Seguro", "Ativação com proteção criptográfica individual vinculada ao dispositivo do técnico, controle online de versões."),
]
for i, (t, d) in enumerate(feats_s8):
    c_, r_ = i % 3, i // 3
    x, y = 0.8 + c_ * 3.95, 3.4 + r_ * 1.7
    rect(s8, x, y, 3.7, 1.52, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s8, x + 0.2, y + 0.12, 3.3, 0.4, t, size=13.5, color=WINE, bold=True)
    text(s8, x + 0.2, y + 0.55, 3.3, 0.88, d, size=11.5)

notes(s8, "Reforçar que o SPARC não é uma ideia teórica ou protótipo: já é uma ferramenta prática, testada e em operação "
          "que entrega resultados mensuráveis no dia a dia.")


# ==============================================================================
# SLIDE 9: MATRIZ ESTRATÉGICA (SPARC VS SOLUÇÃO EXTERNA)
# ==============================================================================
s9 = prs.slides.add_slide(BLANK)
header(s9, "Matriz Estratégica: Inovação Interna (SPARC) vs. Solução Externa de Mercado",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

dims = [
    ("Resiliência & Conectividade",
     "Arquitetura híbrida e flexível: funciona 100% offline em bancada (Plug & Play) e online pelo link em campo. Evolução contínua sob demanda.",
     "Modelo tradicional focado na visita em campo; depende de link ativo no cliente e fila de suporte externa.",
     "Zero paralisação por oscilação de rede externa"),
    ("Flexibilidade Orçamentária",
     "Ativo proprietário da Claro com custo marginal zero de licença de software e aproveitamento de ativos existentes (Edge Computing).",
     "Modelo convencional de licenciamento por usuário/período e necessidade de servidores dedicados adicionais.",
     "Previsibilidade orçamentária e redução de TCO"),
    ("Estrutura Contratual de Campo",
     "Equipamento sai testado e pronto da base; desonera a complexidade técnica do terceiro e viabiliza redução do valor de OS.",
     "Mantém a complexidade de configuração na ponta, conservando o modelo de custo contratual de terceiros.",
     "Redução direta do custo unitário por OS"),
    ("Domínio Tecnológico & Agilidade",
     "Capital intelectual retido 100% dentro da Claro. Triagem preventiva de hardware e evolução alinhada à cultura interna.",
     "Dependência de roadmap e ciclos de desenvolvimento de fornecedor externo para atendimento de demandas locais.",
     "Soberania técnica e controle de processos"),
]

cw = [2.2, 3.6, 3.6, 2.33]
x0, y0 = 0.8, 1.45
for j, h in enumerate(["Dimensão", "SPARC (Interno Claro)", "Solução Externa (ex: Panteon)", "Impacto no Negócio"]):
    xx = x0 + sum(cw[:j])
    c = rect(s9, xx, y0, cw[j], 0.42, WINE if j == 0 else (GREEN if j == 1 else (RGBColor(0x33, 0x41, 0x55) if j == 2 else BLUE)))
    text(s9, 0, 0, 0, 0, h, size=12, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.CENTER)

for i, (dm, sp, ext, imp) in enumerate(dims):
    y = y0 + 0.42 + i * 1.25
    rect(s9, x0, y, sum(cw), 1.25, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s9, x0 + 0.1, y, cw[0] - 0.2, 1.25, dm, size=12, bold=True, color=WINE, anchor=MSO_ANCHOR.MIDDLE)
    text(s9, x0 + cw[0] + 0.1, y, cw[1] - 0.2, 1.25, sp, size=10.5, color=INK, anchor=MSO_ANCHOR.MIDDLE)
    text(s9, x0 + sum(cw[:2]) + 0.1, y, cw[2] - 0.2, 1.25, ext, size=10.5, color=MUTED, anchor=MSO_ANCHOR.MIDDLE)
    text(s9, x0 + sum(cw[:3]) + 0.1, y, cw[3] - 0.2, 1.25, imp, size=10.5, bold=True, color=GREEN, anchor=MSO_ANCHOR.MIDDLE)

notes(s9, "Apresentar a matriz de forma elegante: não se trata de depreciar a solução de mercado, "
          "mas de mostrar que a abordagem interna do SPARC atende muito melhor aos objetivos estratégicos e financeiros da Claro.")


# ==============================================================================
# SLIDE 10: TRANSPARÊNCIA E CONVERGÊNCIA
# ==============================================================================
s10 = prs.slides.add_slide(BLANK)
header(s10, "Transparência Total: Plano de Convergência e Alinhamento Corporativo",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

gaps = [
    ("Script Completo da GER CPE",
     "Hoje o SPARC aplica o script base de conectividade e acesso remoto seguro, com motor de automação 100% homologado.",
     "Receber da GER CPE o layout padrão unificado. O motor de automação e templates do SPARC já está pronto.",
     "Rápido (dias)", GREEN),
    ("Acesso à API Corporativa",
     "Hoje o SPARC extrai parâmetros diretamente da ficha técnica oficial SAIP (PDF/TXT) com zero intervenção manual.",
     "Disponibilizar para o SPARC a mesma credencial de API corporativa já compartilhada com soluções externas.",
     "Médio", AMBER),
    ("Governança e Oficialização",
     "Hoje o SPARC opera como projeto inovador da Regional SC em versão beta perfeitamente estável.",
     "Homologar no repositório institucional Claro, com revisão de Segurança da Informação e 2º mantenedor formal.",
     "Médio", AMBER),
    ("Perfil Modular 'SPARC Tester'",
     "Hoje a versão atual contempla todas as ferramentas avançadas de bancada, engenharia e diagnóstico.",
     "Disponibilizar o perfil restrito e simplificado para distribuição segura e padronizada às parceiras terceirizadas.",
     "Em curso", BLUE),
]

cw10 = [2.4, 3.9, 3.9, 1.53]
for j, h in enumerate(["Item", "Situação Atual", "Plano de Fechamento", "Prazo / Esforço"]):
    xx = 0.8 + sum(cw10[:j])
    c = rect(s10, xx, y0, cw10[j], 0.42, WINE)
    text(s10, 0, 0, 0, 0, h, size=12.5, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.CENTER)

for i, (g, a, b, e, col) in enumerate(gaps):
    y = y0 + 0.42 + i * 1.25
    rect(s10, 0.8, y, sum(cw10), 1.25, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s10, 0.85, y, cw10[0] - 0.1, 1.25, g, size=12, bold=True, color=WINE, anchor=MSO_ANCHOR.MIDDLE)
    text(s10, 0.8 + cw10[0], y, cw10[1] - 0.1, 1.25, a, size=11, anchor=MSO_ANCHOR.MIDDLE)
    text(s10, 0.8 + cw10[0] + cw10[1], y, cw10[2] - 0.1, 1.25, b, size=11, anchor=MSO_ANCHOR.MIDDLE)
    pill(s10, 0.8 + sum(cw10[:3]) + 0.12, y + 0.45, cw10[3] - 0.24, e, col, WHITE, size=11, h=0.36)

notes(s10, "A liderança valoriza transparência: deixar claro que as lacunas não são limitações tecnológicas, "
          "mas sim liberações formais de insumos internos da própria Claro (layout de script e credencial de API).")


# ==============================================================================
# SLIDE 11: PLANO DE AÇÃO E ENCAMINHAMENTO
# ==============================================================================
s11 = prs.slides.add_slide(BLANK)
header(s11, "Plano de Ação: Proposta de Encaminhamento para a Liderança",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

asks = [
    ("1", "Aprovar o Modelo Modular (Full + Tester)",
     "Implementar a triagem de hardware e preparo prévio na Assistência Técnica da Regional SC e distribuir o SPARC Tester aos parceiros."),
    ("2", "Intermediar Insumos Técnicos com a GER CPE",
     "Obter o layout do script unificado e a concessão da API corporativa já disponibilizada a soluções de mercado."),
    ("3", "Revisão e Estudo de Contratos Terceirizados",
     "Iniciar alinhamento com Suprimentos e Gestão de Contratos para estudo de readequação do item de configuração de roteador por OS."),
    ("4", "Oficialização Corporativa e Governança",
     "Integrar aos canais oficiais da Claro com homologação de Segurança da Informação e expansão para as demais regionais."),
]

for i, (n, t, d) in enumerate(asks):
    y = 1.45 + i * 1.02
    circ = rect(s11, 0.8, y + 0.08, 0.7, 0.7, RED, shape=MSO_SHAPE.OVAL)
    text(s11, 0, 0, 0, 0, n, size=22, color=WHITE, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=circ)
    rect(s11, 1.7, y, 6.3, 0.88, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s11, 1.85, y + 0.04, 6.0, 0.38, t, size=14, bold=True, color=WINE)
    text(s11, 1.85, y + 0.42, 6.0, 0.45, d, size=11.5)

# Card Lateral de Ganhos Consolidados
rect(s11, 8.3, 1.45, 4.23, 3.94, WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s11, 8.5, 1.58, 3.9, 0.4, "Ganhos Imediatos para a Claro", size=16, color=PINK, bold=True)
tl = [
    ("Economia Contratual", "Readequação de valor por OS em contratos de prestação."),
    ("Zero Equipamento com Falha", "Triagem prévia na base substitui hardware antes do campo."),
    ("Resiliência Operacional", "Opera 100% offline em bancada ou online pelo link do cliente."),
    ("Custo Marginal Zero", "Ativo próprio da Claro sem taxas de licenciamento de software."),
]
for i, (w, d) in enumerate(tl):
    y = 2.05 + i * 0.82
    text(s11, 8.5, y, 3.9, 0.32, "✔ " + w, size=12.5, color=WHITE, bold=True)
    text(s11, 8.5, y + 0.3, 3.9, 0.45, d, size=11, color=PINK)

# Faixa de Conclusão e Decisão Estratégica
c_dec = rect(s11, 0.8, 5.65, 11.733, 1.15, RED, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s11, 0, 0, 0, 0, [
    [("Decisão Estratégica Baseada em Valor: ", {"bold": True, "size": 13.5}),
     ("O SPARC atende integralmente às necessidades de ativação e entrega diferenciais exclusivos: ", {"size": 12.5}),
     ("preparo 100% offline em bancada, triagem prévia de hardware, custo marginal zero de licenças e redução direta de custos em contratos de terceiros.", {"bold": True, "size": 12.5})]],
    size=12.5, color=WHITE, anchor=MSO_ANCHOR.MIDDLE, align=PP_ALIGN.CENTER, shape=c_dec)

notes(s11, "Fechamento forte e propositivo: solicitar formalmente o apoio da liderança para os 4 passos "
          "e reiterar que a Claro ganha soberania técnica e economia real em escala.")

prs.save(OUT)
print("Apresentação Executiva salva com sucesso em:")
print(OUT)
