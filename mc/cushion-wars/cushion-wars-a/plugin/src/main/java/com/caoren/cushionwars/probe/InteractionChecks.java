package com.caoren.cushionwars.probe;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.block.Block;
import org.bukkit.entity.*;
/** 使用服务端原版交互距离属性和碰撞射线，不能穿墙或从高处操作。 */
public final class InteractionChecks {
    private InteractionChecks(){}
    public static boolean entity(Player p,Entity target){
        double reach=p.getAttribute(Attribute.ENTITY_INTERACTION_RANGE).getValue();
        var eye=p.getEyeLocation();var hit=eye.getWorld().rayTrace(eye,eye.getDirection(),reach,FluidCollisionMode.NEVER,true,.05,e->e==target);
        return hit!=null&&hit.getHitEntity()==target;
    }
    public static boolean block(Player p,Block target){
        double reach=p.getAttribute(Attribute.BLOCK_INTERACTION_RANGE).getValue();
        var hit=p.rayTraceBlocks(reach,FluidCollisionMode.NEVER);
        return hit!=null&&target.equals(hit.getHitBlock());
    }
}
