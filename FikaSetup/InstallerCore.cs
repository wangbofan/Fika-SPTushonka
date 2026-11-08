using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FikaSetup;

public enum InstallState
{
    NotInstalled,
    Partial,
    Installed
}

public sealed record InstallResult(bool Ok, List<string> HeadlessProfileIds, string? Error);

public static class InstallerCore
{
    public const string CoreDirRel = @"BepInEx\plugins\Fika";
    public const string ServerDirRel = @"SPT_Runtime\user\mods\FikaServer";
    public const string BackupDirName = "_fika_installer_backup";
    public const string ConfigRel = @"SPT_Runtime\user\mods\FikaServer\assets\configs\fika.jsonc";
    public const string ScriptsRel = @"SPT_Runtime\user\mods\FikaServer\assets\scripts";

    public static readonly string[] Candidates =
    [
        @"E:\TKF", @"D:\TKF", @"C:\TKF", @"E:\SPT", @"D:\SPT", @"C:\SPT"
    ];

    // ---------- 环境探测 ----------

    public static string? DetectGameDir()
    {
        var probes = new List<string>();
        probes.AddRange(Candidates);
        probes.Add(AppContext.BaseDirectory);
        probes.Add(Directory.GetCurrentDirectory());

        foreach (var p in probes)
        {
            if (Validate(p).Count == 0) return Path.GetFullPath(p).TrimEnd('\\');
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 3 && dir is not null; i++)
        {
            if (Validate(dir.FullName).Count == 0) return dir.FullName.TrimEnd('\\');
            dir = dir.Parent;
        }

        return null;
    }

    public static List<string> Validate(string? dir)
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(dir)) { errs.Add("路径为空"); return errs; }
        if (!Directory.Exists(dir)) { errs.Add("目录不存在"); return errs; }
        if (!File.Exists(Path.Combine(dir, "EscapeFromTarkov.exe"))) errs.Add("找不到 EscapeFromTarkov.exe");
        if (!Directory.Exists(Path.Combine(dir, "BepInEx"))) errs.Add("找不到 BepInEx 目录");
        if (!Directory.Exists(Path.Combine(dir, "SPT_Runtime"))) errs.Add("找不到 SPT_Runtime 目录");
        return errs;
    }

    public static (string eft, string spt) ReadVersions(string gameDir)
    {
        string eft = "?", spt = "?";
        try
        {
            var log = Path.Combine(gameDir, @"BepInEx\LogOutput.log");
            if (File.Exists(log))
            {
                foreach (var line in File.ReadLines(log).Take(400))
                {
                    var m = Regex.Match(line, @"build:\s*([0-9][0-9A-Za-z\.\-]+)");
                    if (m.Success) { eft = m.Groups[1].Value; break; }
                }
            }
        }
        catch { }

        try
        {
            var launcher = Path.Combine(gameDir, @"SPT_Runtime\user\logs\Launcher.log");
            if (File.Exists(launcher))
            {
                foreach (var line in File.ReadLines(launcher).Take(80))
                {
                    var m = Regex.Match(line, @"server version:\s*([0-9][0-9A-Za-z\.\-]+)");
                    if (m.Success) { spt = m.Groups[1].Value; break; }
                }
            }
        }
        catch { }

        return (eft, spt);
    }

    public static InstallState GetState(string gameDir)
    {
        bool core = File.Exists(Path.Combine(gameDir, CoreDirRel, "Fika.Core.dll"));
        bool server = File.Exists(Path.Combine(gameDir, ServerDirRel, "FikaServer.dll"));
        if (core && server) return InstallState.Installed;
        if (core || server) return InstallState.Partial;
        return InstallState.NotInstalled;
    }

    public static bool IsHeadlessInstalled(string gameDir)
        => File.Exists(Path.Combine(gameDir, CoreDirRel, "Fika.Headless.dll"));

    public static string? FindHeadlessConfig(string gameDir)
    {
        var p = Path.Combine(gameDir, "HeadlessConfig.json");
        if (File.Exists(p)) return p;
        var scripts = Path.Combine(gameDir, ScriptsRel);
        if (!Directory.Exists(scripts)) return null;
        return Directory.GetFiles(scripts, "HeadlessConfig.json", SearchOption.AllDirectories).FirstOrDefault();
    }

    public static List<string> RunningProcesses()
    {
        var names = new[] { "EscapeFromTarkov", "SPT.Server" };
        var list = new List<string>();
        foreach (var n in names)
        {
            var procs = Process.GetProcessesByName(n);
            if (procs.Length > 0) list.Add(n + ".exe");
            foreach (var p in procs) p.Dispose();
        }
        return list;
    }

    public static string Describe(string gameDir)
    {
        var (eft, spt) = ReadVersions(gameDir);
        var state = GetState(gameDir);
        var sb = new StringBuilder();
        sb.Append($"EFT {eft} / SPT {spt}   ");
        sb.Append(state switch
        {
            InstallState.Installed => "Fika：已安装",
            InstallState.Partial => "Fika：不完整（客户端/服务端缺一个）",
            _ => "Fika：未安装"
        });
        if (IsHeadlessInstalled(gameDir)) sb.Append("（含无头端）");
        var rootCfg = Path.Combine(gameDir, "HeadlessConfig.json");
        if (File.Exists(Path.Combine(gameDir, "FikaHeadlessManager.exe"))) sb.Append("（Manager 已装）");
        if (File.Exists(rootCfg)) sb.Append("（根目录已有 HeadlessConfig）");
        var headlessCount = CountHeadlessProfiles(gameDir);
        if (headlessCount > 0) sb.Append($"（无头档案 {headlessCount} 个）");
        return sb.ToString();
    }

    // ---------- 安装 / 卸载 ----------

    private static void BackupExisting(string gameDir, Action<string> log)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var root = Path.Combine(gameDir, BackupDirName, stamp);
        var moved = 0;

        void CopyDir(string rel)
        {
            var src = Path.Combine(gameDir, rel);
            if (!Directory.Exists(src)) return;
            var dst = Path.Combine(root, rel);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var relf = Path.GetRelativePath(src, f);
                var target = Path.Combine(dst, relf);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, true);
                moved++;
            }
        }

        CopyDir(CoreDirRel);
        CopyDir(ServerDirRel);

        if (moved == 0)
        {
            try { Directory.Delete(root, true); } catch { }
            return;
        }

        log($"已备份 {moved} 个文件 → {root}");
    }

    private static int ExtractPayload(string gameDir, bool withHeadless, Action<string> log)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fika_setup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("payload.zip"))
            {
                if (stream is null) throw new InvalidOperationException("安装包损坏：找不到内置载荷");
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                zip.ExtractToDirectory(tmp, true);
            }

            int copied = 0;
            foreach (var src in Directory.GetFiles(tmp, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(tmp, src);

                if (!withHeadless &&
                    (rel.EndsWith("Fika.Headless.dll", StringComparison.OrdinalIgnoreCase) ||
                     rel.EndsWith("FikaHeadlessManager.exe", StringComparison.OrdinalIgnoreCase) ||
                     rel.EndsWith("FikaHeadlessManager-LICENSE.md", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var dst = Path.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, true);
                copied++;
            }

            return copied;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    public static InstallResult Install(string gameDir, bool withHeadless, int headlessProfiles, bool firewall, Action<string> log,
        string? headlessCopyTo = null, bool fullCopy = false)
    {
        var errs = Validate(gameDir);
        if (errs.Count > 0)
        {
            foreach (var e in errs) log("✗ " + e);
            return new(false, [], string.Join("；", errs));
        }

        var running = RunningProcesses();
        if (running.Count > 0)
        {
            log("✗ 请先关闭：" + string.Join("、", running));
            return new(false, [], "进程占用");
        }

        log($"目标目录：{gameDir}");
        var state = GetState(gameDir);
        log(state == InstallState.NotInstalled ? "→ 全新安装" : "→ 覆盖安装 / 更新（旧文件会先备份）");
        BackupExisting(gameDir, log);

        // 若同时创建无头副本，主客户端只装 Core（无头插件只进副本，避免日常启动变无头）
        var mainHeadless = withHeadless && string.IsNullOrWhiteSpace(headlessCopyTo);

        int copied;
        try
        {
            copied = ExtractPayload(gameDir, mainHeadless, log);
        }
        catch (Exception ex)
        {
            log("✗ 安装失败：" + ex.Message);
            return new(false, [], ex.Message);
        }

        log($"✓ 文件安装完成：{copied} 个");

        if (!mainHeadless)
        {
            var stale = Path.Combine(gameDir, CoreDirRel, "Fika.Headless.dll");
            if (File.Exists(stale))
            {
                try
                {
                    File.Delete(stale);
                    log("  · 已移除主目录里的 Fika.Headless.dll（恢复为普通客户端，不会再进无头模式）");
                }
                catch (Exception ex)
                {
                    log("  ! 移除 Fika.Headless.dll 失败：" + ex.Message);
                }
            }
        }
        log(mainHeadless
            ? "  · 客户端：Fika.Core.dll + Fika.Headless.dll + FikaHeadlessManager.exe"
            : "  · 客户端：Fika.Core.dll（无头插件只装进副本）");
        log("  · 服务端：user\\mods\\FikaServer");

        var ids = new List<string>();

        if (headlessProfiles > 0)
        {
            log("");
            log($"=== 创建 {headlessProfiles} 个无头档案 ===");
            try
            {
                ids = CreateHeadlessProfiles(gameDir, headlessProfiles, log);
                if (ids.Count > 0)
                {
                    var cfg = FindHeadlessConfig(gameDir);
                    if (cfg is not null)
                    {
                        log($"  · HeadlessConfig.json 位于：{cfg}");
                        if (withHeadless)
                        {
                            var dest = Path.Combine(gameDir, "HeadlessConfig.json");
                            File.Copy(cfg, dest, true);
                            log($"  · 已复制到游戏根目录：{dest}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log("✗ 创建无头档案失败：" + ex.Message);
            }
        }

        // amount 与实际无头档案数保持一致（含 0 的情况），避免服务端拒绝无头客户端
        try { SyncHeadlessAmount(gameDir, log); } catch { }

        if (firewall)
        {
            log("");
            log("=== 添加防火墙规则 ===");
            EnsureFirewall(log);
        }

        if (!string.IsNullOrWhiteSpace(headlessCopyTo))
        {
            log("");
            log("=== 创建无头客户端副本 ===");
            try
            {
                CreateHeadlessCopy(gameDir, headlessCopyTo!, fullCopy, log);
            }
            catch (Exception ex)
            {
                log("✗ 创建副本失败：" + ex.Message);
            }
        }

        log("");
        log("下一步：启动 SPT.Server，再用 SPT.Launcher 进游戏；主菜单 Fika 面板点 Host Raid 建房。");
        return new(true, ids, null);
    }

    public static bool Uninstall(string gameDir, Action<string> log)
    {
        var running = RunningProcesses();
        if (running.Count > 0)
        {
            log("✗ 请先关闭：" + string.Join("、", running));
            return false;
        }

        int removed = 0;
        foreach (var rel in new[] { CoreDirRel, ServerDirRel })
        {
            var dir = Path.Combine(gameDir, rel);
            if (!Directory.Exists(dir)) continue;
            try
            {
                Directory.Delete(dir, true);
                removed++;
                log($"已删除 {rel}");
            }
            catch (Exception ex)
            {
                log($"✗ 删除 {rel} 失败：{ex.Message}");
            }
        }

        foreach (var f in new[] { "HeadlessConfig.json", "FikaHeadlessManager.exe", "FikaHeadlessManager-LICENSE.md" })
        {
            var fp = Path.Combine(gameDir, f);
            if (!File.Exists(fp)) continue;
            try { File.Delete(fp); log("已删除 " + f); } catch { }
        }

        if (removed == 0) log("没有发现已安装的 Fika 文件");
        else log("✓ 卸载完成（备份目录保留，可手动删除 " + BackupDirName + "）");
        return removed > 0;
    }

    // ---------- 防火墙 ----------

    public static bool EnsureFirewall(Action<string> log)
    {
        var rules = new (string name, string proto, int port)[]
        {
            ("Fika - SPT Server (TCP 6969)", "TCP", 6969),
            ("Fika - Coop (UDP 25565)", "UDP", 25565)
        };

        bool allOk = true;
        foreach (var (name, proto, port) in rules)
        {
            var exists = RunNetsh($"advfirewall firewall show rule name=\"{name}\"");
            if (exists.exit == 0)
            {
                log($"  · 规则已存在：{name}");
                continue;
            }

            var add = RunNetsh($"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={proto} localport={port}");
            if (add.exit == 0)
            {
                log($"  ✓ 已添加：{name}");
            }
            else
            {
                allOk = false;
                log($"  ✗ 添加失败：{name}（需要管理员权限，请用“以管理员身份运行”重开本程序）");
            }
        }

        return allOk;
    }

    public static bool IsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static bool RelaunchElevated(string args)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch { return false; }
    }

    private static (int exit, string output) RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return (p.HasExited ? p.ExitCode : -1, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }


    // ---------- 无头客户端副本（junction 快速 / 完整复制） ----------

    public static bool CreateHeadlessCopy(string sourceGameDir, string targetDir, bool fullCopy, Action<string> log)
    {
        if (Validate(sourceGameDir).Count > 0) { log("✗ 源目录无效"); return false; }
        if (string.Equals(Path.GetFullPath(sourceGameDir).TrimEnd('\\'), Path.GetFullPath(targetDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            log("✗ 目标目录不能和源目录相同");
            return false;
        }

        var existing = Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any();
        if (existing)
        {
            log($"目标目录已存在 → {targetDir}");
            log("  · 按“更新无头文件”处理：只刷新 Fika.Core / Fika.Headless / Manager / HeadlessConfig / run-headless.cmd，不重新复制游戏本体");
            RefreshHeadlessFiles(sourceGameDir, targetDir, log);
            log("✓ 无头副本已更新。启动方式：在副本目录运行 run-headless.cmd");
            return true;
        }

        Directory.CreateDirectory(targetDir);
        log($"创建无头副本 → {targetDir}（{(fullCopy ? "完整复制" : "junction 链接")}）");

        foreach (var entry in Directory.GetFileSystemEntries(sourceGameDir))
        {
            var name = Path.GetFileName(entry);
            var dest = Path.Combine(targetDir, name);

            if (name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("_fika_installer_backup", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("Logs", StringComparison.OrdinalIgnoreCase)) continue;

            if (Directory.Exists(entry))
            {
                if (fullCopy) CopyDirRecursive(entry, dest);
                else Junction(entry, dest, log);
            }
            else
            {
                File.Copy(entry, dest, true);
            }
        }

        // BepInEx 需要单独处理：plugins 要放无头专用的插件
        var be = Path.Combine(targetDir, "BepInEx");
        Directory.CreateDirectory(Path.Combine(be, "plugins"));

        foreach (var sub in Directory.GetDirectories(Path.Combine(sourceGameDir, "BepInEx")))
        {
            var n = Path.GetFileName(sub);
            var d = Path.Combine(be, n);
            if (n.Equals("plugins", StringComparison.OrdinalIgnoreCase)) continue;
            if (fullCopy) CopyDirRecursive(sub, d);
            else Junction(sub, d, log);
        }

        var srcPlugins = Path.Combine(sourceGameDir, @"BepInEx\plugins");
        if (Directory.Exists(srcPlugins))
        {
            foreach (var sub in Directory.GetDirectories(srcPlugins))
            {
                var n = Path.GetFileName(sub);
                if (n.Equals("Fika", StringComparison.OrdinalIgnoreCase)) continue;
                var d = Path.Combine(be, "plugins", n);
                if (fullCopy) CopyDirRecursive(sub, d);
                else Junction(sub, d, log);
            }
            foreach (var f in Directory.GetFiles(srcPlugins))
            {
                File.Copy(f, Path.Combine(be, "plugins", Path.GetFileName(f)), true);
            }
        }

        RefreshHeadlessFiles(sourceGameDir, targetDir, log);
        log("✓ 无头副本就绪。启动方式：在副本目录运行 FikaHeadlessManager.exe（脚本见 run-headless.cmd）");
        return true;
    }


    /// <summary>把无头相关文件刷新到一个已存在的副本目录里（安装与更新共用）。</summary>
    private static void RefreshHeadlessFiles(string sourceGameDir, string targetDir, Action<string> log)
    {
        var fikaDir = Path.Combine(targetDir, @"BepInEx\plugins\Fika");
        Directory.CreateDirectory(fikaDir);
        ExtractFromPayload("BepInEx/plugins/Fika/Fika.Core.dll", Path.Combine(fikaDir, "Fika.Core.dll"), log);
        ExtractFromPayload("BepInEx/plugins/Fika/Fika.Headless.dll", Path.Combine(fikaDir, "Fika.Headless.dll"), log);
        ExtractFromPayload("FikaHeadlessManager.exe", Path.Combine(targetDir, "FikaHeadlessManager.exe"), log);

        var cfgTarget = Path.Combine(targetDir, "HeadlessConfig.json");

        // 已有配置且指向的档案还在 → 保留，不要乱改
        var keepExisting = false;
        if (File.Exists(cfgTarget))
        {
            var current = ReadHeadlessConfigTarget(targetDir);
            if (!string.IsNullOrEmpty(current) && FindHeadlessProfileId(sourceGameDir) is not null)
            {
                var profilesDir = Path.Combine(sourceGameDir, @"SPT_Runtime\user\profiles");
                keepExisting = current is not null && File.Exists(Path.Combine(profilesDir, current + ".json"));
            }
        }

        if (keepExisting)
        {
            log("  · 已有 HeadlessConfig.json 且对应档案存在，保持不变");
        }
        else
        {
            var pid = FindHeadlessProfileId(sourceGameDir);
            if (pid is null)
            {
                log("  ! 没有找到无头档案 —— 请先到「无头账户管理」新建 1 个");
            }
            else
            {
                var json = "{\n  \"ProfileId\": \"" + pid + "\",\n  \"BackendUrl\": \"https://127.0.0.1:6969\",\n  \"StartMinimized\": true,\n  \"ExtraLogging\": false\n}\n";
                File.WriteAllText(cfgTarget, json);
                log($"  · 已写入 HeadlessConfig.json（ProfileId={pid}）");
            }
        }

        var runner = Path.Combine(targetDir, "run-headless.cmd");
        File.WriteAllText(runner, "@echo off\r\ncd /d \"%~dp0\"\r\nFikaHeadlessManager.exe\r\nif errorlevel 1 pause\r\n");
    }

    private static void ExtractFromPayload(string entry, string dest, Action<string> log)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
            if (stream is null) throw new InvalidOperationException("载荷缺失");
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var e = zip.GetEntry(entry) ?? throw new InvalidOperationException("载荷里找不到 " + entry);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
        }
        catch (Exception ex)
        {
            log("  ✗ 解出 " + entry + " 失败：" + ex.Message);
        }
    }

    private static void Junction(string target, string link, Action<string> log)
    {
        var r = Run("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"");
        if (r.exit != 0) log($"  ! junction 失败（改为复制）：{Path.GetFileName(link)}");
        if (r.exit != 0) CopyDirRecursive(target, link);
    }

    private static void CopyDirRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDirRecursive(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static (int exit, string output) Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            return (p.HasExited ? p.ExitCode : -1, o);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    // ---------- 无头档案（走 Fika API，和官方安装器同一套流程） ----------

    public static List<string> CreateHeadlessProfiles(string gameDir, int count, Action<string> log)
    {
        var serverExe = Path.Combine(gameDir, @"SPT_Runtime\SPT.Server.exe");
        if (!File.Exists(serverExe)) throw new InvalidOperationException("找不到 SPT_Runtime\\SPT.Server.exe");

        var configPath = Path.Combine(gameDir, ConfigRel);

        // 配置不存在 → 先启一次服务端让它生成
        if (!File.Exists(configPath))
        {
            log("  · fika.jsonc 不存在，先启动一次服务端生成配置（会在新窗口打开）…");
            StopIfOurs(gameDir, 6969, log);
            LaunchServerWindow(serverExe, log);
            WaitFor(() => File.Exists(configPath), TimeSpan.FromMinutes(2), 500);
            Thread.Sleep(1500);
            StopServer(gameDir, log);
            if (!File.Exists(configPath)) throw new InvalidOperationException("服务端未生成 fika.jsonc");
            log("  · 配置已生成");
        }

        var root = JsonNode.Parse(File.ReadAllText(configPath), nodeOptions: null,
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidOperationException("fika.jsonc 解析失败");

        var port = root["server"]?["SPT"]?["http"]?["port"]?.GetValue<int>() ?? 6969;
        var apiKey = root["server"]?["apiKey"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("fika.jsonc 里没有 apiKey");

        // 端口若被本目录的 SPT.Server 占着，先停掉（否则新进程只会报 AddressAlreadyInUse）
        StopIfOurs(gameDir, port, log);
        if (ListenersOn(port).Count > 0)
        {
            throw new InvalidOperationException($"端口 {port} 被其它程序占用：{DescribePort(port)}，请先关闭它");
        }

        var existing = CountHeadlessProfiles(gameDir);
        log($"  · 现有无头档案：{existing} 个");
        SetHeadlessAmount(root, configPath, existing);

        var ids = new List<string>();
        log($"  · 正在新窗口启动服务端（端口 {port}），建档完成后会自动关闭该窗口…");

        LaunchServerWindow(serverExe, log);
        try
        {
            using var http = BuildHttp(port, apiKey);
            if (!WaitHeartbeat(http, TimeSpan.FromMinutes(2), log))
            {
                throw new InvalidOperationException("服务端没有在 2 分钟内就绪（请看新开的服务端窗口里的报错）");
            }

            for (int i = 0; i < count; i++)
            {
                var id = CreateOne(http, log);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
        }
        finally
        {
            // 按端口结束，确保不留下任何后台进程
            StopServer(gameDir, log);
        }

        var root2 = JsonNode.Parse(File.ReadAllText(configPath), nodeOptions: null,
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        SetHeadlessAmount(root2, configPath, existing + ids.Count);

        log($"  ✓ 已创建 {ids.Count} 个无头档案，amount 已更新为 {existing + ids.Count}");
        foreach (var id in ids) log("    - " + id);
        return ids;
    }

    /// <summary>判断监听端口的进程是不是本目录的 SPT.Server（路径拿不到时按进程名兜底）。</summary>
    private static bool IsSameServer(string expectedExe, string? actualPath, string processName)
    {
        if (actualPath is null) return processName.Equals("SPT.Server", StringComparison.OrdinalIgnoreCase);
        try
        {
            var a = Path.GetFullPath(Path.GetDirectoryName(actualPath)!).TrimEnd('\\', '/');
            var b = Path.GetFullPath(Path.GetDirectoryName(expectedExe)!).TrimEnd('\\', '/');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return processName.Equals("SPT.Server", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void StopIfOurs(string gameDir, int port, Action<string> log)
    {
        var pids = ListenersOn(port);
        if (pids.Count == 0) return;

        var exe = Path.Combine(gameDir, @"SPT_Runtime\SPT.Server.exe");
        foreach (var pid in pids)
        {
            string? path = null;
            try
            {
                using var p = Process.GetProcessById(pid);
                try { path = p.MainModule?.FileName; } catch { }
            }
            catch { continue; }

            if (IsSameServer(exe, path, "SPT.Server") is false) continue;

            log($"  · 端口 {port} 上有一个本目录的 SPT.Server（PID {pid}）在运行，先结束它…");
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(true);
                p.WaitForExit(8000);
            }
            catch (Exception ex) { log("    ! 结束失败：" + ex.Message); }
        }

        if (pids.Count > 0) WaitPortFree(port, TimeSpan.FromSeconds(10));
    }

    /// <summary>用 cmd start 在桌面上开一个独立的服务端控制台窗口（标题固定，便于辨认）。</summary>
    private static void LaunchServerWindow(string exe, Action<string> log)
    {
        var dir = Path.GetDirectoryName(exe)!;
        var psi = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c start \"SPT 服务端\" /D \"{dir}\" \"{exe}\"",
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            Process.Start(psi)?.Dispose();
            log("  · 已在新窗口启动 SPT.Server.exe（桌面任务栏可见，窗口标题：SPT 服务端）");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("启动服务端失败：" + ex.Message);
        }
    }

    // ---------- 端口 / 服务端进程可见性 ----------

    /// <summary>返回监听指定 TCP 端口的进程 ID（通过 netstat，避免语言差异，用 远端=0.0.0.0:0 判定 LISTEN）。</summary>
    public static List<int> ListenersOn(int port)
    {
        var pids = new List<int>();
        try
        {
            var output = Run("netstat.exe", "-ano").output;
            foreach (var raw in output.Split('\n'))
            {
                var parts = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (!parts[0].StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!parts[1].EndsWith(":" + port, StringComparison.Ordinal)) continue;
                var remote = parts[2];
                if (remote != "0.0.0.0:0" && remote != "[::]:0" && remote != "*:*") continue;
                if (int.TryParse(parts[^1], out var pid) && pid > 0) pids.Add(pid);
            }
        }
        catch { }
        return pids.Distinct().ToList();
    }

    public static string DescribePort(int port)
    {
        var pids = ListenersOn(port);
        if (pids.Count == 0) return "空闲";
        var names = new List<string>();
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                names.Add($"{p.ProcessName}.exe / PID {pid}");
            }
            catch { names.Add($"PID {pid}"); }
        }
        return "已占用（" + string.Join("、", names) + "）";
    }

    /// <summary>紧凑写法，给界面状态栏用：SPT.Server (PID 1234)。</summary>
    public static string DescribePortShort(int port)
    {
        var pids = ListenersOn(port);
        if (pids.Count == 0) return "空闲";
        try
        {
            using var p = Process.GetProcessById(pids[0]);
            return $"{p.ProcessName} (PID {pids[0]})";
        }
        catch { return $"PID {pids[0]}"; }
    }

    public static bool WaitPortFree(int port, TimeSpan timeout)
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            if (ListenersOn(port).Count == 0) return true;
            Thread.Sleep(500);
        }
        return ListenersOn(port).Count == 0;
    }

    public static int ServerPort(string gameDir)
    {
        var cfg = Path.Combine(gameDir, ConfigRel);
        if (!File.Exists(cfg)) return 6969;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(cfg), nodeOptions: null,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return root?["server"]?["SPT"]?["http"]?["port"]?.GetValue<int>() ?? 6969;
        }
        catch { return 6969; }
    }

    /// <summary>在桌面上打开一个可见的服务端窗口。已在运行则直接返回。</summary>
    public static bool StartServerVisible(string gameDir, Action<string> log)
    {
        var exe = Path.Combine(gameDir, @"SPT_Runtime\SPT.Server.exe");
        if (!File.Exists(exe)) { log("✗ 找不到 SPT_Runtime\\SPT.Server.exe"); return false; }

        var port = ServerPort(gameDir);
        var pids = ListenersOn(port);
        if (pids.Count > 0)
        {
            log($"· 端口 {port} 已被占用：{DescribePort(port)}，不再重复启动");
            return false;
        }

        try
        {
            // Fika 原版在 amount 与实际档案数不一致时会拒绝无头客户端，启动前先校正
            SyncHeadlessAmount(gameDir, log);

            LaunchServerWindow(exe, log);
            log("  等待端口就绪…");
            var ok = WaitFor(() => ListenersOn(port).Count > 0, TimeSpan.FromSeconds(40), 500);
            log(ok
                ? $"  ✓ 端口 {port} 已开始监听：{DescribePort(port)}"
                : $"  ! 40 秒内没监听 {port}，请看新开的服务端窗口里的报错");
            log("  关闭那个窗口或点“停止服务端”即可结束。");
            return ok;
        }
        catch (Exception ex)
        {
            log("✗ 启动失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>停止占用端口的 SPT.Server（只杀本游戏目录下的 SPT.Server.exe，避免误杀别的程序）。</summary>

    /// <summary>
    /// 一键体检 / 自动修复无头环境：校正 amount、补写副本 HeadlessConfig、校验插件与 Manager。
    /// </summary>
    public static bool HeadlessDoctor(string gameDir, string? copyDir, Action<string> log)
    {
        log("=== 无头环境体检 ===");
        var ok = true;

        var profiles = ListHeadlessProfiles(gameDir);
        log($"· 无头档案：{profiles.Count} 个");
        foreach (var p in profiles) log($"    {p.Id}  {p.Nickname}  Lv{p.Level}");

        var cfg = Path.Combine(gameDir, ConfigRel);
        if (!File.Exists(cfg))
        {
            log("  ! fika.jsonc 不存在（服务端首次启动会自动生成），先启动一次服务端即可");
        }
        else
        {
            var before = ReadHeadlessAmount(gameDir);
            var after = SyncHeadlessAmount(gameDir, log);
            if (before != after) log($"  ✓ 已自动修正 amount：{before} → {after}");
        }

        if (profiles.Count == 0)
        {
            log("  ✗ 没有无头档案 —— 在「无头账户管理」里点“新建 1 个”");
            ok = false;
        }

        var banned = FindBlacklistedMods(gameDir);
        foreach (var (file, guid) in banned)
        {
            log($"  ✗ 黑名单插件：{Path.GetFileName(file)}（{guid}）会让无头拒绝托管，请从副本里删掉");
            ok = false;
        }

        if (!string.IsNullOrWhiteSpace(copyDir) && Directory.Exists(copyDir))
        {
            var plugin = Path.Combine(copyDir, @"BepInEx\plugins\Fika\Fika.Headless.dll");
            var mgr = Path.Combine(copyDir, "FikaHeadlessManager.exe");
            if (!File.Exists(plugin) || !File.Exists(mgr))
            {
                log($"  ! 副本缺少无头插件/Manager → 用「一键全部安装」勾选副本目录即可自动补齐");
                ok = false;
            }

            var current = ReadHeadlessConfigTarget(copyDir);
            var valid = current is not null && profiles.Any(p => p.Id.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (!valid)
            {
                if (profiles.Count > 0)
                {
                    WriteHeadlessConfig(gameDir, profiles[^1].Id, copyDir, log);
                    log("  ✓ 已把副本 HeadlessConfig.json 指向现有无头档案（覆盖了缺失/失效的配置）");
                }
            }
            else
            {
                log($"  · 副本 HeadlessConfig.json 正常（{current}）");
            }
        }
        else if (!string.IsNullOrWhiteSpace(copyDir))
        {
            log($"  ! 副本目录不存在：{copyDir}（如需无头客户端，勾选“创建独立无头客户端副本”再装一次）");
        }

        log(ok ? "结论：环境就绪，直接启动服务端 + run-headless.cmd 即可" : "结论：按上面提示处理即可（多数项已自动修复）");
        return ok;
    }


    /// <summary>
    /// Fika.Headless 1.5.3 内置的黑名单：带这些插件时无头会自检失败、拒绝托管
    /// （源码 FikaHeadlessPlugin.VerifyPlugins 里硬编码）。
    /// </summary>
    public static readonly string[] HeadlessBlacklist =
    [
        "com.Amanda.Graphics",
        "com.Amanda.Sense",
        "VIP.TommySoucy.MoreCheckmarks",
        "com.kmyuhkyuk.EFTApi",
        "com.mpstark.DynamicMaps",
        "IhanaMies.LootValue",
        "com.cactuspie.ramcleanerinterval",
        "com.TYR.DeClutter",
        "SPTVRAMCleaner.UniqueGUID",
        "harmonyzt.sptleaderboard",
        "com.acidphantasm.stattrack"
    ];

    /// <summary>扫描目录下的 BepInEx 插件，找出命中无头黑名单的 dll。</summary>
    public static List<(string File, string Guid)> FindBlacklistedMods(string gameDir)
    {
        var hits = new List<(string, string)>();
        var plugins = Path.Combine(gameDir, @"BepInEx\plugins");
        if (!Directory.Exists(plugins)) return hits;

        foreach (var dll in Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var bytes = File.ReadAllBytes(dll);
                foreach (var guid in HeadlessBlacklist)
                {
                    if (bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(guid)) >= 0)
                    {
                        hits.Add((dll, guid));
                        break;
                    }
                }
            }
            catch { }
        }

        return hits;
    }

    /// <summary>无头环境自检：档案 / amount / 插件 / Manager 是否齐备。</summary>
    public static bool HeadlessCheck(string gameDir, Action<string> log)
    {
        var profiles = ListHeadlessProfiles(gameDir);
        var amount = ReadHeadlessAmount(gameDir);
        var cfgExists = File.Exists(Path.Combine(gameDir, ConfigRel));
        var plugin = File.Exists(Path.Combine(gameDir, CoreDirRel, "Fika.Headless.dll"));
        var manager = File.Exists(Path.Combine(gameDir, "FikaHeadlessManager.exe"));
        var cfgFile = File.Exists(Path.Combine(gameDir, "HeadlessConfig.json"));
        var copyCfg = File.Exists(Path.Combine(gameDir, "HeadlessConfig.json"));

        log($"无头档案：{profiles.Count} 个");
        foreach (var p in profiles) log($"  · {p.Id}  {p.Nickname}  Lv{p.Level}");

        log($"fika.jsonc：{(cfgExists ? "存在" : "缺失")}；headless.profiles.amount = {amount}");
        log($"Fika.Headless.dll：{(plugin ? "已装" : "未装")}；FikaHeadlessManager.exe：{(manager ? "已装" : "未装")}；根目录 HeadlessConfig.json：{(cfgFile ? "有" : "无")}");

        var ok = true;

        var banned = FindBlacklistedMods(gameDir);
        foreach (var (file, guid) in banned)
        {
            log($"  ✗ 黑名单插件：{Path.GetFileName(file)}（{guid}）");
            log($"      → 无头会以「invalid plugins found」拒绝托管，请删掉/移出 {Path.GetDirectoryName(file)}");
            ok = false;
        }
        if (profiles.Count == 0) { log("  ✗ 没有任何无头档案 —— 用「无头账户管理」新建 1 个"); ok = false; }
        if (!plugin) { log("  ✗ 当前目录没有 Fika.Headless.dll —— 这不是无头副本，请用副本目录"); ok = false; }
        if (cfgExists && amount != profiles.Count)
        {
            log($"  ! amount({amount}) 与档案数({profiles.Count}) 不一致；已装的服务端已打补丁可忽略，未打补丁会报 Invalid headless client");
            log("    → 执行 --sync-amount 或点「同步 amount」修正");
        }

        log(ok ? "结论：可以启动无头客户端" : "结论：还有缺失，见上面 ✗ 项");
        return ok;
    }

    public static bool StopServer(string gameDir, Action<string> log)
    {
        var exe = Path.Combine(gameDir, @"SPT_Runtime\SPT.Server.exe");
        var port = ServerPort(gameDir);
        var pids = ListenersOn(port);

        if (pids.Count == 0)
        {
            log($"· 端口 {port} 本就是空闲的");
            return true;
        }

        var killed = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }

                if (!IsSameServer(exe, path, p.ProcessName))
                {
                    log($"  ! 端口 {port} 被其它程序占用：{path ?? p.ProcessName}（PID {pid}），未结束它");
                    continue;
                }

                p.Kill(true);
                p.WaitForExit(8000);
                log($"  · 已结束 SPT.Server.exe（PID {pid}）");
                killed++;
            }
            catch (Exception ex)
            {
                log($"  ! 结束 PID {pid} 失败：{ex.Message}");
            }
        }

        var free = WaitPortFree(port, TimeSpan.FromSeconds(15));
        log(free ? $"  ✓ 端口 {port} 已释放" : $"  ✗ 端口 {port} 仍被占用：{DescribePort(port)}");
        return free && (killed > 0 || pids.Count == 0);
    }

    private static HttpClient BuildHttp(int port, string apiKey)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
        http.DefaultRequestHeaders.Add("requestcompressed", "0");
        return http;
    }

    private static bool WaitHeartbeat(HttpClient http, TimeSpan timeout, Action<string> log)
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            try
            {
                var r = http.GetAsync("fika/api/heartbeat").GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return true;
            }
            catch { }
            Thread.Sleep(1000);
        }
        return false;
    }

    private static string? CreateOne(HttpClient http, Action<string> log)
    {
        try
        {
            var content = new StringContent("null", Encoding.UTF8, "application/json");
            var resp = http.PostAsync("fika/api/createheadlessprofile", content).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                log($"  ✗ 创建失败 {(int)resp.StatusCode}: {body}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString();
                }
            }
            log("  ✗ 响应里没有 id：" + body);
            return null;
        }
        catch (Exception ex)
        {
            log("  ✗ 请求异常：" + ex.Message);
            return null;
        }
    }

    public static string? FindHeadlessProfileId(string gameDir)
    {
        var dir = Path.Combine(gameDir, @"SPT_Runtime\user\profiles");
        if (!Directory.Exists(dir)) return null;
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                if (doc.RootElement.TryGetProperty("characters", out var chars) &&
                    chars.TryGetProperty("pmc", out var pmc) &&
                    pmc.TryGetProperty("Info", out var info) &&
                    info.TryGetProperty("MemberCategory", out var mc) &&
                    mc.ValueKind == JsonValueKind.Number && mc.GetInt32() == 128)
                {
                    return Path.GetFileNameWithoutExtension(f);
                }
            }
            catch { }
        }
        return null;
    }

    public static int CountHeadlessProfiles(string gameDir)
    {
        var dir = Path.Combine(gameDir, @"SPT_Runtime\user\profiles");
        if (!Directory.Exists(dir)) return 0;
        int n = 0;
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                if (doc.RootElement.TryGetProperty("characters", out var chars) &&
                    chars.TryGetProperty("pmc", out var pmc) &&
                    pmc.TryGetProperty("Info", out var info) &&
                    info.TryGetProperty("MemberCategory", out var mc) &&
                    mc.ValueKind == JsonValueKind.Number &&
                    mc.GetInt32() == 128)
                {
                    n++;
                }
            }
            catch { }
        }
        return n;
    }

    private static void SetHeadlessAmount(JsonNode root, string configPath, int amount)
    {
        if (root["headless"] is not JsonObject h)
        {
            h = new JsonObject();
            root["headless"] = h;
        }
        if (h["profiles"] is not JsonObject p)
        {
            p = new JsonObject();
            h["profiles"] = p;
        }
        p["amount"] = amount;

        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool WaitFor(Func<bool> cond, TimeSpan timeout, int intervalMs)
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            if (cond()) return true;
            Thread.Sleep(intervalMs);
        }
        return false;
    }

    // ---------- 无头账户管理 ----------

    public sealed record HeadlessProfileInfo(
        string Id, string Nickname, int Level, int MemberCategory, DateTime Modified, long SizeBytes, string? ConfigPath);

    public static List<HeadlessProfileInfo> ListHeadlessProfiles(string gameDir)
    {
        var list = new List<HeadlessProfileInfo>();
        var dir = Path.Combine(gameDir, @"SPT_Runtime\user\profiles");
        if (!Directory.Exists(dir)) return list;

        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                if (!doc.RootElement.TryGetProperty("characters", out var chars)) continue;
                if (!chars.TryGetProperty("pmc", out var pmc)) continue;
                if (!pmc.TryGetProperty("Info", out var info)) continue;
                if (!info.TryGetProperty("MemberCategory", out var mc)) continue;
                if (mc.ValueKind != JsonValueKind.Number || mc.GetInt32() != 128) continue;

                var id = Path.GetFileNameWithoutExtension(f);
                var nick = info.TryGetProperty("Nickname", out var n) ? n.GetString() ?? "" : "";
                var lvl = info.TryGetProperty("Level", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0;
                var cfg = Path.Combine(gameDir, ScriptsRel, id, "HeadlessConfig.json");

                list.Add(new HeadlessProfileInfo(id, nick, lvl, 128, File.GetLastWriteTime(f),
                    new FileInfo(f).Length, File.Exists(cfg) ? cfg : null));
            }
            catch { }
        }

        return list.OrderBy(x => x.Modified).ToList();
    }

    public static string? WriteHeadlessConfig(string gameDir, string profileId, string? targetDir, Action<string> log)
    {
        var target = string.IsNullOrWhiteSpace(targetDir) ? gameDir : targetDir!;
        try
        {
            Directory.CreateDirectory(target);
            var path = Path.Combine(target, "HeadlessConfig.json");
            var json = "{\n  \"ProfileId\": \"" + profileId + "\",\n  \"BackendUrl\": \"https://127.0.0.1:6969\",\n  \"StartMinimized\": true,\n  \"ExtraLogging\": false\n}\n";
            File.WriteAllText(path, json);
            log("  · 已写入 " + path);
            return path;
        }
        catch (Exception ex)
        {
            log("  ✗ 写入 HeadlessConfig.json 失败：" + ex.Message);
            return null;
        }
    }

    public static bool DeleteHeadlessProfile(string gameDir, string profileId, Action<string> log, IEnumerable<string>? extraDirs = null)
    {
        var file = Path.Combine(gameDir, @"SPT_Runtime\user\profiles", profileId + ".json");
        var scriptDir = Path.Combine(gameDir, ScriptsRel, profileId);
        var ok = true;

        var backup = Path.Combine(gameDir, BackupDirName, "headless_profiles", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        try { Directory.CreateDirectory(backup); } catch { }

        if (File.Exists(file))
        {
            try
            {
                File.Copy(file, Path.Combine(backup, profileId + ".json"), true);
                File.Delete(file);
                log($"  · 已删除档案 {profileId}（副本已备份到 {backup}）");
            }
            catch (Exception ex)
            {
                log($"  ✗ 删除 {profileId}.json 失败：{ex.Message}");
                ok = false;
            }
        }
        else
        {
            log("  ! 档案文件不存在：" + file);
            ok = false;
        }

        if (Directory.Exists(scriptDir))
        {
            try { Directory.Delete(scriptDir, true); log("  · 已删除 scripts\\" + profileId); }
            catch (Exception ex) { log("  ! 删除脚本目录失败：" + ex.Message); }
        }

        var cfgDirs = new List<string> { gameDir };
        if (extraDirs is not null) cfgDirs.AddRange(extraDirs.Where(d => !string.IsNullOrWhiteSpace(d)));

        foreach (var dir in cfgDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rootCfg = Path.Combine(dir, "HeadlessConfig.json");
            if (!File.Exists(rootCfg)) continue;
            try
            {
                if (File.ReadAllText(rootCfg).Contains(profileId, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(rootCfg);
                    log($"  · {dir} 下的 HeadlessConfig.json 指向该档案，已一并删除");
                }
            }
            catch { }
        }

        SyncHeadlessAmount(gameDir, log);
        return ok;
    }

    public static string? ReadHeadlessConfigTarget(string dir)
    {
        var path = Path.Combine(dir, "HeadlessConfig.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("ProfileId", out var p) ? p.GetString() : "（无 ProfileId）";
        }
        catch { return "（解析失败）"; }
    }

    public static int ReadHeadlessAmount(string gameDir)
    {
        var cfg = Path.Combine(gameDir, ConfigRel);
        if (!File.Exists(cfg)) return -1;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(cfg), nodeOptions: null,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return root?["headless"]?["profiles"]?["amount"]?.GetValue<int>() ?? 0;
        }
        catch { return -1; }
    }

    public static int SyncHeadlessAmount(string gameDir, Action<string> log)
    {
        var count = CountHeadlessProfiles(gameDir);
        var cfg = Path.Combine(gameDir, ConfigRel);
        if (!File.Exists(cfg))
        {
            log("  ! 找不到 fika.jsonc，跳过 amount 同步");
            return count;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(cfg), nodeOptions: null,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
            var cur = root["headless"]?["profiles"]?["amount"]?.GetValue<int>() ?? 0;
            if (cur != count)
            {
                SetHeadlessAmount(root, cfg, count);
                log($"  · 已修正 headless.profiles.amount：{cur} → {count}");
            }
            else
            {
                log($"  · headless.profiles.amount = {count}（无需修改）");
            }
        }
        catch (Exception ex)
        {
            log("  ✗ 同步 amount 失败：" + ex.Message);
        }

        return count;
    }
}
