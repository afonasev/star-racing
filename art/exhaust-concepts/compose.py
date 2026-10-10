from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
out=Path(__file__).resolve().parent
sheet=Image.new('RGB',(1920,1360),'#131b27');d=ImageDraw.Draw(sheet)
font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf',28)
for col,v in enumerate(['1-dense','2-wide']):
 for row,mode in enumerate(['gas','nitro']):
  img=Image.open(out/f'{v}-{mode}.png').convert('RGB');sheet.paste(img,(col*960,row*680+50))
  d.text((col*960+25,row*680+12),f'{col+1}. '+('Dense flame' if col==0 else 'Wide arcade flame')+' / '+mode.upper(),font=font,fill='white')
sheet.save(out/'comparison.png')
