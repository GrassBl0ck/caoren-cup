const test=require('node:test'),assert=require('node:assert/strict'),fs=require('fs'),path=require('path'),vm=require('vm');
const root=path.resolve(__dirname,'..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('anonymous status broadcasts do not enter the private lobby renderer',()=>{
 const source=read('public/js/lobby-app.js');const prefix=source.split("ws.on('GAME_STATE', (state) => {")[1].split('window._currentPlayer = currentPlayer || null;')[0];
 assert.doesNotThrow(()=>vm.runInNewContext('(state=>{'+prefix+'})({matchStatus:"waiting"})',{}));
});
async function serverPage(bridge,connectUrl='steam://connect/play.cup.example.org:27015'){
 const handlers={},elements=new Map(),messages=[];let launches=0;
 const el=id=>{if(!elements.has(id))elements.set(id,{textContent:'',disabled:false,classList:{toggle(){},remove(){}},addEventListener(e,f){handlers[id]=f}});return elements.get(id)};
 vm.runInNewContext(read('public/js/access-admin.js'),{document:{readyState:'complete',body:{dataset:{page:'cs'}},getElementById:el},window:{caorenDesktop:bridge,location:{}},navigator:{clipboard:{writeText:async text=>messages.push(text)}},fetch:async()=>({ok:true,json:async()=>({success:true,joinAllowed:true,pluginReady:true,connectUrl})}),setInterval(){}});
 await new Promise(setImmediate);return {handlers,el,messages};
}
test('desktop Steam launch failure supplies a manual connection fallback',async()=>{
 const calls=[];const p=await serverPage({connectGameServer:async url=>{calls.push(url);return {ok:false,reason:'steam_launch_failed'}}});
 await p.handlers['v1333-connect-server-btn']();assert.match(p.el('server-connect-message').textContent,/启动失败/);assert.deepEqual(calls,['steam://connect/play.cup.example.org:27015']);
 await p.handlers['copy-server-command']();assert.deepEqual(p.messages,['connect play.cup.example.org:27015']);
});
test('missing or malformed connection addresses cannot be copied',async()=>{
 for(const value of ['', 'not-a-steam-url']){
  const page=await serverPage({},value);
  assert.equal(page.el('copy-server-command').disabled,true);
  await page.handlers['copy-server-command']();
  assert.deepEqual(page.messages,[]);
 }
});
