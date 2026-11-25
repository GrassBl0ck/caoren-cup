'use strict';

(() => {
  const elem = id => document.getElementById('keybinds-' + id);
  const dialog = elem('dialog'), list = elem('list'), status = elem('status');
  const save = elem('save'), cancel = elem('cancel');
  const ipc = window.caorenDesktop?.keybinds;
  let model, selected, pending, revision = 0, writing = false;
  const named = {
    Enter: 'ENTER', Backspace: 'BACKSPACE', Tab: 'TAB', Space: 'SPACE', Insert: 'INS',
    Home: 'HOME', PageUp: 'PGUP', Delete: 'DEL', End: 'END', PageDown: 'PGDN',
    ArrowRight: 'RIGHTARROW', ArrowLeft: 'LEFTARROW', ArrowDown: 'DOWNARROW', ArrowUp: 'UPARROW',
    ControlLeft: 'CTRL', ShiftLeft: 'SHIFT', AltLeft: 'ALT',
  };
  const showKey = key => ({ MOUSE3: '鼠标中键', MOUSE4: '鼠标侧键 4', MOUSE5: '鼠标侧键 5', SPACE: '空格' })[key] || key;
  const display = (cmd, label) => !cmd ? '未绑定' : label && label !== cmd ? label + '（' + cmd + '）' : cmd;

  function reset(message = '') {
    revision++;
    selected = pending = null;
    save.hidden = cancel.hidden = true;
    list.querySelector('.listening')?.classList.remove('listening');
    status.textContent = message;
  }
  function render() {
    list.replaceChildren();
    for (const admin of [false, true]) {
      const heading = document.createElement('h3');
      heading.textContent = admin ? '管理员' : '玩家';
      list.append(heading);
      for (const action of model.actions.filter(entry => entry.admin === admin)) {
        const row = document.createElement('div');
        row.className = 'keybind-row';
        const name = document.createElement('span');
        name.textContent = action.name + (action.hold ? '（按住）' : '');
        const choose = document.createElement('button');
        choose.type = 'button';
        choose.textContent = action.key ? showKey(action.key) : '未绑定';
        choose.setAttribute('aria-label', action.name + '，' + choose.textContent + '，点此选键');
        choose.onclick = () => {
          if (writing) return;
          reset('请按要绑定的按键或鼠标侧键。Esc 取消。');
          selected = action;
          cancel.hidden = false;
          choose.classList.add('listening');
        };
        row.append(name, choose);
        if (action.key) {
          const clear = document.createElement('button');
          clear.type = 'button'; clear.textContent = '清除';
          clear.setAttribute('aria-label', '清除' + action.name + '的绑定');
          clear.onclick = () => { if (!writing) { reset(); void review(action, null); } };
          row.append(clear);
        }
        list.append(row);
      }
    }
  }
  async function reload(message = '') {
    reset(message);
    const ticket = revision;
    try {
      const result = await ipc('list');
      if (ticket !== revision || !dialog.open) return;
      if (result.error) throw Error(result.error);
      model = result;
      elem('account').textContent = 'Steam 账号 · ' + (model.persona ? model.persona + ' · ' : '') + model.steamId;
      render();
    } catch (err) {
      if (ticket === revision) { model = null; list.replaceChildren(); status.textContent = err.message; }
    }
  }
  async function review(action, key) {
    reset('正在检查原绑定…');
    cancel.hidden = false;
    const ticket = revision;
    const request = { id: action.id, key, steamId: model.steamId };
    try {
      const result = await ipc('review', request);
      if (ticket !== revision || !dialog.open) return;
      if (result.error) throw Error(result.error);
      if (result.unchanged) { reset('这个动作已经使用该键位。'); return; }
      const lines = [];
      if (key) {
        lines.push(showKey(key) + ' 上次保存为：' + display(result.current, result.label));
        if (result.occupiedBy && result.occupiedBy !== action.name) lines.push('此键已分配给「' + result.occupiedBy + '」，替换后会解除该分配。');
        lines.push('将绑定到「' + action.name + '」。');
      }
      if (result.oldKey && result.oldKey !== key) {
        lines.push(showKey(result.oldKey) + ' 将恢复为：' + display(result.restore, result.restoreLabel) + '（若已另行改键，则保留新设置）。');
      }
      status.textContent = lines.join('\n');
      pending = { ...request, stamp: result.stamp };
      save.textContent = !key ? '确认清除' : result.current || result.occupiedBy ? '确认替换' : '保存';
      save.hidden = false;
      save.focus();
    } catch (e) { if (ticket === revision) reset(e.message); }
  }

  elem('open').addEventListener('click', () => {
    if (!ipc || writing || dialog.open) return;
    list.replaceChildren(); elem('account').textContent = '';
    dialog.showModal();
    void reload('正在读取 Steam 账号…');
  });
  elem('close').onclick = () => { if (!writing) dialog.close(); };
  cancel.onclick = () => { if (!writing) reset('已取消选择。'); };
  dialog.addEventListener('close', () => reset());
  dialog.addEventListener('cancel', e => { if (writing || selected || pending) { e.preventDefault(); if (!writing) reset(); } });
  save.onclick = async () => {
    if (!pending || writing) return;
    writing = save.disabled = true;
    try {
      const result = await ipc('save', pending);
      if (result.error) throw Error(result.error);
      await reload('已保存，下次从草台杯客户端启动 CS2 时生效。');
    } catch (e) { await reload(e.message); }
    finally { writing = save.disabled = false; }
  };
  window.addEventListener('keydown', e => {
    if (!dialog.open || !selected) return;
    e.preventDefault(); e.stopImmediatePropagation();
    if (e.repeat) return;
    if (e.code === 'Escape') { reset('已取消选择。'); return; }
    const key = named[e.code] || (/^Key[A-Z]$/.test(e.code) ? e.code.slice(3) :
      /^Digit[0-9]$/.test(e.code) ? e.code.slice(5) : /^F([1-9]|1[0-2])$/.test(e.code) ? e.code : null);
    if (key) void review(selected, key);
    else status.textContent = '暂不支持此键。可以选择字母、数字、F1–F12、空格或鼠标侧键等。';
  }, true);
  window.addEventListener('mousedown', e => {
    if (!dialog.open || !selected || ![1, 3, 4].includes(e.button)) return;
    e.preventDefault(); e.stopImmediatePropagation();
    void review(selected, ({ 1: 'MOUSE3', 3: 'MOUSE4', 4: 'MOUSE5' })[e.button]);
  }, true);
  for (const type of ['mouseup', 'auxclick']) window.addEventListener(type, e => {
    if (dialog.open && [1, 3, 4].includes(e.button)) e.preventDefault();
  }, true);
})();
