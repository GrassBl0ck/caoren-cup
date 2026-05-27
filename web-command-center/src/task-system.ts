// task-system.ts
import { TaskTemplate, TaskCell, Player } from './types';

export const getDefaultTaskTemplate = (): TaskTemplate => {
    return {
        cells: {
            'A1': { levelLabel: '1', description: '\u51fb\u6740 2 \u540d\u654c\u4eba', level: 1, type: 'count', targetCount: 2, nType: 'none', nValue: 0 },
            'A2': { levelLabel: '3N', description: 'N\u56de\u5408\uff0c\u51fb\u6740\u4e00\u540d\u961f\u53cb', level: 3, type: 'custom', nType: '3N_multi', nMin: 1, nMax: 3, nValue: 0 },
            'A3': { levelLabel: '3', description: '\u62c6\u5305\u4e00\u6b21', level: 3, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'B1': { levelLabel: '5/4N', description: '\u5355\u56de\u5408\u8fde\u7eed\u51fb\u6740\u961f\u53cb', level: 5, type: 'custom', nType: '5_4N_single', nMin: 1, nMax: 3, baseLevel: 5, extraLevel: 4, nValue: 0 },
            'B2': { levelLabel: '2', description: '\u95ea\u778e 3 \u540d\u654c\u4eba', level: 2, type: 'count', targetCount: 3, nType: 'none', nValue: 0 },
            'B3': { levelLabel: '1', description: '\u5b58\u6d3b\u5230\u56de\u5408\u7ed3\u675f', level: 1, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'C1': { levelLabel: '2', description: '\u8d62\u5f97 1 \u4e2a\u56de\u5408', level: 2, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'C2': { levelLabel: '3N', description: '\u5355\u56de\u5408\u51fb\u6740N\u540d\u961f\u53cb', level: 3, type: 'custom', nType: '3N_single', nMin: 1, nMax: 3, nValue: 0 },
            'C3': { levelLabel: '3', description: '\u9020\u6210 1000 \u70b9\u4f24\u5bb3', level: 3, type: 'damage', targetCount: 1000, nType: 'none', nValue: 0 },
        },
        lines: [
            ['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3'],
            ['A1', 'B1', 'C1'], ['A2', 'B2', 'C2'], ['A3', 'B3', 'C3'],
            ['A1', 'B2', 'C3'], ['A3', 'B2', 'C1'],
        ],
        replacementTask: { level: 4, description: '\u9690\u85cf\u7684\u672a\u77e5\u66ff\u6362\u4efb\u52a1' }
    };
};

export const assignTaskGridToPlayer = (player: Player, template: TaskTemplate): void => {
    const grid: Record<string, TaskCell> = {};
    for (const [cellId, cell] of Object.entries(template.cells)) {
        grid[cellId] = {
            ...cell,
            cellId,
            currentCount: 0,
            status: 'Incomplete',
            isHintUsed: false,
            isReplaced: false,
            borderHistory: [],
        } as TaskCell;
    }
    player.taskGrid = grid;
};
