(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.CaorenUpdateAnnouncementReadState = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
    function mergeReadState() {
        const next = {};
        Array.from(arguments).forEach(function (value) {
            if (!value || typeof value !== 'object' || Array.isArray(value)) return;
            Object.entries(value).forEach(function (entry) {
                const revision = entry[1];
                if (!Number.isInteger(revision) || revision < 0) return;
                next[entry[0]] = Math.max(Number(next[entry[0]] || 0), revision);
            });
        });
        return next;
    }

    function parse(raw) {
        try {
            const value = JSON.parse(String(raw || '{}'));
            return mergeReadState(value);
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

    function createOpenSnapshotSession(sessionId, hasAuthoritativeList, announcements, read) {
        const waitingForInitialSnapshot = !hasAuthoritativeList;
        const unread = new Set(findUnread(announcements, read));
        return {
            sessionId: sessionId,
            waitingForInitialSnapshot: waitingForInitialSnapshot,
            snapshot: waitingForInitialSnapshot
                ? []
                : (announcements || [])
                    .filter(function (item) { return unread.has(item.id); })
                    .map(function (item) {
                        return { id: item.id, reminderRevision: item.reminderRevision };
                    }),
        };
    }

    function captureFirstAuthoritativeSnapshot(session, sessionId, announcements, read) {
        if (!session
            || session.sessionId !== sessionId
            || !session.waitingForInitialSnapshot) return session;
        return createOpenSnapshotSession(sessionId, true, announcements, read);
    }

    function isFetchCurrent(startSocketRevision, currentSocketRevision, requestId, latestRequestId) {
        return startSocketRevision === currentSocketRevision && requestId === latestRequestId;
    }

    return {
        parse: parse,
        findUnread: findUnread,
        markRead: markRead,
        mergeReadState: mergeReadState,
        isFetchCurrent: isFetchCurrent,
        createOpenSnapshotSession: createOpenSnapshotSession,
        captureFirstAuthoritativeSnapshot: captureFirstAuthoritativeSnapshot,
    };
});
