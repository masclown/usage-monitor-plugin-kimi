using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-110：账号为中心生命周期模型——存量迁移（M1/M2/M3）与网页身份绑定（BoundStableId）单元测试。
/// <para>迁移测试通过"写盘 → 重新 Load"触发 NormalizeAfterLoad 内的 MigrateAccountCardStructure，
/// 并二次 Load 验证幂等；绑定测试覆盖首绑写入 / 不一致不覆盖 / 无身份兜底值忽略三种分支。</para>
/// </summary>
public class Req110LifecycleMigrationTests : IDisposable
{
    private readonly TempDir _tempDir;

    public Req110LifecycleMigrationTests()
    {
        _tempDir = new TempDir();
    }

    public void Dispose() => _tempDir.Dispose();

    /// <summary>创建配置路径重定向到临时目录的 ConfigService（不碰用户真实配置）。</summary>
    private ConfigService CreateConfigService()
    {
        var svc = new ConfigService();
        ReflectionHelpers.SetField(svc, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(svc, "_configFilePath", _tempDir.Combine("config.json"));
        return svc;
    }

    // ===== 死锁回归：ConfigChanged 触发时发布线程不得持有 _ioLock =====

    [Fact]
    public void TryBindAccountStableId_FiresConfigChanged_WithoutHoldingIoLock()
    {
        // 回归背景：TryBindAccountStableId 曾在 lock(_ioLock) 内调 Save()，ConfigChanged 在持锁状态下触发——
        // 后台刷新线程持锁 + 订阅者 Dispatcher.Invoke 同步回 UI 线程 + UI 线程正在等 _ioLock（展开账号列表
        // RefreshStatus → GetEffectiveAccountConfig）构成交叉死锁，主窗口永久卡死。
        // 本用例在回调里用另一线程尝试拿 _ioLock：若发布线程仍持锁则必然超时失败。
        var svc = CreateConfigService();
        svc.AddAccount("MiniMax", null);

        var ioLock = ReflectionHelpers.GetField<object>(svc, "_ioLock")!;
        bool? lockFreeDuringEvent = null;
        svc.ConfigChanged += (_, _) =>
        {
            // 另开线程拿锁：Monitor 可重入，同线程 TryEnter 无法检测"自己持锁"，必须跨线程探测。
            var probe = new Thread(() =>
            {
                if (Monitor.TryEnter(ioLock, TimeSpan.FromSeconds(2)))
                {
                    lockFreeDuringEvent = true;
                    Monitor.Exit(ioLock);
                }
                else
                {
                    lockFreeDuringEvent = false;
                }
            });
            probe.Start();
            probe.Join(TimeSpan.FromSeconds(5));
        };

        svc.TryBindAccountStableId("MiniMax", "default", "abcdef0123456789").Should().BeTrue();

        lockFreeDuringEvent.Should().BeTrue(
            "ConfigChanged 触发时发布线程必须已释放 _ioLock，否则与 UI 线程的 Dispatcher 同步回调会交叉死锁");
        svc.GetAccount("MiniMax", "default")!.BoundStableId.Should().Be("abcdef0123456789");
    }

    // ===== M1：零卡片账号补建 default-card =====

    [Fact]
    public void Load_M1_BackfillsDefaultCard_ForCardlessAccount()
    {
        // 模拟旧版数据：账号存在但名下零卡片（当年 MiniMax_1 黑屏现场）
        var writer = CreateConfigService();
        writer.Settings.Accounts.Add(new Account
        {
            ProviderId = "MiniMax",
            AccountId = "927d7188aa79eab9",
            Nickname = "MiniMax_1",
            UseNickname = true,
            Enabled = true,
            IsDefault = true
        });
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();

        reader.GetCards("MiniMax", "927d7188aa79eab9")
            .Should().ContainSingle().Which.CardId.Should().Be("default-card");
    }

    [Fact]
    public void Load_M1_IsIdempotent_AcrossMultipleLoads()
    {
        var writer = CreateConfigService();
        writer.Settings.Accounts.Add(new Account { ProviderId = "MiniMax", AccountId = "acc1", Enabled = true, IsDefault = true });
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();
        reader.Save();
        reader.Load(); // 二次 Load 验证幂等

        reader.GetCards("MiniMax", "acc1").Should().HaveCount(1, "M1 迁移必须幂等，不重复补卡");
    }

    // ===== M2：旧 Provider:default:* 定制 rekey 到默认账号 =====

    [Fact]
    public void Load_M2_RekeysLegacyDefaultCustomization_ToDefaultAccount()
    {
        var writer = CreateConfigService();
        writer.Settings.Accounts.Add(new Account { ProviderId = "MiniMax", AccountId = "927d7188aa79eab9", Enabled = true, IsDefault = true });
        // 旧三段 default 定制（含用户已保存的迷你图勾选）
        writer.Settings.AccountCustomizations["MiniMax:default:default-card"] = new AccountCustomization
        {
            VisibleMiniCharts = new System.Collections.Generic.List<string> { "mm.mini.ring" }
        };
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();

        // rekey 到哈希账号名下，旧 key 移除
        reader.Settings.AccountCustomizations.Should().ContainKey("MiniMax:927d7188aa79eab9:default-card");
        reader.Settings.AccountCustomizations["MiniMax:927d7188aa79eab9:default-card"]
            .VisibleMiniCharts.Should().Equal("mm.mini.ring");
        reader.Settings.AccountCustomizations.Should().NotContainKey("MiniMax:default:default-card");
    }

    [Fact]
    public void Load_M2_DoesNotOverwrite_ExistingTargetKey()
    {
        var writer = CreateConfigService();
        writer.Settings.Accounts.Add(new Account { ProviderId = "MiniMax", AccountId = "acc1", Enabled = true, IsDefault = true });
        writer.Settings.AccountCustomizations["MiniMax:default:default-card"] = new AccountCustomization
        {
            VisibleMiniCharts = new System.Collections.Generic.List<string> { "old" }
        };
        // 目标 key 已有用户手动定制——不允许被 rekey 覆盖
        writer.Settings.AccountCustomizations["MiniMax:acc1:default-card"] = new AccountCustomization
        {
            VisibleMiniCharts = new System.Collections.Generic.List<string> { "new" }
        };
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();

        reader.Settings.AccountCustomizations["MiniMax:acc1:default-card"]
            .VisibleMiniCharts.Should().BeEquivalentTo(new[] { "new" }, "M2 rekey 仅在目标 key 不存在时执行，不覆盖");
    }

    // ===== M3：有凭据无账号的已启用 Provider 兜底建账号 =====

    [Fact]
    public void Load_M3_BootstrapsAccount_ForCredentialedProviderWithoutAccounts()
    {
        var writer = CreateConfigService();
        var pc = new ProviderConfig { ProviderId = "MiniMax" };
        pc.SetValue("ApiKey", "sk-test");
        writer.Settings.ProviderConfigs["MiniMax"] = pc;
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();

        var accounts = reader.GetAccounts("MiniMax");
        accounts.Should().ContainSingle();
        accounts[0].IsDefault.Should().BeTrue();
        accounts[0].Nickname.Should().Be("MiniMax_1");
        // M1 兜底：兜底账号同样有默认卡
        reader.GetCards("MiniMax", accounts[0].AccountId).Should().ContainSingle();
    }

    [Fact]
    public void Load_M3_SkipsProvider_WithoutCredential()
    {
        var writer = CreateConfigService();
        writer.Settings.ProviderConfigs["MiniMax"] = new ProviderConfig { ProviderId = "MiniMax" }; // 无凭据
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();

        reader.GetAccounts("MiniMax").Should().BeEmpty("未配置凭据的 Provider 不做 M3 兜底建号");
    }

    // ===== TryBindAccountStableId：网页身份哈希绑定 =====

    [Fact]
    public void TryBindAccountStableId_FirstBind_WritesHash()
    {
        var svc = CreateConfigService();
        svc.AddAccount("MiniMax", null);

        var ok = svc.TryBindAccountStableId("MiniMax", "default", "abcdef0123456789");

        ok.Should().BeTrue();
        svc.GetAccount("MiniMax", "default")!.BoundStableId.Should().Be("abcdef0123456789");
    }

    [Fact]
    public void TryBindAccountStableId_Mismatch_ReturnsFalse_AndKeepsOriginal()
    {
        var svc = CreateConfigService();
        svc.AddAccount("MiniMax", null);
        svc.TryBindAccountStableId("MiniMax", "default", "hash-A");

        var ok = svc.TryBindAccountStableId("MiniMax", "default", "hash-B");

        ok.Should().BeFalse("已绑定且哈希不一致时返回 false（网页侧换号告警）");
        svc.GetAccount("MiniMax", "default")!.BoundStableId.Should().Be("hash-A", "不覆盖已绑定值");
    }

    [Fact]
    public void TryBindAccountStableId_DefaultOrEmptyHash_Ignored()
    {
        var svc = CreateConfigService();
        svc.AddAccount("MiniMax", null);

        svc.TryBindAccountStableId("MiniMax", "default", "default").Should().BeTrue();
        svc.TryBindAccountStableId("MiniMax", "default", "").Should().BeTrue();
        svc.TryBindAccountStableId("MiniMax", "default", null).Should().BeTrue();

        svc.GetAccount("MiniMax", "default")!.BoundStableId.Should().BeNull("无身份兜底值不写入绑定");
    }

    [Fact]
    public void BoundStableId_SurvivesSaveAndLoad()
    {
        var svc = CreateConfigService();
        svc.AddAccount("MiniMax", null);
        svc.TryBindAccountStableId("MiniMax", "default", "abcdef0123456789");
        svc.Save();

        var reader = CreateConfigService();
        reader.Load();

        reader.GetAccount("MiniMax", "default")!.BoundStableId
            .Should().Be("abcdef0123456789", "绑定哈希随快照持久化（MakeSnapshot 深拷贝含 BoundStableId）");
    }

    // ===== req-110 P2-1：账号级凭据（覆盖 + 回退） =====

    [Fact]
    public void GetEffectiveAccountConfig_FallsBackToProviderLevel_WhenNoOverlay()
    {
        // 存量单账号兼容：Provider 级凭据无需迁移即对账号生效（P2-5）
        var svc = CreateConfigService();
        var pc = new ProviderConfig { ProviderId = "MiniMax" };
        pc.SetValue("Cookie", "provider-cookie");
        pc.SetValue("Region", "CN");
        svc.Settings.ProviderConfigs["MiniMax"] = pc;

        var eff = svc.GetEffectiveAccountConfig("MiniMax", "acc1");

        eff.GetValue("Cookie").Should().Be("provider-cookie", "账号级缺失时回退 Provider 级");
        eff.GetValue("Region").Should().Be("CN", "非凭据共享项来自 Provider 级基底");
        eff.GetValue("_accountId").Should().Be("acc1", "注入账号提示键供 Cookie 自愈选择账号级文件");
    }

    [Fact]
    public void SetAccountCredential_OverridesProviderLevel_PerAccount()
    {
        var svc = CreateConfigService();
        var pc = new ProviderConfig { ProviderId = "MiniMax" };
        pc.SetValue("Cookie", "provider-cookie");
        svc.Settings.ProviderConfigs["MiniMax"] = pc;

        // 账号 acc2 写入独立 Cookie；acc1 不受影响
        svc.SetAccountCredential("MiniMax", "acc2", "Cookie", "acc2-cookie");

        svc.GetEffectiveAccountConfig("MiniMax", "acc2").GetValue("Cookie")
            .Should().Be("acc2-cookie", "账号级凭据覆盖 Provider 级");
        svc.GetEffectiveAccountConfig("MiniMax", "acc1").GetValue("Cookie")
            .Should().Be("provider-cookie", "其他账号不受影响（凭据隔离）");
        svc.GetAccountCredential("MiniMax", "acc2", "Cookie").Should().Be("acc2-cookie");
        svc.GetAccountCredential("MiniMax", "acc1", "Cookie").Should().BeNull();
    }

    [Fact]
    public void AccountCredential_SurvivesSaveAndLoad_WithEncryption()
    {
        var svc = CreateConfigService();
        svc.SetAccountCredential("MiniMax", "acc2", "Cookie", "secret-cookie");
        svc.Save();

        // 落盘密文：文件内不应出现明文（敏感键自动 DPAPI 加密）
        var raw = System.IO.File.ReadAllText(_tempDir.Combine("config.json"));
        raw.Should().NotContain("secret-cookie", "账号级凭据复用 ProviderConfigs 加密链路");

        var reader = CreateConfigService();
        reader.Load();
        reader.GetAccountCredential("MiniMax", "acc2", "Cookie")
            .Should().Be("secret-cookie", "Load 后自动解密");
    }

    [Fact]
    public void RemoveAccount_CleansAccountCredentialOverlay()
    {
        var svc = CreateConfigService();
        svc.AddAccount("MiniMax", null); // default
        var acc2 = svc.AddAccount("MiniMax", "小号");
        svc.SetAccountCredential("MiniMax", acc2.AccountId, "Cookie", "acc2-cookie");

        svc.RemoveAccount("MiniMax", acc2.AccountId);

        svc.GetAccountCredential("MiniMax", acc2.AccountId, "Cookie")
            .Should().BeNull("req-110 P2-1：删账号级联清理账号级凭据覆盖条目");
    }

    [Fact]
    public void Load_M3_IgnoresAccountCredentialCompositeKeys()
    {
        // 账号级凭据复合 key（Provider#Account）不应被 M3 当作独立 Provider 兜底建号
        var writer = CreateConfigService();
        var overlay = new ProviderConfig { ProviderId = "MiniMax" };
        overlay.SetValue("Cookie", "c");
        writer.Settings.ProviderConfigs["MiniMax#acc2"] = overlay;
        writer.Save();

        var reader = CreateConfigService();
        reader.Load();

        reader.GetAccounts("MiniMax#acc2").Should().BeEmpty();
        reader.Settings.Accounts.Should().BeEmpty("复合 key 不参与 M3 兜底建号");
    }
}
