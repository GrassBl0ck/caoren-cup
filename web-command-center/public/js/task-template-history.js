(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.CaorenTaskTemplateHistory = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  const clone = (value) => JSON.parse(JSON.stringify(value));

  class TaskTemplateHistory {
    constructor(limit = 100, coalesceMs = 400) {
      this.limit = Math.max(1, Number(limit) || 100);
      this.coalesceMs = Math.max(0, Number(coalesceMs) || 0);
      this.past = [];
      this.present = null;
      this.future = [];
      this.lastKey = '';
      this.lastTimestamp = 0;
    }

    reset(value) {
      this.past = [];
      this.present = clone(value);
      this.future = [];
      this.lastKey = '';
      this.lastTimestamp = 0;
      return this.current();
    }

    record(value, options = {}) {
      const timestamp = Number(options.timestamp ?? Date.now());
      const key = String(options.coalesceKey || '');
      const canCoalesce = !!key && key === this.lastKey && timestamp - this.lastTimestamp <= this.coalesceMs;
      if (!canCoalesce && this.present !== null) {
        this.past.push(clone(this.present));
        if (this.past.length > this.limit) this.past.splice(0, this.past.length - this.limit);
      }
      this.present = clone(value);
      this.future = [];
      this.lastKey = key;
      this.lastTimestamp = timestamp;
      return this.current();
    }

    undo() {
      if (this.past.length === 0 || this.present === null) return this.current();
      this.future.unshift(clone(this.present));
      this.present = this.past.pop();
      this.lastKey = '';
      return this.current();
    }

    redo() {
      if (this.future.length === 0 || this.present === null) return this.current();
      this.past.push(clone(this.present));
      this.present = this.future.shift();
      this.lastKey = '';
      return this.current();
    }

    current() { return this.present === null ? null : clone(this.present); }
    get pastSize() { return this.past.length; }
  }

  return { TaskTemplateHistory };
});
