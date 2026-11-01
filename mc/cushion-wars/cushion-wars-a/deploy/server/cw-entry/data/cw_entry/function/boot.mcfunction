# 棋盘初始化、出生点与模板恢复统一由插件 ArenaManager 负责。
# 不再按中心白色混凝土判断是否建图，避免中央改色/自定义地图被覆盖。
# 世界规则仍由本数据包设置；不会修改其他地图。
function cw_entry:rules
tellraw @a {"text":"[坐垫战争] 世界规则已加载，地图模板由插件恢复；/cw help 查看命令。","color":"aqua"}
