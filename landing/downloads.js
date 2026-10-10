(async()=>{
 const track=new URLSearchParams(location.search).get('channel')==='test'?'test':'production';
 const note=document.getElementById('release-note');
 const decode=s=>Uint8Array.from(atob(s),c=>c.charCodeAt(0));
 const key=await crypto.subtle.importKey('spki',decode('MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA41CZ75P4eTOPpQYahLiJaSmTHArwgy9FxtMpvDWDknhU1SUM46zMhB05SmcsEytzXiShRxg2tTnkKujetdtBjb7UOrCa8GZpddIwXp1w6vp4muKjKB/6jZVvlc8LJ6DqNwbPAcv6+EU6gUmuh2MZrou4vzrsnNm54IDRrE/Ya0ITzEKeTzy92YV4qGnYtVarTSLzvfsmgTyo/Zz3J5DJesipqcK2Z67nlJtggfYrZi1uqh1RY97+NYUrFFOTYrB13w5gD7pu7bEP7PtGV9o4WpO6rAacVfFL32iL+UJVnnYVyThQMmkdEOIyHYb6EvvR+0zOSzm4rLy5FlA9BmqsIXFIeTavQpKB97HnPCyAUlv1e/0uAUafO4VtRuQneXV7SzXCr722Nghb7RFKTewn4CwcdT0eXlSzRC6FpEFbZTVWIv3iu8bfgTlySVHNmVSCz5qx0YkdBI+niZP+1HKFzGmhYDavdPH0tdJQlfwP2gIx0OnyLUgVwDS7Ru9ehC9RAgMBAAE='),{name:'RSASSA-PKCS1-v1_5',hash:'SHA-256'},false,['verify']);
 try{
  const response=await fetch('https://api.github.com/repos/afonasev/star-racing/releases/tags/channel-'+track,{cache:'no-store'});
  if(response.status===404){note.textContent=track==='production'?'Стабильный выпуск готовится. Можно выбрать тестовый канал ниже.':'Тестовый выпуск готовится.';return;}
  if(!response.ok)throw Error('catalog unavailable');
  const github=await response.json();
  if(github.draft||github.prerelease!==(track==='test')||github.tag_name!=='channel-'+track)throw Error('wrong GitHub channel');
  const catalog=JSON.parse(github.body);
  const unsigned=catalog.schema===2&&catalog.unsignedProductionCatalog===true;
  if((!unsigned&&catalog.schema!==1)||catalog.track!==track||(unsigned&&track!=='production'))throw Error('wrong catalog');
  const entries=[];
  for(const [platform,id] of [['win-x64','windows'],['osx-universal','mac']]){
   const envelope=catalog.platforms[platform];let release;
   if(unsigned){release=envelope;if(!release||typeof release!=='object')throw Error('invalid unsigned release');}
   else {if(envelope.keyId!=='star-racing-test-2026'||envelope.payloadBase64.length>180000)throw Error('wrong key');const payload=decode(envelope.payloadBase64);if(!await crypto.subtle.verify('RSASSA-PKCS1-v1_5',key,decode(envelope.signatureBase64),payload))throw Error('bad signature');release=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(payload));}
   if(release.schema!==2||release.releaseTrack!==track||release.channel!==platform||release.appId!=='tech.afonasev.star-racing.'+platform||release.version!==catalog.version||!/^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$/.test(release.version)||(track==='test')!==release.version.includes('-'))throw Error('wrong release');
   const entry=release.installer;
   const name='Star-Racing-'+release.version+(id==='mac'?'-macOS-Setup.pkg':'-Windows-Setup.exe');
   const expected='https://github.com/afonasev/star-racing/releases/download/v'+release.version+'/'+name;
   if(entry.fileName!==name||entry.url!==expected||!Number.isSafeInteger(entry.size)||entry.size<=0||! /^[a-f0-9]{64}$/.test(entry.sha256))throw Error('invalid installer');
   entries.push({id,entry,release});
  }
  // Activate both links only after both signed platform descriptors passed.
  for(const {id,entry,release} of entries){
   const link=document.getElementById(id+'-download');link.href=entry.url;link.removeAttribute('aria-disabled');link.dataset.sha256=entry.sha256;
   document.getElementById(id+'-details').textContent=(id==='mac'?'Apple Silicon + Intel':'Windows 10/11 · x64')+' / '+release.version+' / '+Math.round(entry.size/1048576)+' МБ';
  }
  note.textContent=(track==='test'?'Тестовый канал. Проверка на устройствах ещё идёт.':unsigned?'Стабильный канал. Метаданные обновления не подписаны RSA; пакеты сверяются по размеру и SHA-256.':'Стабильный канал.')+(unsigned?'':' Подпись сведений о скачивании проверена.');
  const mac=/Mac|iPhone|iPad/.test(navigator.platform||'');document.getElementById((mac?'mac':'windows')+'-download').classList.add('recommended');
 }catch(error){note.textContent='Не удалось проверить выпуск. Попробуйте обновить страницу позже.';}
})();
