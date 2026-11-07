# NOTICE

本仓库包含 [Project Fika](https://github.com/project-fika) 的修改版本，按
**CC BY-NC-SA 4.0**（Attribution-NonCommercial-ShareAlike 4.0 International）分发。

## 上游项目

| 项目 | 来源 | 许可 |
|---|---|---|
| Fika-Plugin | https://github.com/project-fika/Fika-Plugin （`dev-5.0` 分支） | CC BY-NC-SA 4.0 |
| Fika-Headless | https://github.com/project-fika/Fika-Headless （`dev-5.0` 分支） | CC BY-NC-SA 4.0 |
| Fika-Server-CSharp | https://github.com/project-fika/Fika-Server-CSharp （`dev-5.0` 分支） | CC BY-NC-SA 4.0 |
| Fika-Headless-Manager | https://github.com/project-fika/Fika-Headless-Manager （v1.5.1） | MIT © 2026 Lacyway |

上游版权归 Project Fika 及其贡献者所有。本仓库不是官方发布，与 Project Fika 团队无关。

## 本仓库的修改

以上游 `dev-5.0` 为基线，改动集中在构建配置与少量源码：

**Fika-Plugin**
- `Fika.Core/Fika.Core.csproj`：替换全部程序集引用（BepInEx 6 / Il2CppInterop / SPTushonka）
- `Build.props`：`TargetFramework` 改为 `net10.0`
- `Fika.Core/Main/Components/CutscenePortStubs.cs`：新增（补齐上游未提交的两个类型，空实现）

**Fika-Headless**
- `Fika.Headless/Fika.Headless.csproj`：引用路径参数化

**Fika-Server**
- `FikaServer/OnLoad/FikaPostLoad.cs`：无头档案加载不再受 `headless.profiles.amount` 门槛限制

**新增（非上游）**
- `FikaSetup/`：一键安装器
- `FikaHeadlessManager/`：无头启动器适配分支（跳过 `.delta` 补丁、自动生成配置、`--show-config`）
- `PORTING-REPORT.md`：移植报告

完整差异说明见 `PORTING-REPORT.md`。

## 未包含的第三方内容

本仓库**不包含**以下内容，使用时需自行准备：

- Escape from Tarkov 的游戏文件（`EscapeFromTarkov.exe`、`GameAssembly.dll`、`*_Data` 等）
- SPT / SPTushonka 的服务端与客户端程序集（`SPTarkov.*`、`SPTushonka.*`、`spt-*.dll`）
- BepInDX / BepInEx / Il2CppInterop 的二进制
- 上游 `References/` 目录下的引用程序集

`Fika.Core/Bundles/Files/masterbundle.bundle` 为 Fika 自身的资源文件，随上游许可一并分发。

## 使用限制

CC BY-NC-SA 4.0 明确**禁止商业使用**。分发本仓库或其衍生作品时，必须保留本 NOTICE 与许可声明，并以相同许可发布。
