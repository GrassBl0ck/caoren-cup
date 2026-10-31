(function () {
  'use strict';
  var address = '';
  var target = document.getElementById('mc-message');
  var button = document.getElementById('copy-mc-address');
  function message(text) { target.textContent = '[草人杯] ' + text; }
  fetch('/api/mc/info', { credentials: 'same-origin', cache: 'no-store' }).then(async function (response) {
    if (response.status === 401) { window.location.replace('/?next=mc'); return; }
    if (!response.ok) throw new Error('暂时无法读取入服信息，请稍后重试。');
    var data = await response.json();
    if (!data.success) throw new Error('暂时无法读取入服信息。');
    address = data.serverAddress;
    document.getElementById('mc-version').textContent = 'Java ' + data.version;
    document.getElementById('mc-address').textContent = address;
    button.disabled = !address;
  }).catch(function (error) { message(error.message || '连接失败，请稍后重试。'); });
  button.addEventListener('click', async function () {
    try { await navigator.clipboard.writeText(address); message('地址已复制，请在 Minecraft 多人游戏中添加服务器。'); }
    catch (_) { message('无法自动复制，请手动选中上面的服务器地址复制。'); }
  });
})();
