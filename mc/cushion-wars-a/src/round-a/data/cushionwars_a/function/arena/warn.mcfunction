# 显示下一失效圈预警（只播撒粒子，不放置方块、不改变碰撞与射线）。
#
# 用法（宏参数）：
#   /function cushionwars_a:arena/warn {team:4,round:6}
# team = 1—8；round = 当前回合，只有该尺寸存在“下一圈”的回合号才有效
# （各尺寸范围不同，见 maps/sizes/MAP-n<N>.md）。纯展示，可重复调用。

$function cushionwars_a:arena/gen/n$(team)/warn/r$(round)
