(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.CaorenUpdateAnnouncementAdminMutationState = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
    function begin(state) {
        if (state.pending) {
            return { accepted: false, generation: state.generation, state: state };
        }
        const generation = state.generation + 1;
        return {
            accepted: true,
            generation: generation,
            state: { pending: true, generation: generation },
        };
    }

    function isCurrent(state, generation) {
        return Boolean(state.pending && state.generation === generation);
    }

    function finish(state, generation) {
        if (!isCurrent(state, generation)) return state;
        return { pending: false, generation: state.generation };
    }

    return { begin: begin, isCurrent: isCurrent, finish: finish };
});
