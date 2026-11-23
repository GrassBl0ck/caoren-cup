const fs = require('node:fs');
const path = require('node:path');

function installLocalImages(session, root, origin) {
  const indexPath = path.join(root, 'index.json');
  if (!fs.existsSync(indexPath)) return;
  const { images, size } = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
  const archivePath = path.join(root, 'images.pack');
  if (!images || typeof images !== 'object' || fs.statSync(archivePath).size !== size) throw new Error('图片资源包不完整');
  const file = fs.openSync(archivePath, 'r');
  const lookup = url => {
    try { return images[decodeURIComponent(new URL(url).pathname)]; } catch { return undefined; }
  };
  session.protocol.handle('caoren-image', async request => {
    const url = new URL(request.url);
    const item = url.host === 'bundle' && lookup(request.url);
    if (!item) return new Response(null, { status: 404 });
    const [offset, length, mime] = item;
    const body = Buffer.alloc(length);
    let read = 0;
    while (read < length) {
      const n = await new Promise((resolve, reject) => fs.read(file, body, read, length - read, offset + read,
        (error, count) => error ? reject(error) : resolve(count)));
      if (!n) throw new Error('图片资源读取不完整');
      read += n;
    }
    return new Response(body, { headers: { 'Content-Type': mime, 'Cache-Control': 'public, max-age=31536000, immutable' } });
  });
  session.webRequest.onBeforeRequest({ urls: [origin + '/assets/*', origin + '/weaponpaints/*'] }, (request, done) => {
    if (request.resourceType === 'image' && new URL(request.url).origin === origin && lookup(request.url)) {
      done({ redirectURL: 'caoren-image://bundle' + new URL(request.url).pathname });
    } else done({});
  });
}
module.exports = { installLocalImages };
