# Fika 三件套 → SPTushonka 5.0 移植报告

生成时间：2026-11-07 19:00
目标环境：`E:\TKF`（EFT 1.3.0.1-48558 / SPTushonka 5.0.0）
上游源码：`project-fika` 的 `dev-5.0` 分支（Plugin / Headless / Server-CSharp）

---

## 0. 一句话结论

**编译层面成功**：三个工程全部 0 错误编过，产物在本目录 `dist/`。
**运行层面已验证到「加载 + 握手」**：客户端插件、服务端 mod 都实际装进 `E:\TKF` 跑过一遍，`/fika/client/check/mods` 返回成功、WebSocket 通道建立（详见 4.5 节）。
**未验证**：进战局的玩家/AI/伤害/撤离同步；过场与终局剧情同步被占位类关闭。

| 工程 | 上游状态 | 移植动作 | 编译 | 运行 |
|---|---|---|---|---|
| Fika.Core（客户端插件） | dev-5.0 | 换引用 + 改 TFM + 补 2 个缺失类 | ✅ 0 error / 40 warn，2.1 MB | ✅ 加载成功、握手成功 |
| Fika.Headless（无头端） | dev-5.0 | 只改 `GameDir` 参数 | ✅ 0 error / 0 warn，126 KB | ⚪ 未测 |
| Fika.Server（服务端） | dev-5.0 | 加 NuGet 源直接还原 | ✅ 0 error / 374 warn | ✅ 加载 9 个 patch、路由生效 |

---

## 1. 目标环境剖析

### 1.1 目录结构

```
E:\TKF
├─ EscapeFromTarkov.exe        游戏本体（1.3.0.1-48558）
├─ GameAssembly.dll            IL2CPP 编译产物（~124 MB）
├─ UnityPlayer.dll             Unity 2022.3.43f2
├─ winhttp.dll / version.dll   doorstop 注入链
├─ BepInEx/
│  ├─ core/                    BepInXDX 6.0.0-be.35（BepInEx 6 bleed edge 的魔改分支）
│  ├─ interop/                 172 个 IL2CPP 生成的托管程序集（Assembly-CSharp.dll 等）
│  └─ plugins/
│     ├─ sptushonka/           SPTushonka 客户端模块（Common/Core/Custom/Debugging/Reflection/SinglePlayer）
│     └─ ConfigurationManager/
├─ SPT_Runtime/                SPT 服务端（5.0.0）
│  ├─ SPT.Server.exe           net10.0 + ASP.NET Core 10
│  ├─ SPTarkov.Server.Core.dll 等（服务端保持官方 SPTarkov.* 命名）
│  ├─ SPT_Data/database/       bots / quests / locations / locales / traders ...
│  └─ user/{profiles,mods,logs,sptRegistry}
├─ dotnet/                     自带的 .NET 10.0.12 运行时（无 SDK）
└─ tushonka.log                SPTushonka 的 GameAssembly 内存补丁日志
```

### 1.2 技术栈

| 层 | 实现 |
|---|---|
| 引擎 | Unity 2022.3.43f2，IL2CPP |
| 运行时 | .NET 10.0.12 |
| 客户端注入 | BepInXDX `6.0.0-be.35`（BepInEx 6 BE 分支）+ Il2CppInterop 1.6.8 |
| SPT 客户端模块 | 程序集被改名成 `SPTushonka.*`（5.0.0.0），命名空间同步改为 `SPTushonka.Common` / `.Reflection` / `.Custom` / `.SinglePlayer` |
| SPT 服务端 | `SPTarkov.*` 5.0.0.0，net10.0 + ASP.NET Core 10 |
| 启动链 | `SPT.Launcher` 拉起 → doorstop → SPTushonka PrePatch 打 GameAssembly（导出表 38 个 gated export 恢复）→ BepInEx 加载插件 |

### 1.3 与"官方 SPT"的关键差异（决定移植成败的地方）

1. **客户端程序集改名**：官方是 `spt-common.dll`（assembly name `spt-common`, 4.1.3），这里是 `SPTushonka.Common.dll`（`SPTushonka.Common`, 5.0.0）。
2. **BepInEx 版本换代**：官方 4.1 生态是 BepInEx 5（`BepInEx.dll`），这里是 BepInEx 6（`BepInEx.Core.dll` + `BepInEx.Unity.IL2CPP.dll`），插件基类从 `BaseUnityPlugin` 变成 `BasePlugin`。
3. **EA 侧的类改名**：EFT 1.3 的 `Assembly-CSharp` 在上游工程里叫 `hollowed.dll`，说明被改名/混淆过，补丁目标方法名随时可能漂移。
4. **服务端不受影响**：服务端仍是 `SPTarkov.*`，所以 Fika 服务端只要 NuGet 包能还原就能编。

---

## 2. 三个工程的实际状态

| 维度 | Fika.Core | Fika.Headless | Fika.Server |
|---|---|---|---|
| 代码是否已迁到 SPTushonka 命名空间 | 是（628 个 .cs 里有 128 个 `using SPTushonka.*`，无残留 `SPTarkov.*`） | 是（151 个里有 140 个） | 不需要（服务端仍是 `SPTarkov.*`） |
| 工程文件是否已迁 | **否**：csproj 还引用 `References\spt-*.dll`（4.1.3），且用 `PackageReference BepInEx.Core 5.*` | **是**：csproj 用 `$(BepInExCore)` / `$(Interop)` / `$(SptPlugins)` 变量，只是默认值写死在作者机器 `D:/SPT-5.0.0/DEV` | 是：直接引用 `SPTushonka.*` NuGet 包 |
| 目标框架 | `netstandard2.1`（BepInEx 5 时代） | `net10.0` | `net10.0` |
| 源码完整性 | **缺类**：`FikaCutsceneManager` / `FikaFinalMissionDirector` 只有调用、没有实现 | 完整 | 完整 |
| 上游 CI | 9/21→11/06 `.NET Test` 全 failure | 同左 | — |

---

## 3. 移植方案

### 阶段 0：版本锚定（已完成）

- 读 `Launcher.log` 拿到 `server version 5.0.0`、`BepInEx/LogOutput.log` 拿到 `build: 1.3.0.1-48558`、`BepInEx.Unity.IL2CPP` 6.0.0.0、`Il2CppInterop` 1.6.8。
- 结论：目标 = SPTushonka 5.0 客户端 + SPTarkov 5.0 服务端 + BepInEx 6 + .NET 10。

### 阶段 1：Fika.Core（客户端插件）

改动文件：`port/Fika-Plugin/Fika.Core/Fika.Core.csproj`、`port/Fika-Plugin/Build.props`、新增 `CutscenePortStubs.cs`。

1. **删掉两处 PackageReference**：`BepInEx.Core 5.*`、`UnityEngine.Modules 2022.3.43`（NuGet 上的 BepInEx 5 与本地 BepInEx 6 不兼容）。
2. **删掉旧引用组**：`References\spt-*.dll`（4.1.3）、`References\hollowed.dll`。
3. **换成指向本机程序集的引用组**：

```xml
<Reference Include="$(BepInExCore)/BepInEx.Core.dll" />
<Reference Include="$(BepInExCore)/BepInEx.Unity.IL2CPP.dll" />
<Reference Include="$(BepInExCore)/BepInEx.Unity.Common.dll" />
<Reference Include="$(BepInExCore)/0Harmony.dll" />
<Reference Include="$(BepInExCore)/SemanticVersioning.dll" />
<Reference Include="$(BepInExCore)/Il2CppInterop.*.dll" />
<Reference Include="$(Interop)/*.dll" Exclude="$(Interop)/__Generated.dll" />
<Reference Include="$(SptPlugins)/SPTushonka.*.dll" />
```

4. **TFM 改 `netstandard2.1` → `net10.0`**（因为要引用 `BepInEx.Unity.IL2CPP`，它是 net6+；游戏宿主本身是 .NET 10）。
5. **去掉 `RestorePackagesWithLockFile`** 和 PostBuild 批处理（避免依赖作者的打包脚本）。
6. **补 2 个缺失类**（占位实现）：

```csharp
public sealed class FikaCutsceneManager {
    public static FikaCutsceneManager Instance { get; private set; }
    public bool ReplayingHostStart => false;
    public bool IsActive => false;
    public static void Create(GameWorld gameWorld) { Instance = new(); }
    public void Tick() { } public void Dispose() { Instance = null; }
    public void LocalViewComplete(object processId) { }
    public void HostStartedCutscene(string cutsceneId, CutsceneProcess process, bool fixedTime, bool forceMode, float initTime) { }
    public void CutsceneEnded(object processId, bool ended) { }
    public void ApplyStartPacket(CutsceneStartPacket p) { }
    public void ReceiveSkipPacket(CutsceneSkipPacket p) { }
    public void ReceiveEndPacket(CutsceneEndPacket p) { }
    public void ReceiveSkipIntention(CutsceneSkipIntentionPacket p) { }
    public void ReceiveViewComplete(CutsceneViewCompletePacket p) { }
}
public sealed class FikaFinalMissionDirector {
    public static FikaFinalMissionDirector Instance { get; private set; }
    public static void Create(GameWorld gameWorld) { Instance = new(); }
    public static void StripProfile(EFT.Profile profile, int netId) { }
    public void Tick() { }
    public void OnPacketReceived(FinalMissionPacket p) { }
}
```

### 阶段 2：Fika.Headless（无头端）

上游工程本身已经是"变量化"的，只要把默认 `GameDir = D:/SPT-5.0.0/DEV` 覆盖成本机即可：

```bat
dotnet build Fika.Headless.csproj -c Release ^
  -p:GameDir=E:\TKF ^
  -p:FikaPlugins=<Fika.Core.dll 所在目录>
```

注意 `FikaPlugins` 必须指向编好的 `Fika.Core.dll`，否则 `CS0234: 命名空间 Fika 中不存在 Core`。

### 阶段 3：Fika.Server（服务端）

服务端已经是 `SPTushonka.*` NuGet 包引用，唯一问题是默认 NuGet 源拉不到，需要显式加源（本目录已带 `nuget.config`）：

```xml
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
<add key="sptushonka" value="https://nuget.azure.cn/v3/index.json" />
```

包版本：`SPTushonka.Common/DI/Reflection/Server.Core/Server.Web 5.0.0-pre.202609141239`（该源上还有 `202611061606` 的更新版本）。

---

## 4. 实测编译结果

构建工具：官方 .NET SDK 10.0.403（本次下载解压到 `tools/dotnet`，不入系统 PATH）。

| 工程 | 命令要点 | 结果 | 产物 |
|---|---|---|---|
| Fika.Core | `dotnet build -c Release` | ✅ 0 error / 40 warning / 15 s | `bin/Release/Fika.Core.dll`（2.1 MB） |
| Fika.Headless | `-p:GameDir=E:\TKF -p:FikaPlugins=...` | ✅ 0 error / 0 warning | `bin/Release/Fika.Headless.dll`（126 KB） |
| Fika.Server | `dotnet restore` + `build` | ✅ 0 error / 374 warning | `bin/Release/FikaServer.dll`（+ FikaShared.dll、assets） |

首轮编译的 19 个错误全部是同一类：

```
error CS0103: 当前上下文中不存在名称"FikaCutsceneManager"
error CS0103: 当前上下文中不存在名称"FikaFinalMissionDirector"
```

即上游缺文件，不是环境问题——补占位类后一次通过。

---

## 4.5 实机验证结果（19:03–19:12，真跑过）

以下是在 `E:\TKF` 上实际安装并启动游戏得到的结果，不是推断。

**第 1 次：只装客户端（Fika.Core.dll）**

```
[Error : BepInXDX] Error loading [Fika.Core 3.0.0]: System.Exception: Http response status code: NotFound
   at Fika.Core.FikaPlugin.GetNatPunchServerConfig()
   at Fika.Core.FikaPlugin.Load()
```

→ **插件加载失败**。原因：`Load()` 阶段就去请求服务端 Fika 接口，没有服务端 mod 就 404 直接抛异常。这条依赖关系上游没写在显眼位置，是移植时容易踩的坑。

**第 2 次：补装服务端 mod（`user/mods/FikaServer/`）后重启**

客户端：

```
[Info : BepInXDX] Loading [Fika.Core 3.0.0]
[Info :RequestHandler] [REQUEST]: /fika/client/check/mods
[Info :RequestHandler] [REQUEST SUCCESSFUL] /fika/client/check/mods
[Info : Fika.Core] Locales are ready!
[Info : Fika.Core] Cached local IPs: 0.0.0.0, ::, 192.168.31.140
```

服务端：

```
[Fika Server] Loading patch: SendFriendRequestOverride ...（共 9 个 override 补丁）
[Fika Server] Overriding SPT configuration
[客户端请求] /fika/client/check/mods
[WebSocket 请求] /fika/notification/
[Fika Notification Manager] User is <你的档案ID>
```

→ **插件加载成功、HTTP 握手成功、WebSocket 通道建立成功**。全程只有 1 条错误日志：
`[Error :Il2CppInterop] Assembly Fika.Core.dll is not registered in il2cpp`（BE6 + Il2CppInterop 的已知噪音，不影响加载）。

**验证边界**：只做到「进主菜单 + 客户端/服务端握手」。**没有进战局**，玩家同步、AI 同步、伤害结算、撤离结算全部未验证。

**当前机器状态**：`BepInEx/plugins/Fika/Fika.Core.dll` + `SPT_Runtime/user/mods/FikaServer/` 已安装且可用；`Fika.Headless.dll` 未安装。
原插件备份在 `E:\TKF_backup_20261107_1902\BepInEx_plugins`。

## 5. 依赖闭合验证

用 Mono.Cecil 写了个小程序（`tools/refcheck`）把产物引用的程序集逐个拿到本机目录里找：

- **Fika.Core.dll**：70 个引用，除 28 个 `System.*`（由 .NET 运行时提供）外全部命中：
  `BepInEx.Core 6.0.0.0`、`BepInEx.Unity.IL2CPP 6.0.0.0`、`Il2CppInterop.Runtime 1.6.8.0`、`Assembly-CSharp`、`SPTushonka.Common/Custom/Reflection/SinglePlayer 5.0.0.0`、`0Harmony 2.16.1.0`。
- **Fika.Headless.dll**：41 个引用，全部命中（含 `Fika.Core 3.0.0.0`）。
- **FikaServer.dll**：39 个引用，`SPTarkov.Common/DI/Reflection/Server.Core/Server.Web 5.0.0.0` 全部命中；`FikaShared` 与 ASP.NET Core 程序集由宿主提供。

结论：**静态依赖是闭合的**，不存在"少了某个 dll 一定加载失败"的问题。

另外做了一次**补丁目标抽查**：从源码里抽出 31 个 `AccessTools.Method/PropertyGetter(typeof(X), nameof(...))` 目标，拿 172 个 interop 程序集（20583 个类型）做匹配：

```
types indexed: 20583, targets: 31
bad=0   （Skill.IsInRaid 是 attribute getter，按 get_ 处理后命中；CoopGame 是 Fika 自己的类）
```

31/31 都能在 1.3.0.1-48558 的 interop 里找到对应类型/成员。这不能保证运行期一定不炸（泛型实例、内联方法另说），但至少说明**补丁目标本身没被这次版本更新删掉**。

---

## 6. 需要调整/仍在风险中的模块

| 模块 | 状态 | 说明 |
|---|---|---|
| `Fika.Core/Main/Components/CutscenePortStubs.cs` | 🔴 占位 | 上游缺失的过场/终局同步实现，当前为 no-op；剧情过场不会在多人间同步 |
| `Fika.Core/Main/Patches/Cutscenes/*` | 🔴 受影响 | 5 个补丁仍会挂到 `CutscenesServerController` / `CutscenesClientController`，但管理器是空壳，等于只做了"主机允许开始、客户端回放"的拦截 |
| `Fika.Core/Main/Patches/*`（其余） | 🟡 未验证 | Harmony 目标方法在 48558 是否还存在，只有进游戏才报 `NullReferenceException / TargetMethod not found` |
| 网络层 `Networking/*` | 🟡 未验证 | 依赖 `NetworkPackets`、`BitPacking` 等 EFT 内部包结构，1.3 若有字段变化会出现序列化错位 |
| `Fika.Headless` 的 `HeadlessGame` / `TransitController` | 🟡 未验证 | 上游 11-06 还在"Fix up headless on latest client" |
| `FikaServer` 的 DI/路由 | 🟡 版本漂移 | 编的是 `20260914` 的 pre 包，本机服务端是 5.0.0 正式版，API 若在 9 月后有改动会在启动时抛 `MissingMethodException` |
| FikaWebApp（后台面板） | ⚪ 未编译 | 独立 Blazor 工程，本次只编了 Server；缺它只是没有 Web 面板 |

---

## 7. 验证步骤（按成本从低到高）

**V1 加载验证（30 秒）**
1. 备份 `E:\TKF\BepInEx\plugins`（或直接记住要删的目录）。
2. 把 `dist\BepInEx\plugins\Fika\` 整个拷进 `E:\TKF\BepInEx\plugins\`。
3. 启动游戏，看 `E:\TKF\BepInEx\LogOutput.log`：
   - 期望：`Loading [Fika.Core 3.0.0]`，且没有 `MissingMethodException` / `TypeLoadException`。
   - 失败：删掉该目录即回滚（不影响存档）。

**V2 服务端验证（1 分钟）**
1. 把 `dist\SPT_Runtime\user\mods\FikaServer\` 拷到 `E:\TKF\SPT_Runtime\user\mods\`。
2. 启 `SPT.Server.exe`，看 `SPT_Runtime\user\logs\spt\*.log`：
   - 期望：Fika 的 DI 注册与路由注册无异常。
   - 失败：删目录回滚。

**V3 单机进图（5 分钟）**
- 主菜单出现 Fika 的 Host/Join 界面 → 自己开一局，确认不崩、能正常撤离。

**V4 双开联机（15 分钟）**
- 两个 profile 各开一个客户端（或一台 + 一台 headless），Host 建房、Join 进房。
- 重点看：玩家同步、AI 同步、子弹/伤害、撤离结算。

**V5 无头端（30 分钟+）**
- `Fika.Headless.dll` 放同一目录，加启动参数（见上游 Headless FAQ），配 `HeadlessConfig.json`，确认 headless profile 能进战局并接管 AI。

任何一步失败，回滚方式都是"删掉对应目录"，不改动 `profiles` 与 `SPT_Data`。

---

## 8. 产物位置

```
port/
├─ dist/                                  ← 可直接拷进 E:\TKF 的安装包（3.4 MB）
│  ├─ BepInEx/plugins/Fika/{Fika.Core.dll, Fika.Headless.dll}
│  └─ SPT_Runtime/user/mods/FikaServer/{FikaServer.dll, FikaShared.dll, assets, ...}
├─ Fika-Plugin/      改好的客户端工程（csproj 已换引用 + net10.0 + 占位类）
├─ Fika-Headless/    原样 + 构建参数
├─ Fika-Server/      原样 + nuget.config
├─ Directory.Build.props   指向 E:\TKF 的路径配置
└─ build/plugins/Fika.Core.dll   headless 编译时的依赖副本
```

重编命令（在 `port/` 下）：

```bat
set DOTNET=..\tools\dotnet\dotnet.exe
%DOTNET% build Fika-Plugin\Fika.Core\Fika.Core.csproj -c Release
copy /Y Fika-Plugin\Fika.Core\bin\Release\Fika.Core.dll build\plugins\
%DOTNET% build Fika-Headless\Fika.Headless\Fika.Headless.csproj -c Release -p:GameDir=E:\TKF -p:FikaPlugins=%CD%\build\plugins
%DOTNET% build Fika-Server\FikaServer\FikaServer.csproj -c Release
```

---

## 9. 无头端（Fika.Headless）实测

安装 `Fika.Headless.dll` 后用 headless 档案启动，日志如下：

```
[Info : BepInXDX] Loading [Fika.Headless 1.5.3]
[Info :Fika.Headless PatchManager] Enabled 157 patches
[Info :Fika.Headless] Fika.Headless loaded! OS: Windows 11  (10.0.26200) 64bit
[Info :Fika.Headless] Profile verified
[Info :Fika.Headless] Plugins verified successfully
[Info :Fika.Headless] Plugin validation completed
[Info :Fika.HeadlessWebSocket] Attempting to connect to wss://127.0.0.1:6969/fika/headless/client...
[Message:Fika.HeadlessWebSocket] Connected to HeadlessWebSocket
```

服务端：

```
[WebSocket 请求] /fika/headless/client
[Fika Headless Client] User is 01c13a8838e44338b00c186f
[WS] Starting read loop for websocket reference 20261107112015887
```

**关键结论：无头端插件加载正常（157 个补丁全挂上），并能以 headless 身份连上服务端 WebSocket。**

踩到的两个坑：

1. **普通档案启动会被服务端拒绝**：
   `[Fika Headless Client] Invalid headless client <id> tried to authenticate!`
   原因：`HeadlessHelper.IsHeadlessClient()` 只认 `MemberCategory == UnitTest(128)` 的档案（`HeadlessProfileService` 在启动时筛出来的列表）。
2. 服务端可以**自动建档**：`assets/configs/fika.jsonc` 里
   ```jsonc
   "headless": { "profiles": { "amount": 0 }, "scripts": { "generate": true } }
   ```
   把 `amount` 改成 1 → 重启服务端会自动创建 `headless_<id>` 档案，并在 `assets/scripts/` 生成启动脚本（本次因为手工放了一个 UnitTest 档案、count 已满足，所以没触发建档）。

**未验证**：真正开一局让 headless 接管 AI。需要正常客户端在 Fika 界面里选 “Use headless” 建房，这步必须有 UI 操作，命令行做不到。

**测试后已还原**：删除 `Fika.Headless.dll`、删除测试档案、`headless.profiles.amount` 改回 0。

---

## 10. 一键安装器（FikaSetup.exe）

`installer/FikaSetup` → 发布产物 `installer/FikaSetup/bin/Release/net10.0-windows/win-x64/publish/FikaSetup.exe`（单文件 1.9 MB，依赖系统 .NET 10 桌面运行时，本机已有 10.0.9）。

功能：
- 自动探测游戏目录（`E:\TKF`、`D:\TKF`… 或手动浏览），校验 `EscapeFromTarkov.exe` + `BepInEx` + `SPT_Runtime`
- 显示 `EFT 1.3.0.1-48558-06f265a4 / SPT 5.0.0` 与当前 Fika 安装状态
- 一键安装（可选同时装无头端）、卸载、刷新状态
- 安装前自动把旧的 `BepInEx\plugins\Fika` 与 `user\mods\FikaServer` 备份到 `_fika_installer_backup\<时间戳>`
- 安装/卸载前检测 `EscapeFromTarkov.exe`、`SPT.Server.exe` 是否在运行
- 载荷（1.7 MB zip）内嵌在 exe 里，单文件即可分发

命令行模式（便于脚本化，已实测）：

```bat
FikaSetup.exe --status  E:\TKF
FikaSetup.exe --install E:\TKF [--headless]
FikaSetup.exe --uninstall E:\TKF
```

实测结果：临时目录 53 个文件安装成功 → 状态识别“已安装（含无头端）”→ 卸载后只剩 `EscapeFromTarkov.exe`；真实目录状态识别为 `EFT 1.3.0.1-48558-06f265a4 / SPT 5.0.0   Fika：已安装`。


### 10.1 与官方 Fika-Installer 的功能对齐（已逐项对照源码）

官方安装器源码 `project-fika/Fika-Installer`，主菜单只有三项：Install/Update Fika、Uninstall Fika、Advanced Options（Install/Update Fika Headless、Install Fika in current folder、Add firewall rules）。无头建档走 `POST /fika/api/createheadlessprofile`（Bearer apiKey），随后把 `assets/scripts/<id>/HeadlessConfig.json` 拷到安装目录。

| 能力 | 官方 | 本安装器 | 说明 |
|---|---|---|---|
| 安装 Fika（客户端+服务端） | ✅ 拉 GitHub 最新 release | ✅ 内置移植版载荷 | 官方版不支持 SPTushonka，必须内置 |
| 更新 Fika | ✅ 覆盖+备份 | ✅ 覆盖+备份到 `_fika_installer_backup` | |
| 卸载 | ✅ | ✅ | |
| 安装无头端 | ✅ Advanced | ✅ 勾选框 | |
| 创建无头档案 | ✅ 可选已有/新建 | ✅ 新建 N 个（同一 API） | 实测创建成功 |
| 复制 HeadlessConfig.json | ✅ | ✅ 复制到游戏根目录 | |
| 防火墙 6969/TCP + 25565/UDP | ✅ netsh | ✅ netsh（非管理员会请求提权） | |
| 安装 SPT 运行库依赖 | ✅ | ❌ | 本机自带 .NET 10，未做 |
| 把 SPT 复制到新目录当无头机 | ✅ HardCopy / Symlink | ❌ | 40 GB 级操作，未实现 |
| 命令行 | ✅ CLI | ✅ `--install/--uninstall/--status/--firewall` | |

实测（`--install E://TKF --profiles 1`）：

```
→ 覆盖安装 / 更新（旧文件会先备份）
已备份 55 个文件 → E://TKF//_fika_installer_backup//20261107_192711
✓ 文件安装完成：52 个
=== 创建 1 个无头档案 ===
  · 现有无头档案：0 个
  ✓ 已创建 1 个无头档案，amount 已更新为 1
    - 6aef0b159e852335c40e17f4
  · HeadlessConfig.json 位于：...\assets\scripts\6aef0b159e852335c40e17f4\HeadlessConfig.json
```

验证结果：档案 `headless_6aef0b159e852335c40e17f3`（MemberCategory = 128 / UnitTest，Level 1）、`assets/scripts/<id>/HeadlessConfig.json`（114 B）、`fika.jsonc` 的 `headless.profiles.amount` = 1。


### 10.2 补漏：安装器体积与 FikaHeadlessManager.exe

**体积差异不是缺文件**：官方 `Fika-Installer.csproj` 用 `SelfContained=true` + `PublishSingleFile` + `EnableCompressionInSingleFile`，等于把 .NET 10 运行时一起打包（官方 50,751 KB）。本安装器最初是 framework-dependent（1.9 MB，依赖系统运行时）。现已改为自包含压缩单文件：**52,103,928 B ≈ 50,882 KB**，与官方同一量级。

**FikaHeadlessManager.exe 来源**（官方源码）：
- `Globals.cs:31` → `_fikaHeadlessManagerRelease = new("Fika.Headless.Manager", "https://api.github.com/repos/project-fika/Fika-Headless-Manager/releases/latest")`
- `FikaInstaller.cs:94` → `Path.Combine(installDir, "FikaHeadlessManager.exe")`
- 它**不在** `Fika.Headless.x.y.z.zip` 里（那个包只有 `Fika.Headless.dll` + `LICENSE-HEADLESS.md`），是从 Headless-Manager 仓库单独下载的。

已补：载荷新增 `FikaHeadlessManager.exe`（2.3 MB，v1.5.1）+ `FikaHeadlessManager-LICENSE.md`，勾选"安装无头端"时落到游戏根目录，卸载时一并删除。

⚠️ **风险**：该 Manager 发布说明写的是 "Updated for SPT 4.1.3"，其工作方式是给客户端文件打 `.delta` 二进制补丁（SharpHDiffPatch）。在 EFT 1.3.0.1-48558 上大概率打不上或打错。本次只做到"装到位"，**没有运行它**（运行会改游戏文件）。


### 10.3 自研适配版 FikaHeadlessManager

上游 v1.5.1 发布说明写的是 "Updated for SPT 4.1.3"，但它本质是个独立 .NET 控制台程序（net10.0），不引用 EFT/SPT 程序集，所以适配成本很低。改动两处：

1. **跳过客户端打补丁**：它会在 `SPT_Data/Launcher/Patches/*` 上跑 `.delta` 二进制补丁（本机只有 `SPT-core/.../global-metadata.dat.delta`）。SPTushonka 走的是运行时内存补丁，再叠一层文件补丁风险很大 → 检测到 `BepInEx/plugins/sptushonka` 就跳过，并支持 `--no-patch` 强制跳过。
2. **启动参数补齐 `MatchingVersion`**：和官方启动器一致，避免版本校验问题。

实测（`FikaHeadlessManager.exe` 在 `E://TKF` 运行）：

```
Skipping client patching (SPTushonka build or --no-patch). The SPTushonka preloader patches at runtime.
Verifying bundles...
The server published no bundles.
Press 'g' to start with graphics or wait 3 seconds...
Starting headless client without graphics and no extra logging.
```

客户端侧：

```
[Info : BepInXDX] Loading [Fika.Core 3.0.0]
[Info : BepInXDX] Loading [Fika.Headless 1.5.3]
[Info :Fika.Headless] Profile verified
[Message:Fika.HeadlessWebSocket] Connected to HeadlessWebSocket
```

无游戏界面：管理器默认 3 秒后以 `-nographics -batchmode` 启动，只有 BepInDX 的控制台窗口，没有渲染。它按 `HeadlessConfig.json` 里的 `ProfileId/BackendUrl` 直接拉起客户端，战局结束后自动重启，省掉启动器和点选流程 —— 这就是它"快速启动无头客户端"的作用。

注意：`Fika.Headless.dll` 只能放在**专职无头的那份客户端**里，装进日常客户端会导致正常启动也进无头模式。


### 10.4 一键整合（当前版本功能全貌）

一个 `FikaSetup.exe`（52 MB，自包含单文件）做完所有事：

| 步骤 | 行为 |
|---|---|
| 1. 主客户端 | 装 `Fika.Core.dll`、`FikaServer`（无头插件**不**进日常客户端） |
| 2. 无头档案 | 起服务端 → `/fika/api/createheadlessprofile` → 生成 `headless_<id>` |
| 3. 无头副本 | 新建目录，用 junction 链接大目录 + 拷贝启动文件，`BepInEx\plugins` 独立（`sptushonka` 链接 + `Fika` 实体） |
| 4. 副本内容 | `Fika.Core.dll` + `Fika.Headless.dll` + `FikaHeadlessManager.exe` + `HeadlessConfig.json` + `run-headless.cmd` |
| 5. 配置自愈 | `headless.profiles.amount` 与实际无头档案数不一致时自动修正 |
| 6. 防火墙 | netsh 放行 TCP 6969 / UDP 25565 |
| 7. 卸载 | 只删 `plugins\Fika`、`user\mods\FikaServer`、根目录三个文件，不动存档 |

命令行：`--install <dir> [--headless] [--profiles N] [--copy-to <dir>] [--full-copy] [--no-firewall]`

**实测（20:01–20:05）**：
```
=== 创建无头客户端副本 ===
创建无头副本 → E://TKF_Headless（junction 链接）
  · 已自动生成 HeadlessConfig.json（ProfileId=6aef0b159e852335c40e17f4）
✓ 无头副本就绪。启动方式：在副本目录运行 FikaHeadlessManager.exe（脚本见 run-headless.cmd）
```
从副本运行 `FikaHeadlessManager.exe`：
```
[Info :Fika.Headless] Profile verified
[Message:Fika.HeadlessWebSocket] Connected to HeadlessWebSocket
```
服务端：`[Fika Headless Client] User is 6aef0b159e852335c40e17f4`（无 Invalid 报错）。

**踩到的坑（已修）**：`fika.jsonc` 的 `headless.profiles.amount` 若为 0，即使档案存在，服务端也会丢 `Invalid headless client ... tried to authenticate!`；安装器现在每次安装都会把 amount 校正为实际无头档案数。


### 10.5 无头账户管理（新增）

安装器新增「无头账户管理…」按钮（独立窗口 `HeadlessProfilesForm`），以及一组 CLI：

| 操作 | CLI | 说明 |
|---|---|---|
| 列出档案 | `--headless-list <dir>` | 只列 MemberCategory=128 的档案：ID / 昵称 / 等级 / 创建时间 / 大小 / 有无 HeadlessConfig |
| 新建 | `--headless-add <dir> --count N` | 临时启服务端 → `/fika/api/createheadlessprofile` → 关服务端；会先检测端口占用 |
| 写配置 | `--headless-config <dir> --id <id> [--copy-to <副本目录>]` | 把 HeadlessConfig.json 指到该档案 |
| 删除 | `--headless-delete <dir> --id <id> [--copy-to <副本目录>]` | 备份 profile → 删档案 → 删 `assets/scripts/<id>` → 删指向它的 HeadlessConfig（主目录 + 副本）→ 校正 amount |
| 同步 amount | `--sync-amount <dir>` | 把 `headless.profiles.amount` 校正为实际档案数 |

GUI 里额外显示两行状态：
```
共 1 个无头档案；fika.jsonc 里 headless.profiles.amount = 1（一致）
主目录 HeadlessConfig → 无    副本 → 6aef0b159e852335c40e17f4
```
并提供「同时写入无头副本」勾选、双击行写配置、打开档案目录。

**实测（沙盒 + 真实目录）**：

1. 沙盒（2 个 profile，只 1 个是 128；amount 故意写成 5）
   `--headless-list` → 只列出 128 那个，提示 amount=5；
   `--sync-amount` → `已修正 headless.profiles.amount：5 → 1`；
   `--headless-delete` → 备份到 `_fika_installer_backup\headless_profiles\<ts>`、删 scripts 目录、删根目录配置、amount 1→0；普通档案未受影响。
2. 真实目录 `E://TKF`
   `--headless-add --count 1` → 新增 `6aef15c462a2160f5c1476b5`，amount 1→2；
   `--headless-config --copy-to E://TKF_Headless` → 副本配置写到新档案；
   `--headless-delete ... --copy-to E://TKF_Headless` → 档案+脚本+两份配置全部清掉，amount 2→1；
   最后把副本配置重新指回原有档案 `6aef0b15…`。

### 10.6 服务端改为可见窗口（修“后台偷偷开 6969”）

**问题**：建档/安装时需要用 Fika API 创建无头档案，必须临时启动 `SPT.Server.exe`。旧实现用 `CreateNoWindow=true` + 重定向输出，进程完全隐藏——结果就是端口 6969 被占、界面上看不到任何窗口，用户再手动启动服务端只会得到 `AddressAlreadyInUse (10048)`。

**现在的做法**：

1. 用 `start` 命令在桌面上开**独立控制台窗口**（标题先叫 `SPT 服务端`，服务端启动后会自己改成 `SPT 5.0.0 - …`），不再隐藏。
2. 主界面新增「启动服务端（可见窗口）」「停止服务端」两个按钮，端口状态每 3 秒轮询：`端口 6969：空闲（服务端未运行）` / `端口 6969：已占用（SPT.Server.exe / PID 8462）`。
3. 停止服务端按**端口占用**结束进程，不再依赖启动时拿到的 Process 对象；启动前先 `StopIfOurs`：6969 上是本目录的 SPT.Server 就先停掉，是别的程序则明确报出来、不误杀。
4. 建档的 `finally` 里统一 `StopServer` + `WaitPortFree`，确保不留后台进程。

CLI：`--start-server <dir>` / `--stop-server <dir>` / `--port <dir>`。

**实测（20:30）**：

```
· 已在新窗口启动 SPT.Server.exe（桌面任务栏可见，窗口标题：SPT 服务端）
  等待端口就绪…
  ✓ 端口 6969 已开始监听：已占用（SPT.Server.exe / PID 8460）
```

窗口枚举同样命中：`WindowsTerminal  MainWindowTitle = "SPT 5.0.0 - 血色边缘测试版"`，即桌面上真实可见的窗口；停止后 `6969 已释放`、`SPT.Server 进程数 = 0`。

**附带修掉的一个坑**：勾选「安装无头端」但没勾「创建副本」时，`Fika.Headless.dll` 会进日常客户端——该插件在 `Load()` 第一段就无条件 `IsHeadless = true`，之后从这里启动游戏直接进无头模式（没有画面）。现在：勾选时会弹窗明确警告；未勾选时安装会**自动删掉**主目录残留的 `Fika.Headless.dll`，日志提示“恢复为普通客户端”。

### 10.7 run-headless.cmd 报 “Unable to find HeadlessConfig.json”

**现象**：在副本目录双击 `run-headless.cmd`，管理器直接退出：

```
Unable to find the configuration file 'HeadlessConfig.json'.
Make sure that you have configured the headless correctly!
请按任意键继续. . .
```

**原因**：`FikaHeadlessManager` 只认当前目录的 `HeadlessConfig.json`，没有就 `Fatal` 退出。而当时目录里既没有配置、也没有可用的无头档案（20:27 用「无头账户管理」把档案全删了，20:28 重装又重建了 `user\mods\FikaServer`，运行期生成的 `fika.jsonc` 也随之消失）。

**改动**（`manager/Program.cs`，自研 fork）：

1. **配置缺失自动生成**：扫描 `SPT_Runtime\user\profiles\*.json`，取 `characters.pmc.Info.MemberCategory == 128` 的最新一个，端口从 `fika.jsonc` 的 `server.SPT.http.port` 读取（缺省 6969），就地写出 `HeadlessConfig.json` 并继续启动；只有连一个无头档案都找不到时才报错退出。
2. 新增 `--show-config`：只打印解析到的 `ProfileId / BackendUrl / Config` 路径后退出，方便排查。
3. 控制台改 UTF-8 输出，中文提示不再乱码。

**实测**（20:41，副本目录，先把配置改名走）：

```
HeadlessConfig.json
缺失，已按无头档案自动生成（ProfileId=6aef1a487777cf043c5b01d1）。
ProfileId = 6aef1a487777cf043c5b01d1
BackendUrl = https://127.0.0.1:6969/
Config = E:\TKF_Headless\HeadlessConfig.json
```

**端到端复测**（20:44，先起服务端再跑 `run-headless.cmd`）：

```
Skipping client patching (SPTushonka build or --no-patch)...
Starting headless client without graphics and no extra logging.
[Info   :Fika.Headless] Plugin validation completed
[Info   :Fika.HeadlessWebSocket] Attempting to connect to wss://127.0.0.1:6969/fika/headless/client...
[Message:Fika.HeadlessWebSocket] Connected to HeadlessWebSocket
```

**顺带修的两处**：

- `CreateHeadlessCopy` 遇到目标目录非空不再报 “✗ 目标目录非空”，改为**更新模式**：只刷新 `Fika.Core.dll` / `Fika.Headless.dll` / `FikaHeadlessManager.exe` / `HeadlessConfig.json` / `run-headless.cmd`；若已有配置且其 `ProfileId` 对应档案仍存在，则保持不动。
- `run-headless.cmd` 末尾由 `pause` 改为 `if errorlevel 1 pause`，正常退出不再卡住。

### 10.8 真正的无头失败原因：`amount = 0` 导致服务端不加载任何无头档案

**现象**：客户端日志里有 `Connected to HeadlessWebSocket`，但服务端日志紧接着一行：

```
[Debug][FikaServer.WebSockets.HeadlessClientWebSocket] [Fika Headless Client] User is 6aef1a487777cf043c5b01d1
[Error][FikaServer.WebSockets.HeadlessClientWebSocket] [Fika Headless Client] Invalid headless client ... tried to authenticate!
```

**根因**（`FikaServer/OnLoad/FikaPostLoad.cs`）：

```csharp
if (fikaServerConfig.Headless.Profiles.Amount > 0)
{
    await headlessProfileService.OnPostLoadAsync(cancellationToken);
}
```

`HeadlessProfileService.OnPostLoadAsync()` 里第一件事就是 `LoadHeadlessProfiles()`。也就是说 **`amount` 决定“要不要加载已存在的无头档案”，而不只是“要不要自动补建”**。当时 `fika.jsonc` 的 `amount` 是 0（删档后同步成 0、后来重装也没纠正），于是服务端 `HeadlessProfiles` 恒为空 → 所有无头档案都被判 `Invalid headless client`，客户端却仍显示“已连接”。

日志上的分界也很清楚：`amount=0` 的启动没有 `Found N headless profiles` 这一行。

**修复**：直接改 Fika 服务端源码并重新编译 `FikaServer.dll`：

```csharp
// 始终加载：amount 只用于“自动补建”，不再决定“是否加载”
await headlessProfileService.OnPostLoadAsync(cancellationToken);
```

**验证**（故意把 `amount` 改成 0）：

```
[12:59:46] Found 1 headless profiles        ← amount=1 时
[13:02:59] Found 1 headless profiles        ← amount=0 且已打补丁，仍然加载
[13:04:25] [Fika Headless Client] User is 6aef1a48...   （无 Invalid 报错）
```

**配套加固**：

- 安装器的「启动服务端」现在会先 `SyncHeadlessAmount`，把 `amount` 校正成实际档案数再启动；
- 新增 `--headless-check <dir>` 自检：档案数 / amount / `Fika.Headless.dll` / Manager / `HeadlessConfig.json` 一次列清并给结论；
- 副本目录自检输出：

```
无头档案：1 个
  · 6aef1a487777cf043c5b01d1  headless_6aef1a487777cf043c5b01d0  Lv1
fika.jsonc：存在；headless.profiles.amount = 1
Fika.Headless.dll：已装；FikaHeadlessManager.exe：已装；根目录 HeadlessConfig.json：有
结论：可以启动无头客户端
```

**关于截图里的 Unity 红字**：`Video shaders not found`、`Hidden/UI/SoftMask: invalid pass index`、`Custom render path shader needs at least 1 passes`、`Quest. Can't find condition for target ...` 都是无头（无渲染）模式下的固有噪音，和 Fika 鉴权无关；判断是否成功要看**服务端日志**里的 `Found N headless profiles` 与有没有 `Invalid headless client`。

### 10.9 不需要手动改任何文件

把上面所有“需要手改”的点都做成了自动：

| 场景 | 以前 | 现在 |
|---|---|---|
| 删掉无头档案后 `amount` 变 0 | 要手改 `fika.jsonc` | 服务端已打补丁：`amount` 只控制“自动补建”，不再决定“是否加载” |
| `amount` 与实际档案数不一致 | 要手改 | 安装、启动服务端、建档、删档、`--doctor` 全部自动 `SyncHeadlessAmount` |
| 副本缺 `HeadlessConfig.json` | 手建 JSON | 副本创建/更新时自动写；Manager 启动时若仍缺失，会按 `MemberCategory=128` 的最新档案自动生成 |
| 不知道哪一步坏了 | 翻三个日志 | 主界面「一键体检 / 修复」按钮 / `--doctor <dir> [--copy-to <副本>]`，自动修 amount 与失效配置并给结论 |

`--doctor` 沙盒实测（故意把 amount 设 0、副本配置指向不存在的档案）：

```
=== 无头环境体检 ===
· 无头档案：1 个
    aaaaaaaaaaaaaaaaaaaaaaaa  h1  Lv1
  · 已修正 headless.profiles.amount：0 → 1
  ✓ 已自动修正 amount：0 → 1
  ✓ 已把副本 HeadlessConfig.json 指向现有无头档案（覆盖了缺失/失效的配置）
结论：环境就绪，直接启动服务端 + run-headless.cmd 即可
```

修正后跑 `run-headless.cmd`，服务端日志：

```
[13:08:26] [WebSocket 请求] /fika/headless/client
[13:08:26] [Fika Headless Client] User is 6aef1a487777cf043c5b01d1      ← 无 Invalid
```

### 10.10 无头控制台的 Unity 红字噪声（不影响运行）

无头客户端不渲染，Unity 仍会初始化部分渲染管线，于是固定刷这些：

```
[Error : Unity] This custom render path shader needs to have at least 1 passes.
[Error : Unity] Could not find material Hidden/VideoDecode...
[Error : Unity] Could not find video decode shader pass YCbCr_To_RGB1 ...
[Error : Unity] RenderingCommandBuffer: shader Hidden/UI/SoftMask: invalid pass index 0 in DrawMesh
[Warning: Unity] <b>Quest.</b> Can't find condition for target ... quest id: ...
```

全部与 Fika 无关，也不影响联机；官方 BepInEx 5 控制台看起来“干净”只是因为它不把 Unity 日志转发到控制台。

**决定：保持开启**（`UnityLogListening = true`，默认值）。理由：关掉后 Unity 侧真出问题时反而看不到。所以这些红字会一直在，判断标准改成“看关键词”而不是“看颜色”：

| 看到 | 含义 |
|---|---|
| `Connected to HeadlessWebSocket` | ✅ 客户端已连上服务端（这是唯一必须出现的一行） |
| `Plugin validation completed` / `Profile verified` | ✅ 插件自检通过 |
| `VideoDecode` / `SoftMask` / `custom render path` / `Quest. Can't find condition` | ⚪ 无头模式的固定噪声，忽略 |
| `Exception` / `NullReferenceException` / `MissingMethodException` | 🔴 真异常，需要看上下文 |
| 服务端 `Invalid headless client` | 🔴 鉴权失败（`amount`/档案问题，见 10.8） |

安装器不修改该项，保持 BepInEx 默认行为。

---

## 11. 法律/许可提醒

Fika 是 **CC BY-NC-SA 4.0**：可以自用、改、分发，但**不能商用**，分发修改版要署名并以相同协议发布。自己机器上跑没问题。
