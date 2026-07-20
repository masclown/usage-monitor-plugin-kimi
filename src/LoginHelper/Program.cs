using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.LoginHelper;

/// <summary>
/// req-090-005：UsageMonitor Login Helper（通用 CLI 工具）。
/// 支持 --list 枚举可登录 Provider，--provider &lt;id&gt; 指定登录目标。
/// </summary>
public class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // req-090-007：统一错误格式
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args.Contains("--list"))
        {
            return ListProviders();
        }

        var providerIdx = Array.IndexOf(args, "--provider");
        if (providerIdx < 0 || providerIdx + 1 >= args.Length)
        {
            Console.WriteLine("[ERROR] Action=ParseArgs Message=--provider requires a value");
            PrintUsage();
            return 1;
        }

        var providerId = args[providerIdx + 1];
        return await LoginProviderAsync(providerId);
    }

    /// <summary>打印使用说明</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("UsageMonitor Login Helper - 通用登录辅助工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  UsageMonitor.LoginHelper.exe --list");
        Console.WriteLine("    枚举所有支持浏览器登录的 Provider");
        Console.WriteLine();
        Console.WriteLine("  UsageMonitor.LoginHelper.exe --provider <id>");
        Console.WriteLine("    为指定 Provider 启动浏览器登录流程");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  UsageMonitor.LoginHelper.exe --provider MiniMax");
        Console.WriteLine();
    }

    /// <summary>req-090-005：枚举所有有 LoginConfig 的内置插件</summary>
    private static int ListProviders()
    {
        Console.WriteLine("支持浏览器登录的 Provider 列表:");
        Console.WriteLine();

        var pluginManager = CreatePluginManager();
        var found = false;

        foreach (var plugin in pluginManager.Plugins)
        {
            var loginConfig = plugin.Provider.LoginConfig;
            if (loginConfig != null)
            {
                found = true;
                Console.WriteLine($"  {plugin.Provider.ProviderId,-15} {plugin.Provider.DisplayName}");
                Console.WriteLine($"  {" ",-15} 登录 URL: {loginConfig.LoginUrl}");
                Console.WriteLine();
            }
        }

        if (!found)
        {
            Console.WriteLine("  (无可用 Provider)");
        }

        return 0;
    }

    /// <summary>req-090-005：为指定 Provider 执行浏览器登录</summary>
    private static async Task<int> LoginProviderAsync(string providerId)
    {
        Console.WriteLine("===========================================");
        Console.WriteLine($"UsageMonitor Login Helper - {providerId}");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        var pluginManager = CreatePluginManager();
        var plugin = pluginManager.GetPlugin(providerId);

        if (plugin == null)
        {
            Console.WriteLine($"[ERROR] Provider={providerId} Action=FindPlugin Message=Provider not found");
            Console.WriteLine("Use --list to see available providers.");
            return 1;
        }

        var loginConfig = plugin.Provider.LoginConfig;
        if (loginConfig == null)
        {
            Console.WriteLine($"[ERROR] Provider={providerId} Action=GetLoginConfig Message=Provider does not support browser login");
            return 1;
        }

        Console.WriteLine($"Will launch a temporary Edge window for {plugin.Provider.DisplayName} login.");
        Console.WriteLine("Your existing Edge browser is not affected.");
        Console.WriteLine();
        Console.WriteLine("Please complete the following in the new window:");
        Console.WriteLine($"  1. Visit {loginConfig.LoginUrl}");
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
            // req-057：跨进程 Mutex 互斥——与主程序 ConfigService 共用同一命名 Mutex，避免同时写 config.json
            using var configMutex = new Mutex(false, "Global\\UsageMonitor-ConfigService");
            bool acquired = false;
            try
            {
                acquired = configMutex.WaitOne(TimeSpan.FromSeconds(10));
                if (!acquired)
                {
                    Console.WriteLine("[ERROR] Action=AcquireMutex Message=无法获取配置写入锁（主程序可能正在保存配置），请稍后重试。");
                    return 1;
                }

                // req-065 B4：BrowserLoginService 去静态化，创建独立实例
                var loginService = new BrowserLoginService();
                var data = await loginService.LoginAndExtractCookieAsync(loginConfig, cts.Token);
                var cookie = data?.Cookie;

                if (string.IsNullOrEmpty(cookie))
                {
                    Console.WriteLine();
                    Console.WriteLine($"[ERROR] Provider={providerId} Action=Login Message=Login failed or cancelled");
                    Console.WriteLine("Last error: " + loginService.LastError);
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("V Login successful! Cookie obtained.");
                Console.WriteLine($"  Cookie length: {cookie.Length} chars");

                // 保存到 config.json（经 ConfigService 加密）
                var configService = new ConfigService();
                configService.Load();
                var providerCfg = configService.GetProviderConfig(providerId, plugin.Provider);
                providerCfg.SetValue("Cookie", cookie);
                providerCfg.SetValue("_userAgent", data?.UserAgent ?? "UsageMonitor");
                configService.UpdateProviderConfig(providerId, providerCfg);

                // 同时保存到 cookies/*.json（新格式，供主程序直接读取）
                if (data != null)
                {
                    BrowserLoginService.SaveCookieData(data);
                }

                Console.WriteLine();
                Console.WriteLine($"V Cookie saved (encrypted) to %AppData%/UsageMonitor/config.json");
                Console.WriteLine();
                Console.WriteLine("Now you can:");
                Console.WriteLine("  1. Open UsageMonitor main program");
                Console.WriteLine("  2. Right-click tray -> Refresh Now");
                Console.WriteLine($"  3. See {plugin.Provider.DisplayName} usage data");
                Console.WriteLine();

                Console.WriteLine("Press any key to exit...");
                try { Console.ReadKey(); } catch { }

                return 0;
            }
            finally
            {
                if (acquired) configMutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"[ERROR] Provider={providerId} Action=Login Message={ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 创建 PluginManager 并注册内置插件（仅内置，不加载外部 DLL）。
    /// </summary>
    private static PluginManager CreatePluginManager()
    {
        PluginManager.AllowExternalPlugins = false; // 仅内置插件
        var pm = new PluginManager();
        // 注册内置插件（与 App.xaml.cs RegisterBuiltinPlugins 保持一致）
        pm.RegisterPlugin(new Plugin.Deepseek.DeepseekProvider());
        pm.RegisterPlugin(new Plugin.MiMo.MiMoProvider());
        pm.RegisterPlugin(new Plugin.OpenAI.OpenAIProvider());
        pm.RegisterPlugin(new Plugin.MiniMax.MiniMaxProvider());
        return pm;
    }
}
