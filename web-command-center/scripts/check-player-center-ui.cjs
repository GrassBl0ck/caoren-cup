const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'); const html=fs.readFileSync(path.join(root,'pages/cs.html'),'utf8');
assert.doesNotMatch(html,/id="player-center-login-password"|id="player-center-game-code"|id="match-option-team-a-size"|id="match-option-team-b-size"|不平衡竞技目标/);
for(const id of ['player-center-home','player-center-join-btn','admin-login-password','admin-login-btn','server-connect-command','copy-server-command']) assert.ok(html.includes('id="'+id+'"'),id);
assert.match(html,/href="\/profile"/); assert.doesNotMatch(html,/href="\/(?:admin|mc)"/);
console.log('email account and independent management UI contract passed');
