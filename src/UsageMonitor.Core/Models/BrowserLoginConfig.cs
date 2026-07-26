// SPDX-License-Identifier: Apache-2.0
// 插件 SDK 契约文件：本文件按 Apache License 2.0 授权（见仓库根目录 LICENSE-APACHE），
// 供第三方插件开发自由引用；仓库其余部分适用 BSL 1.1（见 LICENSE）。
using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 娴忚鍣ㄧ櫥褰曡姹傛ā鍨?- 鎻忚堪濡備綍涓轰竴涓?AI 鏈嶅姟鍟嗚幏鍙栫櫥褰曟€?Cookie銆?
/// <para>
/// 鍙傝€冮攢椤规暟鎹姪鎵嬮」鐩?<c>browser-cookie-manager</c> Skill 鐨勮璁℃€濊矾锛?
/// 鎻掍欢閫氳繃 <see cref="ProviderId"/> + <see cref="LoginUrl"/> + <see cref="CookieDomainFilters"/>
/// 澹版槑鑷繁鐨勭櫥褰曢渶姹傦紝<c>BrowserLoginService</c> 鎹鍚姩涓存椂 Edge 绐楀彛骞堕€氳繃
/// Chrome DevTools Protocol 鎻愬彇鏄庢枃 Cookie銆?
/// </para>
/// <para>
/// 浣跨敤绀轰緥锛圡iniMax锛夛細
/// <code>
/// var cfg = new BrowserLoginConfig
/// {
///     ProviderId = "MiniMax",
///     LoginUrl = "https://platform.minimaxi.com",
///     CookieDomainFilters = new[] { "minimaxi.com" },
/// };
/// var cookie = await BrowserLoginService.LoginAndExtractCookieAsync(cfg, ct);
/// </code>
/// </para>
/// </summary>
public class BrowserLoginConfig
{
    /// <summary>鎵€灞炴彃浠?/ 鏈嶅姟鍟?ID锛堝 "MiniMax"銆?DeepSeek"锛?/summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>鐧诲綍鍏ュ彛 URL锛屾柊绐楀彛鑷姩鎵撳紑姝ゅ湴鍧€锛堝 https://platform.minimaxi.com锛?/summary>
    public string LoginUrl { get; set; } = string.Empty;

    /// <summary>
    /// 鍒ゅ畾鐧诲綍鎴愬姛鐨?Cookie 鍩熷悕杩囨护鍒楄〃锛堜换涓€鍛戒腑鍗宠涓哄凡鐧诲綍锛夈€?
    /// 渚嬪 <c>{ "minimaxi.com", "api.minimaxi.com" }</c>
    /// </summary>
    public IReadOnlyList<string> CookieDomainFilters { get; set; } = new List<string>();

    /// <summary>
    /// 绛夊緟鐢ㄦ埛瀹屾垚鐧诲綍鐨勮秴鏃舵椂闂淬€傞粯璁?2 鍒嗛挓锛堜笌閿€椤规暟鎹姪鎵嬬殑 5 鍒嗛挓鐩告瘮鏇寸揣鍑戯紝
    /// 閬垮厤鐢ㄦ埛鍦?UI 涓婇暱鏃堕棿绛夊緟锛夈€?
    /// </summary>
    public TimeSpan LoginTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 鍚姩鏃朵娇鐢ㄧ殑 DevTools 绔彛銆傞粯璁?0 琛ㄧず鑷姩鍒嗛厤绌洪棽绔彛锛岄伩鍏嶄笌宸叉湁 Edge 鍐茬獊銆?
    /// </summary>
    public int DevToolsPort { get; set; } = 0;

    /// <summary>
    /// 鐧诲綍鎸夐挳 / 鏍囩鐨勬彁绀烘枃鏈紝鐢ㄤ簬鍦?UI 涓婂睍绀虹粰鐢ㄦ埛銆?
    /// 渚嬪 "馃寪 鑾峰彇 MiniMax 鐧诲綍鎬?銆?
    /// </summary>
    public string? UiButtonText { get; set; }

    /// <summary>
    /// 楠岃瘉 Cookie 鏄惁浠嶆湁鏁堟椂浣跨敤鐨勫彲閫?HTTP 娴嬭瘯 URL銆?
    /// 璁剧疆鍚?<c>BrowserLoginService.CheckCookieValidAsync</c> 浼氶€氳繃 GET 姝?URL 骞?
    /// 妫€鏌ョ姸鎬佺爜锛?00=鏈夋晥锛?01/302/303=宸茶繃鏈燂紙涓庨攢椤规暟鎹姪鎵嬪垽瀹氫竴鑷达級銆?
    /// </summary>
    public string? ValidateUrl { get; set; }

    /// <summary>
    /// 涓ユ牸鐧诲綍鍒ゅ畾鐨勫叧閿?Cookie 鍚嶇О鍒楄〃锛堝彲閫夛級銆?
    /// <para>
    /// 鑳屾櫙锛氭煇浜涘ぇ妯″瀷缃戦〉锛堝 MiniMax锛夎惤鍦伴〉鏈韩灏辨湁 <c>_oauth_state</c>銆?
    /// <c>sensorsdata*</c> 绛?璺熻釜 Cookie"锛屼粎闈?<see cref="CookieDomainFilters"/>
    /// 浼氬鑷磋惤鍦伴〉鎵撳紑鐨勭灛闂村氨璇垽涓?鐧诲綍鎴愬姛"銆傛瀛楁澹版槑蹇呴』瀛樺湪鐨勫叧閿細璇?Cookie锛?
    /// 鍏ㄩ儴瀛樺湪鎵嶈涓虹敤鎴峰凡鐪熸鐧诲綍銆?
    /// </para>
    /// <para>
    /// 浣跨敤绀轰緥锛圡iniMax锛夛細
    /// <code>
    /// RequiredCookieNames = new[] { "acw_tc" }  // 闃块噷浜?WAF 浼氳瘽 Cookie锛岀湡姝ｇ殑鐧诲綍鍑瘉
    /// </code>
    /// </para>
    /// <para>
    /// 鐣欑┖锛堥粯璁わ級琛ㄧず涓嶆鏌ワ紝琛屼负閫€鍖栦负浠?<see cref="CookieDomainFilters"/> 鍒ゅ畾锛?
    /// 淇濇寔鍚戝悗鍏煎銆?
    /// </para>
    /// </summary>
    public IReadOnlyList<string> RequiredCookieNames { get; set; } = new List<string>();

    /// <summary>
    /// 涓ユ牸鐧诲綍鍒ゅ畾鐨勫叧閿煙鍚嶏紙鍙€夛級銆傝鍩熷悕涓嬪繀椤绘湁 Cookie 鎵嶇畻鐧诲綍鎴愬姛銆?
    /// <para>
    /// 渚嬪 MiniMax 鐨勭湡姝ｇ櫥褰曚細璇濆湪 <c>account.minimaxi.com</c> 鍩燂紝鑰岃惤鍦伴〉鍦?
    /// <c>platform.minimaxi.com</c>銆傝缃瀛楁鍚庯紝<see cref="BrowserLoginService"/>
    /// 浼氭鏌ヨ鍩熷悕涓嬫槸鍚﹀瓨鍦?Cookie锛堜换鎰忓悕绉帮級銆?
    /// </para>
    /// <para>
    /// 鐣欑┖锛堥粯璁わ級琛ㄧず涓嶆鏌ワ紝淇濇寔鍚戝悗鍏煎銆?
    /// </para>
    /// </summary>
    public string? RequiredCookieDomain { get; set; }

    /// <summary>
    /// 涓ユ牸鐧诲綍鍒ゅ畾鐨?URL 鍏抽敭瀛楀垪琛紙鍙€夛紝鎺ㄨ崘浣跨敤锛夈€?
    /// <para>
    /// 鑳屾櫙锛氫緷璧栫壒瀹?Cookie 鍚嶏紙濡?<c>acw_tc</c>锛夌殑鍒ゅ畾鏂瑰紡涓嶅椴佹鈥斺€擟ookie 鍚嶅彲鑳藉洜
    /// 鏈嶅姟鍟嗗悗绔敼鍔ㄨ€屽彉鍖栥€傛敼鐢?URL 鍏抽敭瀛楀垽瀹氭洿鍙潬锛氱櫥褰曢〉閫氬父 URL 鍖呭惈
    /// <c>login</c>/<c>oauth</c>/<c>auth</c>/<c>unified-login</c> 绛夊叧閿瓧锛?
    /// 鐧诲綍鎴愬姛鍚庝細璺冲洖涓婚〉锛圲RL 涓嶅啀鍖呭惈杩欎簺鍏抽敭瀛楋級銆?
    /// </para>
    /// <para>
    /// 璁剧疆鍚庯紝<see cref="BrowserLoginService"/> 浼氭鏌ユ墍鏈?CDP 椤甸潰鐨?URL锛?
    /// <list type="bullet">
    ///   <item>URL 涓嶅寘鍚?<see cref="LoginUrlKeywords"/> 涓换鎰忓叧閿瓧</item>
    ///   <item>URL 鍩熷悕鍖归厤 <see cref="LoginUrl"/> 鐨?host</item>
    /// </list>
    /// 涓や釜鏉′欢閮芥弧瓒虫墠璁や负鐧诲綍鎴愬姛銆?
    /// </para>
    /// <para>
    /// 浣跨敤绀轰緥锛圡iniMax锛夛細
    /// <code>
    /// LoginUrlKeywords = new[] { "login", "oauth", "auth", "unified-login", "passport" }
    /// </code>
    /// </para>
    /// <para>
    /// 鐣欑┖锛堥粯璁わ級鍒欏彧渚濊禆 Cookie 鍒ゅ畾锛屼繚鎸佸悜鍚庡吋瀹广€?
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LoginUrlKeywords { get; set; } = new List<string>();

    /// <summary>
    /// 鐧诲綍鎴愬姛鏃?URL 搴斿尮閰嶇殑 host锛堝彲閫夛級銆?
    /// <para>
    /// 濡傛灉璁剧疆锛孶RL 鍏抽敭瀛楁鏌ラ€氳繃鍚庤繕浼氶獙璇?URL 鐨?host 蹇呴』绛変簬姝ゅ€笺€?
    /// 榛樿浠?<see cref="LoginUrl"/> 鎻愬彇锛堝 <c>platform.minimaxi.com</c>锛夈€?
    /// </para>
    /// </summary>
    public string? LoginSuccessHost { get; set; }

    /// <summary>
    /// 已登录主机的 host（可选）。默认从 <see cref="LoginUrl"/> 提取。
    /// <para>
    /// 用于明确判定"已登录"：URL host 必须等于此值，且 path 非根路径（避免把落地页误判为已登录）。
    /// </para>
    /// </summary>
    public string? LoggedInHost { get; set; }

    /// <summary>
    /// 已登录 URL path 必须包含的关键字（任一命中即可，可选）。
    /// <para>
    /// 例如 MiniMax：登录成功后会跳转到 /console/plan 或 /user-center/payment，
    /// 这些 path 才算"已登录"。避免落地页 / 被误判为已登录。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LoggedInPathKeywords { get; set; } = new List<string>();
}