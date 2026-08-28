// task-system.ts
import { TaskTemplate, TaskCell, Player } from './types';

export const getDefaultTaskTemplate = (): TaskTemplate => {
    const template: TaskTemplate = {
        cells: {
            'A1': { levelLabel: '3', description: '', level: 3, type: 'custom', nType: 'none', nValue: 0 },
            'A2': { levelLabel: '1', description: '', level: 1, type: 'custom', nType: 'none', nValue: 0 },
            'A3': { levelLabel: '4', description: '', level: 4, type: 'custom', nType: 'none', nValue: 0 },
            'B1': { levelLabel: '2', description: '', level: 2, type: 'custom', nType: 'none', nValue: 0 },
            'B2': { levelLabel: '5', description: '', level: 5, type: 'custom', nType: 'none', nValue: 0 },
            'B3': { levelLabel: '2', description: '', level: 2, type: 'custom', nType: 'none', nValue: 0 },
            'C1': { levelLabel: '4', description: '', level: 4, type: 'custom', nType: 'none', nValue: 0 },
            'C2': { levelLabel: '1', description: '', level: 1, type: 'custom', nType: 'none', nValue: 0 },
            'C3': { levelLabel: '3', description: '', level: 3, type: 'custom', nType: 'none', nValue: 0 },
        },
        lines: [
            ['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3'],
            ['A1', 'B1', 'C1'], ['A2', 'B2', 'C2'], ['A3', 'B3', 'C3'],
            ['A1', 'B2', 'C3'], ['A3', 'B2', 'C1'],
        ],
        replacementTask: { level: 4, description: '' }
    };
    for (const cell of Object.values(template.cells)) cell.hint = '';
    template.replacementTask.hint = '';
    return template;
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
