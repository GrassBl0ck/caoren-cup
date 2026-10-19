(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.WeaponPaintsEntry = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    const PLAYER_CENTER_EVENT = 'caoren:player-center-session';
    const ADMIN_EVENT = 'caoren:admin-session';
    const PROMO_ID = 'weaponpaints-open-btn';

    const isActive = (value) => value === true;

    const normalizeIdentity = (input) => {
        const value = input || {};
        return {
            playerCenterActive: isActive(value.playerCenterActive),
            adminActive: isActive(value.adminActive),
        };
    };

    const hasIdentity = (input) => {
        const value = normalizeIdentity(input);
        return value.playerCenterActive || value.adminActive;
    };

    // 入口只在“面板关闭且身份有效”时显示；身份有效性由玩家中心会话或管理员会话提供。
    function shouldShowPromo(input) {
        const value = input || {};
        if (isActive(value.panelOpen)) return false;
        return hasIdentity(value);
    }

    // 账号会话与管理员会话是两条独立通道，任一失效都不能掩盖另一条仍有效的身份。
    function applyIdentityEvent(current, event) {
        const identity = normalizeIdentity(current);
        if (!event || !event.type) return identity;
        const detail = event.detail || {};
        if (event.type === PLAYER_CENTER_EVENT) identity.playerCenterActive = isActive(detail.active);
        else if (event.type === ADMIN_EVENT) identity.adminActive = isActive(detail.active);
        return identity;
    }

    function create(dependencies) {
        const deps = dependencies || {};
        const getElement = typeof deps.getElement === 'function' ? deps.getElement : () => null;
        const readIdentity = typeof deps.readIdentity === 'function' ? deps.readIdentity : () => ({});
        const onIdentityLost = typeof deps.onIdentityLost === 'function' ? deps.onIdentityLost : () => undefined;

        let identity = normalizeIdentity(readIdentity());
        let panelOpen = false;

        function render() {
            const visible = shouldShowPromo({
                panelOpen,
                playerCenterActive: identity.playerCenterActive,
                adminActive: identity.adminActive,
            });
            const button = getElement(PROMO_ID);
            if (button) button.hidden = !visible;
            return visible;
        }

        function setPanelOpen(open) {
            panelOpen = isActive(open);
            return render();
        }

        function update(nextIdentity) {
            const previous = identity;
            identity = normalizeIdentity(nextIdentity);
            const lost = hasIdentity(previous) && !hasIdentity(identity);
            if (lost) panelOpen = false;
            render();
            if (lost) onIdentityLost();
            return !lost;
        }

        function handleEvent(event) {
            return update(applyIdentityEvent(identity, event));
        }

        render();

        return {
            setPanelOpen,
            isPanelOpen: () => panelOpen,
            update,
            handleEvent,
            identity: () => normalizeIdentity(identity),
            isVisible: render,
        };
    }

    return {
        PLAYER_CENTER_EVENT,
        ADMIN_EVENT,
        PROMO_ID,
        normalizeIdentity,
        hasIdentity,
        shouldShowPromo,
        applyIdentityEvent,
        create,
    };
});
