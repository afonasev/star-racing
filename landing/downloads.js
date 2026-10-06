(async()=>{
 try{
  const response=await fetch('/downloads.json',{cache:'no-store'});
  if(!response.ok)throw Error('catalog unavailable');
  const data=await response.json();
  for(const [channel,id] of [['win-x64','windows'],['osx-universal','mac']]){
   const entry=data.downloads[channel];
   const url=new URL(entry.url,location.origin);
   if(url.origin!=='https://github.com'||!url.pathname.startsWith('/afonasev/star-racing/releases/download/v'+entry.version+'/')||!Number.isFinite(entry.size)||entry.size<=0)throw Error('invalid download');
   const link=document.getElementById(id+'-download');link.href=url.href;link.removeAttribute('aria-disabled');
   document.getElementById(id+'-details').textContent=(id==='mac'?'Apple Silicon + Intel':'Windows 10/11 · x64')+' / '+entry.version+' / '+Math.round(entry.size/1048576)+' МБ';
  }
  const mac=/Mac|iPhone|iPad/.test(navigator.platform||'');document.getElementById((mac?'mac':'windows')+'-download').classList.add('recommended');
 }catch(error){document.getElementById('release-note').textContent='Загрузки временно недоступны. Попробуйте обновить страницу позже.';}
})();
