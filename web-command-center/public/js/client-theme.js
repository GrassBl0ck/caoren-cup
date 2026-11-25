(() => {
  const key = 'caoren-theme';
  const root = document.documentElement;
  const desktop = () => Boolean(window.caorenDesktop) || root.hasAttribute('data-caoren-window');
  let preferred;
  try { preferred = localStorage.getItem(key); } catch { /* Storage can be disabled. */ }

  function apply(preference) {
    const theme = !desktop() && preference === 'light' ? 'light' : 'dark';
    root.dataset.cupTheme = theme;
    if (document.body) document.body.dataset.theme = theme;
    const button = document.getElementById('theme-toggle');
    if (button) {
      button.hidden = desktop();
      button.textContent = theme === 'dark' ? '浅色模式' : '深色模式';
      button.setAttribute('aria-label', '切换到' + button.textContent);
    }
  }

  apply(preferred);
  document.addEventListener('DOMContentLoaded', () => {
    apply(root.dataset.cupTheme);
    document.getElementById('theme-toggle')?.addEventListener('click', () => {
      if (desktop()) return;
      const next = root.dataset.cupTheme === 'dark' ? 'light' : 'dark';
      apply(next);
      try { localStorage.setItem(key, next); } catch { /* Still usable on this page. */ }
    });
  });
  window.addEventListener('storage', event => {
    if (event.key === key || event.key === null) apply(event.newValue);
  });
})();
