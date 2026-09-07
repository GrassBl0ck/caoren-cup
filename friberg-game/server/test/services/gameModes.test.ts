import { describe, expect, it } from 'vitest';
import {
  getRoomGameModeDefinition,
  isRoomGameMode,
  listRoomGameModes,
  listSingleGameVariants,
  singleGameVariantSchema,
} from '../../src/services/gameModes';
import { roomCreatePayloadSchema } from '../../src/socket/schemas';

describe('game mode registry', () => {
  it('exposes the current modes as stable machine-readable capabilities', () => {
    const modes = listRoomGameModes();
    expect(modes.map((mode) => mode.key)).toEqual(['classic', 'relay', 'relay2v2']);
    expect(modes.find((mode) => mode.key === 'relay2v2')).toMatchObject({
      engine: 'player-guess',
      players: { min: 4, max: 4 },
      capabilities: { requiresTeams: true, cooperative: false },
    });
  });

  it('rejects unknown modes instead of falling back to classic at the API boundary', () => {
    expect(isRoomGameMode('turtle-soup')).toBe(false);
    expect(isRoomGameMode('toString')).toBe(false);
    expect(getRoomGameModeDefinition('turtle-soup').key).toBe('classic');
    expect(roomCreatePayloadSchema.safeParse({
      dbType: 'easy',
      gameMode: 'turtle-soup',
    }).success).toBe(false);
  });

  it('uses registry player constraints for existing modes', () => {
    expect(roomCreatePayloadSchema.safeParse({
      dbType: 'easy',
      gameMode: 'relay2v2',
      maxPlayers: 3,
    }).success).toBe(false);
    expect(roomCreatePayloadSchema.safeParse({
      dbType: 'easy',
      gameMode: 'relay2v2',
      maxPlayers: 4,
    }).success).toBe(true);
  });

  it('keeps single difficulty and variant as separate contracts', () => {
    expect(listSingleGameVariants()).toEqual([expect.objectContaining({ key: 'classic' })]);
    expect(singleGameVariantSchema.parse('classic')).toBe('classic');
    expect(singleGameVariantSchema.safeParse('turtle-soup').success).toBe(false);
  });
});
