"""Presentation-only contact sheet: identical pixel crop for each offline render."""
from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
out=Path(__file__).resolve().parent
sheet=Image.new('RGB',(1200,850),'#17202c');draw=ImageDraw.Draw(sheet)
font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',23)
for col,style in enumerate(['1-natural','2-plasma']):
 for row,mode in enumerate(['gas','nitro']):
  source=Image.open(out/f'{style}-{mode}-blender.png').convert('RGB')
  # Preserve original pixels; crop just the shared empty studio margin.
  crop=source.crop((330,190,930,570));sheet.paste(crop,(col*600,row*425+45))
  label=f'{col+1}. '+('Natural flame' if col==0 else 'Plasma jet')+' / '+mode.upper()
  draw.text((col*600+18,row*425+12),label,font=font,fill='white')
sheet.save(out/'comparison-blender.png')
