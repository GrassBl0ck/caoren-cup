/* PHASE2_SAFE_ADMIN_VISIBILITY_V3 extracted from index.html */
(function() {
  var MARK = 'PHASE2_SAFE_ADMIN_VISIBILITY_V3';

  function norm(s) {
    return String(s || '').replace(/\s+/g, ' ').trim();
  }

  function findCurrentUserLine() {
    // 只找“你是：...”这条当前用户身份提示。
    // 不用整页 body.textContent 判断，避免把玩家表里的“管理员”误当成当前用户管理员。
    var nodes = Array.prototype.slice.call(document.querySelectorAll('body *'));
    var best = '';

    for (var i = 0; i < nodes.length; i += 1) {
      var el = nodes[i];
      if (!el || !el.textContent) continue;
      var text = norm(el.textContent);
      if (!/你是\s*[：:]/.test(text)) continue;
      if (text.length > 90) continue;

      // 优先选择最小的元素：如果子元素也包含“你是：”，当前元素就是外层容器，先跳过。
      var childHasSelfLine = false;
      for (var j = 0; j < el.children.length; j += 1) {
        if (/你是\s*[：:]/.test(norm(el.children[j].textContent))) {
          childHasSelfLine = true;
          break;
        }
      }
      if (childHasSelfLine) continue;

      if (!best || text.length < best.length) best = text;
    }

    if (best) return best;

    // 兜底：直接扫描文本节点，再向上取一个较短的父元素文本。
    try {
      var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null);
      var node;
      while ((node = walker.nextNode())) {
        if (!/你是\s*[：:]/.test(norm(node.nodeValue))) continue;
        var p = node.parentElement;
        while (p && p !== document.body) {
          var t = norm(p.textContent);
          if (/你是\s*[：:]/.test(t) && t.length <= 90) return t;
          p = p.parentElement;
        }
      }
    } catch (e) {}

    return '';
  }

  function isCurrentUserAdmin() {
    var line = findCurrentUserLine();
    if (!line) return false;
    return /(管理员|admin)/i.test(line);
  }

  function getTargets() {
    var selectors = [
      '.caoren-unified-mod-shell',
      '.caoren-batch2-wrapper',
      '[data-caoren-mod-panel]',
      '[data-caoren-unified-panel]'
    ];
    return Array.prototype.slice.call(document.querySelectorAll(selectors.join(',')));
  }

  function enforce() {
    var admin = isCurrentUserAdmin();
    var targets = getTargets();

    targets.forEach(function(el) {
      el.style.display = admin ? '' : 'none';
      el.setAttribute('data-caoren-admin-visible', admin ? '1' : '0');
    });

    document.documentElement.setAttribute('data-caoren-current-user-admin', admin ? '1' : '0');
  }

  function boot() {
    enforce();

    var timer = null;
    var observer = new MutationObserver(function() {
      if (timer) window.clearTimeout(timer);
      timer = window.setTimeout(enforce, 60);
    });
    observer.observe(document.documentElement || document.body, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ['class', 'style']
    });

    window.addEventListener('storage', enforce);
    window.setTimeout(enforce, 100);
    window.setTimeout(enforce, 300);
    window.setTimeout(enforce, 800);
    window.setTimeout(enforce, 1500);

    console.info('[' + MARK + '] 当前用户身份行：', findCurrentUserLine() || '(未找到)');
    console.info('[' + MARK + '] 当前用户是否管理员：', isCurrentUserAdmin());
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();