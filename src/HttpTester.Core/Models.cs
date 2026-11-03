using System.Collections.ObjectModel;

namespace HttpTester.Core;

public sealed class Field
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsFile { get; set; }
}

public sealed class RequestProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "未命名请求";
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public ObservableCollection<Field> Query { get; set; } = [];
    public ObservableCollection<Field> Headers { get; set; } = [];
    public ObservableCollection<Field> Form { get; set; } = [];
    public string BodyKind { get; set; } = "无";
    public string Body { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool FollowRedirects { get; set; } = true;
    public string AuthKind { get; set; } = "无";
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed record ResponseResult(int StatusCode, string Reason, string Headers, string Body,
    byte[] Bytes, long ElapsedMilliseconds, bool Truncated, bool IsBinary);
