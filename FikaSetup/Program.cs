namespace FikaSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 命令行：FikaSetup.exe --install <dir> [--headless] [--profiles N] [--no-firewall]
        //          FikaSetup.exe --uninstall <dir> | --status <dir> | --firewall
        if (args.Length >= 1 && args[0].StartsWith("--"))
        {
            var mode = args[0];
            string ArgValue(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
            }

            var dir = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : "";
            var headless = args.Contains("--headless");
            var firewall = !args.Contains("--no-firewall");
            var profiles = int.TryParse(ArgValue("--profiles"), out var n) ? n : 0;
            var copyTo = ArgValue("--copy-to");
            var fullCopy = args.Contains("--full-copy");
            var id = ArgValue("--id");
            var count = int.TryParse(ArgValue("--count"), out var c) ? c : 1;

            var logFile = Path.Combine(Path.GetTempPath(), "fika_setup_cli.log");
            var lines = new List<string>();
            void Collect(string s) => lines.Add(s);

            if (File.Exists(logFile)) File.Delete(logFile);

            int code;
            switch (mode)
            {
                case "--install":
                    var result = InstallerCore.Install(dir, headless, profiles, firewall, Collect,
                        string.IsNullOrWhiteSpace(copyTo) ? null : copyTo, fullCopy);
                    code = result.Ok ? 0 : 1;
                    break;
                case "--uninstall":
                    code = InstallerCore.Uninstall(dir, Collect) ? 0 : 1;
                    break;
                case "--status":
                    Collect(InstallerCore.Describe(dir));
                    code = 0;
                    break;
                case "--firewall":
                    code = InstallerCore.EnsureFirewall(Collect) ? 0 : 1;
                    break;
                case "--headless-list":
                    {
                        var list = InstallerCore.ListHeadlessProfiles(dir);
                        var amount = InstallerCore.ReadHeadlessAmount(dir);
                        Collect($"无头档案：{list.Count} 个；amount = {amount}");
                        foreach (var p in list)
                        {
                            Collect($"  {p.Id}  昵称={p.Nickname}  等级={p.Level}  创建={p.Modified:yyyy-MM-dd HH:mm}  " +
                                    $"大小={p.SizeBytes / 1024}KB  HeadlessConfig={(p.ConfigPath is null ? "缺失" : "有")}");
                        }
                        code = 0;
                        break;
                    }
                case "--headless-add":
                    try
                    {
                        var ids = InstallerCore.CreateHeadlessProfiles(dir, count, Collect);
                        Collect("创建结果：" + string.Join(", ", ids));
                        code = ids.Count == count ? 0 : 1;
                    }
                    catch (Exception ex) { Collect("失败：" + ex.Message); code = 1; }
                    break;
                case "--headless-delete":
                    if (string.IsNullOrWhiteSpace(id)) { Collect("缺少 --id"); code = 2; break; }
                    code = InstallerCore.DeleteHeadlessProfile(dir, id, Collect,
                        string.IsNullOrWhiteSpace(copyTo) ? null : [copyTo]) ? 0 : 1;
                    break;
                case "--headless-config":
                    if (string.IsNullOrWhiteSpace(id)) { Collect("缺少 --id"); code = 2; break; }
                    code = InstallerCore.WriteHeadlessConfig(dir, id, string.IsNullOrWhiteSpace(copyTo) ? null : copyTo, Collect) is null ? 1 : 0;
                    break;
                case "--start-server":
                    code = InstallerCore.StartServerVisible(dir, Collect) ? 0 : 1;
                    break;
                case "--stop-server":
                    code = InstallerCore.StopServer(dir, Collect) ? 0 : 1;
                    break;
                case "--port":
                    {
                        var port = InstallerCore.ServerPort(dir);
                        Collect($"端口 {port}：{InstallerCore.DescribePort(port)}");
                        code = 0;
                        break;
                    }
                case "--doctor":
                    {
                        var copy = string.IsNullOrWhiteSpace(copyTo) ? null : copyTo;
                        code = InstallerCore.HeadlessDoctor(dir, copy, Collect) ? 0 : 1;
                        break;
                    }
                case "--headless-check":
                    code = InstallerCore.HeadlessCheck(dir, Collect) ? 0 : 1;
                    break;
                case "--sync-amount":
                    Collect("无头档案数：" + InstallerCore.SyncHeadlessAmount(dir, Collect));
                    code = 0;
                    break;
                default:
                    Collect("unknown mode: " + mode);
                    code = 2;
                    break;
            }

            File.WriteAllLines(logFile, lines);
            return code;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
