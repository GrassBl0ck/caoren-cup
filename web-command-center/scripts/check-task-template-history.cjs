const assert = require('node:assert/strict');
const path = require('node:path');
const { TaskTemplateHistory } = require(path.resolve(__dirname, '..', 'public', 'js', 'task-template-history.js'));

const initial = { cells: { A1: { description: '', hint: '', level: 1 } } };
const history = new TaskTemplateHistory(100, 400);
history.reset(initial);

history.record({ cells: { A1: { description: 'a', hint: '', level: 1 } } }, { coalesceKey: 'A1:description', timestamp: 100 });
history.record({ cells: { A1: { description: 'ab', hint: '', level: 1 } } }, { coalesceKey: 'A1:description', timestamp: 200 });
assert.equal(history.undo().cells.A1.description, '', 'continuous typing should undo as one edit');
assert.equal(history.redo().cells.A1.description, 'ab', 'redo should restore coalesced text');

history.record({ cells: { A1: { description: 'ab', hint: 'hint', level: 1 } } }, { coalesceKey: 'A1:hint', timestamp: 1000 });
assert.equal(history.undo().cells.A1.hint, '', 'hint edits should be undoable');
assert.equal(history.redo().cells.A1.hint, 'hint', 'hint edits should be redoable');

for (let index = 0; index < 120; index += 1) {
  history.record({ cells: { A1: { description: String(index), hint: 'hint', level: 1 } } }, { coalesceKey: `level:${index}`, timestamp: 2000 + index });
}
assert.ok(history.pastSize <= 100, 'history must keep at most 100 undo states');

const snapshot = history.current();
snapshot.cells.A1.description = 'mutated outside';
assert.notEqual(history.current().cells.A1.description, 'mutated outside', 'history snapshots must be deep cloned');

console.log('task template history checks passed');
