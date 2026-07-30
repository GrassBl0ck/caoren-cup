const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'weaponpaints-app.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'weaponpaints.css'), 'utf8');

assert.match(html, /id="weaponpaints-open-btn"/);
assert.match(html, /id="weaponpaints-panel"/);
assert.match(html, /weaponpaints-app\.js/);
assert.match(html, /weaponpaints\.css/);
for (const category of ['gun', 'knife', 'glove', 'agent', 'music', 'pin', 'sticker', 'keychain']) {
    assert.match(js, new RegExp(`['"]${category}['"]`), `缺少分类 ${category}`);
}
assert.match(js, /WEAPONPAINTS_ACTION/);
assert.match(js, /copyTeam/);
assert.match(js, /forceRefresh/);
assert.match(js, /stattrak/i);
assert.match(js, /stickers/);
assert.match(js, /item\.imageUrl/);
assert.match(js, /weaponpaints-placeholder\.svg/);
assert.match(js, /<img/);
assert.match(css, /grid-template-columns/);
assert.match(css, /\.weaponpaints-card-image/);
assert.match(css, /@media\s*\(max-width:/);
assert.doesNotMatch(js, /https?:\/\//i, '换肤 UI 不应依赖远程图片或接口');

console.log('weaponpaints UI checks passed');
