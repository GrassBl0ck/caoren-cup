# 每 30 秒把时间重置到正午、清一次天气。
# 这是 gamerule 的替代实现：26.3 的 gamerule 名称尚未确认（写错会让整个函数加载失败）。
time set 6000
weather clear
schedule function cw_entry:keep_day 600t
