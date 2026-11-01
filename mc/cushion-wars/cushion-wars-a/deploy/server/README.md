# 服务器端部署文件（源）

这个目录里的东西是要上传到 `123.206.213.166:/opt/caoren-mc-26.3/` 的运维脚本与地图包，
**不是**本地运行的东西。改动后按下面的对应关系部署。

## 文件对应关系

| 本地 | 服务器 | 说明 |
|---|---|---|
| `mapctl` | `/opt/caoren-mc-26.3/mapctl` | 地图切换工具，同时是 systemd 的 `ExecStart`。root 755 |
| `mcctl` | `/opt/caoren-mc-26.3/mcctl` | RCON 客户端，本机调试用。root 755 |
| `maps/<id>/map.conf` | `/opt/caoren-mc-26.3/maps/<id>/map.conf` | 地图元数据（id / title / 备注） |
| `cw-entry/` | `/opt/caoren-mc-26.3/maps/cushion-wars/world/datapacks/cw-entry/` | 坐垫战争地图自举包 |

`mapctl` 与 `mcctl` 用 Python/Bash 写成，部署时必须转换换行：

```bash
sed -i 's/\r$//' /tmp/mapctl   # 或 dos2unix
```

## 部署示例

```bash
scp deploy/server/mapctl root@123.206.213.166:/tmp/mapctl
ssh root@123.206.213.166 'sed -i "s/\r$//" /tmp/mapctl && install -m 755 /tmp/mapctl /opt/caoren-mc-26.3/mapctl'
```

`cw-entry` 目录要连同属主一起交付：

```bash
ssh root@123.206.213.166 'chown -R caoren-mc:caoren-mc /opt/caoren-mc-26.3/maps/cushion-wars/world/datapacks/cw-entry && chmod -R u+rwX,go+rX /opt/caoren-mc-26.3/maps/cushion-wars/world/datapacks/cw-entry'
```

## 地图切换速查

```bash
cd /opt/caoren-mc-26.3
./mapctl list                     # 有哪些地图，当前是哪张
./mapctl status                   # 服务状态、世界大小、端口、内存
sudo ./mapctl use minigames        # 切到小游戏合集（重启 25—35 秒）
sudo ./mapctl use cushion-wars     # 切回坐垫战争
sudo ./mapctl create <id> "标题"    # 新建一张空地图
```

完整说明见 `docs/10-多地图切换方案.md`。
