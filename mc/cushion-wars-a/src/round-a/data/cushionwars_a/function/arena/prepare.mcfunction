# 组织者入口：按人数准备棋盘。
#
# 用法（宏参数）：
#   /function cushionwars_a:arena/prepare {team:4}
# 可选 1—8，对应 1v1—8v8；边长 19/23/27/31/35/39/43/47。
#
# 流程由 arena/gen/n<N>/prepare 执行：强制加载 → 清空最大施工区 → 地面 → 掩体 → 出生坐垫 → 自检，
# 最后把 #team_size 写入 cw_a_match，供缩圈、预警与插件读取。
#
# 注意：同一场地反复切换人数是安全的（先清空再建），但每次都会重建 47x47 范围，
# 不要在这个范围内放任何非本项目的建筑。

$function cushionwars_a:arena/gen/n$(team)/prepare
