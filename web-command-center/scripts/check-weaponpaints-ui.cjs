const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'weaponpaints-app.js'), 'utf8');
const lobbyJs = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'weaponpaints.css'), 'utf8');
const webPackageScript = fs.readFileSync(path.join(root, '..', 'scripts', 'package-caoren-web.ps1'), 'utf8');
const socketApi = fs.readFileSync(path.join(root, 'src', 'weaponpaints', 'socket-api.ts'), 'utf8');
const service = fs.readFileSync(path.join(root, 'src', 'weaponpaints', 'service.ts'), 'utf8');
const repository = fs.readFileSync(path.join(root, 'src', 'weaponpaints', 'repository.ts'), 'utf8');
const mysqlRepository = fs.readFileSync(path.join(root, 'src', 'weaponpaints', 'mysql-repository.ts'), 'utf8');

assert.match(html, /id="weaponpaints-open-btn"/);
assert.match(html, /id="weaponpaints-panel"/);
assert.match(html, /weaponpaints-app\.js/);
assert.match(html, /weaponpaints\.css/);
assert.match(html, /weaponpaints\.css\?v=1\.9\.2-qol/);
assert.match(html, /weaponpaints-app\.js\?v=1\.9\.2/);
for (const category of ['gun', 'knife', 'glove', 'agent', 'music', 'pin', 'keychain']) {
    assert.match(js, new RegExp(`['"]${category}['"]`), `缺少分类 ${category}`);
}
assert.match(js, /id:\s*['"]sticker['"][^\n]*showInNav:\s*false/, '印花不应作为顶部独立分类');
assert.match(js, /id:\s*['"]keychain['"][^\n]*showInNav:\s*false/, '挂件不应作为顶部独立分类');
assert.match(js, /selectedCosmeticKey/, '单选装备必须保留待保存状态');
assert.match(js, /当前使用/, '右侧必须显示数据库中当前使用的单选装备');
assert.match(js, /待保存/, '右侧必须区分尚未保存的预览');
assert.match(js, /wp-save-cosmetic/, '探员、音乐盒和徽章必须通过明确的保存按钮生效');
assert.match(js, /weaponpaints-card-status/, '目录卡片必须显示当前使用或待保存状态');
assert.match(js, /currentSelectionItem/, '当前使用卡片必须按已保存涂装解析，不能被待保存预览覆盖');
assert.match(js, /finishItemStatus/, '涂装弹层必须区分当前使用和待保存涂装');
assert.match(js, /WEAPONPAINTS_ACTION/);
assert.match(js, /state\.status\?\.isAdmin[\s\S]*targetSteamId/, '仅管理员代管请求可携带目标 SteamID');
assert.doesNotMatch(js, /selfSteamId/, '普通玩家不应从服务端接收或编辑 SteamID');
assert.doesNotMatch(html, /weaponpaints-copy-btn/, '阵营配置覆盖按钮应完全移除');
assert.doesNotMatch(js, /copyTeam|action:\s*['"]copyTeam['"]/, '前端阵营配置覆盖逻辑应完全移除');
assert.doesNotMatch(socketApi, /copyTeam/, 'Socket 不应继续接受阵营配置覆盖操作');
assert.doesNotMatch(service, /copyTeam/, '服务层不应保留阵营配置覆盖逻辑');
assert.doesNotMatch(repository, /copyTeam|copy_team|TeamCopyRules/, '仓储接口不应保留阵营配置覆盖能力');
assert.doesNotMatch(mysqlRepository, /copyTeam|buildTeamCopyStatements|TeamCopyRules/, 'MySQL 仓储不应保留阵营复制 SQL');
assert.match(js, /label:\s*['"]探员['"]/, '角色分类应统一称为探员');
assert.doesNotMatch(js, /label:\s*['"]人物['"]/, '换肤分类不应继续显示人物');
assert.match(html, /id="weaponpaints-unsaved-dialog"/, '必须使用自定义未保存提示对话框');
assert.match(html, /保存并继续/);
assert.match(html, /放弃更改/);
assert.match(html, /返回编辑/);
assert.match(js, /guardUnsavedChange/, '所有会丢弃草稿的跳转应经过统一保护');
assert.match(html, /weaponpaints-draft\.js/, '页面必须加载可测试的草稿比较模块');
assert.match(css, /\.weaponpaints-toast[^}]*position\s*:\s*fixed/s, '保存反馈应固定在可视区域');
assert.match(css, /\.weaponpaints-unsaved-dialog/, '未保存提示必须使用项目自定义样式');
assert.match(js, /weaponpaints-rarity-/, '皮肤名称应携带稀有度颜色类');
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
assert.match(js, /weaponpaints-target[\s\S]{0,1200}loadTarget\(\);\s*await loadCatalog\(false\)/, '管理员切换玩家后必须同步刷新分组预览');
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
assert.doesNotMatch(css, /#weaponpaints-copy-btn/, '不应残留已删除按钮的样式');
assert.match(css, /@media\s*\(max-width:/);
assert.doesNotMatch(js, /https?:\/\//i, '换肤 UI 不应依赖远程图片或接口');
assert.match(webPackageScript, /public[\\\\/]weaponpaints/, '网页主包必须排除独立发布的 WeaponPaints 图片目录');
assert.match(webPackageScript, /weaponpaints-data/, '网页主包必须携带 WeaponPaints 本地目录数据');

console.log('weaponpaints UI checks passed');
