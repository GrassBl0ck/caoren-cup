(function () {
    'use strict';

    const dialogQueue = [];
    let activeRequest = null;

    function elements() {
        return {
            dialog: document.getElementById('caoren-dialog'),
            card: document.querySelector('#caoren-dialog .caoren-dialog-card'),
            title: document.getElementById('caoren-dialog-title'),
            message: document.getElementById('caoren-dialog-message'),
            inputWrap: document.getElementById('caoren-dialog-input-wrap'),
            inputLabel: document.getElementById('caoren-dialog-input-label'),
            input: document.getElementById('caoren-dialog-input'),
            inputError: document.getElementById('caoren-dialog-input-error'),
            cancel: document.getElementById('caoren-dialog-cancel'),
            confirm: document.getElementById('caoren-dialog-confirm'),
        };
    }

    function enqueueDialog(type, message, options) {
        return new Promise((resolve) => {
            dialogQueue.push({ type, message: String(message || ''), options: options || {}, resolve });
            openNextDialog();
        });
    }

    function openNextDialog() {
        if (activeRequest || dialogQueue.length === 0) return;
        const el = elements();
        if (!el.dialog || !el.card) return;
        activeRequest = dialogQueue.shift();
        const { type, message, options } = activeRequest;
        const isPrompt = type === 'prompt';
        const isAlert = type === 'alert';
        const isDanger = options.tone === 'danger';

        el.card.classList.toggle('is-danger', isDanger);
        el.title.textContent = options.title || (isDanger ? '确认危险操作' : (isAlert ? '操作提示' : '请确认操作'));
        el.message.textContent = message;
        el.cancel.hidden = isAlert;
        el.cancel.textContent = options.cancelText || '取消';
        el.confirm.textContent = options.confirmText || (isAlert ? '知道了' : '确认');
        el.confirm.classList.toggle('is-danger', isDanger);
        el.inputWrap.hidden = !isPrompt;
        el.inputError.hidden = true;
        el.inputError.textContent = '';

        if (isPrompt) {
            el.inputLabel.textContent = options.inputLabel || '请输入内容';
            el.input.type = options.inputType || 'text';
            el.input.value = options.value || '';
            el.input.placeholder = options.placeholder || '';
            el.input.inputMode = options.inputMode || '';
            el.input.min = options.min === undefined ? '' : String(options.min);
            el.input.max = options.max === undefined ? '' : String(options.max);
            el.input.step = options.step === undefined ? '' : String(options.step);
        }

        el.dialog.showModal();
        requestAnimationFrame(() => (isPrompt ? el.input : el.confirm).focus());
    }

    function finishDialog(value) {
        if (!activeRequest) return;
        const el = elements();
        const request = activeRequest;
        activeRequest = null;
        if (el.dialog?.open) el.dialog.close();
        request.resolve(value);
        setTimeout(openNextDialog, 0);
    }

    function confirmActiveDialog() {
        if (!activeRequest) return;
        const el = elements();
        if (activeRequest.type !== 'prompt') {
            finishDialog(activeRequest.type === 'confirm' ? true : undefined);
            return;
        }
        const value = el.input.value;
        if (activeRequest.options.required && !value.trim()) {
            el.inputError.textContent = activeRequest.options.requiredMessage || '请输入内容后再继续。';
            el.inputError.hidden = false;
            el.input.focus();
            return;
        }
        finishDialog(value);
    }

    function cancelActiveDialog() {
        if (!activeRequest) return;
        finishDialog(activeRequest.type === 'confirm' ? false : (activeRequest.type === 'prompt' ? null : undefined));
    }

    document.addEventListener('DOMContentLoaded', () => {
        const el = elements();
        el.confirm?.addEventListener('click', confirmActiveDialog);
        el.cancel?.addEventListener('click', cancelActiveDialog);
        el.input?.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter') return;
            event.preventDefault();
            confirmActiveDialog();
        });
        el.dialog?.addEventListener('cancel', (event) => {
            event.preventDefault();
            cancelActiveDialog();
        });
        el.dialog?.addEventListener('click', (event) => {
            if (event.target === el.dialog) cancelActiveDialog();
        });
        document.addEventListener('click', async (event) => {
            const trigger = event.target?.closest?.('[data-caoren-confirm]');
            if (!trigger) return;
            event.preventDefault();
            event.stopPropagation();
            const confirmed = await window.caorenConfirm(trigger.dataset.confirmMessage || '', {
                title: trigger.dataset.confirmTitle || '请确认操作',
                confirmText: trigger.dataset.confirmText || '确认',
                cancelText: trigger.dataset.cancelText || '取消',
                tone: trigger.dataset.confirmTone || undefined,
            });
            if (!confirmed || !trigger.dataset.confirmEvent) return;
            document.dispatchEvent(new CustomEvent(trigger.dataset.confirmEvent, { detail: { triggerId: trigger.id } }));
        }, true);
        openNextDialog();
    });

    window.caorenConfirm = (message, options) => enqueueDialog('confirm', message, options);
    window.caorenPrompt = (message, options) => enqueueDialog('prompt', message, options);
    window.caorenAlert = (message, options) => enqueueDialog('alert', message, options);
})();
