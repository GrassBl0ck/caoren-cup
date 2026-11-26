const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

const lobby = fs.readFileSync('public/js/lobby-app.js', 'utf8');
function between(start, end) {
    const from = lobby.indexOf(start);
    const to = lobby.indexOf(end, from);
    assert.ok(from >= 0 && to > from, 'expected lobby section ' + start);
    return lobby.slice(from, to);
}

test('account and game binding forms block native submissions even before the handlers load', () => {
    for (const page of ['login', 'profile', 'cs']) {
        const html = fs.readFileSync('pages/' + page + '.html', 'utf8');
        assert.match(html.split('</head>')[0], /<meta http-equiv="Content-Security-Policy" content="form-action 'none'">/);
        const source = page === 'cs' ? lobby : html;
        const forms = [...source.matchAll(/<form\b[^>]*>/g)];
        assert.equal(forms.length, { cs: 1, login: 3, profile: 4 }[page]);
        for (const [tag] of forms) assert.match(tag, /\bmethod="post"/i);
    }
});

const name = "小鱼 <青队> & O'Neil";
const text = '小鱼 &lt;青队&gt; &amp; O&#39;Neil';
const phases = ['CaptainSelection', 'Roll', 'PlayerDraft', 'MapBan', 'SidePick', 'PreGameSetup', 'PostGameAccusation', 'Scoreboard'];
const renderPhases = between("if (state.phase === 'CaptainSelection')", '\n        });');
const helpers = between('function htmlEscape(', 'function renderSafeTaskRichText(') +
    between('const MAP_THUMBNAILS =', 'function getLevelColor(');

for (const isAdmin of [false, true]) test('nicknames remain literal text throughout the ' + (isAdmin ? 'admin' : 'player') + ' view', () => {
    const players = {
        a: { playerId: 'a', name, role: 'Player', rosterTeam: 'A', isReady: true },
        b: { playerId: 'b', name, role: 'Player', rosterTeam: 'B', isReady: true },
        c: { playerId: 'c', name, role: 'Player' },
        host: { playerId: 'host', name: 'Host', role: 'Admin' },
    };
    const state = {
        players, captains: { A: 'a', B: 'b' }, rollValues: { A: 1, B: 2 },
        teams: { A: { players: ['a'] }, B: { players: ['b'] } }, draftOrder: ['A'], draftIndex: 0,
        mapPool: ['Mirage'], bannedMaps: [], mapVote: { team: 'A', votes: { a: 'Mirage' } },
        sideVote: { team: 'A', votes: { a: 'CT' } }, accusations: { a: { own: null, enemy: 'b' } },
    };
    const currentPlayer = isAdmin ? players.host : players.a;
    const extraDiv = { innerHTML: '' };
    const ctx = vm.createContext({
        state, extraDiv, currentPlayer, myPlayerId: currentPlayer.playerId, isAdmin,
        undercoverEnabled: true, isDuel: false, getTimerSecondsText: () => '15',
        renderPostmatchPanels() {}, renderDuelControlPanel: () => '', renderCaorenMatchRuntimePanel: () => '',
        resolveAbilityPreGameRenderDecision: () => ({}), getLiveSide: () => '', getExpectedSide: () => ({}), isSideMismatch: () => false,
    });
    vm.runInContext(helpers, ctx);
    for (const phase of phases) {
        state.phase = phase;
        extraDiv.innerHTML = '';
        vm.runInContext(renderPhases, ctx);
        assert.ok(extraDiv.innerHTML.includes(text), phase + ' must display the name');
        assert.ok(!extraDiv.innerHTML.includes('<青队>'), phase + ' must keep punctuation as text');
        assert.ok(!extraDiv.innerHTML.includes('&amp;lt;'), phase + ' must escape exactly once');
        assert.doesNotMatch(extraDiv.innerHTML, /on\w+="[^"]*(?:小鱼|O&#39;Neil)/);
    }
});

test('the kick button resolves the display name as data when clicked', () => {
    const buttonTemplate = between('const kickOp =', 'const adminOps =');
    assert.ok(buttonTemplate.includes('data-kick-player='));
    assert.doesNotMatch(buttonTemplate, /onclick=|p\.name/);
    let click, kicked;
    vm.runInNewContext(between("document.getElementById('player-list').addEventListener", "ws.on('GAME_STATE'"), {
        document: { getElementById: () => ({ addEventListener: (_, handler) => { click = handler; } }) },
        window: { _currentGameState: { players: { a: { playerId: 'a', name } } } },
        kickPlayer: (id, displayName) => { kicked = [id, displayName]; },
    });
    click({ target: { closest: () => ({ dataset: { kickPlayer: 'a' } }) } });
    assert.deepEqual(kicked, ['a', name]);
});

test('match settings send a proof obtained from the current connection', async () => {
    let sent;
    const ctx = vm.createContext({
        ws: { connected: true, timeout: () => ({ emitWithAck: async event => { assert.equal(event, 'MATCH_ADMIN_SESSION'); return 'local-proof'; } }) },
        fetch: (path, options) => { sent = { path, options }; },
    });
    vm.runInContext(between('async function postMatchAdmin(', 'async function saveMatchOptions('), ctx);
    await ctx.postMatchAdmin('/api/admin/game-mode', { gameModeId: 'competitive' });
    assert.equal(sent.options.headers.Authorization, 'Bearer local-proof');
    assert.deepEqual(JSON.parse(sent.options.body), { gameModeId: 'competitive' });
    sent = undefined;
    ctx.ws.connected = false;
    await assert.rejects(ctx.postMatchAdmin('/api/admin/game-mode'));
    assert.equal(sent, undefined);
});
