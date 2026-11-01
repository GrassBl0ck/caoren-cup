# 26.3 的 gamerule 已统一改名，旧的驼峰名（doDaylightCycle 等）全部不再解析。
# 以下名字均在 purpur-26.3-2651 上用 /gamerule 逐个实测通过，写错会让整个函数加载失败，勿凭记忆改。
#
#   advance_time                = 旧的 doDaylightCycle
#   advance_weather             = 旧的 doWeatherCycle
#   spawn_monsters / spawn_mobs = 旧的 doMobSpawning（26.3 拆成两条）
#   keep_inventory              = 旧的 keepInventory
#   natural_health_regeneration = 旧的 naturalRegeneration
#   fall_damage / show_death_messages / immediate_respawn = 同名去驼峰

gamerule advance_time false
gamerule advance_weather false
gamerule spawn_monsters false
gamerule spawn_mobs false
gamerule keep_inventory true
gamerule natural_health_regeneration false
gamerule fall_damage true
gamerule show_death_messages true
gamerule immediate_respawn false

time set 6000
weather clear
