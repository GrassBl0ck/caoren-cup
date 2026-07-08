const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const lobbyApp = fs.readFileSync(path.join(root, 'public/js/lobby-app.js'), 'utf8');
const appCss = fs.readFileSync(path.join(root, 'public/css/app.css'), 'utf8');
const pluginApi = fs.readFileSync(path.join(root, 'src/plugin-api.ts'), 'utf8');
const bridgePlugin = fs.readFileSync(path.join(root, 'CaorenCupPlugin/CaorenCupPlugin.cs'), 'utf8');

const requiredHeaders = [
  '友伤（道具）',
  '受闪（队友）',
  '蹲下时间',
  '跳跃次数',
  '受道具伤',
  '电击枪/被',
  '匕首/被',
  '命中率（命中/总）',
  '首杀/首死',
  '投掷道具次数',
  '闪光（队友）',
  '保枪率',
  '击杀子弹数',
  '狙杀占比（AWP）',
  '急停击杀率',
];

const requiredTooltips = [
  '对队友造成总伤害（道具伤害）',
  '受到闪白时间（来自队友的）',
  '电击枪造成击杀次数（被电击枪击杀次数）',
  '当玩家击杀敌方玩家时，速度不高于85，视作急停击杀',
];

const requiredStats = [
  'friendlyUtilityDamage',
  'crouchSeconds',
  'jumpCount',
  'zeusKills',
  'zeusDeaths',
  'knifeKills',
  'knifeDeathsByKnife',
  'shotsFired',
  'shotsHit',
  'grenadesThrown',
  'flashSecondsGiven',
  'friendlyFlashSecondsGiven',
  'saveRounds',
  'lostRoundsAlive',
  'killShotsTotal',
  'killShotsCount',
  'awpKills',
  'counterStrafeKills',
];

function assertIncludes(source, value, label) {
  if (!source.includes(value)) {
    throw new Error(`${label} missing: ${value}`);
  }
}

for (const header of requiredHeaders) assertIncludes(lobbyApp, header, 'postmatch fun stat header');
for (const tooltip of requiredTooltips) assertIncludes(lobbyApp, tooltip, 'postmatch fun stat tooltip');
assertIncludes(lobbyApp, 'postmatch-fun-scroll', 'horizontal scroll container');
assertIncludes(lobbyApp, 'renderPostmatchFunStatsTable', 'fun stats table renderer');
assertIncludes(lobbyApp, 'hltv-style-table', 'official stats table style');
assertIncludes(appCss, '.postmatch-fun-scroll', 'horizontal scroll CSS');
if (appCss.includes('.postmatch-fun-table')) {
  throw new Error('fun stats table should reuse hltv-style-table instead of defining a separate table style');
}
for (const stat of requiredStats) assertIncludes(pluginApi, stat, 'plugin-api stat');
assertIncludes(pluginApi, "case 'weapon_fire':", 'weapon_fire event handler');
assertIncludes(pluginApi, "case 'player_jump':", 'player_jump event handler');
assertIncludes(pluginApi, "case 'player_crouch_sample':", 'crouch sample event handler');
assertIncludes(bridgePlugin, 'EventWeaponFire', 'bridge weapon fire event');
assertIncludes(bridgePlugin, 'PlayerButtons.Jump', 'bridge player jump detection');
assertIncludes(bridgePlugin, 'player_jump', 'bridge player jump event');
assertIncludes(bridgePlugin, 'player_crouch_sample', 'bridge crouch sample event');

console.log('postmatch fun stats checks passed');
