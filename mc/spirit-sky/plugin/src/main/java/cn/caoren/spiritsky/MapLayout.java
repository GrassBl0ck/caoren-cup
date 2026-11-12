package cn.caoren.spiritsky;
import com.google.gson.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
final class MapLayout {
    final World world;
    final Location lobby;
    final double ground;
    final double[] bounds;
    record Base(Location center,List<Location> cores,Location origin,Location supply,double radius,List<Location> spawns,Location main,Location backup,Location minion) {
    }
    record Node(Location position,List<String> neighbors) {
    }
    final EnumMap<Rules.Side,Base> bases=new EnumMap<>(Rules.Side.class);
    final Map<String,Location> slots=new LinkedHashMap<>();
    final Map<String,Node> nodes=new LinkedHashMap<>();
    final List<Location> lane=new ArrayList<>();
    MapLayout(World w,Path file) throws IOException {
        world=w;
        JsonObject o;
        try(Reader r=Files.newBufferedReader(file,StandardCharsets.UTF_8)){
            o=JsonParser.parseReader(r).getAsJsonObject();
        }
        if(o.get("schema_version").getAsInt()!=1)throw new IOException("unsupported map schema");
        ground=o.get("standing_y").getAsDouble();
        lobby=pos(o.get("lobby_spawn"));
        Location min=pos(o.getAsJsonObject("battle_bounds").get("min")),max=pos(o.getAsJsonObject("battle_bounds").get("max_exclusive"));
        bounds=new double[]{
            min.getX(),max.getX(),min.getZ(),max.getZ()
        }
        ;
        for(JsonElement e:o.getAsJsonArray("bases")){
            JsonObject b=e.getAsJsonObject(),s=b.getAsJsonObject("supply");
            bases.put(Rules.Side.valueOf(b.get("team").getAsString().toUpperCase(Locale.ROOT)),new Base(pos(b.get("center")),positions(b.getAsJsonArray("core_markers")),pos(b.get("projectile_origin")),pos(s.get("center")),s.get("radius").getAsDouble(),positions(b.getAsJsonArray("player_spawns")),pos(b.get("takeoff_main")),pos(b.get("takeoff_backup")),pos(b.get("minion_spawn"))));
        }
        for(JsonElement e:o.getAsJsonArray("stations")){
            JsonObject b=e.getAsJsonObject();
            slots.put(b.get("id").getAsString(),pos(b.get("center")));
        }
        lane.addAll(positions(o.getAsJsonArray("minion_lane")));
        for(var e:o.getAsJsonObject("air_network").getAsJsonObject("nodes").entrySet()){
            JsonObject n=e.getValue().getAsJsonObject();
            List<String> links=new ArrayList<>();
            n.getAsJsonArray("neighbors").forEach(v->links.add(v.getAsString()));
            nodes.put(e.getKey(),new Node(pos(n.get("position")),links));
        }
        if(bases.size()!=2 || slots.size()!=8 || lane.size()<2 || nodes.isEmpty())throw new IOException("incomplete map points");
        for(Node n:nodes.values())for(String link:n.neighbors)if(!nodes.containsKey(link))throw new IOException("broken air edge "+link);
        for(Base b:bases.values())if(b.spawns.size()!=4 || b.cores.isEmpty() || b.radius<=0)throw new IOException("invalid base points");
    }
    Location pos(JsonElement e){
        JsonArray a=e.getAsJsonArray();
        if(a.size()!=3)throw new IllegalArgumentException("point length");
        double x=a.get(0).getAsDouble(),y=a.get(1).getAsDouble(),z=a.get(2).getAsDouble();
        if(!Double.isFinite(x+y+z))throw new IllegalArgumentException("nonfinite point");
        return new Location(world,x,y,z);
    }
    List<Location> positions(JsonArray a){
        List<Location> r=new ArrayList<>();
        a.forEach(e->r.add(pos(e)));
        return r;
    }
    static double horizontal(Location a,Location b){
        if(a.getWorld()!=b.getWorld())return Double.POSITIVE_INFINITY;
        return Math.hypot(a.getX()-b.getX(),a.getZ()-b.getZ());
    }
    boolean inside(Location p){
        return p.getWorld()==world&&p.getX()>=bounds[0]&&p.getX()<bounds[1]&&p.getZ()>=bounds[2]&&p.getZ()<bounds[3];
    }
    boolean supply(Rules.Side s,Location p){
        Base b=bases.get(s);
        return horizontal(p,b.supply)<=b.radius&&p.getY()>=ground-1&&p.getY()<ground+10;
    }
    double laneDistance(Location p){
        double best=Double.POSITIVE_INFINITY;
        for(int i=1;i<lane.size();i++){
            Location a=lane.get(i-1),b=lane.get(i);
            double dx=b.getX()-a.getX(),dz=b.getZ()-a.getZ();
            double t=Math.clamp(((p.getX()-a.getX())*dx+(p.getZ()-a.getZ())*dz)/(dx*dx+dz*dz),0,1);
            best=Math.min(best,Math.hypot(p.getX()-a.getX()-t*dx,p.getZ()-a.getZ()-t*dz));
        }
        return best;
    }
    List<Location> airPath(Location start,Location end,java.util.function.BiPredicate<Location,Location> clear){
        String from=nearestVisible(start,clear),to=nearestVisible(end,clear);
        if(from==null||to==null)return List.of();
        Map<String,String> prev=new HashMap<>();
        Map<String,Double> cost=new HashMap<>();
        PriorityQueue<String> q=new PriorityQueue<>(Comparator.comparingDouble(cost::get));
        cost.put(from,0d);
        q.add(from);
        while(!q.isEmpty()){
            String u=q.poll();
            if(u.equals(to))break;
            for(String v:nodes.get(u).neighbors){
                double c=cost.get(u)+nodes.get(u).position.distance(nodes.get(v).position);
                if(c<cost.getOrDefault(v,Double.POSITIVE_INFINITY)&&clear.test(nodes.get(u).position,nodes.get(v).position)){
                    q.remove(v);
                    cost.put(v,c);
                    prev.put(v,u);
                    q.add(v);
                }
            }
        }
        if(!cost.containsKey(to))return List.of();
        LinkedList<Location> out=new LinkedList<>();
        for(String n=to;n!=null;n=prev.get(n))out.addFirst(nodes.get(n).position.clone());
        out.add(end.clone());
        return out;
    }
    private String nearestVisible(Location p,java.util.function.BiPredicate<Location,Location> clear){
        return nodes.entrySet().stream().filter(e->clear.test(p,e.getValue().position)).min(Comparator.comparingDouble(e->e.getValue().position.distanceSquared(p))).map(Map.Entry::getKey).orElse(null);
    }
}
