import type { Knex } from 'knex';

export interface DatabaseMigration {
  id: string;
  checksum: string;
  sqliteForeignKeysOff?: boolean;
  up(instance: Knex | Knex.Transaction): Promise<void>;
}
