using System.Text.Json;

namespace HttpTester.Core;

public sealed class ProfileStore(string path)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Path { get; } = path;

    public List<RequestProfile> Load()
    {
        if (!File.Exists(Path)) return [];
        try { return Parse(File.ReadAllText(Path)); }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"保存文件损坏，原文件未被覆盖：{Path}", ex); }
    }

    public void Save(IEnumerable<RequestProfile> profiles)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profiles, JsonOptions));
        if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak", true);
        else File.Move(temp, Path);
    }

    public static List<RequestProfile> Parse(string json)
    {
        var profiles = JsonSerializer.Deserialize<List<RequestProfile>>(json, JsonOptions)
            ?? throw new JsonException("请求集合不能为空。");
        foreach (var profile in profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name) || profile.Query is null ||
                profile.Headers is null || profile.Form is null || profile.Url is null || profile.Body is null ||
                profile.Token is null || profile.Username is null || profile.Password is null ||
                profile.Query.Concat(profile.Headers).Concat(profile.Form).Any(f => f is null || f.Key is null || f.Value is null))
                throw new JsonException("请求数据缺少必要字段。");
            if (!new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE" }.Contains(profile.Method) ||
                !new[] { "无", "JSON", "纯文本", "表单 URL 编码", "文件 / Multipart" }.Contains(profile.BodyKind) ||
                !new[] { "无", "Bearer Token", "Basic Auth" }.Contains(profile.AuthKind) ||
                profile.TimeoutSeconds is < 1 or > 600)
                throw new JsonException("请求配置包含不支持的选项。");
        }
        return profiles;
    }

    public static RequestProfile Clone(RequestProfile profile) =>
        JsonSerializer.Deserialize<RequestProfile>(JsonSerializer.Serialize(profile))!;
}
