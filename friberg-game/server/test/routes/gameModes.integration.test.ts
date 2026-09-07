import http from 'http';
import express from 'express';
import { AddressInfo } from 'net';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import gameRoutes from '../../src/routes/game';
import { errorHandler } from '../../src/middleware/common';

let server: http.Server;
let baseUrl: string;

describe('game mode discovery API', () => {
  beforeAll(async () => {
    const app = express();
    app.use('/api/game', gameRoutes);
    app.use(errorHandler);
    server = http.createServer(app);
    await new Promise<void>((resolve) => server.listen(0, resolve));
    baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  });

  afterAll(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  it('returns registered modes without requiring a session', async () => {
    const response = await fetch(`${baseUrl}/api/game/modes`);
    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('public, max-age=60');
    expect(await response.json()).toMatchObject({
      version: 1,
      scope: 'multiplayer-room',
      modes: [
        { key: 'classic', engine: 'player-guess' },
        { key: 'relay', engine: 'player-guess' },
        { key: 'relay2v2', engine: 'player-guess' },
      ],
    });
  });

  it('exposes single variants without changing difficulty mode semantics', async () => {
    const response = await fetch(`${baseUrl}/api/game/variants`);
    expect(response.status).toBe(200);
    const data = await response.json();
    expect(data).toMatchObject({ version: 1 });
    expect(data.single).toEqual([expect.objectContaining({ key: 'classic', engine: 'player-guess' })]);
    expect(data.multiplayerRoom.map((mode: { key: string }) => mode.key))
      .toEqual(['classic', 'relay', 'relay2v2']);
  });
});
