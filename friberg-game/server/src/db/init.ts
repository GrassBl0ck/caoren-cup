import type { Knex } from 'knex';
import { ensureSchema } from './schema';
import { db } from './knex';
import { runMigrations } from './migrations';
import { seedPlayersIfEmpty } from './seedPlayers';

export { seedPlayersIfEmpty };

export async function initDb(instance: Knex = db): Promise<void> {
  await ensureSchema(instance);
  await runMigrations(instance);
  const seeded = await seedPlayersIfEmpty(instance);
  if (seeded) console.log(`[seed] 已导入 ${seeded} 名选手`);
}
