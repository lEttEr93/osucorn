# osu! corn

osu! Roam 的附属小工具：**按模式 / 键数自动切皮肤、调 mania 流速**。

它**不读写任何 osu! 配置文件**，只发送游戏自带的快捷键，再用 [tosu](https://github.com/KotRikD/tosu) 读回状态确认：

| 功能 | 快捷键 | 依据 |
|---|---|---|
| 下一个皮肤 | `Ctrl+Shift+T` | 实机验证：选歌界面生效，tosu 的 `settings.skin.name` 同步变化 |
| 上一个皮肤 | `Ctrl+Shift+E` | 用户提供，与 lazer 源码对应 |
| mania 流速 +1 | `F4` | 实机验证 20 次，31.4 → 40（40 是上限），游戏内即时生效 |
| mania 流速 −1 | `F3` | 用户提供，与源码对应 |

## 它想解决什么

同一个玩家打 4K 和 7K 时想用不同的流速与皮肤，而 osu! 里这些是全局设置——每次换模式都要手动改一遍。corn 把「模式 / 键数 → 皮肤 / 流速」记成预设，进选歌自动换皮肤、开局立刻调流速。

## 当前状态（v0.1.0）

**只有界面外壳**：

- 和 Roam 同一套配色与字体（Outfit，SIL OFL 1.1，见 `Fonts/ATTRIBUTION.md`）
- 预设列表（`presets.json`，第一次运行自动生成示例）
- 「应用」按钮目前只写日志 —— 引擎（tosu 读状态 / 快捷键发送 / 键数解析）还没接

开发用：`OsuCorn.exe --snapshot=D:\out.png` 渲染一张界面截图后退出。

## 设计要点（下一步）

- **键数**从谱面自己读：tosu 给 `beatmap.id`，corn 去 Roam 的缓存 / 本地 Songs 里取 `.osu` 解析 `CircleSize`；
  不指望 tosu 直接返回键数。
- **皮肤**：启动时循环一轮建立环形列表（`Ctrl+Shift+T` + 读回名字），之后按「目标与当前的环形距离」选方向
  （`Ctrl+Shift+T` 前进 / `Ctrl+Shift+E` 后退），失败要回滚到起始皮肤。
- **流速**：只在 `state=play && !paused` 时调（选歌界面无效，已实测），一次性把 N 键发完再读回，
  差一格就补一把；到 1 / 40 边界停。
- **界面**：游戏出现后自动隐藏到托盘，osu! 退出后自动回来。

## 构建

```
pack.cmd                 -> D:\osu-corn-<VERSION>-local
pack.cmd D:\some\folder  -> 任意目录
```

需要 .NET 8 SDK（脚本默认找 `D:\AI\tools\dotnet\dotnet.exe`，其次 `dotnet`）。
