# 贡献指南

欢迎通过 Issue 反馈问题、提出建议，或提交 Pull Request。交流与文档默认使用中文。

## 开始开发

需要 Windows 10/11 x64 与 .NET 9 SDK。`global.json` 允许使用已安装的最新 .NET 9 稳定版功能带。

```powershell
dotnet restore HttpTester.sln --artifacts-path artifacts/build
dotnet build HttpTester.sln -c Release --artifacts-path artifacts/build
pwsh -NoProfile -File scripts/test.ps1
```

测试是无第三方测试框架的可执行项目，请使用 `scripts/test.ps1`；`dotnet test` 不会执行当前这些检查。核心测试只使用本机 HTTP/TLS 服务。界面测试在临时目录运行并离屏渲染，不改写个人配置，也不控制桌面。

## 修改约定

- 从 `main` 创建有明确用途的分支，例如 `feat/request-search`、`fix/workspace-save` 或 `docs/quick-start`。
- 一个 PR 解决一个清晰的问题，说明变更后的行为和实际验证结果。
- 按 `.editorconfig` 保存 UTF-8 文本；保留项目现有代码风格，避免无关的批量格式化。
- 网络与存储逻辑放在 `HttpTester.Core`，WPF 交互和绘制放在 `HttpTester.App`。
- 数据格式和保存路径有变化时，应考虑旧配置兼容、备份、失败恢复与已有数据保护。
- 功能或修复需要有针对性的验证。界面变更附演示截图，并避免使用个人真实请求数据。
- 保留轻量、快速启动的目标；新增依赖时说明必要性与对体积、启动和内存的影响。

## 数据与安全

不要提交 `data`、请求导出文件、令牌、Cookie、密码、私钥或本机绝对路径。提交前检查 `git diff --cached`。复现请求请使用示例字段和 `localhost`，文件上传使用临时测试文件。

安全问题请按 [SECURITY.md](SECURITY.md) 报告。

## 提交与 PR

提交说明建议使用 `feat:`、`fix:`、`docs:`、`test:`、`build:` 或 `chore:` 前缀，正文解释有必要说明的原因与验证。提交 PR 前完成：

```powershell
git diff --check
pwsh -NoProfile -File scripts/test.ps1
```

涉及发布脚本时，运行两种打包方式并检查 ZIP 不含 `data`。使用 [维护说明](docs/MAINTAINING.md) 中的命令。

提交贡献表示你有权提供相关内容，并同意按本项目的 [MIT 许可证](LICENSE) 分发该贡献。
