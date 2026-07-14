(function () {
    'use strict';

    const readApi = window.CaorenUpdateAnnouncementReadState;
    const STORAGE_KEY = 'caoren-update-announcement-read-v1';
    const trigger = document.getElementById('update-announcement-trigger');
    const dot = document.getElementById('update-announcement-unread-dot');
    const backdrop = document.getElementById('update-announcement-backdrop');
    const drawer = document.getElementById('update-announcement-drawer');
    const closeButton = document.getElementById('update-announcement-close');
    const status = document.getElementById('update-announcement-status');
    const list = document.getElementById('update-announcement-list');
    let announcements = [];
    let openUnreadSnapshot = [];
    let memoryReadState = {};
    let triggerBeforeOpen = null;

    function loadReadState() {
        try {
            memoryReadState = readApi.parse(localStorage.getItem(STORAGE_KEY));
        } catch (_error) {}
        return Object.assign({}, memoryReadState);
    }

    function saveReadState(value) {
        memoryReadState = Object.assign({}, value);
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(memoryReadState));
        } catch (_error) {}
    }

    function formatChinaTimestamp(value) {
        const parts = new Intl.DateTimeFormat('zh-CN', {
            timeZone: 'Asia/Shanghai',
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        }).formatToParts(new Date(value));
        const values = Object.fromEntries(parts.map(function (part) {
            return [part.type, part.value];
        }));
        return values.year + '-' + values.month + '-' + values.day + ' '
            + values.hour + ':' + values.minute + ':' + values.second;
    }

    function isSnapshotItem(item) {
        return openUnreadSnapshot.some(function (snapshot) {
            return snapshot.id === item.id && snapshot.reminderRevision === item.reminderRevision;
        });
    }

    function updateUnreadDot() {
        const unread = new Set(readApi.findUnread(announcements, loadReadState()));
        const visible = announcements.some(function (item) {
            return unread.has(item.id) && !isSnapshotItem(item);
        });
        dot.hidden = !visible;
    }

    function createSection(title, html) {
        const section = document.createElement('section');
        section.className = 'update-announcement-section';
        const heading = document.createElement('h3');
        heading.textContent = title;
        const content = document.createElement('div');
        content.className = 'update-announcement-section-content';
        content.innerHTML = html;
        section.append(heading, content);
        return section;
    }

    function renderAnnouncements() {
        list.replaceChildren();
        if (!announcements.length) {
            status.textContent = '暂时没有已发布的更新公告。';
            return;
        }
        status.textContent = '共 ' + announcements.length + ' 个已发布版本。';
        announcements.forEach(function (item, index) {
            const article = document.createElement('article');
            article.className = 'update-announcement-item';
            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'update-announcement-item-toggle';
            const bodyId = 'update-announcement-body-' + item.id;
            toggle.setAttribute('aria-controls', bodyId);
            toggle.setAttribute('aria-expanded', index === 0 ? 'true' : 'false');

            const version = document.createElement('span');
            version.className = 'update-announcement-version';
            version.textContent = item.version;
            const title = document.createElement('span');
            title.className = 'update-announcement-title';
            title.textContent = item.title;
            const time = document.createElement('time');
            time.className = 'update-announcement-time';
            time.textContent = formatChinaTimestamp(item.publishedAt);
            toggle.append(version, title, time);
            if (isSnapshotItem(item)) {
                const badge = document.createElement('span');
                badge.className = 'update-announcement-new-badge';
                badge.textContent = '新';
                toggle.append(badge);
            }

            const body = document.createElement('div');
            body.id = bodyId;
            body.className = 'update-announcement-item-body';
            body.hidden = index !== 0;
            body.append(
                createSection('一、网页端', item.sections.webHtml),
                createSection('二、游戏插件', item.sections.gamePluginHtml),
                createSection('三、桥接插件', item.sections.bridgePluginHtml),
            );
            toggle.addEventListener('click', function () {
                const expanded = toggle.getAttribute('aria-expanded') === 'true';
                toggle.setAttribute('aria-expanded', expanded ? 'false' : 'true');
                body.hidden = expanded;
            });
            article.append(toggle, body);
            list.append(article);
        });
    }

    function applyAnnouncements(next) {
        announcements = Array.isArray(next) ? next : [];
        renderAnnouncements();
        updateUnreadDot();
    }

    async function refreshPublicAnnouncements() {
        try {
            const response = await fetch('/api/update-announcements', {
                headers: { Accept: 'application/json' },
            });
            const data = await response.json();
            if (!response.ok || !data.success) {
                throw new Error(data.error || '更新公告暂时无法读取');
            }
            applyAnnouncements(data.announcements);
        } catch (_error) {
            status.textContent = '更新公告暂时无法读取，请稍后重试。';
            updateUnreadDot();
        }
    }

    async function openDrawer() {
        triggerBeforeOpen = document.activeElement;
        drawer.hidden = false;
        backdrop.hidden = false;
        document.body.classList.add('update-announcement-open');
        closeButton.focus();
        await refreshPublicAnnouncements();
        const unread = new Set(readApi.findUnread(announcements, loadReadState()));
        openUnreadSnapshot = announcements
            .filter(function (item) { return unread.has(item.id); })
            .map(function (item) {
                return { id: item.id, reminderRevision: item.reminderRevision };
            });
        renderAnnouncements();
        updateUnreadDot();
    }

    function closeDrawer() {
        if (drawer.hidden) return;
        saveReadState(readApi.markRead(loadReadState(), openUnreadSnapshot));
        openUnreadSnapshot = [];
        drawer.hidden = true;
        backdrop.hidden = true;
        document.body.classList.remove('update-announcement-open');
        updateUnreadDot();
        if (triggerBeforeOpen && typeof triggerBeforeOpen.focus === 'function') {
            triggerBeforeOpen.focus();
        }
    }

    function trapFocus(event) {
        if (drawer.hidden || event.key !== 'Tab') return;
        const focusable = Array.from(drawer.querySelectorAll(
            'button, a[href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
        )).filter(function (element) {
            return !element.disabled && !element.closest('[hidden]');
        });
        if (!focusable.length) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        }
        if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    trigger.addEventListener('click', openDrawer);
    closeButton.addEventListener('click', closeDrawer);
    backdrop.addEventListener('click', closeDrawer);
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && !drawer.hidden) closeDrawer();
        trapFocus(event);
    });

    const socket = window.__caorenCupSocket;
    if (socket && typeof socket.on === 'function') {
        socket.on('UPDATE_ANNOUNCEMENTS', function (payload) {
            applyAnnouncements(payload && payload.announcements);
        });
    }
    refreshPublicAnnouncements();
})();
