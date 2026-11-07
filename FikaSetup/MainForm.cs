namespace FikaSetup;

public sealed class MainForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly Label _statusLabel = new();
    private readonly TextBox _log = new();
    private readonly CheckBox _headless = new();
    private readonly CheckBox _firewall = new();
    private readonly CheckBox _makeCopy = new();
    private readonly CheckBox _fullCopy = new();
    private readonly TextBox _copyPath = new();
    private readonly NumericUpDown _profiles = new();
    private readonly Button _installBtn = new();
    private readonly Button _uninstallBtn = new();
    private readonly Button _refreshBtn = new();
    private readonly Button _firewallBtn = new();
    private readonly Button _manageBtn = new();
    private readonly Button _startServerBtn = new();
    private readonly Button _stopServerBtn = new();
    private readonly Button _doctorBtn = new();
    private readonly Label _portLabel = new();

    public MainForm()
    {
        Text = "Fika 一键整合安装器 · SPTushonka 5.0";
        Width = 920;
        Height = 660;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 600);
        Font = new Font("Microsoft YaHei UI", 9F);

        var header = new Label
        {
            Text = "Fika 一键整合安装器（SPT 5.0 / EFT 1.3.0.1-48558 · SPTushonka）",
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 14)
        };

        var pathLabel = new Label { Text = "游戏目录：", AutoSize = true, Location = new Point(22, 58) };
        _pathBox.Location = new Point(100, 54);
        _pathBox.Width = 650;
        _pathBox.Text = InstallerCore.DetectGameDir() ?? @"E:\TKF";

        var browse = new Button { Text = "浏览…", Location = new Point(762, 53), Width = 100 };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "选择 SPT 根目录（包含 EscapeFromTarkov.exe 的那一层）" };
            if (dlg.ShowDialog(this) == DialogResult.OK) { _pathBox.Text = dlg.SelectedPath; RefreshStatus(); }
        };

        _statusLabel.Location = new Point(22, 88);
        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.DimGray;

        _headless.Text = "安装无头端（本目录变无头客户端）";
        _headless.Location = new Point(24, 116);
        _headless.AutoSize = true;

        var profLabel = new Label { Text = "创建无头档案：", AutoSize = true, Location = new Point(330, 118) };
        _profiles.Location = new Point(440, 114);
        _profiles.Width = 55;
        _profiles.Minimum = 0;
        _profiles.Maximum = 8;
        _profiles.Value = 0;

        var profHint = new Label { Text = "0=不创建", AutoSize = true, ForeColor = Color.Gray, Location = new Point(502, 118) };

        _firewall.Text = "添加防火墙规则（TCP 6969 / UDP 25565）";
        _firewall.Location = new Point(24, 144);
        _firewall.AutoSize = true;
        _firewall.Checked = true;

        _makeCopy.Text = "创建独立无头客户端副本 →";
        _makeCopy.Location = new Point(24, 174);
        _makeCopy.AutoSize = true;
        _makeCopy.CheckedChanged += (_, _) => ToggleCopy();

        _copyPath.Location = new Point(240, 171);
        _copyPath.Width = 500;
        _copyPath.Text = DefaultCopyPath();

        var copyBrowse = new Button { Text = "浏览…", Location = new Point(748, 170), Width = 84 };
        copyBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "选择无头副本存放目录（需为空目录）" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _copyPath.Text = dlg.SelectedPath;
        };

        _fullCopy.Text = "完整复制（慢，40 GB+），不勾则用 junction 链接";
        _fullCopy.Location = new Point(240, 198);
        _fullCopy.AutoSize = true;

        _installBtn.Text = "一键全部安装";
        _installBtn.Location = new Point(24, 228);
        _installBtn.Size = new Size(170, 42);
        _installBtn.BackColor = Color.FromArgb(46, 125, 50);
        _installBtn.ForeColor = Color.White;
        _installBtn.FlatStyle = FlatStyle.Flat;
        _installBtn.Click += (_, _) => RunInstall();

        _uninstallBtn.Text = "卸载";
        _uninstallBtn.Location = new Point(206, 228);
        _uninstallBtn.Size = new Size(100, 42);
        _uninstallBtn.Click += (_, _) => RunUninstall();

        _firewallBtn.Text = "只加防火墙";
        _firewallBtn.Location = new Point(318, 228);
        _firewallBtn.Size = new Size(120, 42);
        _firewallBtn.Click += (_, _) =>
        {
            Log("=== 添加防火墙规则 ===");
            if (!InstallerCore.IsAdmin())
            {
                Log("需要管理员权限，正在请求提权…");
                if (InstallerCore.RelaunchElevated("--firewall")) return;
                Log("✗ 提权被取消");
                return;
            }
            InstallerCore.EnsureFirewall(Log);
        };

        _refreshBtn.Text = "刷新状态";
        _refreshBtn.Location = new Point(450, 228);
        _refreshBtn.Size = new Size(110, 42);
        _refreshBtn.Click += (_, _) => RefreshStatus();

        _manageBtn.Text = "无头账户管理…";
        _manageBtn.Location = new Point(572, 228);
        _manageBtn.Size = new Size(150, 42);
        _manageBtn.Click += (_, _) =>
        {
            var errs = InstallerCore.Validate(GameDir);
            if (errs.Count > 0)
            {
                MessageBox.Show(this, string.Join("；", errs), "目录无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using var dlg = new HeadlessProfilesForm(GameDir, () => _copyPath.Text.Trim());
            dlg.ShowDialog(this);
            RefreshStatus();
        };

        _startServerBtn.Text = "启动服务端（可见窗口）";
        _startServerBtn.Location = new Point(24, 280);
        _startServerBtn.Size = new Size(178, 38);
        _startServerBtn.Click += (_, _) =>
        {
            Log("=== 启动服务端 ===");
            InstallerCore.StartServerVisible(GameDir, Log);
            RefreshStatus();
        };

        _stopServerBtn.Text = "停止服务端";
        _stopServerBtn.Location = new Point(208, 280);
        _stopServerBtn.Size = new Size(110, 38);
        _stopServerBtn.Click += (_, _) =>
        {
            Log("=== 停止服务端 ===");
            InstallerCore.StopServer(GameDir, Log);
            RefreshStatus();
        };

        _portLabel.Location = new Point(332, 290);
        _portLabel.AutoSize = false;
        _portLabel.Size = new Size(250, 20);
        _portLabel.AutoEllipsis = true;
        _portLabel.ForeColor = Color.DimGray;

        _doctorBtn.Text = "一键体检 / 修复";
        _doctorBtn.Location = new Point(596, 280);
        _doctorBtn.Size = new Size(160, 38);
        _doctorBtn.Click += (_, _) =>
        {
            Log("=== 一键体检 ===");
            var copy = _makeCopy.Checked ? _copyPath.Text.Trim() : DefaultCopyPath();
            InstallerCore.HeadlessDoctor(GameDir, copy, Log);
            RefreshStatus();
        };

        _log.Location = new Point(24, 330);
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(250, 250, 250);
        _log.Width = ClientSize.Width - 48;
        _log.Height = ClientSize.Height - 330 - 20;
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _log.Font = new Font("Consolas", 9F);

        Controls.AddRange([header, pathLabel, _pathBox, browse, _statusLabel, _headless,
            profLabel, _profiles, profHint, _firewall, _makeCopy, _copyPath, copyBrowse, _fullCopy,
            _installBtn, _uninstallBtn, _firewallBtn, _refreshBtn, _manageBtn,
            _startServerBtn, _stopServerBtn, _portLabel, _doctorBtn, _log]);

        var portTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        portTimer.Tick += (_, _) => UpdatePortLabel();
        portTimer.Start();
        FormClosed += (_, _) => portTimer.Dispose();

        Shown += (_, _) =>
        {
            RefreshStatus();
            ToggleCopy();
            Log("勾选“创建独立无头客户端副本”后，会自动复制一份客户端并装好无头插件，日常客户端不受影响。");
            Log("安装前请关闭游戏与 SPT.Server；旧文件会备份到 _fika_installer_backup。");
            Log("需要手动起服务端时，点“启动服务端（可见窗口）”，控制台会出现在桌面上，不会藏在后台。");
            Log("无头连不上就先点“一键体检 / 修复”，amount、HeadlessConfig 会自动校正。");
            Log("");
        };
    }

    private string DefaultCopyPath()
    {
        var root = GameDir;
        var parent = Path.GetDirectoryName(root) ?? root;
        var name = Path.GetFileName(root);
        return Path.Combine(parent, name + "_Headless");
    }

    private string GameDir => _pathBox.Text.Trim().TrimEnd('\\');

    private void ToggleCopy()
    {
        _copyPath.Enabled = _makeCopy.Checked;
        _fullCopy.Enabled = _makeCopy.Checked;
    }

    private void Log(string s)
    {
        if (_log.InvokeRequired) { _log.BeginInvoke(() => Log(s)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
    }

    private void RefreshStatus()
    {
        var errs = InstallerCore.Validate(GameDir);
        if (errs.Count > 0)
        {
            _statusLabel.Text = "⚠ " + string.Join("；", errs);
            _statusLabel.ForeColor = Color.Firebrick;
            return;
        }

        _statusLabel.Text = InstallerCore.Describe(GameDir);
        _statusLabel.ForeColor = Color.DimGray;
        _headless.Checked = InstallerCore.IsHeadlessInstalled(GameDir);
        UpdatePortLabel();
    }

    private void UpdatePortLabel()
    {
        try
        {
            var port = InstallerCore.ServerPort(GameDir);
            var listeners = InstallerCore.ListenersOn(port);
            _portLabel.Text = listeners.Count == 0
                ? $"端口 {port}：空闲（服务端未运行）"
                : $"端口 {port}：{InstallerCore.DescribePortShort(port)}";
            _portLabel.ForeColor = listeners.Count == 0 ? Color.DimGray : Color.SeaGreen;
        }
        catch { }
    }

    private void RunInstall()
    {
        _installBtn.Enabled = false;
        try
        {
            Log("=== 开始安装 / 更新 ===");
            var copyTo = _makeCopy.Checked ? _copyPath.Text.Trim() : null;
            if (copyTo is not null && !_headless.Checked) _headless.Checked = true;

            if (_headless.Checked && copyTo is null)
            {
                var warn = "你没有勾选“创建独立无头客户端副本”。\n\n" +
                           "这样会把 Fika.Headless.dll 装进当前目录，之后从这里启动游戏会直接进入无头模式（没有游戏画面）。\n\n" +
                           "日常要玩的话，建议勾上副本；或者点“确定”继续（仅当本目录就是专职无头机）。";
                if (MessageBox.Show(this, warn, "确认把本目录变成无头客户端？",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }

            var result = InstallerCore.Install(GameDir, _headless.Checked, (int)_profiles.Value,
                _firewall.Checked, Log, copyTo, _fullCopy.Checked);

            if (result.Ok)
            {
                Log("");
                Log("✓ 完成。启动顺序：SPT.Server → SPT.Launcher → 主菜单 Fika 面板 → Host Raid。");
                if (result.HeadlessProfileIds.Count > 0)
                    Log("无头档案：" + string.Join(", ", result.HeadlessProfileIds));
                if (copyTo is not null)
                    Log("无头客户端：到 " + copyTo + " 运行 run-headless.cmd");
            }
            RefreshStatus();
        }
        finally
        {
            _installBtn.Enabled = true;
        }
    }

    private void RunUninstall()
    {
        if (MessageBox.Show(this, "确认删除 Fika 的客户端插件与服务端 mod？（存档不受影响）",
                "卸载确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        Log("=== 开始卸载 ===");
        InstallerCore.Uninstall(GameDir, Log);
        RefreshStatus();
    }
}
