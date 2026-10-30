import os, shutil
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN

SRC_PPT = r'C:\Users\User\.gemini\antigravity\brain\4c8109a0-5840-4810-a56c-709d56ac8b9f\.user_uploaded\media_1793307933366_60bf5cd5.pptx'
OUT_PPT = r'C:\SPARC\docs\defesa\SPARC_Apresentacao_Inovacao_Claro.pptx'

shutil.copy(SRC_PPT, OUT_PPT)
prs = Presentation(OUT_PPT)
print(f"Loaded {OUT_PPT} successfully with {len(prs.slides)} slides!")
