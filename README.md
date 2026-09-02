# UsageMonitor Provider Plugin: 

Kimi (Moonshot) plugin for the UsageMonitor ecosystem.

> Independent plugin repository. Independently versioned and released.

## Plugin Metadata

| Field | Value |
|---|---|
| Plugin type | `provider` |
| Provider ID | `kimi` |
| Display name |  |
| Min SDK version | `0.45.0` |
| Credential domains | `` |

## Repository Structure

```
.
├── README.md
├── defaults.json   # 插件清单（chartstyle/ministyle 为对应 json）
├── i18n/           # 多语言包
│   ├── zh-CN.json
│   └── en-US.json
├── assets/         # 图标等资源
├── CHANGELOG.md    # 插件独立变更日志
└── LICENSE-APACHE  # Apache License 2.0
```

## Versioning & Release

- 独立版本化：tag `v<semver>` → 独立 zip → 独立 Release
- 版本号变更流程：在主项目目录运行

  ```powershell
  .\scripts\bump-plugin.ps1 -Type provider -Id kimi -Version <X.Y.Z>
  ```

## License

Licensed under **Apache License 2.0** (`LICENSE-APACHE`), consistent with the UsageMonitor SDK / declaration pack licensing.
