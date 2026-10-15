# -*- coding: utf-8 -*-
"""
Gera a Apresentação Unificada SPARC (Apresentação Geral + Comparativo Estratégico SPARC x Panteon)
Total de 14 Lâminas / Slides Widescreen 16:9
Contém: Apresentação Geral da Solução + Dossiê Comparativo Completo + Nota de Transparência (Disclaimer)
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
RED_BG = RGBColor(0xFE, 0xE2, 0xE2)      # Vermelho Claro Fundo (#FEE2E2)
AMBER = RGBColor(0xD9, 0x77, 0x06)       # Âmbar Atenção (#D97706)
AMBER_BG = RGBColor(0xFE, 0xF3, 0xC7)    # Âmbar Claro Fundo (#FEF3C7)
BLUE = RGBColor(0x02, 0x84, 0xC7)        # Azul Destaque (#0284C7)
BLUE_BG = RGBColor(0xE0, 0xF2, 0xFE)     # Azul Claro Fundo (#E0F2FE)
PINK = RGBColor(0xFA, 0xE8, 0xEB)        # Rosa/Salmão Suave Claro (#FAE8EB)
FONT = "Segoe UI"

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "SPARC_Apresentacao_Geral_e_Comparativo.pptx")
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

def header(s, title, kicker="CLARO S.A. — ATIVAÇÃO SC · SPARC"):
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
# SLIDE 1: CAPA GERAL — SPARC
# ==============================================================================
s1 = prs.slides.add_slide(BLANK)
rect(s1, 0, 0, 13.333, 7.5, WINE)
rect(s1, 0, 0, 0.45, 7.5, RED)

pill(s1, 1.15, 0.5, 3.8, "🔴  CLARO S.A. — ATIVAÇÃO SC", RED, WHITE, size=13, h=0.42)
text(s1, 1.1, 1.05, 10, 1.0, "SPARC", size=54, color=WHITE, bold=True)
text(s1, 1.1, 2.15, 11, 0.5, "Sistema de Provisionamento e Ativação de Roteadores Claro",
     size=22, color=PINK, bold=True)

text(s1, 1.15, 2.8, 7.6, 3.7, [
    [("Plataforma autônoma para apoio técnico em processos de Ativações Empresariais de campo, ", {"size": 14}),
     ("focada em solução de zero/baixo custo, com alta adaptabilidade e escalabilidade.", {"bold": True, "size": 14})],
    "",
    [("✔ ", {"color": PINK, "bold": True}), ("Recuperação de roteadores: ", {"bold": True, "color": WHITE}),
     ("Restauração ágil de senhas, configs prévias (reutilizados) e firmwares corrompidos.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Upgrades online de firmwares: ", {"bold": True, "color": WHITE}),
     ("Streaming TFTP/HTTP nativo integrado no SPARC sem softwares adicionais.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Parametrização automatizada de configurações: ", {"bold": True, "color": WHITE}),
     ("Importa dados técnicos diretamente de arquivos PDF/TXT sem erro manual.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Homologação técnica de serviços: ", {"bold": True, "color": WHITE}),
     ("Auditoria e testes automáticos de conectividade e emissão de laudo formal.", {})],
    "",
    [("Foco Estratégico: ", {"bold": True, "color": PINK}),
     ("Preenchimento de lacunas técnicas em campo, promovendo forte sinergia e otimização dos atendimentos do projeto ", {}),
     ("Visita Única", {"bold": True, "color": WHITE}),
     (", flexibilizando a necessidade de capacitação técnica inicial e contínua.", {})]
], size=13, color=WHITE, spacing=4)

if os.path.exists(LOGO):
    rect(s1, 9.1, 3.0, 3.4, 2.4, WHITE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    s1.shapes.add_picture(LOGO, Inches(9.35), Inches(3.25), width=Inches(2.9))

text(s1, 1.15, 6.7, 11, 0.35,
     "Instalação Empresarial - Ativação  |  Regional Santa Catarina  |  george.calcmann@claro.com.br",
     size=12, color=PINK, bold=True)

notes(s1, "Apresentação Geral: O SPARC é um ativo in-house da Claro, funcional em SC, focado em baixo custo, "
          "automação e projeto Visita Única.")


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
    [("• Dificuldade de Diálogo Técnico: ", {"bold": True, "color": RGBColor(0x99, 0x1B, 0x1B)}),
     ("Fricção na comunicação entre o suporte CLARO e o profissional de campo em procedimentos com roteadores.", {})],
    [("• Tempo Médio Elevado: ", {"bold": True, "color": INK}),
     ("Procedimentos manuais de atualização de firmwares consumiam ~50 minutos por roteador.", {})],
    [("• Fricção com Senhas Desconhecidas: ", {"bold": True, "color": INK}),
     ("Necessidade de resets forçados via comandos manuais complexos.", {})],
    [("• Dependência de Múltiplos Softwares: ", {"bold": True, "color": INK}),
     ("Operador precisava de PuTTY, TFTPD32, calculadoras de IP, coleções de scripts e firmwares.", {})],
    [("• Risco de Erro Humano: ", {"bold": True, "color": INK}),
     ("Digitação incorreta, cálculos de máscaras de rede, IPs de WAN/LAN, rotas e configuração de acesso remoto.", {})],
    [("• Validação Manual: ", {"bold": True, "color": INK}),
     ("Validação manual de conectividade e IP, testes manuais de banda e ajustes manuais via Windows.", {})],
], size=12.5, spacing=7)

# Card Direito: Solução Esteira SPARC
rect(s2, 6.78, 1.45, 5.75, 5.35, WHITE, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t_s = rect(s2, 6.78, 1.45, 5.75, 0.55, GREEN)
text(s2, 0, 0, 0, 0, "⚡ Solução com a Esteira SPARC", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_s)

text(s2, 7.03, 2.15, 5.25, 4.45, [
    [("• Intermediação Eficiente: ", {"bold": True, "color": GREEN}),
     ("Intermediação da configuração do roteador, permitindo integração mais eficiente com o suporte Claro.", {})],
    [("• Agilidade Comprovada: ", {"bold": True, "color": INK}),
     ("Provisionamento completo em bancada ou campo em menos de 10 minutos, com poucos cliques.", {})],
    [("• Recuperação Automatizada: ", {"bold": True, "color": INK}),
     ("Recuperação de equipamentos bloqueados por senhas, com configs prévias (reutilizados) e firmwares corrompidos.", {})],
    [("• TFTP/HTTP 100% Integrado: ", {"bold": True, "color": INK}),
     ("Streaming nativo embutido no SPARC, sem necessidade de softwares adicionais.", {})],
    [("• Importação Direta do SAIP: ", {"bold": True, "color": INK}),
     ("Importa dados técnicos diretamente de arquivos PDF/TXT oficiais sem erro de digitação.", {})],
    [("• Auditoria e Testes Automáticos: ", {"bold": True, "color": INK}),
     ("Ajusta configurações da placa de rede do operador e testa ICMP, Telnet e homologação de banda.", {})],
], size=12.5, spacing=7)

notes(s2, "Destaque: o contraste entre o modelo manual anterior disperso e a esteira ponta a ponta em < 10 minutos.")


# ==============================================================================
# SLIDE 3: IMPACTO OPERACIONAL E MÉTRICAS
# ==============================================================================
s3 = prs.slides.add_slide(BLANK)
header(s3, "Impacto Operacional e Métricas de Produtividade",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC BETA")

kpis_s3 = [
    ("-88%", "Redução no Tempo", "Técnico experiente ~50 min para < 10 min por roteador", RED),
    ("100%", "Aderência aos Padrões", "Sem falhas de digitação em IPs e Rotas", GREEN),
    ("1 App", "Solução Portátil", "Substitui PuTTY, TFTPD32, scripts e gestão de firmwares", BLUE),
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
     ("Possibilidade de trabalho modo 'bancada', um único operador ganha capacidade de preparar dezenas de roteadores diariamente sem estresse operacional, entregando os equipamentos pré-configurados para as equipes, ou ainda, o próprio técnico executa o processo completo in loco.", {})],
    "",
    [("• Facilidade de Uso e Interface Amigável: ", {"bold": True, "color": WINE}),
     ("Manual e interface visual intuitiva com indicação em tempo real de progresso e status, reduzindo drasticamente o tempo de treinamento.", {})],
    "",
    [("• Aderência Total ao Projeto Visita Única: ", {"bold": True, "color": WINE}),
     ("Redução de retrabalho, assertividade em campo e garantia de conformidade na primeira visita ao cliente.", {})],
], size=12.5, spacing=4)

notes(s3, "Métricas concretas comprovadas em campo na regional.")


# ==============================================================================
# SLIDE 4: PRÓXIMOS PASSOS E CONCLUSÃO (ROADMAP)
# ==============================================================================
s4 = prs.slides.add_slide(BLANK)
header(s4, "Próximos Passos e Conclusão: Roadmap de Evolução",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC BETA")

# Card Esquerdo: Andamento Atual
rect(s4, 0.8, 1.45, 5.75, 5.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
t_and = rect(s4, 0.8, 1.45, 5.75, 0.55, BLUE)
text(s4, 0, 0, 0, 0, "🚀 Roadmap de Evolução — Andamento", size=14, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_and)

text(s4, 1.05, 2.15, 5.25, 4.45, [
    [("📱 SPARC Móvel, Android (75% concluído): ", {"bold": True, "color": BLUE}),
     ("Em processo de refinamento operacional para celulares Android corporativos.", {})],
    "",
    [("☁️ Repositório Online Homologado: ", {"bold": True, "color": BLUE}),
     ("Elimina necessidade de administração de arquivos pelos usuários. Funcional, em fase de homologação.", {})],
    "",
    [("🧪 Aferições Avançadas (80% concluído): ", {"bold": True, "color": BLUE}),
     ("Certidões de Nascimento sem necessidade de equipamentos dedicados (JDSU e afins) aproveitando a estrutura Claro existente. Loop lógico no roteador com um clique via LAN.", {})],
    "",
    [("🔧 Funções Adicionais e Manutenção (70% concluído): ", {"bold": True, "color": BLUE}),
     ("Funções para equipamentos ativos, atualizações e recuperação de equipamentos em bancada.", {})],
], size=12.5, spacing=6)

# Card Direito: Projeção
rect(s4, 6.78, 1.45, 5.75, 5.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
t_proj = rect(s4, 6.78, 1.45, 5.75, 0.55, WINE)
text(s4, 0, 0, 0, 0, "🎯 Roadmap de Evolução — Projeção", size=14, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_proj)

text(s4, 7.03, 2.15, 5.25, 4.45, [
    [("🔗 Integração com os Sistemas Corporativos Claro: ", {"bold": True, "color": WINE}),
     ("Com uma única variável (OS) o sistema poderá identificar todos os dados necessários para provisionamento definitivo, gerar progresso e demais atualizações sistêmicas online, em tempo real.", {})],
    "",
    [("🔐 Acesso a Banco de Dados de Senha: ", {"bold": True, "color": WINE}),
     ("Acesso protegido e direto sem necessidade de divulgação ao operador de campo.", {})],
    "",
    [("🌐 Expansão de Equipamentos de Rede: ", {"bold": True, "color": WINE}),
     ("Inclusão de suporte a equipamentos de acesso, switches e demais dispositivos de rede de diferentes portes.", {})],
    "",
    [("🏢 Escala Nacional Institucional: ", {"bold": True, "color": WINE}),
     ("Disponibilização da solução para as demais regionais Claro sob governança centralizada.", {})],
], size=12.5, spacing=6)

notes(s4, "Maturidade do roadmap e visão de integração corporativa.")


# ==============================================================================
# SLIDE 5: TRANSIÇÃO COMPARATIVA — SPARC X PANTEON
# ==============================================================================
s5 = prs.slides.add_slide(BLANK)
rect(s5, 0, 0, 13.333, 7.5, WINE)
rect(s5, 0, 0, 0.45, 7.5, RED)

pill(s5, 1.15, 0.5, 3.8, "🔴  CLARO S.A. — ATIVAÇÃO SC", RED, WHITE, size=13, h=0.42)
text(s5, 1.1, 1.05, 10, 1.0, "SPARC × Panteon", size=54, color=WHITE, bold=True)
text(s5, 1.1, 2.15, 11, 0.5, "Decisão Estratégica: Eficiência Operacional, Flexibilidade e Redução de Custos",
     size=22, color=PINK, bold=True)

text(s5, 1.15, 2.8, 11.2, 3.6, [
    [("Como simplificar a operação de campo, economizar recursos contratuais e blindar a entrega contra falhas?", {"bold": True, "size": 15})],
    "",
    [("✔ ", {"color": PINK, "bold": True}), ("Oportunidade com Redução de Custos Contratuais: ", {"bold": True, "color": WHITE}),
     ("Roteador sai pronto da base Claro, permitindo abater item pago a terceiros.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Oportunidade com a Triagem Prévia de Hardware: ", {"bold": True, "color": WHITE}),
     ("Equipamento já sai testado da base; defeituosos são substituídos antes da visita.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Modo Offline com Máxima Flexibilidade: ", {"bold": True, "color": WHITE}),
     ("O SPARC traz flexibilidade, trabalha em modo ONLINE e 'OFFLINE', não depende da conectividade do link para preparar o roteador, podendo executar tudo com antecedência.", {})],
    [("✔ ", {"color": PINK, "bold": True}), ("Zero Investimento em Licenças e Servidores: ", {"bold": True, "color": WHITE}),
     ("Ativo 100% interno Claro, sem Capex/Opex de fornecedor externo engessado.", {})],
], size=13.5, color=WHITE, spacing=6)

text(s5, 1.15, 6.7, 11, 0.35,
     "Instalação Empresarial - Ativação  |  Regional Santa Catarina  |  george.calcmann@claro.com.br",
     size=12, color=PINK, bold=True)

notes(s5, "Transição para o comparativo com a solução de mercado com linguagem executiva e propositiva.")


# ==============================================================================
# SLIDE 6: RESUMO EXECUTIVO — OPORTUNIDADE DO MODO OFFLINE
# ==============================================================================
s6 = prs.slides.add_slide(BLANK)
header(s6, "Resumo Executivo: Oportunidade do Modo OFFLINE",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

# 3 Cards Superiores
top_3 = [
    ("ECONOMIA EM CONTRATOS",
     "O SPARC entra como apoio na configuração do roteador ainda na base Claro com a Assistência Técnica própria, retiramos a responsabilidade do terceiro e renegociamos o item de configuração pago por OS. Entregamos roteador pronto!",
     GREEN),
    ("HARDWARE VALIDADO",
     "Testamos o hardware na base com antecedência (substituição prévia se houver defeito físico ou de portas).",
     GREEN),
    ("ZERO LICENÇAS E SERVIDORES",
     "Sem taxas de licença por usuário, sem renovações bienais e sem compra de servidores dedicados. Código de propriedade da Claro pronto para escala imediata.",
     GREEN),
]
for i, (t, d, c) in enumerate(top_3):
    x = 0.8 + i * 3.95
    rect(s6, x, 1.45, 3.75, 2.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    rect(s6, x, 1.45, 3.75, 0.1, c)
    text(s6, x + 0.25, 1.6, 3.3, 0.45, t, size=15, color=WINE, bold=True)
    text(s6, x + 0.25, 2.1, 3.3, 1.55, d, size=12)

# Card Esquerda Inferior: Gargalos Solução Externa
rect(s6, 0.8, 3.95, 5.75, 2.9, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s6, 1.05, 4.05, 5.3, 0.35, "Possíveis Gargalos na Solução Externa (Panteon):", size=14.5, color=AMBER, bold=True)
text(s6, 1.05, 4.45, 5.3, 2.3, [
    [("• Dependência do Link do Cliente: ", {"bold": True, "color": INK}),
     ("Enquanto o serviço não apresentar conectividade, o Panteon trava e não consegue iniciar o processo em paralelo com outras demandas de suporte.", {})],
    [("• Sem Triagem Prévia de Hardware: ", {"bold": True, "color": INK}),
     ("Descobre defeito físico do roteador na frente do cliente, podendo gerar visita improdutiva.", {})],
    [("• Custo Recorrente e Servidores: ", {"bold": True, "color": INK}),
     ("Licenças proprietárias e renovações periódicas, além de exigir investimento em +10 servidores internos para homologação dos links.", {})],
    [("• Engessamento Operacional: ", {"bold": True, "color": INK}),
     ("Obriga o técnico terceiro a configurar no campo, encarecendo o contrato de instalação.", {})],
], size=11.5, spacing=4)

# Card Direita Inferior: Recomendação Prática
rect(s6, 6.78, 3.95, 5.75, 2.9, WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s6, 7.03, 4.05, 5.3, 0.35, "Nossa Recomendação Prática", size=15, color=PINK, bold=True)
text(s6, 7.03, 4.45, 5.3, 2.3, [
    [("1. ", {"bold": True}), ("Adotar o modelo SPARC Full na Assistência Técnica própria para entregar roteadores 100% testados.", {})],
    "",
    [("2. ", {"bold": True}), ("Disponibilizar o 'SPARC Tester' para os parceiros terceirizados apenas validarem a entrega física/velocidade via SmartPhone.", {})],
    "",
    [("3. ", {"bold": True}), ("Renegociar o valor do item de configuração de roteador nos contratos das prestadoras de serviço.", {})],
    "",
    [("Resultado: ", {"bold": True, "color": PINK}), ("zero visita perdida por defeito de hardware, ativação ágil e economia recorrente.", {})],
], size=12, color=WHITE, spacing=4)

notes(s6, "Resumo Executivo do Modo Offline e recomendação da Regional.")


# ==============================================================================
# SLIDE 7: MODULARIDADE OPERACIONAL (FULL VS TESTER)
# ==============================================================================
s7 = prs.slides.add_slide(BLANK)
header(s7, "Modularidade Operacional: O Modelo SPARC Full × SPARC Tester",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

# Card Base Claro
rect(s7, 0.8, 1.45, 5.75, 4.3, WHITE, line=WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t_full = rect(s7, 0.8, 1.45, 5.75, 0.55, WINE)
text(s7, 0, 0, 0, 0, "1. BASE CLARO: SPARC Full (Técnicos Próprios)", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_full)

text(s7, 1.05, 2.15, 5.25, 3.45, [
    [("Quem opera: ", {"bold": True, "color": WINE}), ("Técnicos da Assistência Técnica e Ativação interna da Claro.", {})],
    [("Onde: ", {"bold": True}), ("Bancada / Laboratório na base regional.", {})],
    [("Triagem de Hardware: ", {"bold": True, "color": RED}), ("Testa portas, boot e placas. Se houver falha, substitui o equipamento na base antes de ir para a rua.", {})],
    [("O que faz: ", {"bold": True}), ("Lê o SAIP automaticamente, atualiza o firmware homologado e aplica a configuração.", {})],
    [("Tempo: ", {"bold": True}), ("Menos de 10 minutos por roteador.", {})],
    [("Resultado: ", {"bold": True, "color": GREEN}), ("Roteador sai 100% pronto, testado e etiquetado ('Plug & Play').", {})],
], size=12.5, spacing=7)

# Card Parceiros Campo
rect(s7, 6.78, 1.45, 5.75, 4.3, WHITE, line=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
t_tst = rect(s7, 6.78, 1.45, 5.75, 0.55, BLUE)
text(s7, 0, 0, 0, 0, "2. CAMPO: SPARC Tester (Técnicos Terceirizados)", size=14.5, color=WHITE, bold=True,
     align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, shape=t_tst)

text(s7, 7.03, 2.15, 5.25, 3.45, [
    [("Quem opera: ", {"bold": True, "color": BLUE}), ("Empresas terceiras responsáveis pela fixação e instalação do acesso.", {})],
    [("O que muda: ", {"bold": True, "color": INK}), ("Retira do terceiro o acesso e a responsabilidade de configurar o roteador.", {})],
    [("O que é o SPARC Tester: ", {"bold": True}), ("Aplicativo simplificado e blindado para celular ou notebook, focado na homologação da entrega.", {})],
    [("O que ele faz: ", {"bold": True}), ("Checa porta de rede, valida link quando o circuito sobe, afere velocidade com Speedtest e gera laudo PDF.", {})],
    [("Versatilidade Híbrida: ", {"bold": True, "color": GREEN}), ("Permite também ativar online pelo link do cliente exatamente como o Panteon, se desejado.", {})],
    [("Impacto: ", {"bold": True, "color": GREEN}), ("Zero visita perdida por equipamento defeituoso e zero erro de configuração.", {})],
], size=12.5, spacing=7)

# Faixa de Vantagem Financeira
c_vf = rect(s7, 0.8, 5.9, 11.733, 1.0, GREEN_BG, line=GREEN, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.5)
text(s7, 1.05, 5.95, 11.23, 0.9, [
    [("💰 Vantagem Financeira Estratégica: ", {"bold": True, "color": GREEN, "size": 13}),
     ("Como o parceiro NÃO precisa mais configurar o roteador no cliente, a Claro ganha respaldo direto para ", {"size": 12.5}),
     ("renegociar e abater o item contratual de configuração de CPE", {"bold": True, "color": INK, "size": 12.5}),
     (". Além disso, a substituição prévia na base elimina o custo de visitas improdutivas por hardware defeituoso!", {"size": 12.5})],
], size=12.5, anchor=MSO_ANCHOR.MIDDLE)

notes(s7, "O modelo modular que protege o contrato e o hardware.")


# ==============================================================================
# SLIDE 8: IMPACTO FINANCEIRO DIRETO
# ==============================================================================
s8 = prs.slides.add_slide(BLANK)
header(s8, "Impacto Financeiro Direto: Onde o SPARC Gera Economia para a Claro",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

fin_cards = [
    ("1. Redução Contratual de Terceiros",
     "Eliminação do custo de 'configuração de CPE' nas ordens de serviço executadas por parceiras externas.",
     "Economia por OS em escala nacional", GREEN),
    ("2. Custo Zero com Licenças",
     "Panteon exige licenças por usuário ou contratos bienais caros. O SPARC é ativo de propriedade da Claro: R$ 0.",
     "100% de economia em licenças de software", GREEN),
    ("3. Zero Investimento em Servidores",
     "Panteon prevê estrutura própria com expansão para 10+ servidores dedicados. O SPARC roda localmente sem novos servidores exclusivos.",
     "Evita Capex e Opex de infraestrutura", GREEN),
    ("4. Eliminação de Visitas Improdutivas",
     "Como o equipamento já é testado na base, elimina visitas perdidas por defeito de hardware ou link instável.",
     "Queda de retrabalho e revisitas", GREEN),
]

for i, (tit, desc, sub, cor) in enumerate(fin_cards):
    x = 0.8 + (i % 2) * 5.98
    y = 1.45 + (i // 2) * 2.65
    rect(s8, x, y, 5.75, 2.45, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    rect(s8, x, y, 5.75, 0.1, cor)
    text(s8, x + 0.3, y + 0.22, 5.15, 0.45, tit, size=15.5, color=WINE, bold=True)
    text(s8, x + 0.3, y + 0.72, 5.15, 0.95, desc, size=12.5)
    pill(s8, x + 0.3, y + 1.85, 5.15, "✔  " + sub, GREEN_BG, GREEN, size=11, h=0.38)

notes(s8, "As 4 frentes financeiras objetivas de economia da Claro.")


# ==============================================================================
# SLIDE 9: SPARC HOJE (FERRAMENTA PRÁTICA)
# ==============================================================================
s9 = prs.slides.add_slide(BLANK)
header(s9, "SPARC Hoje: A Ferramenta Prática que Já Transforma o Campo",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

kpis_s9 = [
    ("-88%", "Tempo de Ativação", "De ~50 min para < 10 min"),
    ("100%", "Leitura Automática", "Zero digitação manual do SAIP"),
    ("100%", "Operação Offline", "Bancada sem depender de link"),
    ("2", "Plataformas Nativas", "Windows (.exe) + Android (.apk)")
]
for i, (v, l, d) in enumerate(kpis_s9):
    x = 0.8 + i * 2.97
    rect(s9, x, 1.45, 2.75, 1.75, WHITE, line=PINK, shape=MSO_SHAPE.ROUNDED_RECTANGLE, lw=1.25)
    text(s9, x, 1.55, 2.75, 0.7, v, size=34, color=RED, bold=True, align=PP_ALIGN.CENTER)
    text(s9, x, 2.27, 2.75, 0.35, l, size=13, bold=True, align=PP_ALIGN.CENTER)
    text(s9, x, 2.62, 2.75, 0.35, d, size=11, color=MUTED, align=PP_ALIGN.CENTER)

feats_s9 = [
    ("📄 Leitor Inteligente do SAIP", "Extrai parâmetros diretamente de PDFs/TXTs da Claro. Sem digitação e sem margem para erro humano."),
    ("🧰 Bancada & Triagem de Hardware", "Testa portas, memória e boot na base Claro. Substitui roteador com defeito antes de ir ao cliente."),
    ("📦 Repositório Offline Embarcado", "Mantém firmwares homologados acessíveis localmente para gravação imediata sem depender de internet."),
    ("🧪 Testes e Certidão em PDF", "Valida internet (Speedtest) e circuito (Y.1564), gerando laudo formal na hora para anexar à OS."),
    ("🔌 Conexão Serial + Cabo de Rede", "Trabalha com cabo console e porta Ethernet simultaneamente pelo celular ou notebook."),
    ("🛡️ Gestão e Controle Seguro", "Ativação com proteção criptográfica individual vinculada ao dispositivo do técnico."),
]
for i, (t, d) in enumerate(feats_s9):
    c_, r_ = i % 3, i // 3
    x, y = 0.8 + c_ * 3.95, 3.4 + r_ * 1.7
    rect(s9, x, y, 3.7, 1.52, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s9, x + 0.2, y + 0.12, 3.3, 0.4, t, size=13.5, color=WINE, bold=True)
    text(s9, x + 0.2, y + 0.55, 3.3, 0.88, d, size=11.5)

notes(s9, "A ferramenta pronta e homologada na regional.")


# ==============================================================================
# SLIDE 10: COMPARATIVO CRITÉRIO A CRITÉRIO
# ==============================================================================
s10 = prs.slides.add_slide(BLANK)
header(s10, "Comparativo com base no que foi apresentado: SPARC × Panteon",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

rows_cmp = [
    ("Investimento em Licenças", "Zero — Propriedade interna Claro", "g", "Alto — Licenças vitalícias ou bienais", "r"),
    ("Redução de Custo Contratual", "Sim — Permite modo SPARC Tester", "g", "Não — Mantém terceiro configurando", "r"),
    ("Tolerância a Link Fora do Ar", "100% Offline — Roteador preparado em paralelo", "g", "Trava — Exige link online", "r"),
    ("Triagem Prévia de Hardware", "Sim — Substituição na base antes da visita", "g", "Não — Descobre falha na frente do cliente", "r"),
    ("Modo Online pelo Link do Cliente", "Sim — Funciona igual ao Panteon se desejado", "g", "Sim — Único modo suportado", "g"),
    ("Necessidade de Servidores Novos", "Zero — Processamento no dispositivo", "g", "Exige 1 + expansão para 10 servidores", "r"),
    ("Plataformas Suportadas", "Windows + Android", "g", "Somente Android", "r"),
    ("Interface de Teste de Campo", "Serial CLI + Cabo de Rede (Ethernet)", "g", "Somente Serial CLI", "r"),
    ("Homologação da Entrega", "Speedtest + Laudo PDF (Módulo Y.1564 em testes)", "g", "Exige servidores internos dedicados", "a"),
    ("Disponibilidade Operacional", "Imediata (Beta em uso na Regional)", "g", "Piloto travado em aprovação financeira", "r"),
    ("Integração com APIs Claro", "Em roadmap (tecnologia já modular)", "a", "Sim (já homologado)", "g"),
]
fills = {"g": (GREEN_BG, GREEN, "✔"), "r": (RED_BG, RED, "✖"), "a": (AMBER_BG, AMBER, "◐")}
x0, y0, rh = 0.8, 1.35, 0.44
cw10 = [3.8, 4.35, 3.58]

for j, h in enumerate(["Critério", "SPARC (Inovação Interna)", "Panteon (Fornecedor Externo)"]):
    xx = x0 + sum(cw10[:j])
    c = rect(s10, xx, y0, cw10[j], 0.36, WINE if j != 1 else RED)
    text(s10, 0, 0, 0, 0, h, size=12, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.LEFT if j == 0 else PP_ALIGN.CENTER)

for i, (crit, sv, sk, pv, pk) in enumerate(rows_cmp):
    y = y0 + 0.36 + i * rh
    rect(s10, x0, y, sum(cw10), rh, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s10, x0 + 0.1, y, cw10[0] - 0.15, rh, crit, size=11, bold=True, anchor=MSO_ANCHOR.MIDDLE)
    for k, (val, key) in enumerate([(sv, sk), (pv, pk)]):
        bg, fg, ic = fills[key]
        xx = x0 + sum(cw10[:k + 1]) + 0.08
        c = rect(s10, xx, y + 0.03, cw10[k + 1] - 0.16, rh - 0.06, bg, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
        text(s10, 0, 0, 0, 0, f"{ic}  {val}", size=10, color=fg, bold=True, anchor=MSO_ANCHOR.MIDDLE,
             align=PP_ALIGN.CENTER, shape=c, spacing=0)

pill(s10, 0.8, 6.45, 4.8, "Vantagens Comerciais/Operacionais:  SPARC 10  ×  2  Panteon", WINE, WHITE, size=11.5, h=0.38)
text(s10, 5.8, 6.45, 6.8, 0.38, "✔ Atende com excelência   ◐ Em andamento/Depende de insumo   ✖ Não atende", size=11, color=MUTED)

notes(s10, "Comparativo objetivo: SPARC ganha em quase todos os critérios práticos de campo e custo.")


# ==============================================================================
# SLIDE 11: FLEXIBILIDADE DE ATIVAÇÃO
# ==============================================================================
s11 = prs.slides.add_slide(BLANK)
header(s11, "Flexibilidade de Ativação: Onde Cada Solução Funciona na Prática",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

scen = [
    ("🌐 Link Ativo no Cliente", "Circuito online: baixa firmware e valida direto pelo link (igual ao Panteon).", "✔", "✔"),
    ("🧰 Bancada na Base (Sem Link)", "Equipamento testado e preparado antes da visita por técnicos Claro. Roteador sai pronto.", "✔", "✖"),
    ("📲 Atualização pelo Dispositivo", "Técnico transfere firmware do próprio celular/notebook quando cliente ainda não tem rede ou em bancada.", "✔", "✖"),
    ("🔴 Link Instável ou com Atraso", "Panteon TRAVA. O SPARC entrega o roteador pronto; sobe assim que a rede normalizar.", "✔", "✖"),
]
for i, (t, d, sp, pa) in enumerate(scen):
    x = 0.8 + i * 2.97
    rect(s11, x, 1.45, 2.75, 2.9, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s11, x + 0.15, 1.55, 2.5, 0.45, t, size=14, color=WINE, bold=True)
    text(s11, x + 0.15, 2.05, 2.5, 0.95, d, size=11.5)
    for k, (nm, v) in enumerate([("SPARC", sp), ("Panteon", pa)]):
        ok = v == "✔"
        pill(s11, x + 0.2, 3.2 + k * 0.52, 2.35, f"{v}  {nm}",
             GREEN_BG if ok else RED_BG, GREEN if ok else RED, size=11.5, h=0.4)

text(s11, 0.8, 4.6, 6, 0.35, "Compatibilidade e Meios de Conexão", size=15, color=WINE, bold=True)
matrix = [
    ("Cabo Console (Serial)", "✔ SPARC", "✔ Panteon", True, True),
    ("Cabo de Rede (Ethernet)", "✔ SPARC", "✖ Panteon", True, False),
    ("Celular / Tablet (Android)", "✔ SPARC", "✔ Panteon", True, True),
    ("Notebook / PC (Windows)", "✔ SPARC", "✖ Panteon", True, False),
]
for i, (nm, sp, pa, spok, paok) in enumerate(matrix):
    x = 0.8 + i * 2.97
    rect(s11, x, 5.05, 2.75, 1.7, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s11, x, 5.15, 2.75, 0.35, nm, size=13, bold=True, align=PP_ALIGN.CENTER)
    pill(s11, x + 0.2, 5.58, 2.35, sp, GREEN_BG if spok else RED_BG, GREEN if spok else RED, size=11, h=0.32)
    pill(s11, x + 0.2, 5.98, 2.35, pa, GREEN_BG if paok else RED_BG, GREEN if paok else RED, size=11, h=0.32)

notes(s11, "Demonstração prática dos cenários reais de instalação em campo.")


# ==============================================================================
# SLIDE 12: MATRIZ ESTRATÉGICA
# ==============================================================================
s12 = prs.slides.add_slide(BLANK)
header(s12, "Matriz Estratégica: Inovação Interna (SPARC) vs. Fornecedor Externo",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

dims = [
    ("Velocidade & Resiliência",
     "Funciona 100% offline em bancada ou online pelo link. Evolução contínua sem depender de fornecedor externo.",
     "Depende obrigatoriamente de link ativo na visita e fila de suporte do fornecedor externo.",
     "Zero paralisação por indisponibilidade de rede"),
    ("Flexibilidade Orçamentária",
     "Zero licenças e zero servidores dedicados. Capacidade de modular (SPARC Full vs SPARC Tester) sob demanda. Windows e Android.",
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

cw12 = [2.2, 3.6, 3.6, 2.33]
x0, y0 = 0.8, 1.45
for j, h in enumerate(["Dimensão", "SPARC (Interno Claro)", "Fornecedor Externo (Panteon)", "Impacto no Negócio"]):
    xx = x0 + sum(cw12[:j])
    c = rect(s12, xx, y0, cw12[j], 0.42, WINE if j == 0 else (GREEN if j == 1 else (RGBColor(0x33, 0x41, 0x55) if j == 2 else BLUE)))
    text(s12, 0, 0, 0, 0, h, size=12, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.CENTER)

for i, (dm, sp, ext, imp) in enumerate(dims):
    y = y0 + 0.42 + i * 1.25
    rect(s12, x0, y, sum(cw12), 1.25, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s12, x0 + 0.1, y, cw12[0] - 0.2, 1.25, dm, size=12, bold=True, color=WINE, anchor=MSO_ANCHOR.MIDDLE)
    text(s12, x0 + cw12[0] + 0.1, y, cw12[1] - 0.2, 1.25, sp, size=10.5, color=INK, anchor=MSO_ANCHOR.MIDDLE)
    text(s12, x0 + sum(cw12[:2]) + 0.1, y, cw12[2] - 0.2, 1.25, ext, size=10.5, color=MUTED, anchor=MSO_ANCHOR.MIDDLE)
    text(s12, x0 + sum(cw12[:3]) + 0.1, y, cw12[3] - 0.2, 1.25, imp, size=10.5, bold=True, color=GREEN, anchor=MSO_ANCHOR.MIDDLE)

notes(s12, "Visão executiva dos 4 pilares estratégicos de decisão.")


# ==============================================================================
# SLIDE 13: TRANSPARÊNCIA TOTAL
# ==============================================================================
s13 = prs.slides.add_slide(BLANK)
header(s13, "Transparência Total: O Que Falta e Como Fechar Rapidamente",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

gaps = [
    ("Script Completo da GER CPE",
     "Hoje o SPARC aplica o script de conectividade e acesso remoto, e a GER CPE conclui remotamente.",
     "Estabelecer com GER CPE o layout padrão do script. O motor de automação do SPARC já está pronto para este suporte.",
     "Rápido (dias)", GREEN),
    ("Acesso à API Corporativa",
     "Hoje o SPARC lê as ordens e parâmetros diretamente da ficha SAIP (PDF/TXT) sem erro humano.",
     "Liberar para o SPARC a mesma credencial de API já concedida ao Panteon para leitura direta de sistema.",
     "Médio", AMBER),
    ("Governança e Oficialização",
     "Hoje o SPARC opera como projeto inovador da Regional SC com versão beta estável.",
     "Oficializar em repositório corporativo Claro, e formação de 2º mantenedor.",
     "Médio", AMBER),
]

cw13 = [2.4, 3.9, 3.9, 1.53]
for j, h in enumerate(["Item", "Situação Atual", "Plano de Fechamento", "Prazo / Esforço"]):
    xx = 0.8 + sum(cw13[:j])
    c = rect(s13, xx, y0, cw13[j], 0.42, WINE)
    text(s13, 0, 0, 0, 0, h, size=12.5, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE, shape=c,
         align=PP_ALIGN.CENTER)

for i, (g, a, b, e, col) in enumerate(gaps):
    y = y0 + 0.42 + i * 1.55
    rect(s13, 0.8, y, sum(cw13), 1.55, WHITE if i % 2 == 0 else BG, line=LINE, lw=0.5)
    text(s13, 0.85, y, cw13[0] - 0.1, 1.55, g, size=12.5, bold=True, color=WINE, anchor=MSO_ANCHOR.MIDDLE)
    text(s13, 0.8 + cw13[0], y, cw13[1] - 0.1, 1.55, a, size=11.5, anchor=MSO_ANCHOR.MIDDLE)
    text(s13, 0.8 + cw13[0] + cw13[1], y, cw13[2] - 0.1, 1.55, b, size=11.5, anchor=MSO_ANCHOR.MIDDLE)
    pill(s13, 0.8 + sum(cw13[:3]) + 0.12, y + 0.55, cw13[3] - 0.24, e, col, WHITE, size=11, h=0.4)

notes(s13, "Transparência total quanto às liberações internas pendentes.")


# ==============================================================================
# SLIDE 14: PLANO DE AÇÃO E ENCAMINHAMENTO + DISCLAIMER
# ==============================================================================
s14 = prs.slides.add_slide(BLANK)
header(s14, "Plano de Ação: O Que Propomos para a Regional e a Claro",
       "CLARO S.A. — ATIVAÇÃO SC · SPARC × PANTEON")

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
    y = 1.45 + i * 0.98
    circ = rect(s14, 0.8, y + 0.06, 0.65, 0.65, RED, shape=MSO_SHAPE.OVAL)
    text(s14, 0, 0, 0, 0, n, size=20, color=WHITE, bold=True, align=PP_ALIGN.CENTER,
         anchor=MSO_ANCHOR.MIDDLE, shape=circ)
    rect(s14, 1.6, y, 6.4, 0.84, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
    text(s14, 1.75, y + 0.04, 6.1, 0.36, t, size=14, bold=True, color=WINE)
    text(s14, 1.75, y + 0.4, 6.1, 0.42, d, size=11)

# Card Lateral de Ganhos Consolidados
rect(s14, 8.3, 1.45, 4.23, 3.8, WINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s14, 8.5, 1.55, 3.9, 0.35, "Ganhos Imediatos para a Claro", size=15.5, color=PINK, bold=True)
tl = [
    ("Economia Contratual", "Abatimento de valor por OS nas faturas terceirizadas."),
    ("Zero Equipamento Defeituoso", "Triagem prévia na base Claro substitui hardware antes do campo."),
    ("Zero Dependência de Link", "Opera 100% offline em bancada ou online pelo link do cliente."),
    ("Zero Capex / Licenças", "Ativo próprio da Claro sem taxas de fornecedor externo."),
]
for i, (w, d) in enumerate(tl):
    y = 1.95 + i * 0.8
    text(s14, 8.5, y, 3.9, 0.3, "✔ " + w, size=12.5, color=WHITE, bold=True)
    text(s14, 8.5, y + 0.28, 3.9, 0.42, d, size=10.5, color=PINK)

# Box Inferior com Nota de Transparência / Disclaimer
c_disc = rect(s14, 0.8, 5.45, 11.733, 1.35, WHITE, line=LINE, shape=MSO_SHAPE.ROUNDED_RECTANGLE)
text(s14, 1.0, 5.5, 11.33, 1.25, [
    [("Nota de Transparência: ", {"bold": True, "color": WINE, "size": 11}),
     ("As referências a soluções de mercado (Panteon) baseiam-se no modelo operacional e fluxo de ativação propostos e divulgados até a presente data, sem o propósito de realizar auditoria técnica aprofundada da ferramenta externa. O objetivo desta apresentação restringe-se a demonstrar a proposta de valor, a modularidade e os ganhos operacionais e financeiros da esteira interna SPARC para a Claro.", {"size": 10.5, "color": MUTED})],
], size=10.5, spacing=0)

notes(s14, "Encerramento formal com solicitação de apoio e a nota de transparência para proteção institucional da Regional.")

prs.save(OUT)
print("Apresentação Unificada de 14 Slides salva com sucesso em:")
print(OUT)
