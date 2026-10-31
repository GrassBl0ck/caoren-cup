# 输出采集摘要。完整数据用 /data get storage cw_p0:log snapshots 查看。
tellraw @a [{"text":"[P0] 采集轮数：","color":"aqua"},{"score":{"name":"#snapshots","objective":"p0_log"}},{"text":"；完整数据：","color":"aqua"},{"text":"/data get storage cw_p0:log snapshots","color":"gray"}]
