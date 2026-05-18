// task-system.ts
import { TaskTemplate, TaskCell, Player } from './types';

export const getDefaultTaskTemplate = (): TaskTemplate => {
    return {
        cells: {
            'A1': { levelLabel: '1', description: '击杀 2 名敌人', level: 1, type: 'count', targetCount: 2, nType: 'none', nValue: 0 },
            'A2': { levelLabel: '3N', description: 'N回合，击杀一名队友', level: 3, type: 'custom', nType: '3N_multi', nMin: 1, nMax: 3, nValue: 0 },
            'A3': { levelLabel: '3', description: '拆包一次', level: 3, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'B1': { levelLabel: '5/4N', description: '单回合连续击杀队友', level: 5, type: 'custom', nType: '5_4N_single', nMin: 1, nMax: 3, baseLevel: 5, extraLevel: 4, nValue: 0 },
            'B2': { levelLabel: '2', description: '闪瞎 3 名敌人', level: 2, type: 'count', targetCount: 3, nType: 'none', nValue: 0 },
            'B3': { levelLabel: '1', description: '存活到回合结束', level: 1, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'C1': { levelLabel: '2', description: '赢得 1 个回合', level: 2, type: 'custom', targetCount: 1, nType: 'none', nValue: 0 },
            'C2': { levelLabel: '3N', description: '单回合击杀N名队友', level: 3, type: 'custom', nType: '3N_single', nMin: 1, nMax: 3, nValue: 0 },
            'C3': { levelLabel: '3', description: '造成 1000 点伤害', level: 3, type: 'damage', targetCount: 1000, nType: 'none', nValue: 0 },
        },
        lines: [
            ['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3'],
            ['A1', 'B1', 'C1'], ['A2', 'B2', 'C2'], ['A3', 'B3', 'C3'],
            ['A1', 'B2', 'C3'], ['A3', 'B2', 'C1'],
        ],
        replacementTask: { level: 4, description: '隐藏的未知替换任务' }
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
        } as TaskCell; // 这里使用断言，因为默认模板字段是完整的
    }
    player.taskGrid = grid;
};