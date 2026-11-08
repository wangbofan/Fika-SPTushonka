![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)

# Fika for SPT 5.0 / SPTushonka（EFT 1.3.0.1-48558）

把 [Project Fika](https://github.com/project-fika) 的 **dev-5.0**（Fika 3.0.0）分支移植到 **SPT 5.0.0 + SPTushonka + BepInDX 6（BepInEx 6 / IL2CPP）** 环境，并附带一键安装器与无头客户端管理器。

> 上游 Fika 目前只发布到 2.4.6（SPT 4.1.6 / EFT 0.16.9），dev-5.0 分支未发布、CI 也是红的。本仓库把这套源码改到能在 48558 客户端上编译、加载、联机，并把踩到的坑都写进 `PORTING-REPORT.md`。

---

## 支持范围

| 项 | 版本 |
|---|---|
| 客户端 | EFT `1.3.0.1-48558` |
| 服务端 | SPT `5.0.0`（SPTushonka，`SPTarkov.*` 程序集） |
| 客户端注入 | BepInDX `6.0.0-be.35+`（BepInEx 6，IL2CPP） |
| 目标框架 | .NET 10 |
| 上游基线 | `project-fika/Fika-Plugin@dev-5.0`、`Fika-Headless@dev-5.0`、`Fika-Server-CSharp@dev-5.0` |
| 版本号 | Fika 3.0.0 / FikaServer 2.4.1 / Fika.Headless 1.5.3 |

**换个客户端 build 就不要用**：Fika 依赖 IL2CPP interop 的具体签名，build 号不同必然出问题。

## 仓库内容

```
Fika-Plugin/            客户端插件（Fika.Core）
Fika-Headless/          无头客户端插件（Fika.Headless）
Fika-Server/            服务端 mod（FikaServer）
FikaSetup/              一键安装器（WinForms，单文件）
FikaHeadlessManager/    无头启动器（上游 1.5.1 的适配分支）
Directory.Build.props   构建路径配置（GameRoot 由命令行传入）
PORTING-REPORT.md  完整移植报告：改了什么、为什么、怎么验证的
```

## 相对上游的改动

### Fika.Core
- 引用全部换掉：`PackageReference BepInEx.Core 5.*` + `UnityEngine.Modules` + `References\spt-*.dll`(4.1.3) + `hollowed.dll` → `BepInEx` 6 核心 + `BepInEx\interop\*.dll`（Il2CppInterop）+ `BepInEx\plugins\sptushonka\SPTushonka.*.dll`
- `TargetFramework`：`netstandard2.1` → `net10.0`（宿主就是 .NET 10，且要引 `BepInEx.Unity.IL2CPP`）
- 补 `Main/Components/CutscenePortStubs.cs`：上游只调用 `FikaCutsceneManager` / `FikaFinalMissionDirector` 却没提交实现，编译会挂；这里给空实现，**代价是过场/终局剧情多人同步关闭**（见「已知限制」）

### Fika.Headless
- 只改了构建路径（原工程把 `D:/SPT-5.0.0/DEV` 写死），用 `-p:GameRoot=` 传参

### Fika-Server
- `OnLoad/FikaPostLoad.cs`：上游是 `if (amount > 0) OnPostLoadAsync()`，而 `OnPostLoadAsync()` 第一件事就是加载已存在的无头档案 —— 于是 `amount` 一旦与实际档案数不一致（尤其为 0），**所有无头客户端都会被判 `Invalid headless client`**，客户端却显示已连接，极难排查。这里改为无条件加载，`amount` 只负责“自动补建”。

### FikaSetup（安装器）
- 主客户端只装 `Fika.Core.dll` + `FikaServer`；`Fika.Headless.dll` 只进「无头副本」，避免日常启动直接进无头模式（该插件 `Load()` 里无条件 `IsHeadless = true`）
- 无头副本：大目录 junction、`BepInEx\plugins` 独立；副本已存在时走“更新模式”而不是报错
- 无头账户管理：列表 / 新建（走 `/fika/api/createheadlessprofile`）/ 删除（自动备份 + 校正 amount）/ 写 `HeadlessConfig.json` / 同步 amount
- 服务端启动一律**可见窗口**（早期版本用 `CreateNoWindow` 后台占 6969，导致用户手动起服务端报 `AddressAlreadyInUse`）
- 一键体检：自动修 `amount`、自动重写失效的 `HeadlessConfig.json`、校验插件与 Manager

### FikaHeadlessManager
- 检测到 SPTushonka（`BepInEx\plugins\sptushonka`）或 `--no-patch` 时跳过 `.delta` 客户端补丁（SPTushonka 是运行时内存补丁，再叠文件补丁会坏事）
- 启动参数补 `MatchingVersion`
- `HeadlessConfig.json` 缺失时，按 `MemberCategory=128` 的最新档案自动生成
- 新增 `--show-config` / `--no-patch`
- 控制台 UTF-8，中文提示不乱码

## 构建

需要 **.NET SDK 10**，以及一份已装好 SPT 5.0 + BepInDX 6 的游戏目录（用于引用程序集，见 `Directory.Build.props`）。

```powershell
# 1) 客户端插件（会顺带把 Fika.Core.dll 拷到 build\plugins）
dotnet build Fika-Plugin/Fika.Core/Fika.Core.csproj -c Release -p:GameRoot="E:\TKF"

# 2) 无头插件（依赖上一步的 build\plugins\Fika.Core.dll）
dotnet build Fika-Headless/Fika.Headless/Fika.Headless.csproj -c Release -p:GameRoot="E:\TKF"

# 3) 服务端 mod
dotnet build Fika-Server/FikaServer/FikaServer.csproj -c Release -p:GameRoot="E:\TKF"
```

产物：`Fika-Plugin/Fika.Core/bin/Release/Fika.Core.dll`、`Fika-Headless/Fika.Headless/bin/Release/Fika.Headless.dll`、`Fika-Server/FikaServer/bin/Release/FikaServer.dll`。

安装器本身：

```powershell
dotnet publish FikaSetup/FikaSetup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

> `FikaSetup` 需要一个 `payload.zip`（内含三个 DLL + 服务端资源 + Manager）作为嵌入资源；仓库不含二进制，请自行用上面三步的产物打包。

## 安装（普通用户）

到 [Releases](../../releases) 下载，三种任选：

| 文件 | 适合谁 | 说明 |
|---|---|---|
| `Fika-3.0.0-SPT5.0.zip` | 手动安装 / 只要本体 | 2.6 MB，`Fika.Core.dll` + `FikaServer` + 可选无头端，附 `安装说明.txt` |
| `FikaSetup.exe` | 大多数人 | 49.7 MB 单文件，一键装客户端 + 服务端 + 无头副本 + 无头档案 + 防火墙 + 一键体检 |
| `FikaHeadlessManager.exe` | 只更新无头启动器 | 2.2 MB |

**用安装器（推荐）**

1. 关闭游戏与 `SPT.Server.exe`
2. 打开安装器 → 选游戏目录 → 勾「创建独立无头客户端副本」→ 点「一键全部安装」
3. 启动顺序：`SPT.Server` → 游戏（主机）/ `run-headless.cmd`（无头）

**用免安装包（手动）**

1. 关闭游戏与 `SPT.Server.exe`
2. 把 `BepInEx/` 覆盖到游戏根目录（每台机器都要）
3. 把 `SPT_Runtime/` 覆盖到游戏根目录（只有开服那台需要）
4. 跑一次 `SPT.Server.exe` 生成 `fika.jsonc`，然后进游戏
5. `可选-无头端/` 里的东西**只放进无头副本**，不要放进日常客户端

联机端口：TCP `6969`（SPT 服务端）、UDP `25565`（Fika）。跨机游玩时另一台机器填**主机局域网 IP**（多网卡机器别选成热点网卡）。


## 常见问题

**Q：无头主机需要单独的 Fika 文件吗？**
不需要。Fika 只有一套，客户端插件 + 服务端 mod；「无头副本」只是多加载一个 `Fika.Headless.dll` 并在 `-nographics` 下运行。

**Q：客机看不到主机的房间 / 看不到主机昵称**
1. 客机的启动器服务器地址必须是**主机的** `IP:6969`（不是自己的 `127.0.0.1`，那样连的是自己的服务器，列表自然是空的）
2. 主机要**真的开好战局并在图里**（普通客户端点 Host Raid 后自己会进图；无头主机由无头进图）。停在菜单/藏身处时列表里没有战局
3. 主机防火墙放行 TCP `6969`、UDP `25565`；跨网段还要通 VPN 或端口转发

**Q：点 Join 是灰的，提示「无法加入其它地图的战局」**
这是 Fika 的 `F_UI_CannotJoinRaidOtherMap`：客机当前**选的地图和主机开战局时的地图不一致**。
在 Matchmaker 界面先把地图切成和主机相同的（时间段也要相同，否则会提示 other time），Join 按钮才会亮。
> 代码位置：`Fika.Core/UI/Custom/MatchMakerUIScript.cs`，`RaidSettings.LocationId != entry.Location` 就禁用按钮。

**Q：无头一直 `Game exited, restarting...`**
见上面「已知限制」下面的说明，99% 是无头副本里装了 Fika.Headless 黑名单里的 mod（画质/UI/内存清理类）。用安装器的「一键体检 / 修复」会直接列出文件。

**Q：能用 EFT 自带的组队 / 邀请吗？**
不行。Fika 的加入入口是 Matchmaker 界面右侧的战局列表（Join），不是好友组队。主机没开局前进组也进不去。

## 已知限制

- **过场/终局剧情同步被关闭**：上游缺文件，本仓库用空实现占位，编译通过但没有功能
- **未做长时间联机压测**：单局 2 人（主机 + 玩家）已验证可正常进出、结算、存档
- `headless.profiles.amount` 已打补丁不再致命，但安装器仍会在启动前校正它
- 无头控制台的 `VideoDecode` / `SoftMask` / `Quest. Can't find condition` 红字是无渲染模式下的固有噪音，可通过 `BepInEx.cfg` 的 `UnityLogListening=false` 关掉（默认保持开启）

## 许可与致谢

- 本仓库是 [Project Fika](https://github.com/project-fika) 的衍生作品，沿用 **CC BY-NC-SA 4.0**：署名、非商业、相同方式共享。详见 `LICENSE`、`NOTICE.md`
- `FikaHeadlessManager` 来自 [Lacyway/Fika-Headless-Manager](https://github.com/project-fika/Fika-Headless-Manager)（MIT）
- SPT / SPTushonka / Escape from Tarkov 的代码与资源**不包含**在本仓库内，需要自行准备
- 仅供个人学习与单机/好友联机使用，请勿用于商业用途

---

## English summary

A community port of **Project Fika dev-5.0** to **SPT 5.0.0 / SPTushonka / EFT 1.3.0.1-48558 / BepInDX 6 (BepInEx 6, IL2CPP)**, plus a one-click installer and an adapted headless manager. The upstream dev-5.0 branch does not compile against this client (stale 4.1.3 references, missing classes); here it builds, loads and plays. Differences are documented in `PORTING-REPORT.md`. Licensed CC BY-NC-SA 4.0, non-commercial only. No SPT/EFT binaries are redistributed.
