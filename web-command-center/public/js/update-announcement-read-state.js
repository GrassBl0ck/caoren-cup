(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.CaorenUpdateAnnouncementReadState = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
    function parse(raw) {
        try {
            const value = JSON.parse(String(raw || '{}'));
            if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
            return Object.fromEntries(Object.entries(value).filter(function (entry) {
                return typeof entry[0] === 'string'
                    && Number.isInteger(entry[1])
                    && entry[1] >= 0;
            }));
        } catch (_error) {
            return {};
        }
    }

    function findUnread(announcements, read) {
        return (announcements || [])
            .filter(function (item) {
                return Number(read[item.id] || 0) < Number(item.reminderRevision || 0);
            })
            .map(function (item) { return item.id; });
    }

    function markRead(read, announcements) {
        const next = Object.assign({}, read || {});
        (announcements || []).forEach(function (item) {
            next[item.id] = Math.max(Number(next[item.id] || 0), Number(item.reminderRevision || 0));
        });
        return next;
    }

    return { parse: parse, findUnread: findUnread, markRead: markRead };
});
