package com.caoren.crossera.bukkit;

import com.caoren.crossera.core.*;
import java.time.Instant;
import java.util.*;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.Damageable;
import org.bukkit.potion.PotionEffectType;
import org.bukkit.util.Vector;

/** All Bukkit reads occur on the server thread; returned maps contain only data. */
final class BukkitSnapshots {
    private BukkitSnapshots() { }
    static PlayerRef ref(Player player) { return new PlayerRef(player.getUniqueId(), player.getWorld().getUID(), player.getWorld().getName()); }
    static Map<String, Object> event(String kind, int tick) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("schemaVersion", 1); result.put("kind", kind);
        result.put("timestamp", Instant.now().toString()); result.put("tick", tick);
        return result;
    }
    static Map<String, Object> vector(Vector vector) { return Map.of("x", vector.getX(), "y", vector.getY(), "z", vector.getZ()); }
    static Map<String, Object> player(Player player, CombatService service) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("uuid", player.getUniqueId().toString());
        result.put("world", player.getWorld().getName());
        result.put("worldId", player.getWorld().getUID().toString());
        try {
            var state = service.profile(ref(player));
            result.put("profile", state.profile().key()); result.put("modeset", state.modeset());
            result.put("stored", state.stored()); result.put("overrides", state.overrides());
            result.put("verifiedModules", state.verifiedModules()); result.put("effectiveModules", state.modules());
        } catch (CombatFailure failure) { result.put("profileError", failure.code().name()); }
        var position = player.getLocation();
        result.put("position", Map.of("x", position.getX(), "y", position.getY(), "z", position.getZ(), "yaw", position.getYaw(), "pitch", position.getPitch()));
        result.put("velocity", vector(player.getVelocity()));
        result.put("health", player.getHealth()); result.put("absorption", player.getAbsorptionAmount());
        result.put("noDamageTicks", player.getNoDamageTicks()); result.put("maximumNoDamageTicks", player.getMaximumNoDamageTicks());
        result.put("lastDamage", player.getLastDamage()); result.put("attackCooldownObserved", player.getAttackCooldown());
        result.put("sprinting", player.isSprinting()); result.put("onGround", player.isOnGround());
        result.put("fallDistance", player.getFallDistance()); result.put("inWater", player.isInWater());
        result.put("climbing", player.isClimbing()); result.put("insideVehicle", player.isInsideVehicle());
        result.put("blindness", player.hasPotionEffect(PotionEffectType.BLINDNESS));
        result.put("mainHand", item(player.getInventory().getItemInMainHand()));
        List<Map<String, Object>> armour = new ArrayList<>();
        for (ItemStack stack : player.getInventory().getArmorContents()) armour.add(item(stack));
        result.put("armourBootsToHelmet", armour);
        result.put("effects", player.getActivePotionEffects().stream().map(effect -> Map.of(
                "type", effect.getType().getKey().toString(), "amplifier", effect.getAmplifier(), "ticks", effect.getDuration())).toList());
        return result;
    }
    private static Map<String, Object> item(ItemStack item) {
        if (item == null) return Map.of("material", "AIR", "amount", 0);
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("material", item.getType().name()); result.put("amount", item.getAmount());
        if (item.getItemMeta() instanceof Damageable damageable) result.put("durabilityDamage", damageable.getDamage());
        result.put("enchanted", !item.getEnchantments().isEmpty());
        return result;
    }
}
