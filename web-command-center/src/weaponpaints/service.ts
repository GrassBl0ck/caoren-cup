import { WeaponPaintsCatalog } from './catalog';
import type { CosmeticKind, CosmeticUpdate, LoadoutRepository, SkinAuditEntry } from './repository';
import type { ResolvedSkinActor } from './permissions';
import { isWeaponAvailableForTeam } from './team-policy';
import { validateTeam, validateWeaponUpdate } from './validation';

const auditFor = (
    actor: ResolvedSkinActor,
    action: SkinAuditEntry['action'],
    details?: Record<string, unknown>,
): SkinAuditEntry => ({ ...actor, action, details });

export class WeaponPaintsService {
    constructor(
        readonly catalog: WeaponPaintsCatalog,
        private readonly repository: LoadoutRepository,
        private readonly enqueueRefresh: (command: string) => void,
    ) {}

    health() {
        return this.repository.health();
    }

    load(actor: ResolvedSkinActor) {
        return this.repository.load(actor.targetSteamId);
    }

    async saveWeapon(actor: ResolvedSkinActor, raw: Record<string, any>) {
        const update = validateWeaponUpdate(raw);
        if (!this.catalog.hasWeaponPaint(update.weaponDefIndex, update.paintId)) {
            throw new Error('所选武器皮肤不在服务器本地目录中。');
        }
        const isGun = this.catalog.isGunDefIndex(update.weaponDefIndex);
        if (isGun && !isWeaponAvailableForTeam(update.weaponDefIndex, update.team)) {
            throw new Error('所选枪械不可用于当前阵营。');
        }
        if (!isGun && update.stickers.some((sticker) => sticker.id)) {
            throw new Error('只有枪械可以使用印花。');
        }
        if (!isGun && update.keychain?.id) {
            throw new Error('只有枪械可以使用挂件。');
        }
        for (const sticker of update.stickers) {
            if (sticker.id && !this.catalog.hasSimpleItem('sticker', sticker.id)) throw new Error(`印花槽 ${sticker.slot + 1} 的 ID 不在服务器本地目录中。`);
        }
        if (update.keychain?.id && !this.catalog.hasSimpleItem('keychain', update.keychain.id)) {
            throw new Error('所选挂件不在服务器本地目录中。');
        }
        await this.repository.saveWeapon(actor.targetSteamId, update, auditFor(actor, 'save_weapon', {
            team: update.team,
            weaponDefIndex: update.weaponDefIndex,
            paintId: update.paintId,
        }));
        this.requestSafeRefresh(actor.targetSteamId);
        return update;
    }

    async saveCosmetic(actor: ResolvedSkinActor, raw: Record<string, any>) {
        const team = validateTeam(raw.team);
        const kind = String(raw.kind || '') as CosmeticKind;
        const itemKey = String(raw.itemKey || '').trim();
        if (!(['Knife', 'Glove', 'Agent', 'MusicKit', 'Pin'] as CosmeticKind[]).includes(kind)) throw new Error('装饰品分类无效。');
        const valid = kind === 'Knife'
            ? this.catalog.hasKnifeKey(itemKey)
            : kind === 'Glove'
                ? this.catalog.hasItemKey('glove', itemKey)
                : kind === 'Agent'
                    ? this.catalog.hasItemKey('agent', itemKey, team)
                    : !itemKey || this.catalog.hasSimpleItem(kind === 'MusicKit' ? 'music' : 'pin', Number(itemKey));
        if (!valid) throw new Error('所选装饰品不在服务器本地目录中，或不适用于当前阵营。');
        const update: CosmeticUpdate = { team, kind, itemKey };
        await this.repository.saveCosmetic(actor.targetSteamId, update, auditFor(actor, 'save_cosmetic', { team, kind, itemKey }));
        this.requestSafeRefresh(actor.targetSteamId);
        return update;
    }

    async reset(actor: ResolvedSkinActor, teamRaw?: unknown) {
        const team = teamRaw === undefined || teamRaw === null || teamRaw === '' ? undefined : validateTeam(teamRaw);
        if (actor.actorRole !== 'Admin' && team === undefined) {
            throw new Error('玩家只能清空自己的当前阵营配置。');
        }
        await this.repository.reset(actor.targetSteamId, team, auditFor(actor, 'reset', { team: team || 'all' }));
        this.requestSafeRefresh(actor.targetSteamId);
    }

    async forceRefresh(actor: ResolvedSkinActor) {
        this.requireAdmin(actor);
        await this.repository.audit(auditFor(actor, 'force_refresh'));
        this.enqueueRefresh(`wp_refresh ${actor.targetSteamId}`);
    }

    private requestSafeRefresh(steamId: string) {
        this.enqueueRefresh(`wp_refresh ${steamId} safe`);
    }

    private requireAdmin(actor: ResolvedSkinActor) {
        if (actor.actorRole !== 'Admin') throw new Error('只有管理员可以执行此操作。');
    }
}
