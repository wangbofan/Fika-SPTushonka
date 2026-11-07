using FikaHeadlessManager.Bundles;
using FikaHeadlessManager.Logging;
using FikaHeadlessManager.Models;
using FikaHeadlessManager.Patching;
using FikaHeadlessManager.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FikaHeadlessManager;

public static class Program
{
    private static async Task Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        var prepareOnly = ParseArguments(args);

        var settings = await LoadSettings();

        if (ShowConfig)
        {
            AnsiConsole.MarkupLine($"[green]ProfileId[/] = {Markup.Escape(settings.ProfileId ?? "")}");
            AnsiConsole.MarkupLine($"[green]BackendUrl[/] = {Markup.Escape(settings.BackendUrl?.ToString() ?? "")}");
            AnsiConsole.MarkupLine($"[green]Config[/] = {Markup.Escape(Path.GetFullPath(Paths.Config))}");
            return;
        }

        if (!string.IsNullOrEmpty(settings.Title))
        {
            Console.Title = $"Headless Manager - {settings.Title}";
        }

        await using var services = new ServiceCollection()
            .AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Information)
                .AddProvider(new SpectreLoggerProvider()))
            .AddSingleton(settings)
            .AddSingleton<ServerClient>()
            .AddSingleton<FilePatcher>()
            .AddSingleton<PatchService>()
            .AddSingleton<BundleService>()
            .AddSingleton<HeadlessRunner>()
            .BuildServiceProvider();

        await services.GetRequiredService<HeadlessRunner>().RunAsync(prepareOnly);
    }

    public static bool NoPatch;
    public static bool ShowConfig;

    private static bool ParseArguments(string[] args)
    {
        var prepareOnly = false;

        foreach (var arg in args)
        {
            if (arg.Equals("--prepare", StringComparison.OrdinalIgnoreCase))
            {
                prepareOnly = true;
                continue;
            }

            if (arg.Equals("--no-patch", StringComparison.OrdinalIgnoreCase))
            {
                NoPatch = true;
                continue;
            }

            if (arg.Equals("--show-config", StringComparison.OrdinalIgnoreCase))
            {
                ShowConfig = true;
                continue;
            }

            Fatal($"Unknown argument '{arg}'." + Environment.NewLine + "Usage: FikaHeadlessManager [--prepare] [--no-patch]");
        }

        return prepareOnly;
    }

    private static async Task<Settings> LoadSettings()
    {
        if (!File.Exists(Paths.ClientExecutable))
        {
            Fatal("Unable to find 'EscapeFromTarkov.exe'.\n" +
                  "Make sure you are running Fika Headless Manager from a valid SPT install folder!");
        }

        if (!File.Exists(Paths.HeadlessPlugin))
        {
            Fatal("Unable to find 'Fika.Headless.dll'.\n" +
                  "Please revisit the documentation and install Fika Headless using Fika-Installer!");
        }

        if (!File.Exists(Paths.Config))
        {
            var generated = TryWriteConfigFromProfiles();
            if (generated is null)
            {
                Fatal("Unable to find the configuration file 'HeadlessConfig.json'\n" +
                      "and no headless profile (MemberCategory=UnitTest) was found under SPT_Runtime\\user\\profiles.\n" +
                      "Create one from the installer (无头账户管理 → 新建 1 个) and try again.");
            }

            AnsiConsole.MarkupLine($"[yellow]HeadlessConfig.json 缺失，已按无头档案自动生成（ProfileId={Markup.Escape(generated!)}）。[/]");
        }

        try
        {
            await using var fileStream = File.OpenRead(Paths.Config);
            var settings = await JsonSerializer.DeserializeAsync<Settings>(fileStream)
                           ?? throw new InvalidOperationException("Failed to deserialize configuration.");

            if (string.IsNullOrEmpty(settings.ProfileId))
            {
                Fatal("ProfileId was null!");
            }

            if (settings.BackendUrl == null)
            {
                Fatal("BackendUrl was null!");
            }

            return settings;
        }
        catch (Exception ex)
        {
            Fatal($"Error loading configuration: {ex.Message}");
            throw; // Fatal exits the process, this only satisfies the compiler
        }
    }

    /// <summary>找不到 HeadlessConfig.json 时，从 UnitTest 档案里挑一个最新的自动生成。</summary>
    private static string? TryWriteConfigFromProfiles()
    {
        var dir = Path.Combine(Paths.Runtime, "user", "profiles");
        if (!Directory.Exists(dir)) return null;

        var candidates = new List<(string Id, DateTime Time)>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("characters", out var chars)) continue;
                if (!chars.TryGetProperty("pmc", out var pmc)) continue;
                if (!pmc.TryGetProperty("Info", out var info)) continue;
                if (!info.TryGetProperty("MemberCategory", out var mc)) continue;
                if (mc.ValueKind != JsonValueKind.Number || mc.GetInt32() != 128) continue;
                candidates.Add((Path.GetFileNameWithoutExtension(file), File.GetLastWriteTime(file)));
            }
            catch { }
        }

        if (candidates.Count == 0) return null;

        var ordered = candidates.OrderByDescending(c => c.Time).ToList();
        var chosen = ordered[0].Id;
        var port = ReadServerPort();

        var settings = new Settings
        {
            ProfileId = chosen,
            BackendUrl = new Uri($"https://127.0.0.1:{port}"),
            StartMinimized = true
        };

        File.WriteAllText(Paths.Config, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        if (ordered.Count > 1)
        {
            AnsiConsole.MarkupLine($"[grey]检测到 {ordered.Count} 个无头档案，使用最新的 {chosen}；如需指定请改 HeadlessConfig.json 的 ProfileId。[/]");
        }

        return chosen;
    }

    private static int ReadServerPort()
    {
        try
        {
            var cfg = Path.Combine(Paths.Runtime, "user", "mods", "FikaServer", "assets", "configs", "fika.jsonc");
            if (!File.Exists(cfg)) return 6969;
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            return doc.RootElement.GetProperty("server").GetProperty("SPT").GetProperty("http").GetProperty("port").GetInt32();
        }
        catch { return 6969; }
    }

    [DoesNotReturn]
    private static void Fatal(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");

        if (!Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("Press any key to exit...");
            Console.ReadKey(true);
        }

        Environment.Exit(1);
    }
}
