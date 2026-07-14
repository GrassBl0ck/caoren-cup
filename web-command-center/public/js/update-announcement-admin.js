(function () {
    'use strict';

    const mutationStateApi = window.CaorenUpdateAnnouncementAdminMutationState;

    async function adminRequest(path, payload) {
        const adminPassword = document.getElementById('extra-input')?.value
            || prompt('请输入管理员密码：')
            || '';
        if (!adminPassword) throw new Error('未输入管理员密码');
        const response = await fetch(path, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
            body: JSON.stringify(Object.assign({ adminPassword: adminPassword }, payload || {})),
        });
        const data = await response.json();
        if (!response.ok || !data.success) throw new Error(data.error || '更新公告操作失败');
        return data;
    }

    const adminStatus = document.getElementById('update-announcement-admin-status');
    const adminPanel = document.getElementById('update-announcement-admin-panel');
    const adminList = document.getElementById('update-announcement-admin-list');
    const filter = document.getElementById('update-announcement-filter');
    const form = document.getElementById('update-announcement-editor-form');
    const formTitle = document.getElementById('update-announcement-editor-title');
    const idInput = document.getElementById('update-announcement-id-input');
    const versionInput = document.getElementById('update-announcement-version-input');
    const titleInput = document.getElementById('update-announcement-title-input');
    const webEditor = document.getElementById('update-announcement-web-editor');
    const gameEditor = document.getElementById('update-announcement-game-plugin-editor');
    const bridgeEditor = document.getElementById('update-announcement-bridge-plugin-editor');
    const remindCheckbox = document.getElementById('update-announcement-remind-again');
    let records = [];
    let originalVersion = '';
    let previouslyPublished = false;
    let latestAdminAnnouncementRequestId = 0;
    let mutationState = { pending: false, generation: 0 };

    const statusLabels = { draft: '草稿', published: '已发布', hidden: '隐藏' };
    const editorBySection = {
        webHtml: webEditor,
        gamePluginHtml: gameEditor,
        bridgePluginHtml: bridgeEditor,
    };

    function formatChinaTimestamp(value) {
        if (!value) return '尚未发布';
        const parts = new Intl.DateTimeFormat('zh-CN', {
            timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
            hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
        }).formatToParts(new Date(value));
        const values = Object.fromEntries(parts.map(function (part) { return [part.type, part.value]; }));
        return values.year + '-' + values.month + '-' + values.day + ' '
            + values.hour + ':' + values.minute + ':' + values.second;
    }

    function actionButton(label, handler, className) {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = label;
        if (className) button.className = className;
        button.addEventListener('click', handler);
        return button;
    }

    function syncMutationButtons() {
        adminPanel.querySelectorAll('button').forEach(function (button) {
            if (mutationState.pending) {
                if (!button.disabled) button.dataset.updateAnnouncementMutationLocked = 'true';
                button.disabled = true;
            } else if (button.dataset.updateAnnouncementMutationLocked === 'true') {
                button.disabled = false;
                delete button.dataset.updateAnnouncementMutationLocked;
            }
        });
    }

    function setMutationState(next) {
        mutationState = next;
        syncMutationButtons();
    }

    function renderAdminList() {
        adminList.replaceChildren();
        const selected = filter.value;
        const visible = records.filter(function (item) {
            return selected === 'all' || item.status === selected;
        });
        if (!visible.length) {
            const empty = document.createElement('p');
            empty.className = 'muted-line';
            empty.textContent = '当前筛选条件下没有更新公告。';
            adminList.append(empty);
            return;
        }
        visible.forEach(function (item) {
            const article = document.createElement('article');
            article.className = 'update-announcement-admin-item';
            const head = document.createElement('div');
            head.className = 'update-announcement-admin-item-head';
            const chip = document.createElement('span');
            chip.className = 'update-announcement-status-chip';
            chip.dataset.status = item.status;
            chip.textContent = statusLabels[item.status] || item.status;
            const title = document.createElement('strong');
            title.textContent = item.version + ' · ' + item.title;
            const published = document.createElement('time');
            published.textContent = formatChinaTimestamp(item.publishedAt);
            head.append(chip, title, published);

            const meta = document.createElement('p');
            meta.className = 'muted-line';
            meta.textContent = '最后编辑：' + formatChinaTimestamp(item.updatedAt);
            const actions = document.createElement('div');
            actions.className = 'update-announcement-admin-item-actions';
            actions.append(actionButton('编辑', function () { openEditor(item); }));
            if (item.status === 'draft') {
                actions.append(actionButton('发布', function () { changeStatus(item, 'published'); }, 'primary-btn'));
            } else if (item.status === 'published') {
                actions.append(actionButton('隐藏', function () { changeStatus(item, 'hidden'); }));
            } else {
                actions.append(actionButton('重新发布', function () { changeStatus(item, 'published'); }, 'primary-btn'));
            }
            article.append(head, meta, actions);
            adminList.append(article);
        });
        syncMutationButtons();
    }

    function openEditor(item) {
        const value = item || null;
        idInput.value = value ? value.id : '';
        versionInput.value = value ? value.version : '';
        titleInput.value = value ? value.title : '';
        webEditor.innerHTML = value ? value.sections.webHtml : '';
        gameEditor.innerHTML = value ? value.sections.gamePluginHtml : '';
        bridgeEditor.innerHTML = value ? value.sections.bridgePluginHtml : '';
        remindCheckbox.checked = false;
        originalVersion = value ? value.version : '';
        previouslyPublished = Boolean(value && value.publishedAt);
        formTitle.textContent = value ? '编辑 ' + value.version : '新建更新公告';
        form.hidden = false;
        versionInput.focus();
    }

    function closeEditor() {
        form.hidden = true;
        idInput.value = '';
        originalVersion = '';
        previouslyPublished = false;
        remindCheckbox.checked = false;
    }

    function isAdminAnnouncementRequestCurrent(requestId, latestRequestId) {
        return requestId === latestRequestId;
    }

    async function refreshAdminAnnouncements() {
        const requestId = ++latestAdminAnnouncementRequestId;
        adminStatus.textContent = '正在读取更新公告……';
        try {
            const data = await adminRequest('/api/admin/update-announcements/list');
            if (!isAdminAnnouncementRequestCurrent(requestId, latestAdminAnnouncementRequestId)) return;
            records = Array.isArray(data.announcements) ? data.announcements : [];
            renderAdminList();
            adminStatus.textContent = '已读取 ' + records.length + ' 条更新公告。';
        } catch (error) {
            if (!isAdminAnnouncementRequestCurrent(requestId, latestAdminAnnouncementRequestId)) return;
            adminStatus.textContent = error.message || '更新公告读取失败。';
        }
    }

    async function saveAnnouncement() {
        const mutation = mutationStateApi.begin(mutationState);
        if (!mutation.accepted) return;
        setMutationState(mutation.state);
        try {
            const versionChanged = previouslyPublished
                && originalVersion
                && versionInput.value.trim() !== originalVersion;
            let confirmedVersionChange = false;
            if (versionChanged) {
                confirmedVersionChange = confirm('发布后的版本号已改变。保存后会重新提醒所有玩家，确定继续吗？');
                if (!confirmedVersionChange) return;
            }
            adminStatus.textContent = '正在保存更新公告……';
            await adminRequest('/api/admin/update-announcements/save', {
                announcement: {
                    id: idInput.value || undefined,
                    version: versionInput.value,
                    title: titleInput.value,
                    sections: {
                        webHtml: webEditor.innerHTML,
                        gamePluginHtml: gameEditor.innerHTML,
                        bridgePluginHtml: bridgeEditor.innerHTML,
                    },
                    remindAgain: remindCheckbox.checked,
                    confirmVersionChange: confirmedVersionChange,
                },
            });
            if (!mutationStateApi.isCurrent(mutationState, mutation.generation)) return;
            closeEditor();
            await refreshAdminAnnouncements();
            if (!mutationStateApi.isCurrent(mutationState, mutation.generation)) return;
            adminStatus.textContent = '更新公告已保存。';
        } catch (error) {
            if (!mutationStateApi.isCurrent(mutationState, mutation.generation)) return;
            adminStatus.textContent = error.message || '更新公告保存失败。';
        } finally {
            setMutationState(mutationStateApi.finish(mutationState, mutation.generation));
        }
    }

    async function changeStatus(item, targetStatus) {
        const mutation = mutationStateApi.begin(mutationState);
        if (!mutation.accepted) return;
        setMutationState(mutation.state);
        const action = targetStatus === 'hidden' ? '隐藏' : item.status === 'hidden' ? '重新发布' : '发布';
        try {
            if (!confirm('确定要' + action + ' ' + item.version + ' 吗？')) return;
            const remindAgain = item.status === 'hidden' && targetStatus === 'published'
                ? confirm('是否同时重新提醒所有玩家？选择“取消”只表示不重复提醒，公告仍会重新发布。')
                : false;
            adminStatus.textContent = '正在' + action + '更新公告……';
            await adminRequest('/api/admin/update-announcements/status', {
                id: item.id,
                status: targetStatus,
                remindAgain: remindAgain,
            });
            if (!mutationStateApi.isCurrent(mutationState, mutation.generation)) return;
            await refreshAdminAnnouncements();
            if (!mutationStateApi.isCurrent(mutationState, mutation.generation)) return;
            adminStatus.textContent = '更新公告已' + action + '。';
        } catch (error) {
            if (!mutationStateApi.isCurrent(mutationState, mutation.generation)) return;
            adminStatus.textContent = error.message || '更新公告状态修改失败。';
        } finally {
            setMutationState(mutationStateApi.finish(mutationState, mutation.generation));
        }
    }

    function formatUpdateAnnouncementEditor(section, command) {
        const editor = editorBySection[section];
        if (!editor) return;
        editor.focus();
        document.execCommand(command, false, null);
    }

    function linkUpdateAnnouncementEditor(section) {
        const editor = editorBySection[section];
        if (!editor) return;
        const url = prompt('请输入链接地址，建议使用 https:// 开头：');
        if (!url) return;
        editor.focus();
        document.execCommand('createLink', false, url);
    }

    document.getElementById('update-announcement-refresh-btn').addEventListener('click', refreshAdminAnnouncements);
    document.getElementById('update-announcement-new-btn').addEventListener('click', function () { openEditor(null); });
    document.getElementById('update-announcement-cancel-btn').addEventListener('click', closeEditor);
    document.getElementById('update-announcement-save-btn').addEventListener('click', saveAnnouncement);
    filter.addEventListener('change', renderAdminList);
    window.addEventListener('caoren:admin-view-changed', function (event) {
        const controls = document.getElementById('admin-controls');
        if (event.detail && event.detail.view === 'announcement'
            && controls && controls.classList.contains('is-admin-session')) {
            refreshAdminAnnouncements();
        }
    });
    Object.assign(window, {
        formatUpdateAnnouncementEditor: formatUpdateAnnouncementEditor,
        linkUpdateAnnouncementEditor: linkUpdateAnnouncementEditor,
        refreshUpdateAnnouncements: refreshAdminAnnouncements,
    });
}());
