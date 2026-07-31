const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'weaponpaints-app.js'), 'utf8');
const lobbyJs = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'weaponpaints.css'), 'utf8');
const webPackageScript = fs.readFileSync(path.join(root, '..', 'scripts', 'package-caoren-web.ps1'), 'utf8');

assert.match(html, /id="weaponpaints-open-btn"/);
assert.match(html, /id="weaponpaints-panel"/);
assert.match(html, /weaponpaints-app\.js/);
assert.match(html, /weaponpaints\.css/);
assert.match(html, /weaponpaints\.css\?v=1\.9-weaponpaints-advanced-decoration/);
assert.match(html, /weaponpaints-app\.js\?v=1\.9-weaponpaints-advanced-decoration/);
for (const category of ['gun', 'knife', 'glove', 'agent', 'music', 'pin', 'keychain']) {
    assert.match(js, new RegExp(`['"]${category}['"]`), `缺少分类 ${category}`);
}
assert.match(js, /id:\s*['"]sticker['"][^\n]*showInNav:\s*false/, '印花不应作为顶部独立分类');
assert.match(js, /id:\s*['"]keychain['"][^\n]*showInNav:\s*false/, '挂件不应作为顶部独立分类');
assert.match(js, /selectedCosmeticKey/, '单选装备必须保留待保存状态');
assert.match(js, /当前使用/, '右侧必须显示数据库中当前使用的单选装备');
assert.match(js, /待保存/, '右侧必须区分尚未保存的预览');
assert.match(js, /wp-save-cosmetic/, '人物、音乐盒和徽章必须通过明确的保存按钮生效');
assert.match(js, /weaponpaints-card-status/, '目录卡片必须显示当前使用或待保存状态');
assert.match(js, /currentSelectionItem/, '当前使用卡片必须按已保存涂装解析，不能被待保存预览覆盖');
assert.match(js, /finishItemStatus/, '涂装弹层必须区分当前使用和待保存涂装');
assert.match(js, /WEAPONPAINTS_ACTION/);
assert.match(js, /copyTeam/);
assert.match(js, /grouped/);
assert.match(js, /weaponpaints-finish-flyout/, '选择涂装应打开侧向弹层');
assert.match(js, /weaponpaints-finish-grid/, '涂装弹层应使用缩略图网格');
assert.match(js, /data-wp-finish-close/, '涂装弹层应提供关闭按钮');
assert.match(js, /<img class="weaponpaints-finish-image"[^>]*item\.imageUrl/, '每个涂装选项必须显示本地截图');
assert.doesNotMatch(js, /<details class="weaponpaints-finish-menu"/, '不应继续使用文字下拉菜单');
assert.match(js, /data-wp-default-glove/, '默认手套应直接选择，不应加载全部手套涂装');
assert.match(js, /selectedWeaponKind\s*===\s*['"]gun['"]/, '印花编辑必须限定为枪械');
assert.match(js, /const keychainEditor\s*=\s*state\.selectedWeaponKind\s*===\s*['"]gun['"]/, '挂件编辑必须限定为枪械');
for (const field of ['schema', 'offset-x', 'offset-y', 'wear', 'scale', 'rotation']) {
    assert.match(js, new RegExp(`wp-edit-sticker-${'\\$'}{slot}-${field}`), `印花必须提供 ${field} 高级参数`);
}
for (const field of ['offset-x', 'offset-y', 'offset-z', 'seed']) {
    assert.match(js, new RegExp(`wp-edit-keychain-${field}`), `挂件必须提供 ${field} 高级参数`);
}
assert.match(js, /schema:\s*Number\(/, '保存时必须收集印花 Schema');
assert.match(js, /rotation:\s*Number\(/, '保存时必须收集印花旋转');
assert.doesNotMatch(js, /scale:\s*Number\([^\n]+\|\|\s*1/, '非法的印花缩放值不能被静默改写为 1');
assert.match(js, /offsetZ:\s*Number\(/, '保存时必须收集挂件 Z 偏移');
assert.match(js, /保存此手套/);
assert.match(js, /用当前.*配置覆盖/);
assert.match(js, /copyButton\.title\s*=/, '复制按钮必须提供悬停说明');
assert.match(js, /weaponpaints-target[^\n]*loadTarget\(\)[^\n]*loadCatalog\(false\)/, '管理员切换玩家后必须同步刷新分组预览');
assert.match(js, /forceRefresh/);
assert.match(js, /stattrak/i);
assert.match(js, /stickers/);
assert.match(js, /item\.imageUrl/);
assert.match(js, /weaponpaints-placeholder\.svg/);
assert.match(js, /<img/);
assert.match(lobbyJs, /window\.__caorenCupLobbySocket\s*=\s*ws/, '大厅必须公开稳定的已登录 Socket');
assert.match(js, /window\.__caorenCupLobbySocket/, '换肤面板必须优先使用已登录大厅 Socket');
assert.match(css, /grid-template-columns/);
assert.match(css, /\.weaponpaints-admin-bar\[hidden\]\s*\{[^}]*display\s*:\s*none\s*!important/s, '普通玩家的管理员栏必须保持隐藏');
assert.match(css, /\.weaponpaints-card-image/);
assert.match(css, /\.weaponpaints-finish-grid\s*\{[^}]*grid-template-columns\s*:\s*repeat\(2,/s, '涂装截图必须按两列显示');
assert.match(css, /\.weaponpaints-finish-flyout\.left/, '靠近屏幕右侧时弹层必须支持向左展开');
assert.match(css, /\.weaponpaints-card-status\.current/, '当前使用状态必须有独立样式');
assert.match(css, /\.weaponpaints-card-status\.pending/, '待保存状态必须有独立样式');
assert.match(css, /#weaponpaints-copy-btn\s*\{[^}]*cursor\s*:\s*help/s, '复制按钮必须使用带问号的帮助指针');
assert.match(css, /@media\s*\(max-width:/);
assert.doesNotMatch(js, /https?:\/\//i, '换肤 UI 不应依赖远程图片或接口');
assert.match(webPackageScript, /public[\\\\/]weaponpaints/, '网页主包必须排除独立发布的 WeaponPaints 图片目录');
assert.match(webPackageScript, /weaponpaints-data/, '网页主包必须携带 WeaponPaints 本地目录数据');

console.log('weaponpaints UI checks passed');
