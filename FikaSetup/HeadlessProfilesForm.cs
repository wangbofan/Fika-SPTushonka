using System.Diagnostics;

namespace FikaSetup;

public sealed class HeadlessProfilesForm : Form
{
    private readonly string _gameDir;
    private readonly ListView _list = new();
    private readonly TextBox _log = new();
    private readonly Label _summary = new();
    private readonly Label _targets = new();
    private readonly Button _addBtn = new();
    private readonly Button _cfgBtn = new();
    private readonly Button _syncBtn = new();
    private readonly Button _delBtn = new();
    private readonly Button _openBtn = new();
    private readonly CheckBox _alsoCopy = new();
    private readonly TextBox _copyDir = new();
    private readonly Func<string> _copyDirProvider;

    public HeadlessProfilesForm(string gameDir, Func<string> copyDirProvider)
    {
        _gameDir = gameDir;
        _copyDirProvider = copyDirProvider;

        Text = "无头账户管理";
        Width = 900;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var header = new Label
        {
            Text = "无头账户管理（MemberCategory = UnitTest 128 的档案）",
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 14)
        };

        var dirLabel = new Label
        {
            Text = gameDir,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(20, 44)
        };

        _summary.Location = new Point(20, 66);
        _summary.AutoSize = true;
        _summary.ForeColor = Color.DimGray;

        _targets.Location = new Point(20, 84);
        _targets.AutoSize = true;
        _targets.ForeColor = Color.DimGray;

        _list.Location = new Point(20, 108);
        _list.Size = new Size(ClientSize.Width - 40, 226);
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = false;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _list.Columns.Add("档案 ID", 200);
        _list.Columns.Add("昵称", 190);
        _list.Columns.Add("等级", 70);
        _list.Columns.Add("创建时间", 150);
        _list.Columns.Add("大小", 80);
        _list.Columns.Add("HeadlessConfig", 140);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => WriteConfig();

        _addBtn.Text = "新建 1 个";
        _addBtn.Location = new Point(20, 344);
        _addBtn.Size = new Size(104, 34);
        _addBtn.Click += (_, _) => AddProfile();

        _cfgBtn.Text = "写入 HeadlessConfig";
        _cfgBtn.Location = new Point(132, 344);
        _cfgBtn.Size = new Size(158, 34);
        _cfgBtn.Click += (_, _) => WriteConfig();

        _syncBtn.Text = "同步 amount";
        _syncBtn.Location = new Point(298, 344);
        _syncBtn.Size = new Size(118, 34);
        _syncBtn.Click += (_, _) =>
        {
            Log("=== 同步 headless.profiles.amount ===");
            var n = InstallerCore.SyncHeadlessAmount(_gameDir, Log);
            Log($"  当前无头档案：{n} 个");
            Reload();
        };

        _delBtn.Text = "删除选中";
        _delBtn.Location = new Point(424, 344);
        _delBtn.Size = new Size(104, 34);
        _delBtn.ForeColor = Color.Firebrick;
        _delBtn.Click += (_, _) => DeleteSelected();

        _openBtn.Text = "打开档案目录";
        _openBtn.Location = new Point(536, 344);
        _openBtn.Size = new Size(130, 34);
        _openBtn.Click += (_, _) => OpenProfilesDir();

        var closeBtn = new Button { Text = "关闭", Location = new Point(ClientSize.Width - 116, 344), Size = new Size(96, 34) };
        closeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeBtn.Click += (_, _) => Close();

        _alsoCopy.Text = "同时写入无头副本：";
        _alsoCopy.Location = new Point(20, 388);
        _alsoCopy.AutoSize = true;

        _copyDir.Location = new Point(160, 385);
        _copyDir.Width = ClientSize.Width - 160 - 40;
        _copyDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _copyDir.Text = copyDirProvider();

        var hint = new Label
        {
            Text = "说明：删除会先备份到 _fika_installer_backup\\headless_profiles，并自动把 amount 校正为剩余数量。双击一行 = 写入 HeadlessConfig。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(20, 414)
        };

        _log.Location = new Point(20, 442);
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(250, 250, 250);
        _log.Font = new Font("Consolas", 9F);
        _log.Width = ClientSize.Width - 40;
        _log.Height = ClientSize.Height - 442 - 20;
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        Controls.AddRange([header, dirLabel, _summary, _targets, _list, _addBtn, _cfgBtn, _syncBtn, _delBtn,
            _openBtn, closeBtn, _alsoCopy, _copyDir, hint, _log]);

        Shown += (_, _) =>
        {
            Reload();
            Log("选择一行后可写入 HeadlessConfig / 删除；新建档案会自动启动一次服务端（请先关闭正在运行的服务端）。");
        };
    }

    private void Reload()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        var profiles = InstallerCore.ListHeadlessProfiles(_gameDir);
        foreach (var p in profiles)
        {
            var item = new ListViewItem(p.Id) { Tag = p };
            item.SubItems.Add(string.IsNullOrEmpty(p.Nickname) ? "（未命名）" : p.Nickname);
            item.SubItems.Add(p.Level.ToString());
            item.SubItems.Add(p.Modified.ToString("yyyy-MM-dd HH:mm"));
            item.SubItems.Add((p.SizeBytes / 1024) + " KB");
            item.SubItems.Add(p.ConfigPath is null ? "缺失" : "有");
            if (p.ConfigPath is null) item.ForeColor = Color.DimGray;
            _list.Items.Add(item);
        }
        _list.EndUpdate();

        var amount = InstallerCore.ReadHeadlessAmount(_gameDir);
        _summary.Text = $"共 {profiles.Count} 个无头档案；fika.jsonc 里 headless.profiles.amount = {amount}" +
                        (amount == profiles.Count ? "（一致）" : "（不一致，点“同步 amount”）");
        _summary.ForeColor = amount == profiles.Count ? Color.DimGray : Color.Chocolate;

        var main = InstallerCore.ReadHeadlessConfigTarget(_gameDir);
        var copyDir = _copyDir.Text.Trim();
        var copy = string.IsNullOrWhiteSpace(copyDir) ? null : InstallerCore.ReadHeadlessConfigTarget(copyDir);
        _targets.Text = $"主目录 HeadlessConfig → {(main ?? "无")}    副本 → {(string.IsNullOrWhiteSpace(copyDir) ? "未设置" : copy ?? "无")}";

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = _list.SelectedItems.Count > 0;
        _cfgBtn.Enabled = has;
        _delBtn.Enabled = has;
    }

    private InstallerCore.HeadlessProfileInfo? Selected()
    {
        if (_list.SelectedItems.Count == 0) return null;
        return _list.SelectedItems[0].Tag as InstallerCore.HeadlessProfileInfo;
    }

    private void AddProfile()
    {
        if (InstallerCore.RunningProcesses().Any(p => p.StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "请先关闭正在运行的 SPT.Server.exe。\n\n建档需要临时独占 6969 端口启动服务端。",
                "服务端正在运行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, "将临时启动 SPT.Server 并通过 Fika API 创建一个新的无头档案，完成后自动关闭服务端。\n\n继续？",
                "新建无头档案", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _addBtn.Enabled = false;
        try
        {
            Log("=== 新建无头档案 ===");
            var ids = InstallerCore.CreateHeadlessProfiles(_gameDir, 1, Log);
            if (ids.Count > 0)
            {
                Log("✓ 已创建：" + ids[0]);
                if (MessageBox.Show(this, "是否把这个新档案写成 HeadlessConfig.json？", "写入配置",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    WriteConfig(ids[0]);
                }
            }
        }
        catch (Exception ex)
        {
            Log("✗ " + ex.Message);
        }
        finally
        {
            _addBtn.Enabled = true;
            Reload();
        }
    }

    private void WriteConfig(string? id = null)
    {
        id ??= Selected()?.Id;
        if (id is null)
        {
            MessageBox.Show(this, "请先选中一行。", "未选择", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Log("=== 写入 HeadlessConfig.json ===");
        var main = InstallerCore.WriteHeadlessConfig(_gameDir, id, null, Log);
        if (main is null) return;
        Log($"  ✓ 主目录配置已指向 {id}");

        if (_alsoCopy.Checked && !string.IsNullOrWhiteSpace(_copyDir.Text) && Directory.Exists(_copyDir.Text.Trim()))
        {
            InstallerCore.WriteHeadlessConfig(_gameDir, id, _copyDir.Text.Trim(), Log);
            Log("  ✓ 无头副本配置已更新");
        }
        else if (_alsoCopy.Checked)
        {
            Log("  ! 无头副本目录不存在，已跳过（先一键安装生成副本）");
        }

        Reload();
    }

    private void DeleteSelected()
    {
        var p = Selected();
        if (p is null) return;

        var scripts = p.ConfigPath is null ? "无" : "有";
        var msg = $"确认删除这个无头档案？\n\n" +
                  $"档案 ID：{p.Id}\n昵称：{(string.IsNullOrEmpty(p.Nickname) ? "（未命名）" : p.Nickname)}\n" +
                  $"等级：{p.Level}\nHeadlessConfig：{scripts}\n\n" +
                  "删除前会备份 profile 到 _fika_installer_backup\\headless_profiles，\n并自动把 amount 校正为剩余数量。此操作不可撤销（但可从备份恢复）。";

        if (MessageBox.Show(this, msg, "删除无头档案", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Log("=== 删除无头档案 " + p.Id + " ===");
        var extra = new List<string>();
        var copyDir = _copyDir.Text.Trim();
        if (!string.IsNullOrWhiteSpace(copyDir) && Directory.Exists(copyDir)) extra.Add(copyDir);
        InstallerCore.DeleteHeadlessProfile(_gameDir, p.Id, Log, extra);
        Log("✓ 完成");
        Reload();
    }

    private void OpenProfilesDir()
    {
        var dir = Path.Combine(_gameDir, @"SPT_Runtime\user\profiles");
        try
        {
            if (!Directory.Exists(dir)) { Log("  ! 目录不存在：" + dir); return; }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("  ✗ 打开失败：" + ex.Message);
        }
    }

    private void Log(string s)
    {
        if (_log.InvokeRequired) { _log.BeginInvoke(() => Log(s)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
    }
}
