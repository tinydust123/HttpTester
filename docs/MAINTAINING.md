# 维护与发布

## 仓库信息

- 名称：Http探测器（代码工程名 HttpTester）。
- 建议仓库描述：轻量 Windows HTTP/HTTPS 接口测试工具，支持工作区、JSON 高亮和便携数据保存。
- 建议 Topics：`windows`、`wpf`、`dotnet`、`http-client`、`api-testing`、`developer-tools`。
- 默认分支：`main`。
- 许可证：[MIT](../LICENSE)。

本地仓库不预设远程地址。创建空 GitHub 仓库后，在本机使用实际仓库地址执行 `git remote add origin <实际仓库地址>`，再执行 `git push -u origin main`。仓库已有内容时，先检查远程历史再决定如何同步。

在 GitHub 中启用 Actions 后，`CI / windows` 会执行构建、测试和打包。可将该检查配置为 `main` 的合并要求；私密漏洞报告需要在仓库设置中单独启用。

## 本地检查

```powershell
dotnet build HttpTester.sln -c Release --artifacts-path artifacts/build
pwsh -NoProfile -File scripts/test.ps1
git diff --check
git status --short
```

## 制作分发包

```powershell
pwsh -NoProfile -File scripts/publish.ps1 -Package
pwsh -NoProfile -File scripts/publish.ps1 -Standalone -Package
```

脚本从项目文件读取版本。程序仍输出到 `artifacts/v<主版本>.<次版本>/portable` 或 `standalone`，ZIP 和 `.sha256` 输出到 `artifacts/packages`。压缩包仅包含 EXE、README、LICENSE、CHANGELOG，以及独立版所用运行时的许可文件；不会递归打包发布目录。

用户 `data` 可能就在发布目录下，**不要把整个发布目录拖进 ZIP，也不要将它提交到仓库**。CI 只上传脚本生成的 ZIP 和校验文件。

独立版许可文件从实际还原的 .NET Runtime 与 Windows Desktop Runtime NuGet 包收集，避免使用开发机上其他 SDK 版本的声明。

发布前：

1. 更新项目 `Version`、界面版本文字与 [CHANGELOG.md](../CHANGELOG.md)。
2. 执行测试和两种打包，解压检查程序、文档、许可和 ZIP 清单。
3. 校验 SHA-256；在另一目录试运行，并确认 `data` 的创建与保存位置。
4. 提交版本变更后，可创建本地标签 `git tag -a v<版本> -m "发布 <版本>"`。
5. 使用项目实际 GitHub 仓库推送标签，再从 Releases 创建发布并上传 ZIP 与 `.sha256`；这不是本地打包脚本的自动行为。

## 依赖与资源

普通构建只需要 .NET SDK，不使用第三方 NuGet 业务依赖。重新生成图标尺寸需要 Python 和 Pillow；图标原图及提示词保存在 [Assets/ICON.md](../src/HttpTester.App/Assets/ICON.md)。

GitHub Actions 使用官方 Action 并固定提交 SHA，Dependabot 定期检查这些版本。维护者应审核更新后再合并。

参考：[GitHub .NET 构建指南](https://docs.github.com/en/actions/tutorials/build-and-test-code/net)、[Issue 表单格式](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms)、[Actions 安全用法](https://docs.github.com/en/actions/reference/security/secure-use)。
