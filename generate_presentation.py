import os
import pptx
from pptx.util import Inches, Pt
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE
from pptx.dml.color import RGBColor

# Cores da Identidade Visual Claro / SPARC
COLOR_BG_DARK = RGBColor(11, 19, 43)        # #0B132B (Azul Petróleo / Slate Escuro)
COLOR_CARD_DARK = RGBColor(26, 38, 66)      # #1A2642
COLOR_CARD_BORDER = RGBColor(45, 62, 100)   # #2D3E64
COLOR_RED_CLARO = RGBColor(218, 41, 28)     # #DA291C (Vermelho Claro)
COLOR_RED_ACCENT = RGBColor(239, 68, 68)    # #EF4444
COLOR_CYAN = RGBColor(56, 189, 248)         # #38BDF8 (Ciano Destaque)
COLOR_GREEN = RGBColor(34, 197, 94)         # #22C55E (Verde Sucesso)
COLOR_AMBER = RGBColor(245, 158, 11)        # #F59E0B (Âmbar/Atenção)
COLOR_WHITE = RGBColor(248, 250, 252)       # #F8FAFC
COLOR_MUTED = RGBColor(148, 163, 184)       # #94A3B8 (Cinza Suave)

LOGO_PATH = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo_full.png"
if not os.path.exists(LOGO_PATH):
    LOGO_PATH = r"C:\SPARC\src\NetworkDevice.UI\Assets\sparc_logo.png"

def create_presentation():
    prs = pptx.Presentation()
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)
    blank_layout = prs.slide_layouts[6]

    def set_slide_background(slide):
        bg_shape = slide.shapes.add_shape(
            MSO_SHAPE.RECTANGLE, 0, 0, Inches(13.333), Inches(7.5)
        )
        bg_shape.fill.solid()
        bg_shape.fill.fore_color.rgb = COLOR_BG_DARK
        bg_shape.line.fill.background()
        return bg_shape

    def add_header(slide, title, category="CLARO S.A. • INSTALAÇÃO EMPRESARIAL B2B • REGIONAL SC"):
        # Faixa superior vermelha fina da Claro
        stripe = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, 0, 0, Inches(13.333), Inches(0.08))
        stripe.fill.solid()
        stripe.fill.fore_color.rgb = COLOR_RED_CLARO
        stripe.line.fill.background()

        # Categoria / Trilha
        cat_box = slide.shapes.add_textbox(Inches(0.8), Inches(0.25), Inches(10), Inches(0.35))
        tf_cat = cat_box.text_frame
        tf_cat.word_wrap = True
        p_cat = tf_cat.paragraphs[0]
        p_cat.text = category.upper()
        p_cat.font.size = Pt(10)
        p_cat.font.bold = True
        p_cat.font.color.rgb = COLOR_RED_CLARO

        # Título do Slide
        title_box = slide.shapes.add_textbox(Inches(0.8), Inches(0.55), Inches(11), Inches(0.7))
        tf_title = title_box.text_frame
        tf_title.word_wrap = True
        p_title = tf_title.paragraphs[0]
        p_title.text = title
        p_title.font.size = Pt(22)
        p_title.font.bold = True
        p_title.font.color.rgb = COLOR_WHITE

        # Logo no canto superior direito
        if os.path.exists(LOGO_PATH):
            try:
                slide.shapes.add_picture(LOGO_PATH, Inches(11.2), Inches(0.25), width=Inches(1.4))
            except Exception:
                pass

    def add_footer(slide):
        footer_box = slide.shapes.add_textbox(Inches(0.8), Inches(7.05), Inches(11.733), Inches(0.35))
        tf = footer_box.text_frame
        p = tf.paragraphs[0]
        p.text = "SPARC • Sistema de Provisionamento e Ativação de Roteadores Claro  |  george.calcmann@claro.com.br"
        p.font.size = Pt(9)
        p.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # SLIDE 1: CAPA EXECUTIVA
    # =========================================================================
    s1 = prs.slides.add_slide(blank_layout)
    set_slide_background(s1)

    # Faixa lateral vermelha
    accent_bar = s1.shapes.add_shape(MSO_SHAPE.RECTANGLE, 0, 0, Inches(0.4), Inches(7.5))
    accent_bar.fill.solid()
    accent_bar.fill.fore_color.rgb = COLOR_RED_CLARO
    accent_bar.line.fill.background()

    # Logo grande na capa
    if os.path.exists(LOGO_PATH):
        try:
            s1.shapes.add_picture(LOGO_PATH, Inches(1.2), Inches(1.0), width=Inches(3.2))
        except Exception:
            pass

    # Tag superior
    tag_box = s1.shapes.add_textbox(Inches(1.2), Inches(2.2), Inches(10), Inches(0.4))
    p_tag = tag_box.text_frame.paragraphs[0]
    p_tag.text = "CLARO S.A.  •  INSTALAÇÃO EMPRESARIAL (ATIVAÇÃO B2B)  •  REGIONAL SC"
    p_tag.font.size = Pt(11)
    p_tag.font.bold = True
    p_tag.font.color.rgb = COLOR_RED_CLARO

    # Título Principal
    t_box = s1.shapes.add_textbox(Inches(1.2), Inches(2.6), Inches(11), Inches(1.8))
    tf_t = t_box.text_frame
    p_t1 = tf_t.paragraphs[0]
    p_t1.text = "SPARC"
    p_t1.font.size = Pt(54)
    p_t1.font.bold = True
    p_t1.font.color.rgb = COLOR_WHITE

    p_t2 = tf_t.add_paragraph()
    p_t2.text = "Sistema de Provisionamento e Ativação de Roteadores Claro"
    p_t2.font.size = Pt(20)
    p_t2.font.bold = True
    p_t2.font.color.rgb = COLOR_CYAN

    # Card de Destaque Estratégico na Capa
    card_capa = s1.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(1.2), Inches(4.5), Inches(10.8), Inches(1.5))
    card_capa.fill.solid()
    card_capa.fill.fore_color.rgb = COLOR_CARD_DARK
    card_capa.line.color.rgb = COLOR_CYAN
    card_capa.line.width = Pt(1.5)

    tf_cc = card_capa.text_frame
    tf_cc.word_wrap = True
    tf_cc.vertical_anchor = MSO_ANCHOR.MIDDLE
    p_cc1 = tf_cc.paragraphs[0]
    p_cc1.text = "PROPÓSITO ESTRATÉGICO:"
    p_cc1.font.size = Pt(11)
    p_cc1.font.bold = True
    p_cc1.font.color.rgb = COLOR_CYAN

    p_cc2 = tf_cc.add_paragraph()
    p_cc2.text = "Eficiência Máxima com Custo Marginal Zero: Plataforma integrada e autônoma na ponta (Edge Computing) para ativação de CPEs (Cisco, HPE e Fortinet). Elimina a necessidade de servidores dedicados de teste e equipamentos de campo caros, acelerando o projeto Visita Única."
    p_cc2.font.size = Pt(13)
    p_cc2.font.color.rgb = COLOR_WHITE

    # Rodapé da Capa
    aut_box = s1.shapes.add_textbox(Inches(1.2), Inches(6.3), Inches(10), Inches(0.8))
    p_aut = aut_box.text_frame.paragraphs[0]
    p_aut.text = "Responsável Técnico: George Calcmann  |  george.calcmann@claro.com.br"
    p_aut.font.size = Pt(12)
    p_aut.font.bold = True
    p_aut.font.color.rgb = COLOR_MUTED

    p_aut2 = aut_box.text_frame.add_paragraph()
    p_aut2.text = "Versão Atual Homologada: v0.8.63 (Windows Single-File & Android Mobile)"
    p_aut2.font.size = Pt(11)
    p_aut2.font.color.rgb = COLOR_GREEN

    # =========================================================================
    # SLIDE 2: O CONTRASTE ESTRATÉGICO (CENTRALIZADO VS SPARC EDGE)
    # =========================================================================
    s2 = prs.slides.add_slide(blank_layout)
    set_slide_background(s2)
    add_header(s2, "Diferencial Competitivo: Centralizado vs. SPARC (Edge)")
    add_footer(s2)

    # Card Esquerda: Projeto Concorrente / Modelo Centralizado
    c_left = s2.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(1.4), Inches(5.6), Inches(5.3))
    c_left.fill.solid()
    c_left.fill.fore_color.rgb = COLOR_CARD_DARK
    c_left.line.color.rgb = COLOR_RED_ACCENT
    c_left.line.width = Pt(1.5)

    tf_l = c_left.text_frame
    tf_l.word_wrap = True
    p_l0 = tf_l.paragraphs[0]
    p_l0.text = "🏢 PROJETOS COM INFRA CENTRALIZADA"
    p_l0.font.size = Pt(14)
    p_l0.font.bold = True
    p_l0.font.color.rgb = COLOR_RED_ACCENT

    items_left = [
        ("Altíssimo CAPEX / OPEX:", "Exige servidores de testes dedicados, infraestrutura de datacenter e licenciamento contínuo."),
        ("Aparelhos de Campo Caros:", "Demanda testadores proprietários (JDSU / Viavi) de dezenas de milhares de reais por técnico."),
        ("Ponto Único de Falha (SPOF):", "Instabilidade no servidor central paralisa técnicos em toda a operação."),
        ("Travamento sem Link WAN:", "Se o circuito da operadora estiver DOWN na ponta, o teste remoto é incapaz de operar."),
        ("Ciclos Longos de Rollout:", "Meses para homologação de infra, aquisição de hardware e liberação de portas em firewall.")
    ]
    for tit, desc in items_left:
        p1 = tf_l.add_paragraph()
        p1.text = f"❌ {tit}"
        p1.font.size = Pt(11.5)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_WHITE
        p2 = tf_l.add_paragraph()
        p2.text = f"    {desc}"
        p2.font.size = Pt(10.5)
        p2.font.color.rgb = COLOR_MUTED

    # Card Direita: SPARC Edge
    c_right = s2.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(6.9), Inches(1.4), Inches(5.6), Inches(5.3))
    c_right.fill.solid()
    c_right.fill.fore_color.rgb = COLOR_CARD_DARK
    c_right.line.color.rgb = COLOR_GREEN
    c_right.line.width = Pt(2)

    tf_r = c_right.text_frame
    tf_r.word_wrap = True
    p_r0 = tf_r.paragraphs[0]
    p_r0.text = "⚡ SOLUÇÃO SPARC: INTELIGÊNCIA NA PONTA"
    p_r0.font.size = Pt(14)
    p_r0.font.bold = True
    p_r0.font.color.rgb = COLOR_GREEN

    items_right = [
        ("Custo Zero de Servidor:", "Processamento 100% no dispositivo local que o técnico já possui (Notebook ou Celular Android)."),
        ("Aproveitamento da Malha Claro:", "Testes de vazão e Y.1564 usam nós e CDNs já existentes da Claro, sem comprar appliances caros."),
        ("Escalabilidade Infinita:", "Arquitetura distribuída (Edge). 10 ou 1.000 técnicos ativam roteadores simultaneamente sem filas."),
        ("Operação Autônoma sem WAN:", "Servidores embarcados (HTTP/FTP/TFTP) recuperam e atualizam firmwares em bancada via OTG/ETH."),
        ("Rollout Imediato (Pronto Hoje):", "Executável único (.exe) e aplicativo Android (.apk) já compilados e operacionais.")
    ]
    for tit, desc in items_right:
        p1 = tf_r.add_paragraph()
        p1.text = f"✅ {tit}"
        p1.font.size = Pt(11.5)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_CYAN
        p2 = tf_r.add_paragraph()
        p2.text = f"    {desc}"
        p2.font.size = Pt(10.5)
        p2.font.color.rgb = COLOR_WHITE

    # =========================================================================
    # SLIDE 3: O CENÁRIO OPERACIONAL ANTERIOR (DORES DE CAMPO)
    # =========================================================================
    s3 = prs.slides.add_slide(blank_layout)
    set_slide_background(s3)
    add_header(s3, "O Processo Manual Anterior: Fricção e Perda de Produtividade")
    add_footer(s3)

    dores = [
        ("⏱️ Tempo Médio Elevado", "35 a 50 minutos por roteador em condições normais. Com reset de senha ou firmware desatualizado, superava facilmente 1h30 por visita."),
        ("📞 Gargalo de Diálogo Técnico", "Dependência contínua de suporte telefônico e intermediação entre o analista Claro e o profissional de campo, gerando filas de espera."),
        ("🔒 Senhas e Equipamentos Presos", "Fricção com equipamentos bloqueados. Exigia digitação manual de comandos arriscados em ROMMON (0x2142) e BootWare (Ctrl+B)."),
        ("🧰 Múltiplos Softwares Dispersos", "O técnico precisava alternar entre PuTTY, TFTPD32, calculadoras de sub-rede, bloco de notas e painel de rede do Windows."),
        ("⚠️ Risco Crítico de Retrabalho", "Falhas de digitação em IPs de WAN/LAN, rotas e máscaras SAIP geravam circuitos inoperantes e necessidade de segundas visitas.")
    ]

    for i, (titulo, desc) in enumerate(dores):
        top_pos = Inches(1.4 + (i * 1.05))
        card = s3.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), top_pos, Inches(11.733), Inches(0.95))
        card.fill.solid()
        card.fill.fore_color.rgb = COLOR_CARD_DARK
        card.line.color.rgb = COLOR_CARD_BORDER
        card.line.width = Pt(1)

        tf = card.text_frame
        tf.word_wrap = True
        tf.vertical_anchor = MSO_ANCHOR.MIDDLE

        p1 = tf.paragraphs[0]
        p1.text = titulo
        p1.font.size = Pt(12.5)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_RED_ACCENT

        p2 = tf.add_paragraph()
        p2.text = desc
        p2.font.size = Pt(11)
        p2.font.color.rgb = COLOR_WHITE

    # =========================================================================
    # SLIDE 4: A SOLUÇÃO — ESTEIRA TUDO-EM-UM SPARC
    # =========================================================================
    s4 = prs.slides.add_slide(blank_layout)
    set_slide_background(s4)
    add_header(s4, "A Esteira SPARC: Automação Inteligente em Menos de 10 Minutos")
    add_footer(s4)

    pilares = [
        ("⚡ Velocidade Extrema", "Esteira ponta a ponta concluída em < 10 minutos com poucos cliques."),
        ("📄 Leitor Automático SAIP", "Importa PDF ou TXT oficial da Claro, preenche WAN/LAN e calcula automaticamente IPs de teste."),
        ("🔓 Desbloqueio Automático", "Quebra senhas desconhecidas em Cisco, HPE e Fortinet sem comandos manuais no bootloader."),
        ("🔌 Servidores Embarcados", "TFTP, HTTP (:8080) e FTP (:2121) 100% nativos no executável/app. Zero softwares de terceiros."),
        ("🧪 Homologação Integrada", "Configura placa do técnico, executa ICMP triplo (LAN/WAN/Web), valida Telnet/SSH e audita banda nominal.")
    ]

    for i, (titulo, desc) in enumerate(pilares):
        col = i % 3
        row = i // 3
        left_pos = Inches(0.8 + (col * 4.0))
        top_pos = Inches(1.5 + (row * 2.6))
        w = Inches(3.7)
        h = Inches(2.3) if row == 0 else Inches(2.3)

        card = s4.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, left_pos, top_pos, w, h)
        card.fill.solid()
        card.fill.fore_color.rgb = COLOR_CARD_DARK
        card.line.color.rgb = COLOR_CYAN
        card.line.width = Pt(1.5)

        tf = card.text_frame
        tf.word_wrap = True
        p1 = tf.paragraphs[0]
        p1.text = titulo
        p1.font.size = Pt(13)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_CYAN

        p2 = tf.add_paragraph()
        p2.text = desc
        p2.font.size = Pt(11)
        p2.font.color.rgb = COLOR_WHITE

    # Adiciona card resumo no espaço restante
    card_resumo = s4.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(4.8), Inches(4.1), Inches(7.733), Inches(2.3))
    card_resumo.fill.solid()
    card_resumo.fill.fore_color.rgb = RGBColor(15, 30, 65)
    card_resumo.line.color.rgb = COLOR_GREEN
    card_resumo.line.width = Pt(1.5)

    tf_cr = card_resumo.text_frame
    tf_cr.word_wrap = True
    p_cr1 = tf_cr.paragraphs[0]
    p_cr1.text = "🎯 IMPACTO DIRETO NO PROJETO VISITA ÚNICA"
    p_cr1.font.size = Pt(13)
    p_cr1.font.bold = True
    p_cr1.font.color.rgb = COLOR_GREEN

    p_cr2 = tf_cr.add_paragraph()
    p_cr2.text = "Ao integrar leitor SAIP, quebra de senhas, carga de firmware e auditoria de banda na mesma esteira autônoma, o SPARC permite que qualquer técnico de campo execute ativações perfeitas logo na primeira visita, sem demandar conhecimento avançado de CLI e sem abrir chamados de apoio."
    p_cr2.font.size = Pt(11.5)
    p_cr2.font.color.rgb = COLOR_WHITE

    # =========================================================================
    # SLIDE 5: MOBILIDADE TOTAL: O SALTO ANDROID (SPARC MOBILE)
    # =========================================================================
    s5 = prs.slides.add_slide(blank_layout)
    set_slide_background(s5)
    add_header(s5, "Mobilidade Total: A Bancada Completa Dentro do Bolso (Android)")
    add_footer(s5)

    cards_mob = [
        ("📱 Operação Híbrida Notebook + Celular",
         "O técnico tem liberdade total de trabalhar com notebook Windows (.exe único) ou direto no seu smartphone Android corporativo (.apk assinado), eliminando a obrigatoriedade de carregar computador para a torre ou rack."),
        ("⚡ HUB USB-C Simultâneo (Serial + TP-Link ETH)",
         "Inovação exclusiva SPARC: permite conectar simultaneamente o cabo de console Serial CLI e a placa de rede Ethernet TP-Link no mesmo conector USB-C do celular, sem travamento de porta e sem troca física de cabos."),
        ("🔌 Transferência Local em Bancada sem WAN",
         "Quando o roteador está em bancada de testes ou a fibra do cliente ainda está sem link (WAN DOWN), o celular fornece o firmware homologado via servidor local embarcado (HTTP :8080 / FTP :2121) a velocidade de rede local (1 Gbps)."),
        ("🛡️ Isolamento Rigoroso de Rede (Process Binding)",
         "Garante 100% de precisão técnica: vincula os sockets do aplicativo estritamente à interface Ethernet conectada ao roteador, impedindo vazamentos ou falsos positivos de banda/ping via dados móveis (4G/5G).")
    ]

    for i, (titulo, desc) in enumerate(cards_mob):
        col = i % 2
        row = i // 2
        left_pos = Inches(0.8 + (col * 6.0))
        top_pos = Inches(1.5 + (row * 2.65))
        w = Inches(5.7)
        h = Inches(2.4)

        card = s5.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, left_pos, top_pos, w, h)
        card.fill.solid()
        card.fill.fore_color.rgb = COLOR_CARD_DARK
        card.line.color.rgb = COLOR_CYAN
        card.line.width = Pt(1.5)

        tf = card.text_frame
        tf.word_wrap = True
        p1 = tf.paragraphs[0]
        p1.text = titulo
        p1.font.size = Pt(13)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_CYAN

        p2 = tf.add_paragraph()
        p2.text = desc
        p2.font.size = Pt(11)
        p2.font.color.rgb = COLOR_WHITE

    # =========================================================================
    # SLIDE 6: MÉTRICAS DE PRODUTIVIDADE & ROI
    # =========================================================================
    s6 = prs.slides.add_slide(blank_layout)
    set_slide_background(s6)
    add_header(s6, "Impacto Operacional & Retorno sobre o Investimento")
    add_footer(s6)

    kpis = [
        ("-88%", "Tempo de Ativação", "De ~50 min para < 10 min por equipamento."),
        ("100%", "Aderência SAIP", "Zero erro de digitação em IPs, rotas e máscaras."),
        ("R$ 0", "Custo de Servidores", "Zero servidores dedicados, zero datacenter adicional."),
        ("1 App", "Solução Portátil", "Substitui PuTTY, TFTPD32, scripts e calculadoras.")
    ]

    for i, (valor, rotulo, detalhe) in enumerate(kpis):
        left_pos = Inches(0.8 + (i * 3.0))
        card = s6.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, left_pos, Inches(1.5), Inches(2.733), Inches(2.5))
        card.fill.solid()
        card.fill.fore_color.rgb = COLOR_CARD_DARK
        card.line.color.rgb = COLOR_RED_CLARO if i == 0 else COLOR_CYAN
        card.line.width = Pt(1.5)

        tf = card.text_frame
        tf.word_wrap = True
        p_val = tf.paragraphs[0]
        p_val.text = valor
        p_val.font.size = Pt(36)
        p_val.font.bold = True
        p_val.font.color.rgb = COLOR_RED_CLARO if i == 0 else COLOR_GREEN

        p_rot = tf.add_paragraph()
        p_rot.text = rotulo
        p_rot.font.size = Pt(12)
        p_rot.font.bold = True
        p_rot.font.color.rgb = COLOR_WHITE

        p_det = tf.add_paragraph()
        p_det.text = detalhe
        p_det.font.size = Pt(10)
        p_det.font.color.rgb = COLOR_MUTED

    # Painel Inferior de Benefícios de Gestão
    card_gestao = s6.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(4.3), Inches(11.733), Inches(2.5))
    card_gestao.fill.solid()
    card_gestao.fill.fore_color.rgb = COLOR_CARD_DARK
    card_gestao.line.color.rgb = COLOR_CARD_BORDER
    card_gestao.line.width = Pt(1)

    tf_g = card_gestao.text_frame
    tf_g.word_wrap = True
    p_g0 = tf_g.paragraphs[0]
    p_g0.text = "💡 GANHOS ESTRATÉGICOS PARA A CLARO S.A.:"
    p_g0.font.size = Pt(13)
    p_g0.font.bold = True
    p_g0.font.color.rgb = COLOR_CYAN

    beneficios = [
        ("Escalabilidade Imediata:", "Capacidade de preparar dezenas de roteadores diariamente por técnico sem estresse operacional."),
        ("Democratização do Conhecimento:", "Técnicos de nível básico executam tarefas de nível sênior (quebra de senha, restauração em ROMMON, bootware)."),
        ("Economia de Combustível e Horas:", "Redução drástica de visitas improdutivas e devolução indevida de CPEs perfeitos para a logística."),
        ("Segurança Operacional:", "Auditoria automática com emissão de relatórios técnicos completos em PDF para documentação da ativação.")
    ]
    for b_tit, b_desc in beneficios:
        p = tf_g.add_paragraph()
        p.text = f"• {b_tit} {b_desc}"
        p.font.size = Pt(11)
        p.font.color.rgb = COLOR_WHITE

    # =========================================================================
    # SLIDE 7: ROADMAP DE EVOLUÇÃO
    # =========================================================================
    s7 = prs.slides.add_slide(blank_layout)
    set_slide_background(s7)
    add_header(s7, "Roadmap Estratégico: Da Ativação à Assistência Técnica")
    add_footer(s7)

    # Coluna Esquerda: Em Andamento Avançado
    c_and = s7.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(1.4), Inches(5.6), Inches(5.3))
    c_and.fill.solid()
    c_and.fill.fore_color.rgb = COLOR_CARD_DARK
    c_and.line.color.rgb = COLOR_GREEN
    c_and.line.width = Pt(1.5)

    tf_and = c_and.text_frame
    tf_and.word_wrap = True
    p_and0 = tf_and.paragraphs[0]
    p_and0.text = "🚀 ANDAMENTO ATUAL DAS ENTREGAS"
    p_and0.font.size = Pt(14)
    p_and0.font.bold = True
    p_and0.font.color.rgb = COLOR_GREEN

    andamento_itens = [
        ("SPARC Mobile (Android) — 85%", "Operação com HUB USB-C simultâneo (Serial + TP-Link ETH), esteira completa e homologação em campo."),
        ("Repositório Central de Firmwares — 95%", "Download automático da nuvem (GitHub Release) para todas as séries homologadas da Claro sem manipulação manual."),
        ("Módulo Assistência Técnica — 80%", "Ferramentas dedicadas para recuperação de placas corrompidas, quebra forçada de BIOS e limpeza de NVRAM."),
        ("Certidões Avançadas Y.1564 — 75%", "Geração de laudo técnico de ativação utilizando nós neutros da Claro, sem depender de testadores dedicados.")
    ]
    for tit, desc in andamento_itens:
        p1 = tf_and.add_paragraph()
        p1.text = f"▶ {tit}"
        p1.font.size = Pt(12)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_CYAN
        p2 = tf_and.add_paragraph()
        p2.text = f"    {desc}"
        p2.font.size = Pt(10.5)
        p2.font.color.rgb = COLOR_WHITE

    # Coluna Direita: Próximos Passos (Projeção)
    c_fut = s7.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(6.9), Inches(1.4), Inches(5.6), Inches(5.3))
    c_fut.fill.solid()
    c_fut.fill.fore_color.rgb = COLOR_CARD_DARK
    c_fut.line.color.rgb = COLOR_CYAN
    c_fut.line.width = Pt(1.5)

    tf_fut = c_fut.text_frame
    tf_fut.word_wrap = True
    p_fut0 = tf_fut.paragraphs[0]
    p_fut0.text = "🎯 PROJEÇÃO E INTEGRAÇÕES FUTURAS"
    p_fut0.font.size = Pt(14)
    p_fut0.font.bold = True
    p_fut0.font.color.rgb = COLOR_CYAN

    futuro_itens = [
        ("Integração Sistêmica Claro:", "Conexão direta com sistemas corporativos Claro (SAIP / NetWin / Despacho) para baixa automática de OS e feedback em tempo real."),
        ("Expansão para Switches de Acesso:", "Inclusão de templates automatizados para switches de borda (Datacom, Huawei, Cisco Catalyst)."),
        ("Assinatura Digital de Ativação:", "Certificação criptográfica do laudo de ativação assinado digitalmente na ponta pelo aplicativo do operador."),
        ("Disponibilização Corporativa Nacional:", "Empacotamento institucional para distribuição entre todas as regionais de Instalação Empresarial da Claro.")
    ]
    for tit, desc in futuro_itens:
        p1 = tf_fut.add_paragraph()
        p1.text = f"★ {tit}"
        p1.font.size = Pt(12)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_AMBER
        p2 = tf_fut.add_paragraph()
        p2.text = f"    {desc}"
        p2.font.size = Pt(10.5)
        p2.font.color.rgb = COLOR_WHITE

    # =========================================================================
    # SLIDE 8: CONCLUSÃO & MENSAGEM FINAL
    # =========================================================================
    s8 = prs.slides.add_slide(blank_layout)
    set_slide_background(s8)
    add_header(s8, "Conclusão: Eficiência Tecnológica na Ponta da Operação")
    add_footer(s8)

    card_conc = s8.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(1.5), Inches(11.733), Inches(3.6))
    card_conc.fill.solid()
    card_conc.fill.fore_color.rgb = COLOR_CARD_DARK
    card_conc.line.color.rgb = COLOR_RED_CLARO
    card_conc.line.width = Pt(2)

    tf_c = card_conc.text_frame
    tf_c.word_wrap = True
    p_c1 = tf_c.paragraphs[0]
    p_c1.text = "A INOVAÇÃO QUE TRANSFORMA A REALIDADE DO CAMPO"
    p_c1.font.size = Pt(16)
    p_c1.font.bold = True
    p_c1.font.color.rgb = COLOR_CYAN

    p_c2 = tf_c.add_paragraph()
    p_c2.text = (
        "O SPARC comprova na prática que resultados extraordinários em operações de telecomunicações "
        "não dependem de investimentos milionários em servidores centralizados ou na compra de ferramentas proprietárias caras.\n\n"
        "Com tecnologia moderna aplicada diretamente na ponta (Edge Computing), entregamos:\n"
        "• Agilidade extrema para os técnicos de campo e de bancada;\n"
        "• Custo de infraestrutura virtualmente ZERO para a Claro;\n"
        "• Padronização técnica rigorosa e garantia de cumprimento das metas do projeto Visita Única."
    )
    p_c2.font.size = Pt(13)
    p_c2.font.color.rgb = COLOR_WHITE

    # Caixa de Encerramento e Contato
    card_fim = s8.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(5.3), Inches(11.733), Inches(1.4))
    card_fim.fill.solid()
    card_fim.fill.fore_color.rgb = RGBColor(18, 30, 56)
    card_fim.line.color.rgb = COLOR_CARD_BORDER
    card_fim.line.width = Pt(1)

    tf_f = card_fim.text_frame
    tf_f.word_wrap = True
    p_f1 = tf_f.paragraphs[0]
    p_f1.text = "CLARO S.A.  •  REGIONAL SANTA CATARINA  •  INSTALAÇÃO EMPRESARIAL"
    p_f1.font.size = Pt(12)
    p_f1.font.bold = True
    p_f1.font.color.rgb = COLOR_RED_CLARO

    p_f2 = tf_f.add_paragraph()
    p_f2.text = "Contato do Projeto: George Calcmann  |  george.calcmann@claro.com.br"
    p_f2.font.size = Pt(13)
    p_f2.font.bold = True
    p_f2.font.color.rgb = COLOR_WHITE

    out_file = r"C:\SPARC\Apresentacao_Executiva_SPARC.pptx"
    prs.save(out_file)
    print(f"Apresentação salva com sucesso em: {out_file}")

if __name__ == "__main__":
    create_presentation()
