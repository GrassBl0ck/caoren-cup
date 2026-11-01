package com.caoren.cushionwars.realtime;

import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.bukkit.Bukkit;
import org.bukkit.entity.AbstractArrow;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Projectile;

/** 只跟踪本局投射物：归属、伤害、到期清理；比赛结束/截止统一清除。 */
public final class ProjectileTracker {
    public static final String MATCH_TAG = "cwb_projectile";

    public record Shot(String ownerId, double damage, boolean tipped, long matchId, boolean potion) {
    }

    private final Map<UUID, Shot> shots = new HashMap<>();
    private final Map<UUID, Long> expiry = new HashMap<>();

    public void track(Projectile projectile, String ownerId, double damage, boolean tipped, long matchId) {
        projectile.setPersistent(false);
        projectile.addScoreboardTag(MATCH_TAG);
        if (projectile instanceof AbstractArrow arrow) {
            arrow.setPickupStatus(AbstractArrow.PickupStatus.DISALLOWED);
            arrow.setCritical(false);
            arrow.setDamage(0.0);
        }
        shots.put(projectile.getUniqueId(), new Shot(ownerId, damage, tipped, matchId, false));
        expiry.put(projectile.getUniqueId(), (long) Bukkit.getCurrentTick() + 200);
    }

    public void trackEffect(Projectile projectile, String ownerId, long matchId) {
        projectile.setPersistent(false);
        projectile.addScoreboardTag(MATCH_TAG);
        shots.put(projectile.getUniqueId(), new Shot(ownerId, 0, false, matchId, true));
        expiry.put(projectile.getUniqueId(), (long) Bukkit.getCurrentTick() + 200);
    }

    public boolean tracked(Entity entity) {
        return entity != null && shots.containsKey(entity.getUniqueId());
    }

    public Shot shot(Entity entity) {
        return entity == null ? null : shots.get(entity.getUniqueId());
    }

    public void forget(Entity entity) {
        if (entity != null) {
            shots.remove(entity.getUniqueId());
            expiry.remove(entity.getUniqueId());
        }
    }

    public void remove(Entity entity) {
        if (entity != null) {
            forget(entity);
            entity.remove();
        }
    }

    public void prune() {
        long now = Bukkit.getCurrentTick();
        List<UUID> ids = List.copyOf(shots.keySet());
        for (UUID id : ids) {
            Entity entity = Bukkit.getEntity(id);
            if (entity == null || !entity.isValid() || expiry.getOrDefault(id, 0L) <= now
                    || entity instanceof AbstractArrow arrow && arrow.isInBlock()) {
                if (entity != null) {
                    entity.remove();
                }
                shots.remove(id);
                expiry.remove(id);
            }
        }
    }

    public void clear() {
        for (UUID id : List.copyOf(shots.keySet())) {
            Entity entity = Bukkit.getEntity(id);
            if (entity != null) {
                entity.remove();
            }
        }
        shots.clear();
        expiry.clear();
    }

    /** 清除所有带本插件比赛标记但仍残留的投射物。 */
    public void clearTagged(org.bukkit.World world) {
        if (world == null) {
            return;
        }
        for (Entity entity : world.getEntities()) {
            if (entity instanceof Projectile projectile
                    && projectile.getScoreboardTags().contains(MATCH_TAG)) {
                projectile.remove();
            }
        }
    }
}
