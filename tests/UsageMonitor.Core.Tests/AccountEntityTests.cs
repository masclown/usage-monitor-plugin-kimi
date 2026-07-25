using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-109：账号实体 / 卡片实体 / 三段 key / 账号 CRUD / 卡片 CRUD 单元测试。
/// <para>验证 AuthManager/LoginStateInfo 不被 CardId 触及（认证层保持 (Provider, Account) 二段）。</para>
/// </summary>
public class AccountEntityTests : IDisposable
{
    private readonly TempDir _tempDir;
    private readonly string _configFilePath;

    public AccountEntityTests()
    {
        _tempDir = new TempDir();
        _configFilePath = _tempDir.Combine("config.json");
    }

    public void Dispose() => _tempDir.Dispose();

    private ConfigService CreateConfigService()
    {
        var svc = new ConfigService();
        ReflectionHelpers.SetField(svc, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(svc, "_configFilePath", _configFilePath);
        return svc;
    }

    // ===== Account entity =====

    [Fact]
    public void Account_MakeKey_FormatsAsProviderIdColonAccountId()
    {
        Account.MakeKey("minimax").Should().Be("minimax:default");
        Account.MakeKey("minimax", "acc1").Should().Be("minimax:acc1");
        Account.MakeKey("minimax", "").Should().Be("minimax:default");
        Account.MakeKey("minimax", null!).Should().Be("minimax:default");
    }

    // ===== req-105 + req-109：AccountCustomization 新字段（Tooltip / Mini 图表） =====

    [Fact]
    public void AccountCustomization_NewFields_DefaultToEmpty()
    {
        var c = new AccountCustomization();
        c.VisibleTooltipFields.Should().BeEmpty();
        c.VisibleMiniCharts.Should().BeNull();
        c.VisibleMiniDataGroups.Should().BeEmpty();
        c.MiniDataGroupOrders.Should().BeEmpty();
    }

    [Fact]
    public void GetEffectiveAccountCustomization_CopiesTooltipAndMiniFields()
    {
        var svc = CreateConfigService();
        var key = AccountCustomization.MakeKey("minimax", "default", "default-card");
        svc.Settings.AccountCustomizations[key] = new AccountCustomization
        {
            VisibleTooltipFields = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["Line"] = new System.Collections.Generic.List<string> { "daily_token_value" }
            },
            VisibleMiniCharts = new System.Collections.Generic.List<string> { "mm.mini.ring" },
            VisibleMiniDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["mm.mini.ring"] = new System.Collections.Generic.List<string> { "mm.taskbar.5h" }
            },
            MiniDataGroupOrders = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, int>>
            {
                ["mm.mini.ring"] = new System.Collections.Generic.Dictionary<string, int> { ["mm.taskbar.5h"] = 0 }
            }
        };

        var eff = svc.GetEffectiveAccountCustomization("minimax", "default", "default-card");

        eff.VisibleTooltipFields["Line"].Should().Equal("daily_token_value");
        eff.VisibleMiniCharts.Should().Equal("mm.mini.ring");
        eff.VisibleMiniDataGroups["mm.mini.ring"].Should().Equal("mm.taskbar.5h");
        eff.MiniDataGroupOrders["mm.mini.ring"]["mm.taskbar.5h"].Should().Be(0);
    }

    [Fact]
    public void CardConfig_MakeKey_FormatsAsThreeSegment()
    {
        CardConfig.MakeKey("minimax", "default", "default-card").Should().Be("minimax:default:default-card");
        CardConfig.MakeKey("minimax", "acc1", "card-2").Should().Be("minimax:acc1:card-2");
        CardConfig.MakeKey("minimax", "", "").Should().Be("minimax:default:default-card");
        CardConfig.MakeKey("minimax", null!, null!).Should().Be("minimax:default:default-card");
    }

    [Fact]
    public void AccountCustomization_MakeKey_ThreeSegment()
    {
        AccountCustomization.MakeKey("minimax").Should().Be("minimax:default:default-card");
        AccountCustomization.MakeKey("minimax", "acc1").Should().Be("minimax:acc1:default-card");
        AccountCustomization.MakeKey("minimax", "acc1", "card-2").Should().Be("minimax:acc1:card-2");
        AccountCustomization.MakeKey("minimax", null!, "").Should().Be("minimax:default:default-card");
    }

    [Fact]
    public void AccountCustomization_Cards_DefaultsToEmpty()
    {
        var c = new AccountCustomization();
        c.Cards.Should().BeEmpty();
    }

    [Fact]
    public void AccountCustomization_Cards_HoldsMultipleCards()
    {
        var c = new AccountCustomization
        {
            Cards = new System.Collections.Generic.List<CardConfig>
            {
                new() { CardId = "api-card", Title = "API 用量", DisplayOrder = 0 },
                new() { CardId = "sub-card", Title = "订阅用量", DisplayOrder = 1 }
            }
        };
        c.Cards.Should().HaveCount(2);
        c.Cards[0].CardId.Should().Be("api-card");
        c.Cards[1].DisplayOrder.Should().Be(1);
    }

    // ===== Account CRUD =====

    [Fact]
    public void AddAccount_FirstAccount_DefaultsToDefault()
    {
        var svc = CreateConfigService();
        var acc = svc.AddAccount("minimax", "工作号");

        acc.AccountId.Should().Be("default");
        acc.ProviderId.Should().Be("minimax");
        acc.Nickname.Should().Be("工作号");
        acc.IsDefault.Should().BeTrue();
        svc.Settings.Accounts.Should().ContainSingle().Which.Should().BeSameAs(acc);
    }

    [Fact]
    public void AddAccount_SecondAccount_NotDefault_GeneratesUniqueId()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", "工作号");
        var acc2 = svc.AddAccount("minimax", "私人号");

        acc2.AccountId.Should().NotBe("default");
        acc2.Nickname.Should().Be("私人号");
        acc2.IsDefault.Should().BeFalse();
        svc.GetAccounts("minimax").Should().HaveCount(2);
    }

    [Fact]
    public void AddAccount_NullOrEmptyNickname_StoredAsNull()
    {
        var svc = CreateConfigService();
        var acc = svc.AddAccount("minimax", null);
        acc.Nickname.Should().BeNull();

        var acc2 = svc.AddAccount("kimi", "   ");
        acc2.Nickname.Should().BeNull();
    }

    [Fact]
    public void AddAccount_DifferentProviders_Independent()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        svc.AddAccount("kimi", null);

        svc.GetAccounts("minimax").Should().HaveCount(1);
        svc.GetAccounts("kimi").Should().HaveCount(1);
        svc.GetAccounts("deepseek").Should().BeEmpty();
    }

    [Fact]
    public void GetAccount_FindsExistingAccount()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        svc.AddAccount("minimax", null);

        svc.GetAccount("minimax", "default").Should().NotBeNull();
        svc.GetAccount("minimax", "non-existent").Should().BeNull();
        svc.GetAccount("kimi", "default").Should().BeNull();
    }

    [Fact]
    public void UpdateAccount_ModifiesExisting()
    {
        var svc = CreateConfigService();
        var acc = svc.AddAccount("minimax", "工作号");

        acc.Nickname = "工作A";
        acc.UseNickname = true;
        svc.UpdateAccount(acc);

        var reloaded = svc.GetAccount("minimax", "default");
        reloaded!.Nickname.Should().Be("工作A");
        reloaded.UseNickname.Should().BeTrue();
    }

    [Fact]
    public void UpdateAccount_NonExistent_Throws()
    {
        var svc = CreateConfigService();
        var ghost = new Account { ProviderId = "ghost", AccountId = "nope" };
        Action act = () => svc.UpdateAccount(ghost);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoveAccount_LastAccount_Succeeds_And_CascadesCleanup()
    {
        // req-110 P1-4：任意账号可删（含最后一个），卡片严格跟随账号生命周期。
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);

        Action act = () => svc.RemoveAccount("minimax", "default");
        act.Should().NotThrow("req-110：最后一个账号也允许删除");

        svc.GetAccounts("minimax").Should().BeEmpty();
        // 级联清理：账号级（二段）与卡片级（三段）定制全部移除
        svc.Settings.AccountCustomizations.Keys
            .Should().NotContain(k => k.StartsWith("minimax:default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveAccount_SecondAccount_Succeeds_AndCleansLoginState()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        var acc2 = svc.AddAccount("minimax", "私人号");

        // 模拟该账号有登录态
        svc.Settings.PersistedLoginStates.Add(new LoginStateInfo
        {
            ProviderId = "minimax",
            AccountId = acc2.AccountId,
            AcquiredAt = DateTime.Now
        });

        svc.RemoveAccount("minimax", acc2.AccountId);

        svc.GetAccounts("minimax").Should().HaveCount(1);
        svc.Settings.PersistedLoginStates.Should().NotContain(s => s.AccountId == acc2.AccountId);
    }

    [Fact]
    public void RemoveAccount_NonExistent_NoOp()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        Action act = () => svc.RemoveAccount("minimax", "ghost");
        act.Should().NotThrow();
    }

    // ===== Card CRUD =====

    [Fact]
    public void AddAccount_AutoCreates_DefaultCard()
    {
        // req-110 P1-1："有账号必有卡片"不变量——建号同时实例化首张卡片。
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);

        var cards = svc.GetCards("minimax", "default");
        cards.Should().ContainSingle().Which.CardId.Should().Be("default-card");
    }

    [Fact]
    public void EnsureAccount_AutoCreates_DefaultCard()
    {
        // req-110 P1-1：EnsureAccount 建号同样维护"有账号必有卡片"不变量。
        var svc = CreateConfigService();
        svc.EnsureAccount("minimax", "927d7188aa79eab9");

        svc.GetCards("minimax", "927d7188aa79eab9")
            .Should().ContainSingle().Which.CardId.Should().Be("default-card");
    }

    [Fact]
    public void AddCard_AfterAutoDefaultCard_GeneratesUniqueId()
    {
        // req-110：AddAccount 已自动建 default-card，后续 AddCard 从 card-2 起号。
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);

        var card = svc.AddCard("minimax", "default", "API 用量");
        card.CardId.Should().NotBe("default-card");
        card.Title.Should().Be("API 用量");
        card.DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void AddCard_SecondCard_GeneratesUniqueId()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        var card1 = svc.AddCard("minimax", "default", "API 用量");

        var card2 = svc.AddCard("minimax", "default", "订阅用量");
        card2.CardId.Should().NotBe("default-card");
        card2.CardId.Should().NotBe(card1.CardId);
        card2.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public void GetCards_OrdersByDisplayOrder()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        var c1 = svc.AddCard("minimax", "default", "First");
        var c2 = svc.AddCard("minimax", "default", "Second");
        var c3 = svc.AddCard("minimax", "default", "Third");

        // 按 DisplayOrder 返回（首位为自动创建的 default-card）
        var cards = svc.GetCards("minimax", "default");
        cards.Select(c => c.CardId).Should().ContainInOrder("default-card", c1.CardId, c2.CardId, c3.CardId);
    }

    [Fact]
    public void UpdateCard_ModifiesExisting()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        var card = svc.AddCard("minimax", "default", "Original");

        card.Title = "Updated";
        card.Customization.VisibleCharts = new System.Collections.Generic.List<string> { "Line" };
        svc.UpdateCard("minimax", "default", card);

        // req-110：账号名下还有自动创建的 default-card，按 CardId 精确查找
        var reloaded = svc.GetCards("minimax", "default").First(c => c.CardId == card.CardId);
        reloaded.Title.Should().Be("Updated");
        reloaded.Customization.VisibleCharts.Should().BeEquivalentTo(new[] { "Line" });
    }

    [Fact]
    public void RemoveCard_RemovesAndCleansAccountCustomization()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        var card = svc.AddCard("minimax", "default", "API");
        card.Customization.VisibleCharts = new System.Collections.Generic.List<string> { "Bar" };
        svc.UpdateCard("minimax", "default", card);

        svc.RemoveCard("minimax", "default", card.CardId);

        // req-110：自动创建的 default-card 仍在，仅新增卡被移除
        svc.GetCards("minimax", "default").Should().ContainSingle().Which.CardId.Should().Be("default-card");
        // 卡片下的扁平字段也清理（按三段 key）
        svc.Settings.AccountCustomizations
            .Should().NotContainKey(AccountCustomization.MakeKey("minimax", "default", card.CardId));
    }

    [Fact]
    public void ReorderCards_UpdatesDisplayOrder()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null); // 自动创建 default-card（参与排序）
        var c0 = svc.GetCards("minimax", "default").Single(); // default-card
        var c1 = svc.AddCard("minimax", "default", "First");
        var c2 = svc.AddCard("minimax", "default", "Second");

        // 反向排序：Second → First → default-card
        svc.ReorderCards("minimax", "default", new[] { c2.CardId, c1.CardId, c0.CardId });

        var cards = svc.GetCards("minimax", "default");
        cards[0].CardId.Should().Be(c2.CardId);
        cards[1].CardId.Should().Be(c1.CardId);
        cards[2].CardId.Should().Be(c0.CardId);
        cards[0].DisplayOrder.Should().Be(0);
        cards[1].DisplayOrder.Should().Be(1);
        cards[2].DisplayOrder.Should().Be(2);
    }

    // ===== 三段 key 集成 =====

    [Fact]
    public void GetEffectiveAccountCustomization_ThreeSegmentKey_IsolatedPerCard()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);
        svc.AddCard("minimax", "default", "API");

        var apiConfig = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "Line" }
        };
        svc.SetCardChartConfiguration("minimax", apiConfig, "default", "default-card");

        var subConfig = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "Bar" }
        };
        svc.SetCardChartConfiguration("minimax", subConfig, "default", "card-2");

        var effApi = svc.GetEffectiveAccountCustomization("minimax", "default", "default-card");
        var effSub = svc.GetEffectiveAccountCustomization("minimax", "default", "card-2");

        effApi.VisibleCharts.Should().BeEquivalentTo(new[] { "Line" });
        effSub.VisibleCharts.Should().BeEquivalentTo(new[] { "Bar" });
    }

    // ===== 验收 #10：AuthManager / LoginStateInfo 不被 CardId 触及 =====

    [Fact]
    public void LoginStateInfo_RemainsTwoSegment_NotAffectedByCardId()
    {
        var svc = CreateConfigService();
        svc.AddAccount("minimax", null);

        // 模拟登录态（认证层仅 2 段）
        svc.Settings.PersistedLoginStates.Add(new LoginStateInfo
        {
            ProviderId = "minimax",
            AccountId = "default",  // 仅 2 段，无 CardId
            AcquiredAt = DateTime.Now
        });

        // 添加卡片不影响登录态
        svc.AddCard("minimax", "default", "API");
        svc.AddCard("minimax", "default", "订阅");

        // 登录态元数据仍仅 1 条（认证层未引入 CardId 维度）
        svc.Settings.PersistedLoginStates.Should().HaveCount(1);
        svc.Settings.PersistedLoginStates[0].ProviderId.Should().Be("minimax");
        svc.Settings.PersistedLoginStates[0].AccountId.Should().Be("default");
    }
}