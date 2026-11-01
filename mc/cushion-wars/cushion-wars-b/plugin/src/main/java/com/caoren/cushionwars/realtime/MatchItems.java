package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.PlayerState;
import com.caoren.cushionwars.realtime.rules.Side;
import java.util.ArrayList;
import java.util.List;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Material;
import org.bukkit.NamespacedKey;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.inventory.meta.PotionMeta;
import org.bukkit.persistence.PersistentDataType;
import org.bukkit.plugin.java.JavaPlugin;
import org.bukkit.potion.PotionType;

/** 比赛物品的命名空间标记、初始快捷栏与库存同步。 */
public class MatchItems {
    public static final String ROLE_BOW = "bow";
    public static final String ROLE_CUSHION = "cushion";
    public static final String ROLE_DOOR = "door";
    public static final String ROLE_HEAL = "heal";
    public static final String ROLE_SPLASH = "splash";
    public static final String ROLE_TIPPED = "tipped";
    public static final String ROLE_ARROW = "arrow";
    public static final String ROLE_CONTROL = "control";

    private final NamespacedKey itemKey;
    private final NamespacedKey roleKey;
    private final NamespacedKey matchKey;
    private long matchId;
    public void beginMatch(long id) { matchId = id; }

    public MatchItems(JavaPlugin plugin) {
        this.itemKey = new NamespacedKey(plugin, "match_item");
        this.roleKey = new NamespacedKey(plugin, "match_role");
        this.matchKey = new NamespacedKey(plugin, "match_id");
    }
    MatchItems(String namespace) {
        this.itemKey = new NamespacedKey(namespace, "match_item");
        this.roleKey = new NamespacedKey(namespace, "match_role");
        this.matchKey = new NamespacedKey(namespace, "match_id");
    }

    public ItemStack mark(ItemStack item, String role) {
        if (item == null || item.getType().isAir()) {
            return item;
        }
        ItemMeta meta = item.getItemMeta();
        meta.getPersistentDataContainer().set(itemKey, PersistentDataType.BYTE, (byte) 1);
        meta.getPersistentDataContainer().set(roleKey, PersistentDataType.STRING, role);
        meta.getPersistentDataContainer().set(matchKey, PersistentDataType.LONG, matchId);
        item.setItemMeta(meta);
        return item;
    }

    public boolean owned(ItemStack item) {
        return item != null && !item.getType().isAir()
                && item.getItemMeta().getPersistentDataContainer().has(itemKey, PersistentDataType.BYTE);
    }

    public String role(ItemStack item) {
        if (!owned(item) || !Long.valueOf(matchId).equals(
                item.getItemMeta().getPersistentDataContainer().get(matchKey, PersistentDataType.LONG))) {
            return null;
        }
        return item.getItemMeta().getPersistentDataContainer().get(roleKey, PersistentDataType.STRING);
    }

    /** 是否存在非本插件签发的物品；赛后只清理本插件物件。 */
    public boolean hasPersonalItems(Player player) {
        for (ItemStack item : player.getInventory().getContents()) {
            if (item != null && !item.getType().isAir() && !owned(item)) {
                return true;
            }
        }
        for (ItemStack item : player.getInventory().getArmorContents()) {
            if (item != null && !item.getType().isAir() && !owned(item)) {
                return true;
            }
        }
        ItemStack offhand = player.getInventory().getItemInOffHand();
        return offhand != null && !offhand.getType().isAir() && !owned(offhand);
    }

    public void clear(Player player) {
        ItemStack[] contents = player.getInventory().getContents();
        for (int i = 0; i < contents.length; i++) {
            if (owned(contents[i])) {
                player.getInventory().setItem(i, null);
            }
        }
        ItemStack offhand = player.getInventory().getItemInOffHand();
        if (owned(offhand)) {
            player.getInventory().setItemInOffHand(null);
        }
    }

    /** 按设计稿第 4.3 节发放初始快捷栏。 */
    public void giveKit(Player player, Side side, PlayerState state) {
        if (hasPersonalItems(player)) throw new IllegalStateException("准备期间出现非比赛物品，开局取消");
        clear(player);
        ItemStack bow = new ItemStack(Material.BOW);
        ItemMeta bowMeta = bow.getItemMeta();
        bowMeta.setUnbreakable(true);
        bowMeta.addEnchant(Enchantment.INFINITY, 1, true);
        bowMeta.displayName(Component.text("无限弓", NamedTextColor.AQUA));
        bow.setItemMeta(bowMeta);
        player.getInventory().setItem(0, mark(bow, ROLE_BOW));
        player.getInventory().setItem(1, cushionStack(side, state.cushions()));
        player.getInventory().setItem(2, doorStack(state.doors()));
        player.getInventory().setItem(3, potion(Material.POTION, PotionType.HEALING, ROLE_HEAL));
        player.getInventory().setItem(4, potion(Material.SPLASH_POTION, PotionType.STRONG_HEALING, ROLE_SPLASH));
        player.getInventory().setItem(5, tippedStack(state.tippedArrows()));
        player.getInventory().setItem(7, controlStack(state.ap().activationsLeft()));
        player.getInventory().setItemInOffHand(mark(new ItemStack(Material.ARROW), ROLE_ARROW));
        player.getInventory().setHeldItemSlot(0);
    }

    /** 将背包中的比赛物品数量与规则层同步；不凭空补回已消耗的药箭/药水。 */
    public void sync(Player player, PlayerState state, Side side) {
        syncRole(player, ROLE_CUSHION, cushionStack(side, state.cushions()), 1);
        syncRole(player, ROLE_DOOR, doorStack(state.doors()), 2);
        ItemStack control = findRole(player, ROLE_CONTROL);
        if (control == null) {
            if (player.getInventory().getItem(7) == null
                    || player.getInventory().getItem(7).getType().isAir()) {
                player.getInventory().setItem(7, controlStack(state.ap().activationsLeft()));
            }
            return;
        }
        ItemMeta meta = control.getItemMeta();
        meta.displayName(controlName(state.ap().activationsLeft()));
        control.setItemMeta(meta);
        int slot = findRoleSlot(player, ROLE_CONTROL);
        if (slot == -2) {
            player.getInventory().setItemInOffHand(control);
        } else if (slot >= 0) {
            player.getInventory().setItem(slot, control);
        }
    }

    /**
     * 同一角色只维护一个背包堆叠：数量跟随规则层；物品被换到副手时原地更新，避免复制；
     * 规则层有库存但物品缺失时补回一格；库存为 0 时清除所有该角色物品。
     */
    private void syncRole(Player player, String role, ItemStack desired, int preferredSlot) {
        List<Integer> slots = roleSlots(player, role);
        boolean empty = desired == null || desired.getType().isAir();
        if (empty) {
            for (int slot : slots) {
                setSlot(player, slot, null);
            }
            return;
        }
        if (!slots.isEmpty()) {
            int first = slots.get(0);
            ItemStack stack = getSlot(player, first);
            if (stack != null) {
                stack.setAmount(desired.getAmount());
                setSlot(player, first, stack);
            }
            for (int i = 1; i < slots.size(); i++) {
                setSlot(player, slots.get(i), null);
            }
            return;
        }
        ItemStack current = player.getInventory().getItem(preferredSlot);
        if (current == null || current.getType().isAir()) {
            player.getInventory().setItem(preferredSlot, desired);
            return;
        }
        int emptySlot = player.getInventory().firstEmpty();
        if (emptySlot >= 0) {
            player.getInventory().setItem(emptySlot, desired);
        }
    }

    private List<Integer> roleSlots(Player player, String role) {
        List<Integer> slots = new ArrayList<>();
        ItemStack[] storage = player.getInventory().getStorageContents();
        for (int i = 0; i < storage.length; i++) {
            if (role.equals(role(storage[i]))) {
                slots.add(i);
            }
        }
        if (role.equals(role(player.getInventory().getItemInOffHand()))) {
            slots.add(-2);
        }
        return slots;
    }

    private ItemStack findRole(Player player, String role) {
        int slot = findRoleSlot(player, role);
        return slot == Integer.MIN_VALUE ? null : getSlot(player, slot);
    }

    private int findRoleSlot(Player player, String role) {
        List<Integer> slots = roleSlots(player, role);
        return slots.isEmpty() ? Integer.MIN_VALUE : slots.get(0);
    }

    private ItemStack getSlot(Player player, int slot) {
        return slot == -2 ? player.getInventory().getItemInOffHand() : player.getInventory().getItem(slot);
    }

    private void setSlot(Player player, int slot, ItemStack item) {
        if (slot == -2) {
            player.getInventory().setItemInOffHand(item);
        } else {
            player.getInventory().setItem(slot, item);
        }
    }

    /** 消耗一个指定角色的物品；成功返回 true。 */
    public boolean consumeOne(Player player, String role) {
        for (int i = 0; i < player.getInventory().getSize(); i++) {
            ItemStack item = player.getInventory().getItem(i);
            if (item != null && role.equals(role(item))) {
                int amount = item.getAmount();
                if (amount <= 1) {
                    player.getInventory().setItem(i, null);
                } else {
                    item.setAmount(amount - 1);
                }
                return true;
            }
        }
        return false;
    }

    private ItemStack cushionStack(Side side, int count) {
        if (count <= 0) {
            return null;
        }
        Material material = side == Side.RED ? Material.RED_CUSHION : Material.BLUE_CUSHION;
        ItemStack item = new ItemStack(material, Math.min(count, material.getMaxStackSize()));
        return mark(item, ROLE_CUSHION);
    }

    private ItemStack doorStack(int count) {
        if (count <= 0) {
            return null;
        }
        ItemStack item = new ItemStack(Material.OAK_DOOR, Math.min(count, 2));
        return mark(item, ROLE_DOOR);
    }

    private ItemStack tippedStack(int count) {
        if (count <= 0) {
            return null;
        }
        ItemStack item = new ItemStack(Material.TIPPED_ARROW, Math.min(count, 2));
        PotionMeta meta = (PotionMeta) item.getItemMeta();
        meta.setBasePotionType(PotionType.HARMING);
        meta.displayName(Component.text("伤害之箭", NamedTextColor.RED));
        item.setItemMeta(meta);
        return mark(item, ROLE_TIPPED);
    }

    private ItemStack potion(Material material, PotionType type, String role) {
        ItemStack item = new ItemStack(material);
        PotionMeta meta = (PotionMeta) item.getItemMeta();
        meta.setBasePotionType(type);
        meta.displayName(Component.text(role.equals(ROLE_HEAL) ? "治疗药水 I" : "喷溅治疗 II",
                NamedTextColor.LIGHT_PURPLE));
        item.setItemMeta(meta);
        return mark(item, role);
    }

    private ItemStack controlStack(int activationsLeft) {
        ItemStack item = new ItemStack(Material.CLOCK);
        ItemMeta meta = item.getItemMeta();
        meta.displayName(controlName(activationsLeft));
        item.setItemMeta(meta);
        return mark(item, ROLE_CONTROL);
    }

    private Component controlName(int activationsLeft) {
        return Component.text("激活即时 AP（剩余 " + activationsLeft + " 次）", NamedTextColor.AQUA);
    }
}
