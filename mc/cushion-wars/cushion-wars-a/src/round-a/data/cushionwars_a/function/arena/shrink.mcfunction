# 执行当回合缩圈（清除失效圈的地面、掩体、坐垫、门与投射物）。
#
# 用法（宏参数）：
#   /function cushionwars_a:arena/shrink {team:4,round:7}
# team = 1—8（开局时由 arena/prepare 写入 #team_size，可省略但建议写明），
# round = 目标回合，必须是该尺寸会缩圈的回合（各尺寸不同，见 maps/sizes/MAP-n<N>.md）。
#
# 前置：只应在“新回合开始、且已判定旧阶段无胜负”时调用一次。
# 本函数自身不做胜负判定，也不做缩圈致死（那依赖尚未验证的淘汰路径）。

$function cushionwars_a:arena/gen/n$(team)/shrink/r$(round)
