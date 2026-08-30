import { LobbyIdentityService } from './identity-service';

export const createTestLoginAccount = async (
    service: LobbyIdentityService,
    input: { steamId: string; nickname: string; password?: string },
) => {
    const opened = await service.openOrBeginAccountRecovery({
        steamId: input.steamId,
        steamNickname: input.nickname,
    });
    if (opened.kind !== 'created') throw new Error(`test_account_not_created:${opened.kind}`);
    const password = input.password || opened.initialPassword;
    if (password !== opened.initialPassword) {
        await service.changeAccountPassword({
            identityId: opened.identityId,
            currentPassword: opened.initialPassword,
            newPassword: password,
            currentSessionId: 'test-account-setup',
        });
    }
    const identity = service.getIdentity(opened.identityId);
    const account = service.getLoginAccount(opened.identityId);
    if (!identity || !account) throw new Error('test_account_missing');
    return { identity, account, password };
};
