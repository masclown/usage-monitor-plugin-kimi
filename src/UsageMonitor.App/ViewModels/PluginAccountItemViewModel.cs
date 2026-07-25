using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Services.Auth;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// S1：插件管理页「账号行」视图模型。
/// <para>封装单个账号的启用开关、昵称编辑（失焦保存）、API/Sub 状态灯、配置入口与删除行为。
/// 状态灯与账号行为全部收敛在此类，避免单账号异常扩散到整页（所有外部读取均带 try/catch 防护）。</para>
/// </summary>
public class PluginAccountItemViewModel : INotifyPropertyChanged
{
    private readonly Account _account;
    private readonly string _providerId;
    private readonly ConfigService _configService;
    private readonly AuthManager? _authManager;
    private readonly PluginItemViewModel _parent;

    private bool _isEnabled;
    private string _nickname = string.Empty;
    private string _nicknameError = string.Empty;
    private bool _hasApiKey;
    private bool _hasLoginState;

    /// <summary>账号 ID（Provider 内唯一，只读展示）。</summary>
    public string AccountId => _account.AccountId;

    /// <summary>是否默认账号（用于展示「默认」标记）。</summary>
    public bool IsDefault => _account.IsDefault;

    /// <summary>
    /// 账号启用状态（绑定账号行启用 CheckBox，改动即 UpdateAccount 持久化并触发 ConfigChanged）。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            _account.Enabled = value;
            OnPropertyChanged();
            PersistAccount("保存账号启用状态");
            // 启用状态影响「可用账号」绿点，通知父项刷新汇总
            _parent.NotifyAccountSummaryChanged();
        }
    }

    /// <summary>
    /// 账号昵称（绑定可编辑 TextBox，实时校验唯一性，失焦时才落盘）。
    /// <para>setter 仅做同 Provider 内唯一性校验并刷新 <see cref="NicknameError"/>，不写盘；
    /// 落盘由 <see cref="CommitNickname"/>（失焦回调）完成，重名时拒绝保存。</para>
    /// </summary>
    public string Nickname
    {
        get => _nickname;
        set
        {
            if (_nickname == value) return;
            _nickname = value;
            OnPropertyChanged();
            ValidateNickname();
        }
    }

    /// <summary>昵称校验错误信息（空=通过；非空=重名等错误，拒绝保存并供 UI 红字提示）。</summary>
    public string NicknameError
    {
        get => _nicknameError;
        private set { if (_nicknameError != value) { _nicknameError = value; OnPropertyChanged(); } }
    }

    /// <summary>API 状态灯：该 Provider 已配置 API Key 时亮（当前 API Key 为 Provider 级存储，同 Provider 账号共享此状态）。</summary>
    public bool HasApiKey
    {
        get => _hasApiKey;
        private set { if (_hasApiKey != value) { _hasApiKey = value; OnPropertyChanged(); } }
    }

    /// <summary>Sub 状态灯：AuthManager 中该账号存在登录态时亮。</summary>
    public bool HasLoginState
    {
        get => _hasLoginState;
        private set { if (_hasLoginState != value) { _hasLoginState = value; OnPropertyChanged(); } }
    }

    /// <summary>打开该账号所属插件的配置窗口（携带 AccountId，使图表/迷你图表启用开关按当前账号生效）。</summary>
    public IRelayCommand ConfigCommand { get; }

    /// <summary>删除该账号（确认后调用 RemoveAccount）。</summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>
    /// 创建账号行视图模型。直接初始化 backing 字段，避免触发 setter 内的持久化逻辑。
    /// </summary>
    public PluginAccountItemViewModel(Account account, string providerId, ConfigService configService,
        AuthManager? authManager, PluginItemViewModel parent)
    {
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _providerId = providerId;
        _configService = configService;
        _authManager = authManager;
        _parent = parent;

        _isEnabled = account.Enabled;
        _nickname = account.Nickname ?? string.Empty;

        // 账号行配置入口：携带 AccountId 调用父项 OpenConfigDialog，确保配置窗口按当前账号生效
        ConfigCommand = new RelayCommand(() => _parent.OpenConfigDialog(AccountId));
        DeleteCommand = new RelayCommand(DeleteAccount);

        RefreshStatus();
    }

    /// <summary>
    /// 刷新 API / Sub 状态灯。读取 SecretStore（经 ProviderConfig）与 AuthManager 登录态，
    /// 均带异常防护，单项失败仅置灰不抛出。
    /// </summary>
    public void RefreshStatus()
    {
        // API 灯：req-110 P2-4 账号级——读账号生效配置（账号级凭据覆盖 + Provider 级回退）
        try
        {
            var config = _configService.GetEffectiveAccountConfig(_providerId, _account.AccountId);
            HasApiKey = !string.IsNullOrWhiteSpace(config.GetValue("ApiKey"));
        }
        catch
        {
            HasApiKey = false;
        }

        // Sub 灯：AuthManager 中该账号是否有登录态记录
        try
        {
            HasLoginState = _authManager?.GetLoginState(_providerId, _account.AccountId) != null;
        }
        catch
        {
            HasLoginState = false;
        }
    }

    /// <summary>持久化当前账号（UpdateAccount 内部 Save 会触发 ConfigChanged，复用既有刷新链路）。</summary>
    private void PersistAccount(string action)
    {
        try
        {
            _configService.UpdateAccount(_account);
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginAccountItem", $"{action}失败：{_providerId}:{_account.AccountId}", ex);
        }
    }

    /// <summary>
    /// S1：昵称唯一性实时校验（同 Provider 内，忽略大小写，以当前编辑值为准）。
    /// <para>空昵称始终合法；与其他账号重名时填充 <see cref="NicknameError"/>。</para>
    /// </summary>
    private void ValidateNickname()
    {
        var trimmed = _nickname?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            NicknameError = string.Empty;
            return;
        }
        var duplicate = _parent.Accounts.Any(a =>
            !ReferenceEquals(a, this) &&
            string.Equals(a.Nickname?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        NicknameError = duplicate ? "昵称已被同 Provider 下其他账号使用" : string.Empty;
    }

    /// <summary>S1：重新执行昵称校验（供兄弟账号昵称落定后批量复检、清除过期错误）。</summary>
    public void RevalidateNickname() => ValidateNickname();

    /// <summary>
    /// S1：提交昵称（TextBox 失焦时调用）。校验通过才落盘；重名时拒绝保存并保留错误提示。
    /// <para>非空昵称自动置 UseNickname=true 使卡片标题使用昵称；清空则置 false 回退 Provider 名。</para>
    /// </summary>
    public void CommitNickname()
    {
        if (!string.IsNullOrEmpty(NicknameError)) return;
        var trimmed = _nickname?.Trim();
        var newNick = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        if (!string.Equals(_account.Nickname, newNick, StringComparison.Ordinal))
        {
            _account.Nickname = newNick;
            _account.UseNickname = !string.IsNullOrWhiteSpace(trimmed);
            PersistAccount("保存账号昵称");
            // 本账号昵称落定后复检其他账号，清除因本次变更产生的过期重名错误
            _parent.RevalidateAllNicknames();
        }
    }

    /// <summary>
    /// 删除账号（req-110 P1-4 / Q2）：弹确认框询问历史数据删/保——
    /// [是] 删账号 + 删历史数据；[否] 删账号但保留历史（重建同一网页账号可经 BoundStableId 重新关联）；[取消] 不删。
    /// 任意账号可删（含最后一个）；该 Provider 已无剩余账号时选删数据会连 Provider 级历史表一并清理。
    /// </summary>
    private void DeleteAccount()
    {
        var displayName = string.IsNullOrWhiteSpace(_nickname) ? _account.AccountId : _nickname;
        var result = System.Windows.MessageBox.Show(
            $"确定删除账号「{displayName}」吗？\n\n" +
            "【是】删除账号，并删除其历史数据\n" +
            "【否】删除账号，但保留历史数据（重建同一网页账号可重新关联）\n" +
            "【取消】不删除",
            "删除账号",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes && result != MessageBoxResult.No) return;

        try
        {
            // 先捕获绑定哈希（RemoveAccount 后配置中已无法取到）
            var boundStableId = _account.BoundStableId;
            _configService.RemoveAccount(_providerId, _account.AccountId);
            _parent.RemoveAccountItem(this);

            // 用户选"删除历史数据"：账号级清库；若该 Provider 已无剩余账号，Provider 级历史表一并清理。
            if (result == MessageBoxResult.Yes)
            {
                var providerId = _providerId;
                var accountId = _account.AccountId;
                var noAccountsLeft = _configService.GetAccounts(providerId).Count == 0;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        using var repo = UsageHistoryRepository.CreateDefault();
                        await repo.DeleteAccountDataAsync(providerId, accountId, boundStableId);
                        if (noAccountsLeft)
                            await repo.DeleteProviderDataAsync(providerId);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Error("PluginAccountItem", $"删除账号历史数据失败：{providerId}:{accountId}", ex);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginAccountItem", $"删除账号失败：{_providerId}:{_account.AccountId}", ex);
            System.Windows.MessageBox.Show($"删除账号失败：{ex.Message}", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
