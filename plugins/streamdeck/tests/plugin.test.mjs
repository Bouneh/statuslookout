import test from 'node:test';
import assert from 'node:assert/strict';
import net from 'node:net';
import { pipePath, request } from '../src/bridge.mjs';
import { keyImage, view } from '../src/render.mjs';
const service = { id:'github', name:'GitHub', status:'Outage', snoozedUntil:null, isMuted:false, logo:null };
const snapshot = { protocol:1, language:'fr', isDemo:false, isPaused:false, isRefreshing:false, overall:'Outage', services:[service] };
test('Disconnected bridge never shows a healthy service', () => {
 const item=view('service',{serviceId:'github'},null); assert.equal(item.detail,'Offline');assert.equal(item.color,'#95A4BE');
});
test('Missing and unconfigured services are distinguished', () => {
 assert.equal(view('service',{serviceId:'missing'},snapshot).detail,'Not monitored');
 assert.equal(view('service',{},snapshot).name,'Choose a service');
});
test('French language and provider status are rendered',()=>{assert.equal(view('service',{serviceId:'github',language:'fr'},snapshot).detail,'Panne');});
test('Automatic language follows StatusLookout',()=>{assert.equal(view('global',{language:'auto'},snapshot).name,'Vue globale');});
test('Paused monitoring is visible instead of a fresh healthy state',()=>{assert.equal(view('service',{serviceId:'github'},{...snapshot,isPaused:true}).detail,'Paused');});
test('Snooze time and permanent mute are distinct',()=>{
 const data={...snapshot,services:[{...service,snoozedUntil:new Date(61000).toISOString()}]};
 assert.equal(view('service',{serviceId:'github'},data,1000).detail,'1 min');
 assert.equal(view('service',{serviceId:'github'},{...data,services:[{...data.services[0],isMuted:true}]},1000).detail,'Muted');
});
test('Provider names cannot inject SVG content',()=>{
 const image=keyImage('service',{serviceId:'github'},{...snapshot,services:[{...service,name:'<script>&'}]});assert.ok(image.includes('&lt;script&gt;&amp;'));assert.ok(!image.includes('<script>'));
});
test('Non-PNG logo URLs are rejected by renderer',()=>{assert.equal(view('service',{serviceId:'github'},{...snapshot,services:[{...service,logo:'https://example.com'}]}).logo,undefined);});
test('Demo snapshots are clearly labelled',()=>{assert.ok(keyImage('global',{}, {...snapshot,isDemo:true}).includes('DEMO'));});
test('Bridge pipe matches current-user server convention',()=>{assert.equal(pipePath('PC','Bouneh'),pipePath('pc','bouneh'));assert.ok(pipePath('PC','Bouneh').startsWith('\\\\.\\pipe\\StatusLookout.StreamDeck.v1.'));});
test('IPC preserves UTF8 characters split across packets',async()=>{
 const server=net.createServer(socket=>{socket.once('data',()=>{
  const bytes=Buffer.from(JSON.stringify({ok:true,snapshot:{...snapshot,services:[{...service,name:'français'}]}})+'\n');
  const index=bytes.indexOf(Buffer.from('ç'))+1;socket.write(bytes.subarray(0,index));setTimeout(()=>socket.end(bytes.subarray(index)),10);
 });});
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{const data=await request('snapshot',{}, {host:'127.0.0.1',port:server.address().port});assert.equal(data.services[0].name,'français');}
 finally{await new Promise(resolve=>server.close(resolve));}
});
test('Unsupported protocol is rejected',async()=>{
 const server=net.createServer(socket=>socket.once('data',()=>socket.end(JSON.stringify({ok:true,snapshot:{protocol:2,services:[]}})+'\n')));
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{await assert.rejects(request('snapshot',{}, {host:'127.0.0.1',port:server.address().port}),/Unsupported/);}
 finally{await new Promise(resolve=>server.close(resolve));}
});
