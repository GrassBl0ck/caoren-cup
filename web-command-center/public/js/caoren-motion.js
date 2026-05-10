/*
 * Caoren Cup motion layer
 * 只负责页面动效与轻量展示增强，不修改比赛状态，不拦截 socket，不发送任何请求。
 */
(function () {
    "use strict";

    const prefersReducedMotion =
        window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    if (prefersReducedMotion) {
        document.documentElement.classList.add("cc-reduce-motion");
        return;
    }

    const seenBannedMaps = new Set();
    let initialBansCaptured = false;
    let lastPhaseText = "";
    let lastSidePickHeroKey = "";
    let scheduled = false;

    function textOf(node) {
        return node ? node.textContent.replace(/\s+/g, " ").trim() : "";
    }

    function getPhaseText() {
        const candidates = [
            document.querySelector("#phase-display .section-title"),
            document.querySelector("#phase-display"),
            ...Array.from(document.querySelectorAll(".section-title"))
        ].filter(Boolean);

        const matched = candidates.find((el) => textOf(el).includes("当前阶段"));
        return matched ? textOf(matched) : "";
    }

    function isProbablySidePickPage() {
        const bodyText = textOf(document.body);

        return (
            bodyText.includes("SidePick") &&
            (
                bodyText.includes("阵营选择") ||
                bodyText.includes("选择 CT") ||
                bodyText.includes("选择 T")
            )
        );
    }

    function getMapCardKey(card) {
        const strong = card.querySelector(".map-card-name strong");
        const slug = card.querySelector(".map-card-slug");

        const strongText = strong ? strong.textContent.trim() : "";
        const slugText = slug ? slug.textContent.trim() : "";
        const titleText = card.getAttribute("title") || "";

        return strongText || slugText || titleText || textOf(card);
    }

    function markPhaseChange() {
        const currentPhaseText = getPhaseText();
        if (!currentPhaseText || currentPhaseText === lastPhaseText) return;

        lastPhaseText = currentPhaseText;

        const phaseTitle =
            document.querySelector("#phase-display .section-title") ||
            document.querySelector("#phase-display");

        const phaseExtra = document.querySelector("#phase-extra");

        [phaseTitle, phaseExtra].forEach((el) => {
            if (!el) return;

            el.classList.remove("cc-phase-pulse");
            void el.offsetWidth;
            el.classList.add("cc-phase-pulse");

            window.setTimeout(() => {
                el.classList.remove("cc-phase-pulse");
            }, 420);
        });
    }

    function markNewBans() {
        const bannedCards = Array.from(document.querySelectorAll(".map-card.banned"));

        bannedCards.forEach((card) => {
            const key = getMapCardKey(card);
            if (!key) return;

            if (!seenBannedMaps.has(key)) {
                if (initialBansCaptured) {
                    card.classList.remove("cc-newly-banned");
                    void card.offsetWidth;
                    card.classList.add("cc-newly-banned");

                    window.setTimeout(() => {
                        card.classList.remove("cc-newly-banned");
                    }, 680);
                }

                seenBannedMaps.add(key);
            }
        });

        initialBansCaptured = true;
    }

    function getSidePickMapName() {
        // 优先从主页面保存的完整 GAME_STATE 里读取服务端真实 selectedMap。
        // 这里拿到的是 "Dust II"，不会被页面文字正则误截成 "Dust"。
        if (window._currentGameState && window._currentGameState.selectedMap) {
            return String(window._currentGameState.selectedMap).trim();
        }

        // 兼容旧页面：如果主页面只暴露了当前地图名，也优先使用它。
        if (window._currentSelectedMap) {
            return String(window._currentSelectedMap).trim();
        }

        // 兜底：从页面文案里提取“本局地图：xxx”。
        // 注意 Dust II 中间有空格，所以不能使用旧版“遇到空格就停止”的正则。
        const bodyText = textOf(document.body);

        const patterns = [
            /本局地图\s*[：:]\s*([^。；;，,\n]+?)(?=\s*[。；;，,\n]|$)/,
            /最终地图\s*[：:]\s*([^。；;，,\n]+?)(?=\s*[。；;，,\n]|$)/,
            /地图\s*[：:]\s*([^。；;，,\n]+?)(?=\s*[。；;，,\n]|$)/,
        ];

        for (const pattern of patterns) {
            const match = bodyText.match(pattern);
            if (match && match[1]) {
                return match[1].replace(/[。,.，；;：:]/g, "").trim();
            }
        }

        return "";
    }

    function isSidePickCard(card) {
        if (!card || card.closest(".sidepick-final-map-wrap")) return false;

        const text = textOf(card);

        return (
            text.includes("选择 CT") ||
            text.includes("选择 T") ||
            text.includes("Counter-Terrorist") ||
            text.includes("Terrorist")
        );
    }

    function findSidePickCards() {
        const cards = Array.from(document.querySelectorAll(".map-card"))
            .filter(isSidePickCard);

        if (cards.length >= 2) return cards;

        return [];
    }

    function commonAncestor(a, b) {
        if (!a || !b) return null;

        const parents = new Set();
        let cur = a;

        while (cur) {
            parents.add(cur);
            cur = cur.parentElement;
        }

        cur = b;

        while (cur) {
            if (parents.has(cur)) return cur;
            cur = cur.parentElement;
        }

        return null;
    }

    function findSidePickSideContainer() {
        const cards = findSidePickCards();

        if (cards.length >= 2) {
            const parent = cards[0].parentElement;

            if (parent && cards.every((card) => card.parentElement === parent)) {
                return parent;
            }

            return commonAncestor(cards[0], cards[1]);
        }

        return null;
    }

    function getMapImagePath(mapName) {
        const safeName = String(mapName || "").trim();

        const normalized = safeName
            .toLowerCase()
            .replace(/^de_/, "")
            .replace(/\s+/g, "_")
            .replace(/[^a-z0-9_]/g, "");

        let slug = normalized;

        if (
            normalized === "dust_ii" ||
            normalized === "dustii" ||
            normalized === "dust2" ||
            normalized === "dust_two" ||
            normalized === "dusttwo"
        ) {
            slug = "dust2";
        }

        return `/assets/maps/de_${slug}.jpg`;
    }

    function buildFallbackMapCard(mapName) {
        const safeName = String(mapName || "").trim();
        const imagePath = getMapImagePath(safeName);

        return `
            <div class="sidepick-map-hero-fallback">
                <div class="sidepick-map-hero-image" style="background-image:url('${imagePath}');">
                    <div class="sidepick-map-hero-name">${safeName || "待定"}</div>
                </div>
                <div class="sidepick-map-hero-body">
                    <strong>${safeName || "待定"}</strong>
                    <span>最终比赛地图</span>
                </div>
            </div>
        `;
    }

    function buildHeroCard(mapName) {
        const displayMapName = String(mapName || "").trim() || "待定";
        let cardHtml = "";

        try {
            if (typeof window.renderMapCard === "function" && displayMapName !== "待定") {
                cardHtml = window.renderMapCard(displayMapName, { canVote: false });
            }
        } catch (error) {
            cardHtml = "";
        }

        if (!cardHtml) {
            cardHtml = buildFallbackMapCard(displayMapName);
        }

        return `
            <div class="sidepick-final-map-kicker">FINAL MAP</div>
            <h3 class="sidepick-final-map-title">最终地图：${displayMapName}</h3>
            <div class="sidepick-final-map-card">${cardHtml}</div>
        `;
    }

    function ensureSidePickHero() {
        if (!isProbablySidePickPage()) {
            lastSidePickHeroKey = "";
            return;
        }

        const mapName = getSidePickMapName();
        if (!mapName) return;

        const sideContainer = findSidePickSideContainer();
        if (!sideContainer || !sideContainer.parentNode) return;

        sideContainer.classList.add("sidepick-side-grid");

        const host =
            sideContainer.closest(".map-bp-board") ||
            sideContainer.closest("#phase-extra") ||
            sideContainer.parentNode;

        let hero = host.querySelector(".sidepick-final-map-wrap");

        if (!hero) {
            hero = document.createElement("div");
            hero.className = "sidepick-final-map-wrap";
            sideContainer.parentNode.insertBefore(hero, sideContainer);
        }

        if (hero.dataset.sidepickMap === mapName) return;

        hero.dataset.sidepickMap = mapName;
        hero.innerHTML = buildHeroCard(mapName);

        if (mapName !== lastSidePickHeroKey) {
            lastSidePickHeroKey = mapName;

            hero.classList.remove("cc-sidepick-hero-enter");
            void hero.offsetWidth;
            hero.classList.add("cc-sidepick-hero-enter");

            window.setTimeout(() => {
                hero.classList.remove("cc-sidepick-hero-enter");
            }, 460);
        }
    }

    function runMotionPass() {
        markPhaseChange();
        markNewBans();
        ensureSidePickHero();
    }

    function scheduleMotionPass() {
        if (scheduled) return;

        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            runMotionPass();
        });
    }

    function debugSidePick() {
        const cards = findSidePickCards();
        const sideContainer = findSidePickSideContainer();

        return {
            phaseText: getPhaseText(),
            isSidePick: isProbablySidePickPage(),
            mapName: getSidePickMapName(),
            mapCardTotal: document.querySelectorAll(".map-card").length,
            sidePickCardCount: cards.length,
            sideContainerFound: !!sideContainer,
            sideContainerClass: sideContainer ? sideContainer.className : "",
            heroFound: !!document.querySelector(".sidepick-final-map-wrap")
        };
    }

    function boot() {
        runMotionPass();

        const observer = new MutationObserver(() => {
            scheduleMotionPass();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        const api = {
            refresh: function () {
                runMotionPass();
            },
            resetBannedMapMemory: function () {
                seenBannedMaps.clear();
                initialBansCaptured = false;
            },
            debugSidePick
        };

        window.CaorenMotion = api;
        window.caorenMotion = api;
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot, { once: true });
    } else {
        boot();
    }
})();