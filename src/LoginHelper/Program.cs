using System.IO;
using System.Text;
using System.Text.Json;
using UsageMonitor.Core.Services;
using UsageMonitor.Plugin.MiniMax;

namespace UsageMonitor.LoginHelper;

/// <summary>
/// UsageMonitor MiniMax login helper (standalone CLI tool).
/// </summary>
public class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("UsageMonitor MiniMax Login Helper");
        Console.WriteLine("===========================================");
        Console.WriteLine();
        Console.WriteLine("Will launch a temporary Edge window for MiniMax login.");
        Console.WriteLine("Your existing Edge browser is not affected.");
        Console.WriteLine();
        Console.WriteLine("Please complete the following in the new window:");
        Console.WriteLine("  1. Visit platform.minimaxi.com");
        Console.WriteLine("  2. Login to your account");
        Console.WriteLine("  3. Wait for this tool to auto-close Edge and save Cookie");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to cancel anytime.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var data = await BrowserLoginService.LoginAndExtractCookieAsync(
                new MiniMaxProvider().LoginConfig, cts.Token);
            var cookie = data?.Cookie;

            if (string.IsNullOrEmpty(cookie))
            {
                Console.WriteLine();
                Console.WriteLine("X Login failed or cancelled");
                Console.WriteLine("Last error: " + BrowserLoginService.LastError);
                Console.WriteLine("Please retry, or check if Edge can access platform.minimaxi.com");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("V Login successful! Cookie obtained.");
            Console.WriteLine($"  Cookie length: {cookie.Length} chars");
            Console.WriteLine($"  Cookie prefix: {cookie.Substring(0, Math.Min(60, cookie.Length))}...");

            // Hand the new cookie to ConfigService so it's stored in the encrypted
            // config.json path (with sensitive-field encryption) instead of being
            // written in plaintext by the original code below.
            var configService = new UsageMonitor.Core.Services.ConfigService();
            configService.Load();
            var miniCfg = configService.GetProviderConfig("MiniMax", new MiniMaxProvider());
            miniCfg.SetValue("Cookie", cookie);
            // _userAgent used by MiniMaxDomExtractor.ExtractAsync
            miniCfg.SetValue("_userAgent", data?.UserAgent ?? "UsageMonitor");
            configService.UpdateProviderConfig("MiniMax", miniCfg); // persists & encrypts
            Console.WriteLine();
            Console.WriteLine($"V Cookie saved (encrypted) to %AppData%/UsageMonitor/config.json");
            Console.WriteLine();
            Console.WriteLine("Now you can:");
            Console.WriteLine("  1. Open UsageMonitor main program");
            Console.WriteLine("  2. Right-click tray -> Refresh Now");
            Console.WriteLine("  3. See MiniMax usage data");
            Console.WriteLine();

            Console.WriteLine("Press any key to exit...");
            try { Console.ReadKey(); } catch { }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"X Error: {ex.Message}");
            return 1;
        }
    }

    private static void SaveCookieToConfig(string cookie)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "UsageMonitor");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "config.json");

        var configJson = File.Exists(configPath)
            ? File.ReadAllText(configPath, Encoding.UTF8)
            : "{}";

        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name == "ProviderConfigs") continue;
                prop.WriteTo(writer);
            }

            writer.WritePropertyName("ProviderConfigs");
            writer.WriteStartObject();

            if (root.TryGetProperty("ProviderConfigs", out var providerConfigs))
            {
                foreach (var prop in providerConfigs.EnumerateObject())
                {
                    if (prop.Name == "MiniMax") continue;
                    prop.WriteTo(writer);
                }
            }

            writer.WritePropertyName("MiniMax");
            writer.WriteStartObject();
            writer.WriteString("ProviderId", "MiniMax");
            writer.WriteBoolean("IsEnabled", true);
            writer.WritePropertyName("Values");
            writer.WriteStartObject();
            writer.WriteString("ApiKey", "");
            writer.WriteString("Cookie", cookie);
            writer.WriteString("Region", "CN");
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        File.WriteAllText(configPath, Encoding.UTF8.GetString(ms.ToArray()));
    }
}