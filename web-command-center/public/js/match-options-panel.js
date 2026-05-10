(function () {
    /**
     * CaorenCup match-options-panel.js
     *
     * 第一阶段最终方案：
     * - 本局模式设置已经内置到 public/index.html 的“管理员控制区”中。
     * - 这个文件保留为空实现，只用于兼容 index.html 中可能仍然存在的 script 引用。
     * - 不要在这里再动态插入“本局模式设置”面板，否则会出现：
     *   1. 面板跑到页面顶部
     *   2. 普通玩家也能看到
     *   3. 与 index.html 内置面板重复
     *   4. 旧隐藏逻辑误伤“启用卧底模式”开关
     */

    console.info('[CaorenCup] match-options-panel.js loaded as compatibility no-op.');
})();