import { timingSafeEqual } from 'node:crypto';
import type { Request } from 'express';
import { PLUGIN_TOKEN } from './game-constants';

export function hasPluginHeader(request: Pick<Request, 'header'>): boolean {
    const value = request.header('x-caoren-plugin-token');
    if (!value || !PLUGIN_TOKEN) return false;
    const token = Buffer.from(value);
    const expected = Buffer.from(PLUGIN_TOKEN);
    return token.length === expected.length && timingSafeEqual(token, expected);
}
