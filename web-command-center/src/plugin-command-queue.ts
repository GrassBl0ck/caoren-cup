import { v4 as uuidv4 } from 'uuid';

export type BridgeCommandStatus = 'queued' | 'sent' | 'acked';

export interface BridgeCommand {
  id: string;
  type: string;
  payload: Record<string, any>;
  status: BridgeCommandStatus;
  createdAt: number;
  sentAt?: number;
  ackedAt?: number;
  sendAttempts: number;
  lastSentAt?: number;
}

export interface PluginCommandQueueSummaryItem {
  id: string;
  type: string;
  status: BridgeCommandStatus;
  label: unknown;
  moduleId: unknown;
  createdAt: number;
  sentAt?: number;
  ackedAt?: number;
  sendAttempts: number;
}

const PLUGIN_COMMAND_TTL_MS = Math.max(
  30_000,
  Number(process.env.PLUGIN_COMMAND_TTL_MS || 5 * 60 * 1000)
);

const PLUGIN_COMMAND_RESEND_AFTER_MS = Math.max(
  1_000,
  Number(process.env.PLUGIN_COMMAND_RESEND_AFTER_MS || 10_000)
);

const PLUGIN_COMMAND_MAX_SEND_ATTEMPTS = Math.max(
  1,
  Number(process.env.PLUGIN_COMMAND_MAX_SEND_ATTEMPTS || 3)
);

const pluginCommandQueue: BridgeCommand[] = [];

const refreshRetryableCommands = (now: number) => {
  for (const cmd of pluginCommandQueue) {
    if (cmd.status !== 'sent') continue;
    if (cmd.sendAttempts >= PLUGIN_COMMAND_MAX_SEND_ATTEMPTS) continue;
    const lastSentAt = cmd.lastSentAt || cmd.sentAt || cmd.createdAt;
    if (now - lastSentAt >= PLUGIN_COMMAND_RESEND_AFTER_MS) {
      cmd.status = 'queued';
    }
  }
};

export const prunePluginCommandQueue = () => {
  const now = Date.now();
  refreshRetryableCommands(now);

  for (let i = pluginCommandQueue.length - 1; i >= 0; i--) {
    const cmd = pluginCommandQueue[i];
    const maxAge = cmd.status === 'acked' ? 30_000 : PLUGIN_COMMAND_TTL_MS;

    if (now - cmd.createdAt > maxAge) pluginCommandQueue.splice(i, 1);
  }
};

export const enqueuePluginCommand = (
  type: string,
  payload: Record<string, any>
): BridgeCommand => {
  prunePluginCommandQueue();

  const cmd: BridgeCommand = {
    id: uuidv4(),
    type,
    payload,
    status: 'queued',
    createdAt: Date.now(),
    sendAttempts: 0,
  };

  pluginCommandQueue.push(cmd);
  return cmd;
};

export const takeQueuedPluginCommands = () => {
  prunePluginCommandQueue();

  const now = Date.now();
  const commands = pluginCommandQueue.filter(cmd => cmd.status === 'queued');

  for (const cmd of commands) {
    cmd.status = 'sent';
    cmd.sentAt = cmd.sentAt || now;
    cmd.lastSentAt = now;
    cmd.sendAttempts += 1;
  }

  return commands.map(cmd => ({
    id: cmd.id,
    type: cmd.type,
    payload: cmd.payload,
  }));
};

export const ackPluginCommand = (commandId: unknown): boolean => {
  prunePluginCommandQueue();

  const id = String(commandId || '').trim();
  if (!id) return false;

  const cmd = pluginCommandQueue.find(item => item.id === id);
  if (!cmd) return false;

  cmd.status = 'acked';
  cmd.ackedAt = Date.now();
  return true;
};

export const getPluginCommandQueueSummary = (): PluginCommandQueueSummaryItem[] => {
  prunePluginCommandQueue();

  return pluginCommandQueue.map(cmd => ({
    id: cmd.id,
    type: cmd.type,
    status: cmd.status,
    label: cmd.payload?.label,
    moduleId: cmd.payload?.moduleId,
    createdAt: cmd.createdAt,
    sentAt: cmd.sentAt,
    ackedAt: cmd.ackedAt,
    sendAttempts: cmd.sendAttempts,
  }));
};
