/* v1.3.5 game-code-login client start */
(function () {
  function byId(id) { return document.getElementById(id); }

  function isDesktopClient() {
    return /\bCaorenCupDesktopClient\/1\.0\b/.test(navigator.userAgent || '');
  }

  function hideDesktopClientDownloadInClient() {
    if (!isDesktopClient()) return;
    var downloadIds = [
      'caoren-desktop-client-download',
      'caoren-desktop-client-github-download'
    ];
    downloadIds.forEach(function (id) {
      var downloadLink = byId(id);
      if (downloadLink) downloadLink.style.display = 'none';
    });
  }

  function ensureDesktopClientRefreshButton() {
    if (!isDesktopClient() || byId('desktop-client-refresh-btn')) return;
    var btn = document.createElement('button');
    btn.id = 'desktop-client-refresh-btn';
    btn.type = 'button';
    btn.textContent = '\u5237\u65b0\u9875\u9762';
    btn.title = '\u9875\u9762\u5361\u4f4f\u6216\u72b6\u6001\u4e0d\u5bf9\u65f6\uff0c\u70b9\u8fd9\u91cc\u91cd\u65b0\u8f7d\u5165\u3002';
    btn.style.position = 'fixed';
    btn.style.top = '12px';
    btn.style.right = '112px';
    btn.style.zIndex = '9999';
    btn.style.padding = '9px 14px';
    btn.style.borderRadius = '999px';
    btn.style.border = '1px solid rgba(148,163,184,.55)';
    btn.style.background = '#0f172a';
    btn.style.color = '#fff';
    btn.style.fontWeight = '800';
    btn.style.boxShadow = '0 8px 22px rgba(15,23,42,.18)';
    btn.addEventListener('click', function () {
      window.location.reload();
    });
    document.body.appendChild(btn);
  }

  function getLoginSocket() {
    try {
      if (typeof ws !== 'undefined' && ws && ws.emit) return ws;
      if (typeof socket !== 'undefined' && socket && socket.emit) return socket;
    } catch (_err) {}
    if (window.__caorenCupSocket && window.__caorenCupSocket.emit) return window.__caorenCupSocket;
    if (window.socket && window.socket.emit) return window.socket;
    return null;
  }

  function setStatus(text, online, hasConnectUrl, joinAllowed) {
    var dot = byId('v1333-server-dot');
    var label = byId('v1333-server-status');
    var btn = byId('v1333-connect-server-btn');
    var lobbyBtn = byId('v1333-lobby-connect-server-btn');
    if (dot) {
      dot.style.background = online ? '#16a34a' : '#dc2626';
      dot.style.boxShadow = online ? '0 0 0 3px rgba(22,163,74,.18)' : '0 0 0 3px rgba(220,38,38,.16)';
    }
    if (label) label.textContent = text;
    if (btn) {
      btn.disabled = !(joinAllowed && hasConnectUrl);
      btn.title = hasConnectUrl
        ? (joinAllowed ? (online ? '通过 Steam 协议连接服务器' : '服务器可连接，桥接插件未就绪，战绩可能暂不可用') : '服务器离线或桥接插件未连接')
        : '未配置 GAME_SERVER_CONNECT_URL 或 GAME_SERVER_ADDRESS';
    }
    if (lobbyBtn) {
      lobbyBtn.disabled = !(joinAllowed && hasConnectUrl);
      lobbyBtn.title = hasConnectUrl ? '通过 Steam 协议重连草人杯服务器' : '未配置服务器连接地址';
    }
  }

  function bootGameCodeLogin() {
    var loginArea = byId('login-area') || document.body;
    var panel = byId('v1333-game-code-login-panel');
    if (!panel) return;
    hideDesktopClientDownloadInClient();
    ensureDesktopClientRefreshButton();

    Array.prototype.forEach.call(loginArea.querySelectorAll('button'), function (el) {
      if (el.id !== 'v1333-connect-server-btn' && el.id !== 'v1335-enter-lobby-btn') el.style.display = 'none';
    });

    var input = byId('v1333-game-login-code-input');
    var connectBtn = byId('v1333-connect-server-btn');
    var lobbyConnectBtn = byId('v1333-lobby-connect-server-btn');
    var enterBtn = byId('v1335-enter-lobby-btn');
    var lastStatus = null;

    function refreshStatus() {
      fetch('/api/public/server-status', { cache: 'no-store' })
        .then(function (res) { return res.json(); })
        .then(function (data) {
          lastStatus = data || {};
          var hasConnectUrl = !!lastStatus.connectUrl;
          if (lastStatus.pluginReady || lastStatus.online) {
            setStatus('草人杯服务器在线，战绩插件已连接', true, hasConnectUrl, true);
          } else if (lastStatus.joinAllowed !== false && hasConnectUrl) {
            setStatus('服务器可连接，桥接插件未就绪，战绩可能暂不可用', false, hasConnectUrl, true);
          } else {
            setStatus('草人杯服务器离线或插件未连接', false, hasConnectUrl, false);
          }
        })
        .catch(function () {
          lastStatus = null;
          setStatus('无法读取服务器状态', false, false, false);
        });
    }

    if (connectBtn) {
      connectBtn.addEventListener('click', function () {
        var url = lastStatus && lastStatus.connectUrl;
        if (!url) {
          alert('服务器连接地址未配置。请在网页端环境变量里设置 GAME_SERVER_CONNECT_URL，推荐使用 steam://connect/IP:端口。');
          return;
        }
        window.location.href = url;
      });
    }

    if (lobbyConnectBtn) {
      lobbyConnectBtn.addEventListener('click', function () {
        if (connectBtn) connectBtn.click();
      });
    }

    function enterLobby() {
      var credential = input ? input.value.trim() : '';
      if (!credential) {
        alert('请输入游戏内返回的码或管理员密码。');
        if (input) input.focus();
        return;
      }
      var loginSocket = getLoginSocket();
      if (!loginSocket) {
        alert('网页 Socket 尚未初始化，请按 Ctrl+F5 强制刷新后重试。');
        return;
      }
      loginSocket.emit('GAME_CODE_LOGIN', { credential: credential });
    }

    if (input) {
      input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') enterLobby();
      });
    }

    if (enterBtn) {
      enterBtn.addEventListener('click', enterLobby);
    }

    refreshStatus();
    setInterval(refreshStatus, 5000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bootGameCodeLogin);
  else bootGameCodeLogin();
})();
/* v1.3.5 game-code-login client end */
