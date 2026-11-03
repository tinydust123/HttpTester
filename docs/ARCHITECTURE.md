# 项目结构

## 目录

```text
src/
  HttpTester.App/          WPF 桌面界面、窗口控件和 JSON 可见区域绘制
  HttpTester.Core/         HTTP 执行、持久化、工作区、迁移和 JSON 分词
tests/
  HttpTester.Tests/        网络、存储、迁移和数据格式检查
  HttpTester.UiTests/      隔离窗口逻辑及离屏布局检查
scripts/                   本地服务、测试、图标和发布脚本
docs/                      架构、维护说明与演示图片
```

`HttpTester` 是历史工程名，产品和 EXE 名称为“Http探测器”。

## 运行分层

1. `MainWindow` 收集界面输入并构造 `RequestProfile`。
2. `RequestRunner` 验证 URL、附加查询参数、设置请求体和认证，再通过 `HttpClient` 发送请求。
3. 完成后界面显示状态、耗时、大小、响应头和正文；JSON 格式化走 `JsonSyntax`，只对可见文本绘制颜色。
4. `ProfileStore` 通过临时文件和原子替换保存请求，`WorkspaceStore` 管理各工作区目录和当前选择。

## 数据边界

数据根目录固定为 `AppContext.BaseDirectory/data`，不受进程当前工作目录影响。默认工作区使用 `requests.json`；其他工作区位于 `workspaces/<GUID>/requests.json`。请求集合和导出文件为明文，因此 `data`、`*.bak`、导出 JSON 与凭据文件均在 `.gitignore` 中排除。

首次启动且 `data` 不存在时，`AppDataDirectory` 可从旧版用户目录迁移数据。迁移先写入暂存目录，校验源快照未变化，再整体切换到目标目录；失败会清理本次暂存内容，原数据和其他已有目录不受影响。

## 扩展与验证

新增请求方法或请求体类型时，需要同步更新存储校验、界面选项和测试。涉及持久化格式的变化必须继续加载已有文件，并保留损坏文件保护。发布前运行完整脚本，不要用只跑部分测试代替发布验收。
