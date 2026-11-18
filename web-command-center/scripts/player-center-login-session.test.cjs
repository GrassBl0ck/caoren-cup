const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('node:assert/strict'),test=require('node:test');
const source=fs.readFileSync(path.join(__dirname,'../public/js/player-center.js'),'utf8');
async function page({joined=false,status='waiting',reason=null,connected=true,admin=false,logoutRace=false}={}){
 const elements=new Map(),handlers={},requests=[],emits=[],redirects=[];
 function element(id){if(!elements.has(id))elements.set(id,{hidden:true,textContent:'',disabled:false,handlers:{},classList:{toggle(){}},addEventListener(e,f){this.handlers[e]=f},appendChild(el){elements.set(el.id,el)}});return elements.get(id)}
 let match={success:true,profile:{email:'p@example.com'},joined,matchStatus:status,joinAvailable:status==='waiting'&&!joined&&!reason,leaveAvailable:status==='waiting'&&joined,joinUnavailableReason:reason};
 const socket={connected,on(e,f){handlers[e]=f},emit(e,d){emits.push({e,d})},connect(){this.connected=true}};
 let holdNext=false,releaseHeld;
 const fetch=async(url,options={})=>{requests.push(url);if(logoutRace && url.endsWith('/logout'))handlers.PLAYER_CENTER_SESSION_INVALID();let data={...match};if(url==='/api/player-center/me'&&holdNext){holdNext=false;await new Promise(resolve=>releaseHeld=resolve)}if(url.endsWith('/socket-ticket'))data={success:true,socketTicket:'fresh-'+requests.length};if(url.endsWith('/join')){match={...match,joined:true,joinAvailable:false,leaveAvailable:true};data={...match,socketTicket:'join-ticket'}}if(url.endsWith('/leave')){match={...match,joined:false,joinAvailable:true,leaveAvailable:false};data={...match}}return {ok:true,status:200,json:async()=>data}};
 vm.runInNewContext(source,{document:{body:{dataset:{page:admin?'admin':'cs'}},readyState:'complete',getElementById:element,createElement(){return {addEventListener(e,f){this.handler=f}}}},window:{caorenDesktop:logoutRace?{logoutDevice:async()=>{assert.deepEqual(redirects,[]);return {ok:true}}}:undefined,__caorenCupLobbySocket:socket,dispatchEvent(){},addEventListener(){},location:{replace(p){redirects.push(p)}}},CustomEvent:function(){},fetch});
 const drain=()=>new Promise(setImmediate);await drain();
 return {element,handlers,requests,emits,socket,redirects,drain,holdNextMe(){holdNext=true},releaseMe(){releaseHeld()},setMatch(next){match={...match,...next}}};
}
test('CS restores account state without another account login',async()=>{const p=await page();assert.deepEqual(p.requests,['/api/player-center/me']);assert.equal(p.element('player-center-account-name').textContent,'p@example.com');assert.equal(p.element('player-center-join-btn').disabled,false)});
test('waiting status refreshes a formerly disabled join button',async()=>{const p=await page({status:'started',reason:'match_started'});assert.equal(p.element('player-center-join-btn').disabled,true);assert.match(p.element('player-center-home-message').textContent,/比赛已经开始/);p.setMatch({matchStatus:'waiting',joinAvailable:true,joinUnavailableReason:null});p.handlers.GAME_STATE({matchStatus:'waiting'});await p.drain();assert.equal(p.element('player-center-join-btn').disabled,false)});
test('unbound Steam identity has a visible profile instruction',async()=>{const p=await page({reason:'game_binding_required'});assert.match(p.element('player-center-home-message').textContent,/个人资料.*绑定/);assert.equal(p.element('player-center-join-btn').disabled,true)});
test('reconnect requests a new single use ticket rather than replaying a pending ticket',async()=>{const p=await page({joined:true});assert.equal(p.emits.length,1);p.handlers.connect();await p.drain();assert.equal(p.emits.length,2);assert.notEqual(p.emits[0].d.ticket,p.emits[1].d.ticket)});
test('socket login failure reports a retry without automatic unbounded retries',async()=>{const p=await page({joined:true});const count=p.requests.length;p.handlers.LOGIN_RESPONSE({success:false,message:'ticket expired'});assert.match(p.element('player-center-home-message').textContent,/连接失败.*重新进入比赛/);assert.equal(p.requests.length,count)});
test('both socket quit and match-ended notifications refresh membership',async()=>{const p=await page({joined:true});p.setMatch({joined:false,joinAvailable:true,leaveAvailable:false});p.handlers.LOGIN_RESPONSE({success:false,message:'你已退出游戏'});await p.drain();assert.equal(p.element('player-center-join-btn').textContent,'加入本场比赛')});
test('admin page does not request a player session',async()=>{const p=await page({admin:true});assert.deepEqual(p.requests,[])});

test('server invalidation during logout waits for desktop credential clearing before navigation',async()=>{
 const p=await page({logoutRace:true});
 await p.element('player-center-logout-btn').handlers.click();
 assert.deepEqual(p.redirects,['/']);
});

test('a new public status arriving during an older me request is refreshed after that request',async()=>{
 const p=await page({status:'started',reason:'match_started'});
 p.holdNextMe();p.handlers.GAME_STATE({matchStatus:'started'});
 p.setMatch({matchStatus:'waiting',joinAvailable:true,joinUnavailableReason:null});
 p.handlers.GAME_STATE({matchStatus:'waiting'});p.releaseMe();
 await p.drain();await p.drain();
 assert.equal(p.element('player-center-join-btn').disabled,false);
 assert.equal(p.requests.filter(url=>url==='/api/player-center/me').length,3);
});
