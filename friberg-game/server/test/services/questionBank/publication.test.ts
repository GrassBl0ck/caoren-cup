import knex, { type Knex } from 'knex';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../../src/db/schema';
import { runMigrations } from '../../../src/db/migrations';
import {
  publishDataVersion,
  rollbackDataVersion,
  submitDataVersionForReview,
} from '../../../src/services/questionBank/publication';

let instance: Knex;
let actorUserId: number;
let sequence = 700;

function uid(): string {
  sequence += 1;
  return `00000000-0000-4000-8000-${String(sequence).padStart(12, '0')}`;
}

beforeEach(async () => {
  sequence = 700;
  instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  await ensureSchema(instance);
  await runMigrations(instance);
  const [actor] = await instance('users').insert({
    username: 'publication-admin',
    password_hash: 'test',
    role: 'admin',
  }).returning('id');
  actorUserId = Number(typeof actor === 'object' ? actor.id : actor);
});

afterEach(async () => {
  await instance.destroy();
});

async function version(status: 'draft' | 'validating' | 'in_review' | 'published') {
  const versionUid = uid();
  const [created] = await instance('data_versions').insert({
    version_uid: versionUid,
    display_version: `version-${sequence}`,
    cutoff_date: '2026-09-10',
    status,
    content_hash: status === 'draft' ? null : 'a'.repeat(64),
  }).returning('id');
  return { versionId: Number(typeof created === 'object' ? created.id : created), versionUid };
}

async function pointTo(target: { versionId: number }) {
  await instance('published_dataset_pointer').insert({ channel: 'default', version_id: target.versionId });
}

describe('data version publication', () => {
  it('moves a validated version into review only when blockers are absent', async () => {
    const candidate = await version('validating');

    await submitDataVersionForReview(candidate.versionUid, actorUserId, instance);

    expect((await instance('data_versions').where({ id: candidate.versionId }).first()).status).toBe('in_review');

    const blocked = await version('validating');
    await instance('data_validation_findings').insert({
      version_id: blocked.versionId,
      finding_code: 'BLOCKED',
      severity: 'blocking',
      entity_type: 'version',
      entity_key: blocked.versionUid,
      message: 'blocked',
      status: 'open',
    });
    await expect(submitDataVersionForReview(blocked.versionUid, actorUserId, instance))
      .rejects.toMatchObject({ code: 'VERSION_HAS_BLOCKERS' });
  });

  it('rejects publication when blockers exist or the expected current version is stale', async () => {
    const blocked = await version('in_review');
    await instance('data_validation_findings').insert({
      version_id: blocked.versionId,
      finding_code: 'BLOCKED',
      severity: 'blocking',
      entity_type: 'version',
      entity_key: blocked.versionUid,
      message: 'blocked',
      status: 'open',
    });
    await expect(publishDataVersion({
      versionUid: blocked.versionUid,
      expectedCurrentVersionUid: null,
      reason: 'test publish',
      idempotencyKey: 'publish-blocked',
      actorUserId,
    }, instance)).rejects.toMatchObject({ code: 'VERSION_HAS_BLOCKERS' });

    const current = await version('published');
    await pointTo(current);
    const candidate = await version('in_review');
    await expect(publishDataVersion({
      versionUid: candidate.versionUid,
      expectedCurrentVersionUid: '00000000-0000-4000-8000-000000009999',
      reason: 'stale publish',
      idempotencyKey: 'publish-stale',
      actorUserId,
    }, instance)).rejects.toMatchObject({ code: 'PUBLISHED_VERSION_CHANGED' });
    expect((await instance('published_dataset_pointer').where({ channel: 'default' }).first()).version_id)
      .toBe(current.versionId);
  });

  it('publishes atomically and returns the same result for a repeated idempotency key', async () => {
    const current = await version('published');
    await pointTo(current);
    const candidate = await version('in_review');
    const input = {
      versionUid: candidate.versionUid,
      expectedCurrentVersionUid: current.versionUid,
      reason: 'publish candidate',
      idempotencyKey: 'publish-once',
      actorUserId,
    };

    const first = await publishDataVersion(input, instance);
    const second = await publishDataVersion(input, instance);

    expect(second).toEqual(first);
    expect(first).toMatchObject({
      action: 'publish',
      previousVersionUid: current.versionUid,
      currentVersionUid: candidate.versionUid,
    });
    expect((await instance('published_dataset_pointer').where({ channel: 'default' }).first()).version_id)
      .toBe(candidate.versionId);
    expect((await instance('data_versions').where({ id: candidate.versionId }).first()).status).toBe('published');
    expect(Number((await instance('data_admin_audit_log').where({ idempotency_key: 'publish-once' }).count({ count: '*' }).first())?.count))
      .toBe(1);
  });

  it('rolls the pointer back to an older published version without changing version content', async () => {
    const older = await version('published');
    const current = await version('published');
    await pointTo(current);

    const result = await rollbackDataVersion({
      targetVersionUid: older.versionUid,
      expectedCurrentVersionUid: current.versionUid,
      reason: 'rollback test',
      idempotencyKey: 'rollback-once',
      actorUserId,
    }, instance);

    expect(result).toMatchObject({
      action: 'rollback',
      previousVersionUid: current.versionUid,
      currentVersionUid: older.versionUid,
    });
    expect((await instance('published_dataset_pointer').where({ channel: 'default' }).first()).version_id)
      .toBe(older.versionId);
    expect(await instance('data_versions').whereIn('id', [older.versionId, current.versionId]).orderBy('id').pluck('status'))
      .toEqual(['published', 'published']);
  });
});
