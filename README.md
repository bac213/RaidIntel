# RaidIntel (`v0.2.0`)

面向 **SPT 4.1.6（EFT `0.16.9.40743`）** 的服务端 + 客户端双端物品需求情报模组，在战局内外（含 PMC 与 Scav 战局）实时展示物品持有量、任务进度与藏身处升级需求。

## 主要功能

### 1. 物品悬停提示（Tooltip）
鼠标悬停在任意已检视/已搜索物品上时，在原版物品名称下方追加紧凑、高可读性的需求情报（不打断原版 0.6 秒悬停延迟）：

```text
拥有：3（FIR：2）
物资短缺 2/3（FIR）
日常任务 2/1
医疗站 2/2（FIR）
收藏·7.62 BP 1/2
检视 2 项后续需求
```

- **持有量统计**：自动合并仓库（主仓库 + 任务仓库）与当前身上携带（局外背包/胸挂/安全箱，或局内 `MainPlayer` 装备与容器，支持 Scav 战局）。
- **状态着色**：需求已满足显示绿色，未满足的进行中任务显示暖琥珀色，藏身处下一级未建造/未升级显示青色，收藏配方未满足显示浅紫色，后续需求显示浅灰提示行。
- **可配置行数**：支持控制是否显示持有量、无需求时是否保留拥有量、收藏配方材料与工具开关，以及悬停最大显示行数。

### 2. 物品检视窗需求卡片（`InspectIntelBlock`）
打开物品检视窗口时，在原版描述位置嵌入与原版 UI 风格一致的深色「需求情报」卡片（左侧强调色条）：
- **物品描述并入**：原版物品描述显示在卡片顶部「物品描述」区（可在 F12 关闭「将物品描述并入情报卡片」恢复原版描述）。
- **统计条**：拥有 / FIR / 仓库 / 携带（局外为「身上」）/ 合计需求 以数字格展示。
- **窄长检视窗**：原版检视窗固定 670×500，描述区只剩几十像素需要滚动；现在默认以宽 460、高为屏幕 85% 的窄长窗口打开（仍可像原版一样拖动边缘自由缩放，拖拽上限放宽到屏幕大小），多出的高度全部用于描述与情报卡片（带配件的武器仍保持原版所需最小宽度；F12 可调宽度/高度比例或关闭「窄长检视窗」）。
- **属性与配件并入滚动区**：属性条、快速属性与配件格移入描述滚动区，预览图与按钮以下合并为一整块连续滚动的阅读区域（F12「属性与配件并入滚动区」可关闭）。
- **需求列表**：每行为 状态徽章 · 任务名（交易商、已交/已埋数量） · 进度条 · 数量 · FIR 标记，列对齐、隔行底色。
- **完整任务需求**：按 `[进行中]` → `[可接取]` → `[后续]` 列出全部未完成任务需求（含日常/周常 `[日常]` / `[周常]`、埋放/标记类 `LeaveItemAtLocation` / `PlaceBeacon` 合并进度 `（埋放）`、类别目标匹配及已交/已埋数量；可在 F12 关闭「包含埋放/标记类任务」）。
- **完整藏身处升级**：按 `[下一级]` → `[后续]` 列出所有未支付材料的藏身处设施与目标等级。
- **收藏配方材料**：自动追踪藏身处已收藏的制造配方（同步原版藏身处与 HideoutQoL 收藏操作），按收藏顺序以 `[配方]` / `[工具]` 列出所需材料与不消耗的工具（可在 F12 独立开关悬停、检视窗与工具提示）。

### 3. 服务端可达性分析（`RaidIntel.Server`）
- 提供 `GET /raidIntel/quests` 接口，结合玩家存档进度、前置任务链可达性与阵营/版本静态条件过滤，提前识别未来可达的任务材料需求（同时客户端保留纯客户端活跃任务回退）。

## 安装方法

1. **服务端模组**：将 `RaidIntel.Server/bin/Release/RaidIntel.Server/` 中的文件复制到 `<SPT根目录>/SPT_Runtime/user/mods/RaidIntel/`（若直接放在 `user/mods/` 结构下请对应调整到服务器模组目录 `E:/SPT/SPT_Runtime/user/mods/RaidIntel/`）。
2. **客户端插件**：将 `RaidIntel.Client/bin/Release/RaidIntel.Client.dll` 复制到 `<SPT根目录>/BepInEx/plugins/`。
3. 启动游戏后可在 **F12 → RaidIntel** 调整通用开关、悬停提示、检视窗及调试日志选项。

## 从源码编译

需要 `.NET SDK`（服务端目标 `net10.0`，客户端目标 `net472` 并指向本机 SPT 4.1.6 `Managed` / `BepInEx` 目录，默认配置于 [RaidIntel.Client.csproj](RaidIntel.Client/RaidIntel.Client.csproj)）：

```powershell
# 编译服务端
dotnet build -c Release RaidIntel.Server/RaidIntel.Server.csproj

# 编译客户端
dotnet build -c Release RaidIntel.Client/RaidIntel.Client.csproj
```

编译产物分别位于：
- `RaidIntel.Server/bin/Release/RaidIntel.Server/RaidIntel.Server.dll`
- `RaidIntel.Client/bin/Release/RaidIntel.Client.dll`

## 许可证

本项目以 **CC0 1.0 Universal** 协议放弃版权并奉献至公共领域，详见 [LICENSE](LICENSE)。
