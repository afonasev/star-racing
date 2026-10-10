"""Compose labelled contact sheets from the actual Blender PNGs, without retouching."""
from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
root=Path(__file__).parent
font_path='/System/Library/Fonts/Supplemental/Arial.ttf'
font=ImageFont.truetype(font_path,28);small=ImageFont.truetype(font_path,22)
def sheet(name,items,cols,w=720):
 h=round(w*830/1200);cellh=h+58;rows=(len(items)+cols-1)//cols
 canvas=Image.new('RGB',(w*cols,cellh*rows),(15,22,31));draw=ImageDraw.Draw(canvas)
 for i,(src,label) in enumerate(items):
  im=Image.open(root/src).convert('RGB');im=im.resize((w,h),Image.Resampling.LANCZOS)
  x=(i%cols)*w;y=(i//cols)*cellh;canvas.paste(im,(x,y));draw.text((x+22,y+h+13),label,font=font,fill=(225,233,244))
 canvas.save(root/name)
sheet('colors-sheet.png',[(f'paint-{s}.png',label) for s,label in [('01-silver','1. Серебристый'),('02-graphite','2. Графит'),('03-pearl','3. Жемчужно-белый'),('04-crimson','4. Красный металлик'),('05-cobalt','5. Кобальтовый синий'),('06-gold','6. Золотистый')]],3)
sheet('exhaust-sheet.png',[('exhaust-gas.png','Газ · короткий огонь'),('exhaust-nitro.png','Нитро · длинная голубая струя')],2,1080)
