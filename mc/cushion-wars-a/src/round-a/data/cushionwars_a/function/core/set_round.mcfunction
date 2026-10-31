# 回合参数推导（纯算术，不涉及任何需要拦截的原版行为）。
# 输入：#round（cw_a_match）、#team_size（cw_a_match，1—8）
# 输出：#radius 保留半宽、#size 边长、#maxhp 该回合最大生命
#
# 公式来自确认稿 5.4 节，并按人数缩放：
#   base   = 2 * team_size + 7          （4v4 时为 15，即 31x31）
#   radius = max(2, base - max(0, round - 6))
#   size   = 2 * radius + 1
#   maxhp  = max(2, 40 - 4 * max(0, round - 11))
#
# 各尺寸会缩圈的回合范围不同：N=1 为 7..13，N=8 为 7..27，见 maps/sizes/MAP-n<N>.md。

scoreboard players operation #radius cw_a_match = #team_size cw_a_match
scoreboard players operation #radius cw_a_match *= #two cw_a_sys
scoreboard players add #radius cw_a_match 7
scoreboard players operation #tmp cw_a_match = #round cw_a_match
scoreboard players remove #tmp cw_a_match 6
execute if score #tmp cw_a_match matches ..0 run scoreboard players set #tmp cw_a_match 0
scoreboard players operation #radius cw_a_match -= #tmp cw_a_match
execute if score #radius cw_a_match matches ..1 run scoreboard players set #radius cw_a_match 2

scoreboard players operation #size cw_a_match = #radius cw_a_match
scoreboard players operation #size cw_a_match *= #two cw_a_sys
scoreboard players add #size cw_a_match 1

scoreboard players set #maxhp cw_a_match 40
scoreboard players operation #tmp cw_a_match = #round cw_a_match
scoreboard players remove #tmp cw_a_match 11
execute if score #tmp cw_a_match matches ..0 run scoreboard players set #tmp cw_a_match 0
scoreboard players operation #tmp cw_a_match *= #four cw_a_sys
scoreboard players operation #maxhp cw_a_match -= #tmp cw_a_match
execute if score #maxhp cw_a_match matches ..1 run scoreboard players set #maxhp cw_a_match 2
