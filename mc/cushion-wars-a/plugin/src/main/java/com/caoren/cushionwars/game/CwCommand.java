package com.caoren.cushionwars.game;
import com.caoren.cushionwars.probe.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import org.bukkit.command.*;
import org.bukkit.entity.Player;

public final class CwCommand implements CommandExecutor,TabCompleter {
    private final CushionWarsProbePlugin plugin;
    private static final List<String> KEYS=List.of("personal-ap","red-shared-ap","blue-shared-ap","initial-health","max-health","action-seconds");
    public CwCommand(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    @Override public boolean onCommand(CommandSender sender,Command command,String label,String[] args){
        String sub=args.length==0?"help":args[0].toLowerCase(Locale.ROOT);
        if(!List.of("help","join","leave","status").contains(sub)&&!sender.isOp()){say(sender,"只有管理员可以执行此操作。");return true;}
        try{
            switch(sub){
                case "help"->help(sender);
                case "join"->{if(!(sender instanceof Player p)){say(sender,"只有玩家可入队。");break;}Side side=Side.parse(args.length>1?args[1]:null);if(side==null)say(sender,"/cw join red|blue");else error(sender,plugin.game().join(p,side));}
                case "leave"->{if(sender instanceof Player p)error(sender,plugin.game().leave(p));}
                case "status"->{say(sender,plugin.state().teamSize()+"v"+plugin.state().teamSize()+" · "+plugin.game().phase()+" · 第 "+plugin.game().round()+" 回合 · "+plugin.game().remainingSeconds()+"秒 · 结果 "+plugin.game().result());say(sender,"存活 红 "+plugin.game().aliveCount(Side.RED)+" / 蓝 "+plugin.game().aliveCount(Side.BLUE));}
                case "start"->error(sender,plugin.game().start());
                case "stop"->plugin.game().stop("管理员手动中止");
                case "skip"->{plugin.game().skip();say(sender,"已跳过介绍（如当前正在介绍）。");}
                case "size","n"->{int n=number(args,1);String error=plugin.game().build(n);if(error==null)say(sender,"已切换为 "+n+"v"+n+" 并恢复对应地图，无需再执行 build。");else say(sender,error);}
                case "build"->{int n=args.length>1?number(args,1):plugin.state().teamSize();String error=plugin.game().build(n);if(error==null)say(sender,"已恢复 "+n+"v"+n+" 地图（优先使用已保存模板）。");else say(sender,error);}
                case "config"->config(sender,args);
                case "edit"->{if(!(sender instanceof Player p)){say(sender,"编辑需要管理员玩家进入游戏。");break;}String action=args.length>1?args[1]:"help";int n=action.equals("begin")?number(args,2):plugin.state().teamSize();say(sender,plugin.game().edit(p,action,n));}
                default->help(sender);
            }
        }catch(Exception ex){say(sender,"操作未完成："+ex.getMessage());}
        return true;
    }
    private int number(String[] args,int i){if(args.length<=i)throw new IllegalArgumentException("请提供每队人数 1—8。");int n=Integer.parseInt(args[i]);if(n<1||n>8)throw new IllegalArgumentException("每队人数只能为 1—8。");return n;}
    private void config(CommandSender sender,String[] args)throws Exception{
        if(args.length<2||args[1].equals("show")){
            say(sender,"下一局："+MatchSettings.load(plugin.getConfig()).describe(plugin.state().teamSize()));
            if(plugin.game().running())say(sender,"当前局："+plugin.game().settings().describe(plugin.state().teamSize()));return;
        }
        if(args.length!=4||!args[1].equals("set")||!KEYS.contains(args[2])){say(sender,"/cw config set <"+String.join("|",KEYS)+"> <数值|auto>；仅共享 AP 可用 auto");return;}
        String key=args[2],path="rules."+(key.equals("action-seconds")?"action-phase-ticks":key);Object old=plugin.getConfig().get(path),value;
        if(key.endsWith("shared-ap")&&args[3].equalsIgnoreCase("auto"))value=-1;
        else if(key.endsWith("health"))value=Double.parseDouble(args[3]);
        else{int n=Integer.parseInt(args[3]);value=key.equals("action-seconds")?Math.multiplyExact(n,20):n;}
        plugin.getConfig().set(path,value);
        try{
            MatchSettings.load(plugin.getConfig());
            Path target=plugin.getDataFolder().toPath().resolve("config.yml"),tmp=Files.createTempFile(target.getParent(),"settings-",".tmp");
            Files.writeString(tmp,plugin.getConfig().saveToString(),StandardCharsets.UTF_8);
            if(Files.exists(target))Files.copy(target,target.resolveSibling("config.yml.bak-"+System.nanoTime()));
            Files.move(tmp,target,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);
        }catch(Exception ex){plugin.getConfig().set(path,old);throw ex;}
        say(sender,"已保存，下一局生效："+MatchSettings.load(plugin.getConfig()).describe(plugin.state().teamSize()));
    }
    private void help(CommandSender sender){
        say(sender,"/cw join red|blue · /cw leave · /cw status");
        if(sender.isOp()){say(sender,"/cw size <1-8>（切换人数并恢复地图） · /cw build [1-8]（重新恢复模板） · /cw start · /cw stop · /cw skip");
            say(sender,"/cw config show | set <参数> <数值>（下一局生效）");
            say(sender,"/cw edit begin <1-8> | save | exit（exit 放弃未保存棋盘修改）");}
    }
    private void error(CommandSender sender,String text){if(text!=null)say(sender,text);}
    private void say(CommandSender sender,String text){sender.sendMessage(ProbeText.prefix(text));}
    @Override public List<String> onTabComplete(CommandSender sender,Command command,String alias,String[] args){
        List<String> options=new ArrayList<>();
        if(args.length==1){options.addAll(List.of("help","join","leave","status"));if(sender.isOp())options.addAll(List.of("size","build","start","stop","skip","config","edit"));}
        else if(args.length==2){
            if(args[0].equals("join"))options=List.of("red","blue");
            else if(sender.isOp()&&args[0].equals("config"))options=List.of("show","set");
            else if(sender.isOp()&&args[0].equals("edit"))options=List.of("begin","save","exit");
            else if(sender.isOp()&&List.of("size","build").contains(args[0]))options=List.of("1","2","3","4","5","6","7","8");
        }else if(sender.isOp()&&args.length==3){
            if(args[0].equals("config")&&args[1].equals("set"))options=KEYS;
            if(args[0].equals("edit")&&args[1].equals("begin"))options=List.of("1","2","3","4","5","6","7","8");
        }else if(sender.isOp()&&args.length==4&&args[0].equals("config"))options=args[2].endsWith("shared-ap")?List.of("auto","0","1","2","4"):List.of("1","2","4","30","40");
        String prefix=args[args.length-1].toLowerCase(Locale.ROOT);return options.stream().filter(s->s.startsWith(prefix)).toList();
    }
}
