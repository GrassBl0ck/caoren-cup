import fs from 'node:fs';
import path from 'node:path';
import { v4 as uuidv4 } from 'uuid';
import { TaskTemplate } from './types';
import { getDefaultTaskTemplate } from './task-system';

export interface TaskPresetRecord {
    id: string;
    name: string;
    taskTemplate: TaskTemplate;
    createdAt: number;
    updatedAt: number;
    system?: boolean;
}

const PRESET_PATH = path.resolve(__dirname, '..', 'runtime', 'undercover-task-presets.json');
const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));
const defaultRecord = (): TaskPresetRecord => ({ id: 'system-default', name: '系统默认', taskTemplate: getDefaultTaskTemplate(), createdAt: Date.now(), updatedAt: Date.now(), system: true });
const normalizeName = (name: unknown) => String(name ?? '').trim();
const validateName = (name: unknown, presets: TaskPresetRecord[], ignoreId?: string) => {
    const normalized = normalizeName(name);
    if (!normalized || normalized.length > 40) throw new Error('预设名称不能为空且不能超过 40 个字符。');
    if (presets.some(p => p.id !== ignoreId && p.name === normalized)) throw new Error('预设名称已存在。');
    return normalized;
};

export const getTaskPresetPath = () => PRESET_PATH;

export const saveTaskPresets = (presets: TaskPresetRecord[]): void => {
    fs.mkdirSync(path.dirname(PRESET_PATH), { recursive: true });
    const temp = `${PRESET_PATH}.tmp`;
    fs.writeFileSync(temp, JSON.stringify(presets, null, 2), 'utf8');
    fs.renameSync(temp, PRESET_PATH);
};

export const loadTaskPresets = (): TaskPresetRecord[] => {
    try {
        if (!fs.existsSync(PRESET_PATH)) {
            const defaults = [defaultRecord()];
            saveTaskPresets(defaults);
            return defaults;
        }
        const parsed = JSON.parse(fs.readFileSync(PRESET_PATH, 'utf8'));
        if (!Array.isArray(parsed)) throw new Error('预设文件格式不是数组。');
        const valid = parsed.filter((p: any) => p && typeof p.id === 'string' && typeof p.name === 'string' && p.taskTemplate?.cells && Array.isArray(p.taskTemplate?.lines));
        if (!valid.some(p => p.id === 'system-default')) throw new Error('缺少系统默认预设。');
        return valid;
    } catch (error) {
        if (fs.existsSync(PRESET_PATH)) fs.renameSync(PRESET_PATH, `${PRESET_PATH}.corrupt-${Date.now()}.json`);
        const defaults = [defaultRecord()];
        saveTaskPresets(defaults);
        console.warn('[TaskPresetStore] 已从损坏文件恢复系统默认预设：', error);
        return defaults;
    }
};

export const getTaskPreset = (id: string) => loadTaskPresets().find(p => p.id === id);

export const createTaskPreset = (name: string, taskTemplate: TaskTemplate): TaskPresetRecord => {
    const presets = loadTaskPresets();
    const now = Date.now();
    const record = { id: uuidv4(), name: validateName(name, presets), taskTemplate: clone(taskTemplate), createdAt: now, updatedAt: now };
    saveTaskPresets([...presets, record]);
    return record;
};

export const renameTaskPreset = (id: string, name: string): TaskPresetRecord => {
    const presets = loadTaskPresets();
    const target = presets.find(p => p.id === id);
    if (!target) throw new Error('预设不存在。');
    if (target.system) throw new Error('系统默认预设不能重命名。');
    target.name = validateName(name, presets, id); target.updatedAt = Date.now(); saveTaskPresets(presets); return target;
};

export const deleteTaskPreset = (id: string): void => {
    const presets = loadTaskPresets();
    const target = presets.find(p => p.id === id);
    if (!target) throw new Error('预设不存在。');
    if (target.system) throw new Error('系统默认预设不能删除。');
    saveTaskPresets(presets.filter(p => p.id !== id));
};
