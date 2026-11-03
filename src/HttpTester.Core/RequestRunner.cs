using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace HttpTester.Core;

public sealed class RequestRunner
{
    public const int ResponseLimit = 10 * 1024 * 1024;

    public static Uri BuildUri(RequestProfile profile)
    {
        if (!Uri.TryCreate(profile.Url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("请输入完整的 http:// 或 https:// 地址。");
        var builder = new UriBuilder(uri) { Fragment = "" };
        var extra = string.Join("&", profile.Query.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
            .Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
        var original = builder.Query.TrimStart('?');
        builder.Query = string.Join("&", new[] { original, extra }.Where(s => s.Length > 0));
        return builder.Uri;
    }

    public static HttpRequestMessage BuildRequest(RequestProfile profile)
    {
        var message = new HttpRequestMessage(new HttpMethod(profile.Method), BuildUri(profile));
        try
        {
            switch (profile.BodyKind)
            {
                case "JSON": message.Content = new StringContent(profile.Body, Encoding.UTF8, "application/json"); break;
                case "纯文本": message.Content = new StringContent(profile.Body, Encoding.UTF8, "text/plain"); break;
                case "表单 URL 编码":
                    message.Content = new FormUrlEncodedContent(profile.Form.Where(IsActive)
                        .Select(f => new KeyValuePair<string, string>(f.Key, f.Value))); break;
                case "文件 / Multipart":
                    var multipart = new MultipartFormDataContent();
                    message.Content = multipart;
                    foreach (var field in profile.Form.Where(IsActive))
                    {
                        if (field.IsFile)
                        {
                            var content = new StreamContent(File.OpenRead(field.Value));
                            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                            multipart.Add(content, field.Key, Path.GetFileName(field.Value));
                        }
                        else multipart.Add(new StringContent(field.Value), field.Key);
                    }
                    break;
            }
            foreach (var field in profile.Headers.Where(IsActive))
            {
                if (field.Key.Contains('\r') || field.Key.Contains('\n') || field.Value.Contains('\r') || field.Value.Contains('\n'))
                    throw new ArgumentException("请求头不能包含换行符。");
                if (field.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content ??= new ByteArrayContent([]);
                    // Multipart 的 boundary 必须与实际编码一致。
                    if (message.Content is MultipartFormDataContent)
                        throw new ArgumentException("Multipart 会自动生成 Content-Type 与 boundary，请取消自定义 Content-Type。");
                    message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(field.Value);
                }
                else if (!message.Headers.TryAddWithoutValidation(field.Key, field.Value))
                {
                    message.Content ??= new ByteArrayContent([]);
                    if (!message.Content.Headers.TryAddWithoutValidation(field.Key, field.Value))
                        throw new ArgumentException($"无法添加请求头：{field.Key}");
                }
            }
            if (profile.AuthKind == "Bearer Token")
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.Token.Trim());
            else if (profile.AuthKind == "Basic Auth")
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Username}:{profile.Password}")));
            return message;
        }
        catch { message.Dispose(); throw; }
    }

    private static bool IsActive(Field field) => field.Enabled && !string.IsNullOrWhiteSpace(field.Key);

    public async Task<ResponseResult> SendAsync(RequestProfile profile, CancellationToken cancellationToken,
        HttpMessageHandler? testHandler = null)
    {
        using var request = BuildRequest(profile);
        using var handler = testHandler ?? new HttpClientHandler
        {
            AllowAutoRedirect = profile.FollowRedirects,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.TimeoutSeconds, 1, 600)));
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            while (buffer.Length <= ResponseLimit)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, ResponseLimit + 1 - (int)buffer.Length)), timeout.Token);
                if (read == 0) break;
                buffer.Write(chunk, 0, read);
            }
            var truncated = buffer.Length > ResponseLimit;
            var bytes = buffer.ToArray();
            if (truncated) Array.Resize(ref bytes, ResponseLimit);
            var headers = $"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}\r\n" +
                string.Join("\r\n", response.Headers.Concat(response.Content.Headers)
                    .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            var binary = mediaType.StartsWith("image/") || mediaType.StartsWith("audio/") || mediaType.StartsWith("video/") ||
                mediaType is "application/octet-stream" or "application/pdf" or "application/zip" ||
                (mediaType.Length == 0 && bytes.Take(512).Contains((byte)0));
            var encoding = Encoding.UTF8;
            var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            if (!string.IsNullOrEmpty(charset))
                try { encoding = Encoding.GetEncoding(charset); } catch (ArgumentException) { }
            var body = binary ? "这是二进制响应。请使用“保存响应”导出文件。" : encoding.GetString(bytes).TrimStart('\uFEFF');
            return new ResponseResult((int)response.StatusCode, response.ReasonPhrase ?? "", headers, body,
                bytes, timer.ElapsedMilliseconds, truncated, binary);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"请求超过 {profile.TimeoutSeconds} 秒，请检查地址或调整超时时间。"); }
    }
}
