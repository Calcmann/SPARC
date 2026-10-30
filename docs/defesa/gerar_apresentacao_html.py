# -*- coding: utf-8 -*-
"""
Gera a versão HTML da Apresentação Executiva SPARC formatada no padrão visual oficial CLARO.
- Paleta oficial Claro Red (#DA291C), White, Charcoal (#1F2937) e Cinza Suave
- Logo esférico 3D da Claro no canto inferior direito de todos os slides
- Imagens da UI Desktop e das 2 telas do Mobile Android embutidas em Base64
- Navegação por teclado (Setas, Espaço, F11 tela cheia, Home, End)
"""

import os
import base64

HERE = os.path.dirname(os.path.abspath(__file__))
CLARO_ASSETS = os.path.join(HERE, "claro_template_assets")
USER_ASSETS = os.path.join(HERE, "user_ppt_assets")

def get_b64(path):
    if os.path.exists(path):
        with open(path, "rb") as f:
            ext = os.path.splitext(path)[1].lower().replace(".", "")
            if ext == "jpg":
                ext = "jpeg"
            return f"data:image/{ext};base64," + base64.b64encode(f.read()).decode("utf-8")
    return ""

logo_claro_b64 = get_b64(os.path.join(CLARO_ASSETS, "slide_1_shape_9_Imagem_8.png"))
cover_claro_b64 = get_b64(os.path.join(CLARO_ASSETS, "slide_1_shape_5_Imagem_4.jpg"))
desktop_ui_b64 = get_b64(os.path.join(USER_ASSETS, "slide_2_36_Imagem_35.png"))
mobile_1_b64 = get_b64(os.path.join(USER_ASSETS, "slide_3_21_Imagem_20.png"))
mobile_2_b64 = get_b64(os.path.join(USER_ASSETS, "slide_3_23_Imagem_22.png"))

html_content = f"""<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>SPARC — Apresentação Executiva Claro</title>
  <script src="https://www.gstatic.com/antigravity/web/dev/tailwindcss.min.js"></script>
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800;900&display=swap');
    body {{ font-family: 'Inter', system-ui, -apple-system, sans-serif; }}
    .slide {{ display: none; }}
    .slide.active {{ display: flex; }}
    .claro-sun-bg {{
      background-color: #FFFFFF;
      background-image: radial-gradient(circle at -5% 105%, rgba(218, 41, 28, 0.08) 0%, rgba(218, 41, 28, 0.03) 30%, transparent 60%);
    }}
  </style>
</head>
<body class="bg-slate-100 text-slate-800 min-h-screen flex flex-col justify-between selection:bg-red-600 selection:text-white">

  <!-- TOP BAR -->
  <header class="bg-white border-b border-slate-200 px-6 py-2.5 flex items-center justify-between sticky top-0 z-50 shadow-sm">
    <div class="flex items-center space-x-3">
      <span class="inline-flex items-center px-2.5 py-1 rounded-full text-xs font-black bg-red-600 text-white tracking-wider">
        CLARO S.A.
      </span>
      <span class="text-sm font-bold text-slate-800">
        SPARC · Ativação Empresarial
      </span>
      <span class="hidden md:inline-block text-xs px-2 py-0.5 rounded bg-slate-100 text-slate-500 border border-slate-200">
        Regional Santa Catarina
      </span>
    </div>

    <!-- CONTROLES DE SLIDE -->
    <div class="flex items-center space-x-2">
      <span id="slideIndicator" class="text-xs font-semibold text-slate-500 mr-2">Slide 1 de 9</span>
      <button onclick="prevSlide()" class="p-1.5 rounded-lg bg-slate-100 hover:bg-slate-200 text-slate-700 transition" title="Anterior (←)">
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7"/></svg>
      </button>
      <button onclick="nextSlide()" class="p-1.5 rounded-lg bg-red-600 hover:bg-red-700 text-white transition font-medium" title="Próximo (→)">
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/></svg>
      </button>
      <button onclick="toggleFullScreen()" class="p-1.5 rounded-lg bg-slate-100 hover:bg-slate-200 text-slate-700 transition hidden sm:inline-block" title="Tela Cheia (F11)">
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-5h-4m4 0v4m0 0l-5-5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4"/></svg>
      </button>
    </div>
  </header>

  <!-- CONTAINER PRINCIPAL (16:9 PROPORÇÃO) -->
  <main class="flex-1 max-w-7xl w-full mx-auto p-4 md:p-6 flex flex-col justify-center">

    <!-- SLIDE 1: CAPA OFICIAL CLARO -->
    <section class="slide active flex-col justify-between bg-gradient-to-r from-red-600 via-red-600 to-red-700 text-white rounded-2xl p-8 md:p-12 shadow-2xl relative overflow-hidden min-h-[600px] border border-red-500">
      <div class="absolute right-0 bottom-0 pointer-events-none opacity-20">
        <svg width="450" height="450" viewBox="0 0 100 100" fill="white">
          <circle cx="0" cy="100" r="45" fill="none" stroke="white" stroke-width="8"/>
          <line x1="0" y1="100" x2="60" y2="40" stroke="white" stroke-width="8"/>
          <line x1="0" y1="100" x2="80" y2="100" stroke="white" stroke-width="8"/>
          <line x1="0" y1="100" x2="0" y2="20" stroke="white" stroke-width="8"/>
        </svg>
      </div>

      <div>
        <div class="inline-flex items-center gap-2 px-3 py-1 rounded-full text-xs font-bold bg-white text-red-600 shadow-sm mb-4">
          <span class="w-2 h-2 rounded-full bg-red-600 animate-pulse"></span>
          CLARO S.A. — ATIVAÇÃO EMPRESARIAL
        </div>
        <h1 class="text-5xl md:text-7xl font-black text-white tracking-tight leading-none mb-2">
          SPARC
        </h1>
        <p class="text-sm md:text-base font-bold text-red-100 uppercase tracking-wider mb-2">
          Sistema de Provisionamento e Ativação de Roteadores da CLARO
        </p>
        <p class="text-xl md:text-2xl font-bold text-white mb-2">
          Novas Oportunidades, Agilidade, Flexibilidade e Redução de Custos
        </p>
        <p class="text-red-100 text-sm md:text-base max-w-2xl leading-relaxed mb-8">
          Solução autônoma da CLARO no apoio às Ativações de campo e Visita Única.
        </p>
      </div>

      <!-- 4 CARDS RESUMO -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mt-auto">
        <div class="bg-red-800/60 backdrop-blur border border-red-400/40 rounded-xl p-4">
          <span class="text-[11px] font-black text-red-200 tracking-wider">FLEXIBILIDADE OPERACIONAL</span>
          <p class="text-xs text-white mt-1.5 leading-relaxed">Em bancada ou no campo, SPARC está pronto para preparação do equipamento. Cache de firmwares homologados embarcado no app.</p>
        </div>
        <div class="bg-red-800/60 backdrop-blur border border-red-400/40 rounded-xl p-4">
          <span class="text-[11px] font-black text-red-200 tracking-wider">BAIXO CUSTO</span>
          <p class="text-xs text-white mt-1.5 leading-relaxed">Sem cobrança de licenças, a ativação é online mediante cadastro via módulo Administrador, cada cluster poderá administrar suas equipes.</p>
        </div>
        <div class="bg-red-800/60 backdrop-blur border border-red-400/40 rounded-xl p-4">
          <span class="text-[11px] font-black text-red-200 tracking-wider">AGILIDADE</span>
          <p class="text-xs text-white mt-1.5 leading-relaxed">Suporte a novos modelos e implementações ágeis, sem filas de fornecedor.</p>
        </div>
        <div class="bg-red-800/60 backdrop-blur border border-red-400/40 rounded-xl p-4">
          <span class="text-[11px] font-black text-red-200 tracking-wider">OPORTUNIDADES</span>
          <p class="text-xs text-white mt-1.5 leading-relaxed">Roteador já pode sair 100% testado da base Claro (Plug & Play), permitindo abater o item pago ao parceiro terceirizado, versatilidade com diferentes modos de operação.</p>
        </div>
      </div>

      <!-- LOGO CLARO -->
      <div class="absolute bottom-6 right-6">
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-16 h-auto drop-shadow-md">
      </div>
    </section>

    <!-- SLIDE 2: COMO A FERRAMENTA FUNCIONA NA PRÁTICA -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-4 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">🔴 CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Como a Ferramenta Funciona na Prática: Esteira Automatizada</h2>
            <p class="text-xs text-slate-500 mt-0.5">Visão operacional da interface unificada que guia o técnico do diagnóstico físico ao laudo formal</p>
          </div>
          <span class="hidden sm:inline-block text-xs font-bold px-3 py-1 bg-red-50 text-red-600 rounded-full border border-red-200">
            Fluxo Unificado
          </span>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-12 gap-5 items-stretch">
          <!-- Imagem UI -->
          <div class="lg:col-span-7 bg-white border border-slate-200 rounded-xl p-3 shadow-sm flex flex-col justify-between">
            <div class="flex items-center justify-between pb-2 mb-2 border-b border-slate-100 text-xs">
              <span class="font-bold text-slate-800 flex items-center gap-1.5">
                <span class="w-2.5 h-2.5 rounded-full bg-red-600"></span> INTERFACE PRINCIPAL — FLUXO UNIFICADO
              </span>
              <span class="text-[11px] text-slate-400">Windows Desktop .NET 8</span>
            </div>
            <div class="rounded-lg overflow-hidden border border-slate-200 shadow-md">
              <img src="{desktop_ui_b64}" alt="SPARC Desktop UI" class="w-full h-auto object-cover max-h-[380px]">
            </div>
            <div class="mt-3 p-2 rounded-lg bg-slate-50 border border-slate-200 text-xs text-slate-600">
              <strong class="text-slate-800">Tela Única Inteligente:</strong> Integra detecção COM automática, leitor de SAIP, terminal CLI colorido e esteira passo a passo com feedback em tempo real.
            </div>
          </div>

          <!-- 4 Passos Operacionais -->
          <div class="lg:col-span-5 flex flex-col justify-between space-y-2.5">
            <div class="bg-blue-50/70 border border-blue-200 rounded-xl p-3">
              <div class="flex items-center gap-2 mb-1">
                <span class="px-2 py-0.5 rounded text-xs font-black bg-blue-600 text-white">P1</span>
                <h4 class="text-xs font-bold text-slate-900">1. Detecção e Autenticação Serial</h4>
              </div>
              <p class="text-xs text-slate-600 leading-relaxed">
                Identifica automaticamente Cisco, HPE ou FortiGate. Desbloqueia e autentica o console sem intervenção manual.
              </p>
            </div>

            <div class="bg-emerald-50/70 border border-emerald-200 rounded-xl p-3">
              <div class="flex items-center gap-2 mb-1">
                <span class="px-2 py-0.5 rounded text-xs font-black bg-emerald-600 text-white">P2</span>
                <h4 class="text-xs font-bold text-slate-900">2. Leitura Inteligente do SAIP (PDF/TXT)</h4>
              </div>
              <p class="text-xs text-slate-600 leading-relaxed">
                Importa a ficha técnica oficial com 1 clique. Preenche WAN, LAN, BGP, rotas e hostname com 100% de precisão e zero digitação. <em>Oportunidade: integração via API Claro com MODOS.</em>
              </p>
            </div>

            <div class="bg-amber-50/70 border border-amber-200 rounded-xl p-3">
              <div class="flex items-center gap-2 mb-1">
                <span class="px-2 py-0.5 rounded text-xs font-black bg-amber-600 text-white">P3</span>
                <h4 class="text-xs font-bold text-slate-900">3. Gravação de Firmware & Provisionamento</h4>
              </div>
              <p class="text-xs text-slate-600 leading-relaxed">
                Servidor TFTP/HTTP nativo grava firmware via cabo de rede na porta WAN/LAN. Aplica o script do circuito e verifica link físico.
              </p>
            </div>

            <div class="bg-red-50/70 border border-red-200 rounded-xl p-3">
              <div class="flex items-center gap-2 mb-1">
                <span class="px-2 py-0.5 rounded text-xs font-black bg-red-600 text-white">P4</span>
                <h4 class="text-xs font-bold text-slate-900">4. Teste de Banda com relatório em PDF</h4>
              </div>
              <p class="text-xs text-slate-600 leading-relaxed">
                Audita ICMP (LAN/WAN/Web), valida Telnet/SSH, afere velocidade e emite resultado formal em PDF com QR Code.
              </p>
            </div>
          </div>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro | Regional SC</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 3: FLEXIBILIDADE DE PLATAFORMAS -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-4 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">🔴 CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Flexibilidade de Plataformas: Desktop na Base + Mobile em Campo</h2>
            <p class="text-xs text-slate-500 mt-0.5">A mesma inteligência operacional adaptada para alta produtividade com mobilidade total</p>
          </div>
          <span class="hidden sm:inline-block text-xs font-bold px-3 py-1 bg-red-50 text-red-600 rounded-full border border-red-200">
            Multiplataforma
          </span>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-12 gap-6 items-stretch">
          <!-- 2 Telas do Mobile -->
          <div class="md:col-span-6 bg-white border border-slate-200 rounded-xl p-4 shadow-sm flex flex-col justify-between">
            <div class="flex items-center justify-between mb-3">
              <span class="px-2.5 py-1 rounded text-xs font-black bg-red-600 text-white">SPARC MOBILE ANDROID (.APK)</span>
              <span class="text-xs text-slate-500">Modo Portátil / Campo</span>
            </div>
            <div class="flex justify-center gap-4 py-2">
              <div class="rounded-xl overflow-hidden border border-slate-300 shadow-md h-80 bg-slate-900">
                <img src="{mobile_1_b64}" alt="SPARC Mobile 1" class="h-full w-auto object-contain">
              </div>
              <div class="rounded-xl overflow-hidden border border-slate-300 shadow-md h-80 bg-slate-900">
                <img src="{mobile_2_b64}" alt="SPARC Mobile 2" class="h-full w-auto object-contain">
              </div>
            </div>
            <div class="text-center text-xs text-slate-500 mt-2">
              Roda direto no celular Android via cabo USB-Serial OTG
            </div>
          </div>

          <!-- Texto de Benefícios -->
          <div class="md:col-span-6 bg-white border border-slate-200 rounded-xl p-5 shadow-sm flex flex-col justify-between">
            <div>
              <span class="text-xs font-bold text-red-600 tracking-wider">MOBILIDADE E AUTONOMIA TOTAL</span>
              <h3 class="text-lg font-bold text-slate-900 mt-1 mb-4">SPARC Mobile no Smartphone Corporativo</h3>
              <ul class="text-xs text-slate-700 space-y-3.5">
                <li class="flex items-start gap-2">
                  <span class="text-emerald-600 font-black text-sm">✔</span>
                  <span><strong class="text-slate-900">100% Portátil:</strong> Roda no celular corporativo Android do técnico de campo via cabo USB-Serial e adaptador OTG padrão.</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="text-emerald-600 font-black text-sm">✔</span>
                  <span><strong class="text-slate-900">Validação e Laudo na Ponta:</strong> Afere largura de banda e reporta resultado via arquivo PDF com QR Code.</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="text-emerald-600 font-black text-sm">✔</span>
                  <span><strong class="text-slate-900">Modelo SPARC Tester:</strong> Ideal para parceiros terceirizados apenas validarem a entrega sem mexer na config.</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="text-emerald-600 font-black text-sm">✔</span>
                  <span><strong class="text-slate-900">Cache de Firmware Offline:</strong> Imagens homologadas salvas na memória interna do aparelho para uso sem internet.</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="text-emerald-600 font-black text-sm">✔</span>
                  <span><strong class="text-slate-900">Mesma Base Tecnológica:</strong> Compartilha 100% dos algoritmos de detecção e validação do Windows Desktop.</span>
                </li>
              </ul>
            </div>
            <div class="mt-4 pt-3 border-t border-slate-100 text-xs text-red-600 font-semibold">
              Mobilidade e agilidade em campo sem depender de carregar notebook
            </div>
          </div>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro | Regional SC</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 4: OS 4 PILARES ESTRATÉGICOS -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-5 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">🔴 CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Os 4 Pilares Estratégicos</h2>
            <p class="text-xs text-slate-500 mt-0.5">Comparativo focado nas reais dores de campo, flexibilidade e retorno financeiro para a Claro</p>
          </div>
          <span class="hidden sm:inline-block text-xs font-bold px-3 py-1 bg-red-50 text-red-600 rounded-full border border-red-200">
            Pilares de Decisão
          </span>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <!-- Pilar 1 -->
          <div class="bg-white border border-slate-200 rounded-xl p-4 flex flex-col justify-between shadow-sm hover:border-red-500 transition">
            <div>
              <span class="px-2 py-0.5 rounded text-[11px] font-black bg-red-600 text-white">1. FLEXIBILIDADE DE USO</span>
              <h3 class="text-base font-bold text-slate-900 mt-2 mb-3">Modo Bancada</h3>
              <ul class="text-xs text-slate-700 space-y-2.5">
                <li><strong class="text-emerald-600">✔ Zero Dependência do Link:</strong> O SPARC foi concebido sob o paradigma Offline-First, operando de forma 100% autónoma diretamente no terminal do técnico, sem necessidade do enlace de acesso durante as etapas preliminares.</li>
                <li><strong class="text-emerald-600">✔ Cache de Firmware Local:</strong> Imagens homologadas (.bin/.ipe/.out) embarcadas, com atualizações via download sempre que necessário.</li>
                <li><strong class="text-emerald-600">✔ Cabo Serial + Ethernet:</strong> Suporta recuperação de firmwares corrompidos via TFTP nativo, restaura configurações de equipamentos com senhas desconhecidas.</li>
              </ul>
            </div>
          </div>

          <!-- Pilar 2 -->
          <div class="bg-white border border-slate-200 rounded-xl p-4 flex flex-col justify-between shadow-sm hover:border-red-500 transition">
            <div>
              <span class="px-2 py-0.5 rounded text-[11px] font-black bg-red-600 text-white">2. REDUÇÃO DE CUSTOS</span>
              <h3 class="text-base font-bold text-slate-900 mt-2 mb-3">Economia Direta</h3>
              <ul class="text-xs text-slate-700 space-y-2.5">
                <li><strong class="text-emerald-600">✔ R$ 0 em Licenças:</strong> Ativo 100% interno Claro. Sem custos com licenças, manutenções e implementações.</li>
                <li><strong class="text-emerald-600">✔ Flexibilidade e Abatimento por OS:</strong> Roteador pode sair pronto da base, configurado e testado; permite renegociar o item 'configuração de CPE’.</li>
                <li><strong class="text-emerald-600">✔ Fim das Visitas Perdidas:</strong> Triagem antecipada elimina retrabalho de técnicos por roteador defeituoso.</li>
              </ul>
            </div>
          </div>

          <!-- Pilar 3 -->
          <div class="bg-white border border-slate-200 rounded-xl p-4 flex flex-col justify-between shadow-sm hover:border-red-500 transition">
            <div>
              <span class="px-2 py-0.5 rounded text-[11px] font-black bg-red-600 text-white">3. AGILIDADE & SUPORTE</span>
              <h3 class="text-base font-bold text-slate-900 mt-2 mb-3">Soberania Técnica Claro</h3>
              <ul class="text-xs text-slate-700 space-y-2.5">
                <li><strong class="text-emerald-600">✔ Sem Fila de Fornecedor:</strong> com SPARC dispensamos longas negociações com fornecedores, aditivos contratuais, defesas de CAPEX, prazos dilatados para ajustes e desenvolvimento.</li>
                <li><strong class="text-emerald-600">✔ Desenvolvimento Colaborativo (Co-design):</strong> alinhamento direto e contínuo com os pontos focais da operação, incorporando feedbacks práticos e evoluindo a solução a partir das dores reais vivenciadas pelas equipes de campo.</li>
                <li><strong class="text-emerald-600">✔ Domínio do Conhecimento:</strong> Know-how mantido 100% dentro da Claro; zero risco de dependência tecnológica.</li>
              </ul>
            </div>
          </div>

          <!-- Pilar 4 -->
          <div class="bg-white border border-slate-200 rounded-xl p-4 flex flex-col justify-between shadow-sm hover:border-red-500 transition">
            <div>
              <span class="px-2 py-0.5 rounded text-[11px] font-black bg-red-600 text-white">4. TRIAGEM DE HARDWARE</span>
              <h3 class="text-base font-bold text-slate-900 mt-2 mb-3">Blindagem da VU</h3>
              <ul class="text-xs text-slate-700 space-y-2.5">
                <li><strong class="text-emerald-600">✔ Teste Prévio na Base:</strong> Portas de rede, memória Flash e boot podem ser avaliados antecipadamente com as facilidades do SPARC.</li>
                <li><strong class="text-emerald-600">✔ Substituição Antecipada:</strong> Se houver defeito físico, troca antes de sair para o cliente.</li>
                <li><strong class="text-emerald-600">✔ Entrega Plug & Play:</strong> Roteador sai da base etiquetado, com IP do circuito e firmware homologado gravado.</li>
                <li><strong class="text-emerald-600">✔ Produtividade em Bancada:</strong> Um único técnico interno prepara dezenas de aparelhos/dia em menos de 10 min cada.</li>
              </ul>
            </div>
          </div>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro | Regional SC</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 5: IMPACTO MENSURADO -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-5 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">🔴 CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Impacto Mensurado, Agilidade de Suporte e Soberania Tecnológica</h2>
            <p class="text-xs text-slate-500 mt-0.5">Resultados consolidados em campo e comparação da governança de longo prazo</p>
          </div>
          <span class="hidden sm:inline-block text-xs font-bold px-3 py-1 bg-red-50 text-red-600 rounded-full border border-red-200">
            KPIs e Governança
          </span>
        </div>

        <!-- 4 KPIs -->
        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-5">
          <div class="bg-white border border-slate-200 rounded-xl p-4 text-center shadow-sm">
            <div class="text-3xl md:text-4xl font-black text-red-600 mb-1">-88%</div>
            <div class="text-xs font-bold text-slate-900 uppercase">Tempo de Ativação</div>
            <div class="text-[11px] text-slate-500 mt-1">De ~50 min com técnico experiente para 10 min com instalador por roteador</div>
          </div>
          <div class="bg-white border border-slate-200 rounded-xl p-4 text-center shadow-sm">
            <div class="text-3xl md:text-4xl font-black text-emerald-600 mb-1">100%</div>
            <div class="text-xs font-bold text-slate-900 uppercase">Aderência SAIP</div>
            <div class="text-[11px] text-slate-500 mt-1">Zero erro de digitação de IPs, rotas e máscaras</div>
          </div>
          <div class="bg-white border border-slate-200 rounded-xl p-4 text-center shadow-sm">
            <div class="text-3xl md:text-4xl font-black text-emerald-600 mb-1">R$ 0</div>
            <div class="text-xs font-bold text-slate-900 uppercase">Licenças & Servidores</div>
            <div class="text-[11px] text-slate-500 mt-1">Economia contínua de Capex/Opex de fornecedor</div>
          </div>
          <div class="bg-white border border-slate-200 rounded-xl p-4 text-center shadow-sm">
            <div class="text-3xl md:text-4xl font-black text-red-600 mb-1">0%</div>
            <div class="text-xs font-bold text-slate-900 uppercase">Visitas Perdidas por HW</div>
            <div class="text-[11px] text-slate-500 mt-1">Triagem prévia na base elimina falhas em campo</div>
          </div>
        </div>

        <!-- Comparativo Sustentação -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div class="bg-emerald-50/50 border border-emerald-300 rounded-xl p-4">
            <h4 class="text-sm font-bold text-emerald-800 mb-2 flex items-center gap-1.5">
              <span>✔</span> SPARC: SUSTENTAÇÃO ÁGIL INTERNA
            </h4>
            <ul class="text-xs text-slate-700 space-y-2">
              <li>• <strong class="text-slate-900">Evolução Ágil e Contínua:</strong> Relacionamento direto com pontos focais, feedbacks colaborativos, desenvolvimento conjunto.</li>
              <li>• <strong class="text-slate-900">Suporte Multi-Fabricante:</strong> Agilidade na ampliação do suporte a novos modelos, podendo evoluir para outras tipos de equipamentos como switches de acesso.</li>
              <li>• <strong class="text-slate-900">Soberania Tecnológica:</strong> As soluções desenvolvidas aqui ficam em casa. Código e know-how mantidos 100% Claro.</li>
            </ul>
          </div>

          <div class="bg-red-50/50 border border-red-300 rounded-xl p-4">
            <h4 class="text-sm font-bold text-red-800 mb-2 flex items-center gap-1.5">
              <span>✖</span> RISCOS COM MODELOS EXTERNOS
            </h4>
            <ul class="text-xs text-slate-700 space-y-2">
              <li>• <strong class="text-slate-900">Dependência Crônica de Fornecedor:</strong> Qualquer customização ou novo modelo depende de orçamento adicional e fila de backlog.</li>
              <li>• <strong class="text-slate-900">Contratos periódicos e Reajustes:</strong> Custos recorrentes que encarecem a operação ano a ano (lock-in comercial).</li>
              <li>• <strong class="text-slate-900">Falta de Flexibilidade:</strong> Limitação contratual que impede implementações e ajustes sem renegociações burocráticas.</li>
            </ul>
          </div>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro | Regional SC</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 6: OPORTUNIDADES COM FORMATOS DE OPERAÇÃO -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-4 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">🔴 CLARO S.A. — ATIVAÇÃO EMPRESARIAL · SPARC</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Oportunidades com Formatos de Operação</h2>
            <p class="text-xs text-slate-500 mt-0.5">A esteira que desonera contratos terceirizados e garante zero entrega com defeito físico</p>
          </div>
          <span class="hidden sm:inline-block text-xs font-bold px-3 py-1 bg-red-50 text-red-600 rounded-full border border-red-200">
            Modelos de Operação
          </span>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-5 mb-4">
          <!-- Bloco 1 -->
          <div class="bg-white border border-slate-200 rounded-xl p-4 shadow-sm">
            <span class="px-2.5 py-1 rounded text-xs font-black bg-red-600 text-white">1. SPARC WINDOWS FULL</span>
            <ul class="text-xs text-slate-700 space-y-2 mt-3">
              <li>• <strong class="text-slate-900">Quem opera:</strong> Todos, Assistência Técnica e Ativação própria da Claro permitindo renegociação de contratos LPU.</li>
              <li>• <strong class="text-slate-900">Onde:</strong> Bancada / Laboratório na Base Regional.</li>
              <li>• <strong class="text-slate-900">Triagem de Hardware:</strong> Testa portas Ethernet, fonte, boot e memórias. Defeito físico é corrigido/substituído na base antes de ir para a rua.</li>
              <li>• <strong class="text-slate-900">Tempo Médio:</strong> Menos de 10 minutos por equipamento.</li>
            </ul>
          </div>

          <!-- Bloco 2 -->
          <div class="bg-white border border-slate-200 rounded-xl p-4 shadow-sm">
            <span class="px-2.5 py-1 rounded text-xs font-black bg-blue-600 text-white">2. SPARC TESTER (Técnicos Terceirizados)</span>
            <ul class="text-xs text-slate-700 space-y-2 mt-3">
              <li>• <strong class="text-slate-900">Quem opera:</strong> Empresas parceiras terceirizadas responsáveis pela instalação física.</li>
              <li>• <strong class="text-slate-900">O que muda:</strong> Retira do terceiro o acesso privilegiado e a responsabilidade de configurar.</li>
              <li>• <strong class="text-slate-900">O que é o SPARC Tester:</strong> App ultraleve e blindado para celular corporativo ou notebook.</li>
              <li>• <strong class="text-slate-900">O que faz:</strong> Valida link, afere largura de banda e gera relatório PDF formal.</li>
            </ul>
          </div>
        </div>

        <!-- Banner Alavanca Financeira -->
        <div class="p-4 rounded-xl bg-emerald-50 border border-emerald-300 shadow-sm">
          <div class="flex items-center gap-2 mb-1.5">
            <span class="text-lg">💰</span>
            <h4 class="text-xs md:text-sm font-black text-emerald-800 uppercase tracking-wide">
              ALAVANCA FINANCEIRA ESTRATÉGICA PARA A CLARO: REDUÇÃO DIRETA DE CONTRATO POR OS
            </h4>
          </div>
          <p class="text-xs text-slate-700 leading-relaxed">
            Como o parceiro terceirizado <strong class="text-slate-900">NÃO precisa mais configurar o roteador no cliente</strong> (o equipamento já chega pronto e testado pela base Claro), a Claro ganha respaldo técnico e operacional imediato para <strong class="text-emerald-700">renegociar e abater o valor do item 'Configuração de CPE'</strong> pago nas ordens de serviço. Em escala regional e nacional, essa economia cobre com folga qualquer investimento interno!
          </p>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro | Regional SC</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 7: SCORECARD COMPARATIVO (10 x 1) -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-4 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">Matriz de Decisão Executiva</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Scorecard Comparativo: SPARC (Interno) × Panteon (Externo)</h2>
          </div>
          <div class="flex items-center gap-2 bg-slate-900 text-white px-3 py-1 rounded-xl">
            <span class="text-xs font-bold text-slate-400">PLACAR:</span>
            <span class="text-sm font-black text-emerald-400">SPARC 10</span>
            <span class="text-xs text-slate-500">×</span>
            <span class="text-sm font-black text-red-400">1 Panteon</span>
          </div>
        </div>

        <div class="overflow-x-auto">
          <table class="w-full text-left text-xs bg-white rounded-lg border border-slate-200 overflow-hidden shadow-sm">
            <thead>
              <tr class="border-b border-slate-200 bg-slate-50 text-slate-600 font-bold uppercase text-[11px]">
                <th class="py-2.5 px-3">Critério Estratégico</th>
                <th class="py-2.5 px-3 text-emerald-700 bg-emerald-50/50">SPARC (Interno Claro)</th>
                <th class="py-2.5 px-3 text-red-700 bg-red-50/50">Panteon (Fornecedor Terceiro)</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 text-slate-700">
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Investimento em Licenças de Software</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ R$ 0 — Ativo proprietário interno Claro</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Alto — Cobrança por usuário ou contratos bienais</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Infraestrutura de Servidores Novos</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Zero servidores — Processamento no endpoint</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Exige 10+ servidores dedicados (Capex/Opex)</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Operação Offline (Sem Link no Cliente)</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ 100% Autônomo via Cache local de firmwares</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ TRAVA — Inviabilizado sem link ativo</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Redução de Custos em Contratos de OS</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Sim — Roteador sai pronto (abate item de CPE)</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Não — Mantém terceiro configurando no campo</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Triagem Prévia de Hardware em Bancada</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Sim — Substituição na base antes da visita</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Não — Descobre falha na frente do cliente</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Plataformas Operacionais Homologadas</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Windows Desktop (.exe) + Android (.apk)</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Apenas Android (sem versão Desktop)</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Interfaces de Conexão Física em Campo</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Serial CLI + Cabo de Rede Ethernet (TFTP/HTTP)</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Somente Cabo Serial CLI</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Agilidade para Correções e Novos Modelos</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Horas ou Dias — Equipe de desenvolvimento Claro</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Fila de Fornecedor — Depende de SLA/contrato</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Recuperação de Firmware Corrompido / BIOS</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Automática via TFTP integrado na porta WAN</td>
                <td class="py-2 px-3 text-red-600 bg-red-50/20">✖ Não recupera equipamentos sem firmware</td>
              </tr>
              <tr>
                <td class="py-2 px-3 font-semibold text-slate-900">Integração com Sistemas Corporativos</td>
                <td class="py-2 px-3 font-bold text-amber-700 bg-amber-50/20">◐ Via SAIP (PDF/TXT) hoje; API em roadmap</td>
                <td class="py-2 px-3 font-bold text-emerald-700 bg-emerald-50/20">✔ Homologado via API Corporativa</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC atende a 10 dos 10 requisitos críticos de ativação Claro</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 8: RECOMENDAÇÕES EXECUTIVAS -->
    <section class="slide claro-sun-bg flex-col justify-between border border-slate-200 rounded-2xl p-6 md:p-8 shadow-xl min-h-[600px] relative">
      <div>
        <div class="border-b border-red-600 pb-3 mb-5 flex items-center justify-between">
          <div>
            <span class="text-xs font-bold text-red-600 uppercase tracking-widest">Diretrizes para a Gestão</span>
            <h2 class="text-2xl md:text-3xl font-extrabold text-slate-900">Recomendações Executivas e Plano de Ação Imediato</h2>
          </div>
          <span class="hidden sm:inline-block text-xs font-bold px-3 py-1 bg-red-50 text-red-600 rounded-full border border-red-200">
            Próximos Passos
          </span>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-3 gap-5 mb-5">
          <div class="bg-white border border-red-200 rounded-xl p-4 shadow-sm">
            <span class="px-2 py-0.5 rounded text-xs font-black bg-red-600 text-white">PASSO 1</span>
            <h3 class="text-sm font-bold text-slate-900 mt-2 mb-2">Aprovar Modelo Modular</h3>
            <p class="text-xs text-slate-600 leading-relaxed">
              Implantar o modelo SPARC Full na Assistência Técnica própria da Regional SC para pré-configuração e triagem, e liberar o SPARC Tester aos parceiros.
            </p>
          </div>
          <div class="bg-white border border-emerald-200 rounded-xl p-4 shadow-sm">
            <span class="px-2 py-0.5 rounded text-xs font-black bg-emerald-600 text-white">PASSO 2</span>
            <h3 class="text-sm font-bold text-slate-900 mt-2 mb-2">Renegociação Contratual</h3>
            <p class="text-xs text-slate-600 leading-relaxed">
              Abrir estudo conjunto com Suprimentos/Contratos para abater o item 'Configuração de CPE' nas faturas de empresas terceirizadas de instalação.
            </p>
          </div>
          <div class="bg-white border border-blue-200 rounded-xl p-4 shadow-sm">
            <span class="px-2 py-0.5 rounded text-xs font-black bg-blue-600 text-white">PASSO 3</span>
            <h3 class="text-sm font-bold text-slate-900 mt-2 mb-2">Oficialização Corporativa</h3>
            <p class="text-xs text-slate-600 leading-relaxed">
              Liberar para o SPARC a mesma credencial de API Corporativa já concedida ao Panteon e oficializar a solução como padrão nacional Claro.
            </p>
          </div>
        </div>

        <!-- Box Conclusão -->
        <div class="p-5 rounded-xl bg-slate-900 text-white border border-slate-800 shadow-md">
          <h4 class="text-xs font-bold text-red-400 uppercase tracking-widest mb-1.5">RESUMO EXECUTIVO PARA TOMADA DE DECISÃO</h4>
          <p class="text-xs md:text-sm text-slate-200 leading-relaxed">
            O <strong class="text-emerald-400">SPARC</strong> entrega uma solução de <strong class="text-white">custo zero em licenças e servidores</strong>, com <strong class="text-red-300">máxima flexibilidade operacional (100% offline via cache de firmware)</strong>, <strong class="text-white">eliminação de retrabalho por triagem de hardware na base</strong> e <strong class="text-emerald-400">agilidade total de sustentação interna</strong>. A adoção do SPARC protege o orçamento da Claro, valoriza a equipe técnica própria e alavanca a economia nos contratos terceirizados.
          </p>
          <div class="mt-3 flex items-center justify-between text-xs pt-3 border-t border-slate-800">
            <span class="text-red-300 font-bold">Decisão Recomendada:</span>
            <span class="font-extrabold text-white bg-red-600 px-3 py-1 rounded-md">Consolidar o SPARC como ativo estratégico prioritário da Claro</span>
          </div>
        </div>
      </div>

      <div class="mt-4 pt-3 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
        <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores Claro | Regional SC</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-10 h-auto">
      </div>
    </section>

    <!-- SLIDE 9: ENCERRAMENTO CLARO -->
    <section class="slide flex-col justify-between bg-gradient-to-r from-red-600 to-red-700 text-white rounded-2xl p-10 md:p-14 shadow-2xl relative overflow-hidden min-h-[600px] border border-red-500">
      <div class="my-auto">
        <h1 class="text-6xl md:text-8xl font-black text-white tracking-tight leading-none mb-3">
          SPARC
        </h1>
        <p class="text-xl md:text-2xl font-bold text-red-100 mb-4">
          Sistema de Provisionamento e Ativação de Roteadores da CLARO
        </p>
        <p class="text-sm md:text-base text-white/90">
          Obrigado! · Regional Santa Catarina · Ativação Empresarial B2B
        </p>
      </div>

      <div class="pt-6 border-t border-red-500/60 flex justify-between items-center text-xs text-red-200">
        <span>george.calcmann@claro.com.br</span>
        <img src="{logo_claro_b64}" alt="Logo Claro" class="w-20 h-auto drop-shadow-md">
      </div>
    </section>

  </main>

  <!-- RODAPÉ DE NAVEGAÇÃO -->
  <footer class="bg-white border-t border-slate-200 px-6 py-2.5 flex items-center justify-between text-xs text-slate-500">
    <div class="flex items-center space-x-2">
      <span class="inline-block w-2 h-2 rounded-full bg-red-600"></span>
      <span>Navegue com as teclas <strong>←</strong> e <strong>→</strong> ou <strong>Espaço</strong></span>
    </div>
    <div class="flex space-x-1" id="dotsContainer"></div>
    <div>
      <span>Padrão Institucional Claro S.A. · 2026</span>
    </div>
  </footer>

  <script>
    const slides = document.querySelectorAll('.slide');
    const totalSlides = slides.length;
    let currentSlide = 0;

    const slideIndicator = document.getElementById('slideIndicator');
    const dotsContainer = document.getElementById('dotsContainer');

    for (let i = 0; i < totalSlides; i++) {{
      const dot = document.createElement('button');
      dot.className = `w-2 h-2 rounded-full transition-all duration-300 ${{i === 0 ? 'bg-red-600 w-6' : 'bg-slate-300 hover:bg-slate-400'}}`;
      dot.title = `Ir para slide ${{i + 1}}`;
      dot.onclick = () => goToSlide(i);
      dotsContainer.appendChild(dot);
    }}

    function updateSlide() {{
      slides.forEach((slide, idx) => {{
        slide.classList.toggle('active', idx === currentSlide);
      }});
      slideIndicator.textContent = `Slide ${{currentSlide + 1}} de ${{totalSlides}}`;

      const dots = dotsContainer.querySelectorAll('button');
      dots.forEach((dot, idx) => {{
        if (idx === currentSlide) {{
          dot.className = 'w-6 h-2 rounded-full bg-red-600 transition-all duration-300';
        }} else {{
          dot.className = 'w-2 h-2 rounded-full bg-slate-300 hover:bg-slate-400 transition-all duration-300';
        }}
      }});
    }}

    function nextSlide() {{
      if (currentSlide < totalSlides - 1) {{
        currentSlide++;
        updateSlide();
      }}
    }}

    function prevSlide() {{
      if (currentSlide > 0) {{
        currentSlide--;
        updateSlide();
      }}
    }}

    function goToSlide(n) {{
      if (n >= 0 && n < totalSlides) {{
        currentSlide = n;
        updateSlide();
      }}
    }}

    function toggleFullScreen() {{
      if (!document.fullscreenElement) {{
        document.documentElement.requestFullscreen().catch(err => {{}});
      }} else {{
        if (document.exitFullscreen) {{
          document.exitFullscreen();
        }}
      }}
    }}

    document.addEventListener('keydown', (e) => {{
      if (e.key === 'ArrowRight' || e.key === 'Space') {{
        e.preventDefault();
        nextSlide();
      }} else if (e.key === 'ArrowLeft') {{
        e.preventDefault();
        prevSlide();
      }} else if (e.key === 'Home') {{
        e.preventDefault();
        goToSlide(0);
      }} else if (e.key === 'End') {{
        e.preventDefault();
        goToSlide(totalSlides - 1);
      }}
    }});
  </script>
</body>
</html>
"""

# Grava nos destinos
out_paths = [
    r"C:\SPARC\dist-beta\SPARC_Apresentacao_Executiva_Claro.html",
    r"C:\SPARC\dist-beta\SPARC_Apresentacao_Executiva_Resumida.html",
    os.path.join(HERE, "SPARC_Apresentacao_Executiva_Claro.html"),
    r"C:\Users\User\.gemini\antigravity\brain\4c8109a0-5840-4810-a56c-709d56ac8b9f\apresentacao_executiva_sparc_vs_pantheon.html"
]

for p in out_paths:
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as f:
        f.write(html_content)
    print(f"[OK] HTML gerado: {p} ({os.path.getsize(p)} bytes)")
