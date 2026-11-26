'use strict';

(() => {
  const dialog = document.getElementById('resources-dialog');
  const list = document.getElementById('resources-list');
  const summary = document.getElementById('resources-summary');
  const status = document.getElementById('resources-status');
  const request = window.caorenDesktop.workshopMaps;
  const entries = new Map();
  let timer, refreshing = false, lastError = '';
  function formatBytes(bytes) {
    return bytes >= 1e9 ? (bytes / 1e9).toFixed(2) + ' GB' : (bytes / 1e6).toFixed(1) + ' MB';
  }
  function entry(map) {
    if (entries.has(map.id)) return entries.get(map.id);
    const row = document.createElement('li');
    const label = document.createElement('div');
    const title = document.createElement('strong');
    title.textContent = map.title;
    const name = document.createElement('small');
    name.textContent = map.name;
    label.append(title, name);
    const size = document.createElement('span');
    const button = document.createElement('button');
    button.type = 'button';
    button.setAttribute('aria-label', '下载 ' + map.title);
    button.onclick = async () => {
      button.disabled = true;
      try {
        const result = await request('download', map.id);
        lastError = result.error || '';
      } catch { lastError = '无法连接客户端，请重新打开资源页。'; }
      await refresh();
    };
    row.append(label, size, button);
    list.append(row);
    const controls = { size, button };
    entries.set(map.id, controls);
    return controls;
  }
  async function refresh() {
    if (refreshing || !dialog.open) return;
    refreshing = true;
    try {
      const { rows, error, hint } = await request('list');
      let total = 0, ready = 0;
      for (const map of rows) {
        const { size, button } = entry(map);
        size.textContent = formatBytes(map.bytes);
        button.textContent = { available: '下载', pending: '等待 Steam', ready: '已就绪', retry: '重试' }[map.state];
        button.disabled = Boolean(error) || map.state === 'ready' || map.state === 'pending';
        total += map.bytes;
        if (map.state === 'ready') ready++;
      }
      summary.textContent = rows.length + ' 张地图 · 合计 ' + formatBytes(total) + ' · 已就绪 ' + ready + ' 张';
      status.textContent = lastError || error || hint || '';
    } catch { status.textContent = '无法读取地图信息，请稍后重试。'; }
    finally { refreshing = false; }
  }
  document.getElementById('resources-open').onclick = () => {
    if (dialog.open) return;
    dialog.showModal();
    lastError = '';
    refresh();
    timer = setInterval(refresh, 2200);
  };
  document.getElementById('resources-close').onclick = () => dialog.close();
  dialog.addEventListener('close', () => clearInterval(timer));
})();
