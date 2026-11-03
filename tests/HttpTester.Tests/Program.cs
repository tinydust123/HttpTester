using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using HttpTester.Core;

var passed = 0;
var failures = new List<string>();
async Task Test(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception ex) { failures.Add(name); Console.WriteLine($"FAIL {name}: {ex}"); }
}
void Assert(bool ok, string message = "断言失败") { if (!ok) throw new Exception(message); }
async Task Throws<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T) { return; } throw new Exception($"预期 {typeof(T).Name}"); }
using var server = new LocalServer();
var runner = new RequestRunner();
RequestProfile Profile(string path = "/echo") => new() { Url = server.Url + path };
async Task<JsonElement> Echo(RequestProfile p)
{
    var result = await runner.SendAsync(p, default);
    Assert(result.StatusCode == 200);
    return JsonDocument.Parse(result.Body).RootElement.Clone();
}

await Test("HTTP GET、重复查询参数、中文与禁用字段", async () =>
{
    var p = Profile("/echo?original=1#fragment");
    p.Query.Add(new() { Key = "q", Value = "中文 & =" });
    p.Query.Add(new() { Key = "q", Value = "second" });
    p.Query.Add(new() { Key = "skip", Value = "no", Enabled = false });
    var data = await Echo(p);
    var target = data.GetProperty("Target").GetString()!;
    Assert(target.Contains("original=1&q=%E4%B8%AD%E6%96%87%20%26%20%3D&q=second") && !target.Contains("skip") && !target.Contains('#'), target);
});
await Test("POST/PUT/PATCH/DELETE/OPTIONS/TRACE 与自定义头", async () =>
{
    foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE" })
    {
        var p = Profile(); p.Method = method;
        p.Headers.Add(new() { Key = "X-Test", Value = "hello" });
        p.Headers.Add(new() { Key = "X-Skip", Value = "no", Enabled = false });
        var data = await Echo(p);
        Assert(data.GetProperty("Method").GetString() == method);
        Assert(data.GetProperty("Headers").GetProperty("X-Test").GetString() == "hello");
        Assert(!data.GetProperty("Headers").TryGetProperty("X-Skip", out _));
    }
});
await Test("HEAD 无响应体", async () => { var p = Profile(); p.Method = "HEAD"; var result = await runner.SendAsync(p, default); Assert(result.Bytes.Length == 0 && result.StatusCode == 200); });
await Test("JSON UTF-8 请求体与 Content-Type 覆盖", async () =>
{
    var p = Profile(); p.Method = "POST"; p.BodyKind = "JSON"; p.Body = "{\"你好\":42}";
    var data = await Echo(p);
    Assert(data.GetProperty("Body").GetString() == p.Body);
    Assert(data.GetProperty("Headers").GetProperty("Content-Type").GetString()!.StartsWith("application/json"));
    p.Headers.Add(new() { Key = "Content-Type", Value = "application/custom+json" });
    data = await Echo(p);
    Assert(data.GetProperty("Headers").GetProperty("Content-Type").GetString() == "application/custom+json");
});
await Test("纯文本与表单 URL 编码", async () =>
{
    var p = Profile(); p.Method = "POST"; p.BodyKind = "纯文本"; p.Body = "hello 世界";
    Assert((await Echo(p)).GetProperty("Body").GetString() == p.Body);
    p.BodyKind = "表单 URL 编码";
    p.Form.Add(new() { Key = "a", Value = "a b&c" });
    p.Form.Add(new() { Key = "a", Value = "2" });
    Assert((await Echo(p)).GetProperty("Body").GetString() == "a=a+b%26c&a=2");
});
await Test("Multipart 文本与真实文件上传", async () =>
{
    var path = Path.GetTempFileName();
    try
    {
        await File.WriteAllTextAsync(path, "FILE-CONTENT-123");
        var p = Profile(); p.Method = "POST"; p.BodyKind = "文件 / Multipart";
        p.Form.Add(new() { Key = "description", Value = "测试" });
        p.Form.Add(new() { Key = "file", Value = path, IsFile = true });
        var data = await Echo(p);
        Assert(data.GetProperty("Body").GetString()!.Contains("FILE-CONTENT-123"));
        Assert(data.GetProperty("Headers").GetProperty("Content-Type").GetString()!.Contains("boundary="));
        p.Headers.Add(new() { Key = "Content-Type", Value = "multipart/form-data" });
        await Throws<ArgumentException>(() => runner.SendAsync(p, default));
    }
    finally { File.Delete(path); }
});
await Test("Bearer 与 Basic 认证", async () =>
{
    var p = Profile(); p.AuthKind = "Bearer Token"; p.Token = "test-token";
    Assert((await Echo(p)).GetProperty("Headers").GetProperty("Authorization").GetString() == "Bearer test-token");
    p.AuthKind = "Basic Auth"; p.Username = "test"; p.Password = "secret";
    Assert((await Echo(p)).GetProperty("Headers").GetProperty("Authorization").GetString() == "Basic dGVzdDpzZWNyZXQ=");
});
await Test("非成功状态码仍显示响应", async () => { var result = await runner.SendAsync(Profile("/missing"), default); Assert(result.StatusCode == 404 && result.Body == "not found"); });
await Test("重定向开关", async () =>
{
    var p = Profile("/redirect");
    Assert((await runner.SendAsync(p, default)).StatusCode == 200);
    p.FollowRedirects = false;
    Assert((await runner.SendAsync(p, default)).StatusCode == 302);
});
await Test("gzip 自动解压与响应头", async () => { var result = await runner.SendAsync(Profile("/gzip"), default); Assert(result.Body == "gzip works 中文" && result.Headers.Contains("X-Server: local-test")); });
await Test("二进制响应原始字节", async () => { var result = await runner.SendAsync(Profile("/binary"), default); Assert(result.IsBinary && result.Bytes.SequenceEqual(new byte[] { 0, 1, 2, 255 })); });
await Test("10 MB 响应上限", async () => { var result = await runner.SendAsync(Profile("/big"), default); Assert(result.Truncated && result.Bytes.Length == RequestRunner.ResponseLimit); });
await Test("取消请求", async () => { using var cts = new CancellationTokenSource(100); await Throws<OperationCanceledException>(() => runner.SendAsync(Profile("/delay"), cts.Token)); });
await Test("请求超时", async () => { var p = Profile("/delay"); p.TimeoutSeconds = 1; await Throws<TimeoutException>(() => runner.SendAsync(p, default)); });
await Test("非法 URL 与请求头换行拒绝", async () =>
{
    foreach (var url in new[] { "file:///C:/test", "not a url", "ftp://localhost/file" })
        await Throws<ArgumentException>(() => runner.SendAsync(new() { Url = url }, default));
    var p = Profile(); p.Headers.Add(new() { Key = "X-Test", Value = "a\r\nb" });
    await Throws<ArgumentException>(() => runner.SendAsync(p, default));
});
await Test("真实 TLS HTTPS 连接及默认拒绝不可信证书", async () =>
{
    using var tls = new LocalServer(true);
    var p = new RequestProfile { Url = tls.Url + "/echo" };
    await Throws<HttpRequestException>(() => runner.SendAsync(p, default));
    // 仅测试客户端允许本次临时自签名证书，生产程序始终使用系统证书验证。
    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.Thumbprint == tls.Thumbprint };
    Assert((await runner.SendAsync(p, default, handler)).StatusCode == 200);
});
await Test("命名配置保存、重载、备份与克隆隔离", async () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "HttpTester-tests-" + Guid.NewGuid());
    var store = new ProfileStore(Path.Combine(directory, "requests.json"));
    try
    {
        Assert(store.Load().Count == 0);
        var p = Profile(); p.Name = "测试请求"; p.BodyKind = "JSON"; p.Body = "{}";
        p.Query.Add(new() { Key = "hello", Value = "中文", Enabled = false });
        p.Headers.Add(new() { Key = "Cookie", Value = "a=b" });
        p.Form.Add(new() { Key = "upload", IsFile = true, Value = "C:/test.txt" });
        p.AuthKind = "Basic Auth"; p.Username = "user"; p.Password = "pass"; p.FollowRedirects = false; p.TimeoutSeconds = 15;
        store.Save([p]);
        var loaded = store.Load().Single();
        Assert(JsonSerializer.Serialize(loaded) == JsonSerializer.Serialize(p));
        var clone = ProfileStore.Clone(loaded); clone.Query[0].Value = "changed";
        Assert(loaded.Query[0].Value == "中文");
        store.Save([clone]); Assert(File.Exists(store.Path + ".bak"));
        Assert(store.Load()[0].Query[0].Value == "changed");
        await File.WriteAllTextAsync(store.Path, "INVALID");
        await Throws<InvalidDataException>(() => Task.FromResult(store.Load()));
        Assert(await File.ReadAllTextAsync(store.Path) == "INVALID");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
await Test("损坏或缺字段的导入文件拒绝", async () =>
{
    foreach (var json in new[] { "null", "[null]", "[{\"Name\":\"\"}]", "[{\"Headers\":null}]", "[{\"Query\":[null]}]", "[{\"Method\":\"UNKNOWN\"}]", "[{\"TimeoutSeconds\":0}]" })
        await Throws<JsonException>(() => Task.FromResult(ProfileStore.Parse(json)));
});
await Test("工作区兼容旧请求文件，初始化不改写已有文件", async () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "HttpDetectorWorkspace-" + Guid.NewGuid());
    try
    {
        var legacy = new ProfileStore(Path.Combine(directory, "requests.json"));
        legacy.Save([new RequestProfile { Name = "旧版请求", Url = "http://localhost/old" }]);
        var before = await File.ReadAllBytesAsync(legacy.Path);
        var workspaces = new WorkspaceStore(legacy.Path);
        var catalog = workspaces.Load();
        Assert(catalog.ActiveWorkspaceId == Guid.Empty && catalog.Workspaces.Single().Name == "默认工作区");
        Assert(workspaces.GetProfileStore(Guid.Empty).Load().Single().Name == "旧版请求");
        Assert(before.SequenceEqual(await File.ReadAllBytesAsync(legacy.Path)) && !File.Exists(workspaces.Path));
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
await Test("工作区独立保存，同名同 ID 请求互不覆盖", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "HttpDetectorWorkspace-" + Guid.NewGuid());
    try
    {
        var workspaces = new WorkspaceStore(Path.Combine(directory, "requests.json"));
        var catalog = workspaces.Load();
        catalog = WorkspaceStore.Add(catalog, "开发 / 测试"); var development = catalog.ActiveWorkspaceId;
        catalog = WorkspaceStore.Add(catalog, "生产环境"); var production = catalog.ActiveWorkspaceId;
        var id = Guid.NewGuid();
        workspaces.GetProfileStore(development).Save([new RequestProfile { Id = id, Name = "同名请求", Url = "http://localhost/dev" }]);
        workspaces.GetProfileStore(production).Save([new RequestProfile { Id = id, Name = "同名请求", Url = "http://localhost/prod" }]);
        Assert(workspaces.GetProfileStore(development).Load().Single().Url.EndsWith("/dev"));
        Assert(workspaces.GetProfileStore(production).Load().Single().Url.EndsWith("/prod"));
        Assert(workspaces.GetProfileStore(Guid.Empty).Load().Count == 0);
        workspaces.Save(catalog); Assert(workspaces.Load().Workspaces.Count == 3);
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
await Test("工作区重命名保持数据路径、切换记忆与备份", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "HttpDetectorWorkspace-" + Guid.NewGuid());
    try
    {
        var workspaces = new WorkspaceStore(Path.Combine(directory, "requests.json"));
        var original = workspaces.Load();
        var catalog = WorkspaceStore.Add(original, "开发环境"); var id = catalog.ActiveWorkspaceId;
        Assert(original.Workspaces.Count == 1);
        var oldPath = workspaces.GetProfileStore(id).Path;
        workspaces.Save(catalog);
        catalog = WorkspaceStore.Rename(catalog, id, "重命名环境");
        workspaces.Save(catalog);
        Assert(workspaces.GetProfileStore(id).Path == oldPath && workspaces.Load().Workspaces.Single(w => w.Id == id).Name == "重命名环境");
        Assert(workspaces.Load().ActiveWorkspaceId == id && File.Exists(workspaces.Path + ".bak"));
        workspaces.Save(WorkspaceStore.Select(catalog, Guid.Empty));
        Assert(workspaces.Load().ActiveWorkspaceId == Guid.Empty);
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
await Test("工作区空名称、重名、过长名称与未知选择拒绝", async () =>
{
    var catalog = WorkspaceStore.Add(WorkspaceCatalog.CreateDefault(), "Dev");
    foreach (var name in new[] { "", "  ", "dev", new string('a', 41) })
        await Throws<ArgumentException>(() => Task.FromResult(WorkspaceStore.Add(catalog, name)));
    await Throws<ArgumentException>(() => Task.FromResult(WorkspaceStore.Rename(catalog, Guid.Empty, "DEV")));
    await Throws<ArgumentException>(() => Task.FromResult(WorkspaceStore.Select(catalog, Guid.NewGuid())));
    Assert(catalog.Workspaces.Count == 2);
});
await Test("工作区目录损坏拒绝加载且不覆盖原文件", async () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "HttpDetectorWorkspace-" + Guid.NewGuid());
    var workspaces = new WorkspaceStore(Path.Combine(directory, "requests.json"));
    Directory.CreateDirectory(directory);
    try
    {
        foreach (var json in new[] { "INVALID", "null", "{}", "{\"Workspaces\":null}", "{\"Workspaces\":[null]}" })
        {
            await File.WriteAllTextAsync(workspaces.Path, json);
            await Throws<InvalidDataException>(() => Task.FromResult(workspaces.Load()));
            Assert(await File.ReadAllTextAsync(workspaces.Path) == json);
        }
    }
    finally { Directory.Delete(directory, true); }
});
await Test("JSON 高亮类型识别、中文、转义与精确文本区间", () =>
{
    const string json = "{\"message\":\"中文和\\\"引号\\\"与\\\\路径\",\"count\":-1.2e+3,\"ok\":true,\"off\":false,\"empty\":null,\"list\":[1,2]}";
    var formatted = JsonSyntax.Format(json);
    Assert(formatted.Contains("中文") && formatted.Contains('\n'));
    var tokens = JsonSyntax.Tokenize(formatted);
    Assert(string.Concat(tokens.Select(t => formatted.Substring(t.Start, t.Length))) == formatted);
    Assert(tokens.Any(t => t.Kind == JsonTokenKind.Property && formatted.Substring(t.Start, t.Length) == "\"message\""));
    Assert(tokens.Any(t => t.Kind == JsonTokenKind.String && formatted.Substring(t.Start, t.Length).Contains("引号")));
    Assert(tokens.Count(t => t.Kind == JsonTokenKind.Boolean) == 2 && tokens.Count(t => t.Kind == JsonTokenKind.Null) == 1);
    Assert(tokens.Any(t => t.Kind == JsonTokenKind.Number && formatted.Substring(t.Start, t.Length).Contains("e+3")));
    using var parsed = JsonDocument.Parse(formatted);
    Assert(parsed.RootElement.GetProperty("message").GetString()!.Contains("\"引号\""));
    return Task.CompletedTask;
});
await Test("JSON 根值与超长字符串高亮不按字符建立 token", () =>
{
    foreach (var json in new[] { "true", "false", "null", "123", "\"hello\"", "[]", "{}" })
    {
        var formatted = JsonSyntax.Format(json);
        Assert(JsonSyntax.Tokenize(formatted).Sum(t => t.Length) == formatted.Length);
    }
    var large = JsonSyntax.Format("{\"value\":\"" + new string('a', 1024 * 1024) + "\"}");
    Assert(JsonSyntax.Tokenize(large).Count < 10);
    return Task.CompletedTask;
});
await Test("JSON 格式化拒绝非法响应", async () =>
{
    foreach (var json in new[] { "", "<html>not json</html>", "{broken", "{\"x\":true,}" })
        await Throws<JsonException>(() => Task.FromResult(JsonSyntax.Format(json)));
});
// 旧路径仅作为历史版本迁移的测试输入。
string LegacyDataPath(string root) => Path.Combine(root, "QingzhouHttp");
string MigrationTestRoot() => Path.Combine(Path.GetTempPath(), "HttpDetectorMigration-" + Guid.NewGuid().ToString("N"));
await Test("首次启动在指定程序目录下建立 data", () =>
{
    var root = MigrationTestRoot();
    try
    {
        var target = AppDataDirectory.Prepare(root, root);
        Assert(target == Path.Combine(root, "data") && Directory.Exists(target));
        Assert(!Directory.Exists(LegacyDataPath(root)));
        Assert(AppDataDirectory.CurrentPath == Path.Combine(AppContext.BaseDirectory, "data"));
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("旧版请求、工作区、备份与其他文件逐字节迁移且保留原目录", () =>
{
    var root = MigrationTestRoot();
    var legacy = LegacyDataPath(root);
    try
    {
        var oldStore = new ProfileStore(Path.Combine(legacy, "requests.json"));
        oldStore.Save([new RequestProfile { Name = "原请求", Url = "http://localhost/original" }]);
        oldStore.Save([new RequestProfile { Name = "最新请求", Url = "http://localhost/latest" }]);
        var workspaces = new WorkspaceStore(oldStore.Path);
        var catalog = WorkspaceStore.Add(workspaces.Load(), "迁移工作区");
        workspaces.Save(catalog); workspaces.Save(catalog);
        var workspaceStore = workspaces.GetProfileStore(catalog.ActiveWorkspaceId);
        workspaceStore.Save([new RequestProfile { Name = "工作区请求", BodyKind = "JSON", Body = "{\"中文\":true}" }]);
        workspaceStore.Save(workspaceStore.Load());
        Directory.CreateDirectory(Path.Combine(legacy, "empty-folder"));
        File.WriteAllBytes(Path.Combine(legacy, "extra-backup.bin"), [0, 1, 255, 128]);
        var files = Directory.GetFiles(legacy, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(legacy, p), File.ReadAllBytes);
        var target = AppDataDirectory.Prepare(root, root);
        foreach (var file in files)
        {
            Assert(file.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(target, file.Key))), file.Key);
            Assert(file.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(legacy, file.Key))), "原目录被修改");
        }
        Assert(Directory.Exists(Path.Combine(target, "empty-folder")));
        var migrated = new WorkspaceStore(Path.Combine(target, "requests.json"));
        Assert(migrated.Load().ActiveWorkspaceId == catalog.ActiveWorkspaceId);
        Assert(migrated.GetProfileStore(catalog.ActiveWorkspaceId).Load().Single().Body.Contains("中文"));
        Assert(new ProfileStore(Path.Combine(target, "requests.json")).Load().Single().Name == "最新请求");
        Assert(!Directory.EnumerateDirectories(root, "HttpDetector.migration-*").Any());
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("重复启动不覆盖新版保存或重新导入已删除的旧文件", () =>
{
    var root = MigrationTestRoot();
    var legacy = LegacyDataPath(root);
    try
    {
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "requests.json"), "old-data");
        File.WriteAllText(Path.Combine(legacy, "removed.txt"), "remove-after-migration");
        var target = AppDataDirectory.Prepare(root, root);
        File.WriteAllText(Path.Combine(target, "requests.json"), "new-data");
        File.Delete(Path.Combine(target, "removed.txt"));
        File.WriteAllText(Path.Combine(legacy, "requests.json"), "old-app-later-update");
        Assert(AppDataDirectory.Prepare(root, root) == target);
        Assert(File.ReadAllText(Path.Combine(target, "requests.json")) == "new-data");
        Assert(!File.Exists(Path.Combine(target, "removed.txt")));
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("新旧目录同时存在时以新目录为准，不自动混合数据", () =>
{
    var root = MigrationTestRoot();
    try
    {
        Directory.CreateDirectory(LegacyDataPath(root));
        File.WriteAllText(Path.Combine(LegacyDataPath(root), "requests.json"), "old-data");
        var target = Path.Combine(root, AppDataDirectory.DirectoryName);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "new-data.txt"), "keep");
        Assert(AppDataDirectory.Prepare(root, root) == target);
        Assert(!File.Exists(Path.Combine(target, "requests.json")));
        Assert(File.ReadAllText(Path.Combine(target, "new-data.txt")) == "keep");
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("迁移失败不启用不完整目录，释放占用后可重试", async () =>
{
    var root = MigrationTestRoot();
    var legacy = LegacyDataPath(root);
    try
    {
        Directory.CreateDirectory(legacy);
        var file = Path.Combine(legacy, "requests.json");
        File.WriteAllText(file, "original-data");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Throws<IOException>(() => Task.FromResult(AppDataDirectory.Prepare(root, root)));
        Assert(!Directory.Exists(Path.Combine(root, AppDataDirectory.DirectoryName)));
        Assert(File.ReadAllText(file) == "original-data");
        Assert(!Directory.EnumerateDirectories(root, "HttpDetector.migration-*").Any());
        var target = AppDataDirectory.Prepare(root, root);
        Assert(File.ReadAllText(Path.Combine(target, "requests.json")) == "original-data");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("目标路径被文件占用时不覆盖该文件或旧数据", async () =>
{
    var root = MigrationTestRoot();
    try
    {
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, AppDataDirectory.DirectoryName);
        File.WriteAllText(target, "keep-existing-file");
        await Throws<IOException>(() => Task.FromResult(AppDataDirectory.Prepare(root, root)));
        Assert(File.ReadAllText(target) == "keep-existing-file");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("并发首次启动迁移安全且清理各自暂存目录", async () =>
{
    var root = MigrationTestRoot();
    var legacy = LegacyDataPath(root);
    try
    {
        Directory.CreateDirectory(legacy);
        var bytes = new byte[256 * 1024]; Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(legacy, "data.bin"), bytes);
        var results = await Task.WhenAll(Task.Run(() => AppDataDirectory.Prepare(root, root)), Task.Run(() => AppDataDirectory.Prepare(root, root)));
        Assert(results[0] == results[1]);
        Assert(File.ReadAllBytes(Path.Combine(results[0], "data.bin")).SequenceEqual(bytes));
        Assert(File.ReadAllBytes(Path.Combine(legacy, "data.bin")).SequenceEqual(bytes));
        Assert(!Directory.EnumerateDirectories(root, "HttpDetector.migration-*").Any());
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("程序与启动工作目录不同时仍保存到 EXE 旁的 data", () =>
{
    var root = MigrationTestRoot();
    var workingDirectory = Environment.CurrentDirectory;
    try
    {
        var appDirectory = Path.Combine(root, "application");
        var differentWorkingDirectory = Path.Combine(root, "launched-from-here");
        Directory.CreateDirectory(differentWorkingDirectory);
        var defaultBefore = AppDataDirectory.CurrentPath;
        Environment.CurrentDirectory = differentWorkingDirectory;
        var target = AppDataDirectory.Prepare(appDirectory, Path.Combine(root, "profile"));
        var store = new ProfileStore(Path.Combine(target, "requests.json"));
        store.Save([new RequestProfile { Name = "程序旁保存" }]);
        Assert(target == Path.Combine(appDirectory, "data"));
        Assert(File.Exists(Path.Combine(appDirectory, "data", "requests.json")));
        Assert(!Directory.Exists(Path.Combine(differentWorkingDirectory, "data")));
        Assert(AppDataDirectory.CurrentPath == defaultBefore && defaultBefore == Path.Combine(AppContext.BaseDirectory, "data"));
        return Task.CompletedTask;
    }
    finally { Environment.CurrentDirectory = workingDirectory; if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("首次便携迁移优先采用已改名的用户数据目录", () =>
{
    var root = MigrationTestRoot();
    try
    {
        var profile = Path.Combine(root, "profile");
        var previous = Path.Combine(profile, "Http探测器");
        Directory.CreateDirectory(previous); Directory.CreateDirectory(LegacyDataPath(profile));
        File.WriteAllText(Path.Combine(previous, "requests.json"), "latest-profile-data");
        File.WriteAllText(Path.Combine(LegacyDataPath(profile), "requests.json"), "older-profile-data");
        var target = AppDataDirectory.Prepare(Path.Combine(root, "program"), profile);
        Assert(File.ReadAllText(Path.Combine(target, "requests.json")) == "latest-profile-data");
        Assert(File.ReadAllText(Path.Combine(previous, "requests.json")) == "latest-profile-data");
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
await Test("携带程序目录后保留 data，不导入另一用户的配置", () =>
{
    var root = MigrationTestRoot();
    try
    {
        var source = Path.Combine(root, "original-program");
        var destination = Path.Combine(root, "moved-program");
        var target = AppDataDirectory.Prepare(source, Path.Combine(root, "first-user"));
        File.WriteAllText(Path.Combine(target, "requests.json"), "portable-data");
        Assert(Path.GetDirectoryName(source) == root && Path.GetDirectoryName(destination) == root);
        Directory.Move(source, destination);
        var otherProfile = Path.Combine(root, "another-user");
        Directory.CreateDirectory(Path.Combine(otherProfile, "Http探测器"));
        File.WriteAllText(Path.Combine(otherProfile, "Http探测器", "requests.json"), "other-user-data");
        var moved = AppDataDirectory.Prepare(destination, otherProfile);
        Assert(moved == Path.Combine(destination, "data"));
        Assert(File.ReadAllText(Path.Combine(moved, "requests.json")) == "portable-data");
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Console.WriteLine($"\n{passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;

sealed class LocalServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly X509Certificate2? _certificate;
    public string Url { get; }
    public string? Thumbprint => _certificate?.Thumbprint;

    public LocalServer(bool tls = false)
    {
        if (tls)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        }
        _listener.Start();
        Url = $"{(tls ? "https" : "http")}://localhost:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _ = AcceptLoop();
    }
    private async Task AcceptLoop()
    {
        try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = Serve(client); } }
        catch (OperationCanceledException) { }
    }
    private async Task Serve(TcpClient client)
    {
        using (client)
        try
        {
            await using Stream stream = _certificate is null ? client.GetStream() : new SslStream(client.GetStream());
            if (stream is SslStream ssl) await ssl.AuthenticateAsServerAsync(_certificate!);
            using var header = new MemoryStream();
            var one = new byte[1];
            while (header.Length < 65536)
            {
                if (await stream.ReadAsync(one, _stop.Token) == 0) return;
                header.WriteByte(one[0]);
                if (header.Length >= 4 && Encoding.ASCII.GetString(header.GetBuffer(), (int)header.Length - 4, 4) == "\r\n\r\n") break;
            }
            var lines = Encoding.UTF8.GetString(header.ToArray()).Split("\r\n");
            var first = lines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1).Where(s => s.Contains(':'))) { var i = line.IndexOf(':'); headers[line[..i]] = line[(i + 1)..].Trim(); }
            var body = new byte[headers.TryGetValue("Content-Length", out var length) ? int.Parse(length) : 0];
            await stream.ReadExactlyAsync(body, _stop.Token);
            var target = first[1];
            if (target == "/delay") await Task.Delay(2000, _stop.Token);
            var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Method = first[0], Target = target, Headers = headers, Body = Encoding.UTF8.GetString(body) }));
            var status = "200 OK";
            var type = "application/json; charset=utf-8";
            var extra = "";
            if (target == "/missing") { status = "404 Not Found"; response = "not found"u8.ToArray(); }
            if (target == "/redirect") { status = "302 Found"; extra = "Location: /echo\r\n"; response = []; }
            if (target == "/binary") { response = [0, 1, 2, 255]; type = "application/octet-stream"; }
            if (target == "/big") { response = new byte[RequestRunner.ResponseLimit + 1024]; Array.Fill(response, (byte)'a'); type = "text/plain"; }
            if (target == "/gzip")
            {
                using var zipped = new MemoryStream();
                using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, true)) gzip.Write(Encoding.UTF8.GetBytes("gzip works 中文"));
                response = zipped.ToArray(); extra = "Content-Encoding: gzip\r\n";
            }
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {response.Length}\r\nX-Server: local-test\r\n{extra}Connection: close\r\n\r\n"), _stop.Token);
            if (first[0] != "HEAD") await stream.WriteAsync(response, _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException) { if (_certificate != null && ex is System.Security.Authentication.AuthenticationException) Console.WriteLine("TLS fixture: " + ex.Message); }
    }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); _certificate?.Dispose(); _stop.Dispose(); }
}
