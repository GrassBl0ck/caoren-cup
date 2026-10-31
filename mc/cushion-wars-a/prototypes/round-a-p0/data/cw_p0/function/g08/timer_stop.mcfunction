# 计时器到期：只输出提示，不做任何玩法处理。
tellraw @a {"text":"[P0] 计时器到期（600 tick）。","color":"aqua"}
title @a[tag=p0_subject] actionbar {"text":"计时结束","color":"red"}
