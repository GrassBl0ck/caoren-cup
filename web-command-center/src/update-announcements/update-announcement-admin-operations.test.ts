import assert from 'node:assert/strict';
import test from 'node:test';
import {
    normalizeUpdateAnnouncementSaveInput,
    toUpdateAnnouncementAdminFailure,
} from './update-announcement-admin-operations';
import { UpdateAnnouncementValidationError } from './update-announcement-types';

test('shared admin operations validate save input', () => {
    assert.equal(normalizeUpdateAnnouncementSaveInput({ version: 1 }), null);
});

test('shared admin operations project validation errors safely', () => {
    assert.deepEqual(
        toUpdateAnnouncementAdminFailure(
            new UpdateAnnouncementValidationError('version_invalid', '版本号格式必须为 vX.Y.Z'),
        ),
        { status: 400, error: '版本号格式必须为 vX.Y.Z' },
    );
});
