let socket, context, actionId, settings = {}, data = {connected:false,services:[],kind:'service',language:'en'};
const $ = id => document.getElementById(id);
const strings = {
 en:{intro:'Your services. One clear signal.',languageLabel:'Language',autoLabel:'Follow StatusLookout',serviceLabel:'Monitored service',serviceHint:'Choose services in StatusLookout first.',durationLabel:'Snooze duration',hourLabel:'1 hour',snoozeHint:'Press again to end the temporary snooze. Monitoring continues.',open:'Open StatusLookout',reload:'Reload services',footer:'Requires StatusLookout 0.5.0 or newer running on this PC.',online:'Connected to StatusLookout',offline:'Disconnected — start StatusLookout 0.5.0 or newer.',choose:'Choose a service…',missing:'Service no longer monitored',global:'Shows monitored service count, affected (!) and unavailable (?) services. Press to open the dashboard.',refresh:'Press to check official status APIs now.',service:'Press to open the selected service’s status page.',snooze:'Temporarily silence alerts for this service.',demo:' · DEMO DATA'},
 fr:{intro:'Tes services. Un signal clair.',languageLabel:'Langue',autoLabel:'Suivre StatusLookout',serviceLabel:'Service suivi',serviceHint:'Choisis d’abord les services dans StatusLookout.',durationLabel:'Durée de suspension',hourLabel:'1 heure',snoozeHint:'Appuie à nouveau pour terminer la suspension temporaire. La surveillance continue.',open:'Ouvrir StatusLookout',reload:'Actualiser les services',footer:'Nécessite StatusLookout 0.5.0 ou plus récent, lancé sur ce PC.',online:'Connecté à StatusLookout',offline:'Déconnecté — lance StatusLookout 0.5.0 ou plus récent.',choose:'Choisir un service…',missing:'Service qui n’est plus suivi',global:'Affiche le nombre de services suivis, touchés (!) et indisponibles (?). Un appui ouvre le tableau de bord.',refresh:'Un appui vérifie les API officielles de statut.',service:'Un appui ouvre la page de statut du service choisi.',snooze:'Suspend temporairement les alertes de ce service.',demo:' · DONNÉES DE DÉMO'}
};
function send(event,payload){ if(socket?.readyState===WebSocket.OPEN) socket.send(JSON.stringify({event,context,...payload})); }
function ask(event){send('sendToPlugin',{action:actionId,payload:{event}});}
function render(){
 const locale=settings.language==='fr'||settings.language==='auto'&&data.language==='fr'?'fr':'en'; const t=strings[locale]; document.documentElement.lang=locale;
 for(const id of ['intro','languageLabel','autoLabel','serviceLabel','serviceHint','durationLabel','hourLabel','snoozeHint','open','reload','footer']) $(id).textContent=t[id];
 $('connection').textContent=(data.connected?t.online:t.offline)+(data.demo?t.demo:''); $('connection').classList.toggle('online',data.connected);
 $('language').value=settings.language||'en'; $('minutes').value=String(settings.minutes===60?60:30);
 $('serviceSection').hidden=!['service','snooze'].includes(data.kind); $('snoozeSection').hidden=data.kind!=='snooze';
 const choice=document.createElement('option');choice.value='';choice.textContent=t.choose; $('service').replaceChildren(choice);
 for(const service of data.services){const option=document.createElement('option');option.value=service.id;option.textContent=service.name;$('service').append(option);}
 if(settings.serviceId&&!data.services.some(s=>s.id===settings.serviceId)){const option=document.createElement('option');option.value=settings.serviceId;option.textContent=t.missing;$('service').append(option);}
 $('service').value=settings.serviceId||'';$('actionHint').textContent=t[data.kind]||'';
}
function save(){settings={...settings,language:$('language').value,serviceId:$('service').value,minutes:Number($('minutes').value)};send('setSettings',{payload:settings});render();}
window.connectElgatoStreamDeckSocket=function(port,uuid,registerEvent,info,actionInfo){
 context=uuid;const action=JSON.parse(actionInfo);actionId=action.action;settings=action.payload?.settings||{};data.kind=actionId.split('.').at(-1);render();
 socket=new WebSocket(`ws://127.0.0.1:${port}`);socket.onopen=()=>{socket.send(JSON.stringify({event:registerEvent,uuid}));ask('getServices');};
 socket.onmessage=ev=>{const message=JSON.parse(ev.data);if(message.event==='sendToPropertyInspector'&&message.payload?.event==='services'){data=message.payload;render();}else if(message.event==='didReceiveSettings'){settings=message.payload.settings;render();}};
 socket.onclose=()=>{data.connected=false;render();};
};
$('language').onchange=save;$('service').onchange=save;$('minutes').onchange=save;$('open').onclick=()=>ask('openDashboard');$('reload').onclick=()=>ask('getServices');render();
