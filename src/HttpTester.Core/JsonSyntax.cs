using System.Text.Encodings.Web;
using System.Text.Json;

namespace HttpTester.Core;

public enum JsonTokenKind { Plain, Property, String, Number, Boolean, Null }
public readonly record struct JsonToken(int Start, int Length, JsonTokenKind Kind);

public static class JsonSyntax
{
    private static readonly JsonSerializerOptions Formatting = new()
    { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Format(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, Formatting);
    }

    // 记录字符区间，不为每个 token 创建界面对象；可用于只绘制当前可见内容。
    public static IReadOnlyList<JsonToken> Tokenize(string text)
    {
        var tokens = new List<JsonToken>();
        var position = 0;
        while (position < text.Length)
        {
            var start = position;
            var kind = JsonTokenKind.Plain;
            var character = text[position++];
            if (character == '"')
            {
                while (position < text.Length)
                {
                    var next = text[position++];
                    if (next == '\\' && position < text.Length) position++;
                    else if (next == '"') break;
                }
                var lookahead = position;
                while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead])) lookahead++;
                kind = lookahead < text.Length && text[lookahead] == ':' ? JsonTokenKind.Property : JsonTokenKind.String;
            }
            else if (character == '-' || char.IsAsciiDigit(character))
            {
                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] is '.' or 'e' or 'E' or '+' or '-')) position++;
                kind = JsonTokenKind.Number;
            }
            else if (text.AsSpan(start).StartsWith("true")) { position = start + 4; kind = JsonTokenKind.Boolean; }
            else if (text.AsSpan(start).StartsWith("false")) { position = start + 5; kind = JsonTokenKind.Boolean; }
            else if (text.AsSpan(start).StartsWith("null")) { position = start + 4; kind = JsonTokenKind.Null; }

            if (kind == JsonTokenKind.Plain && tokens.Count > 0 && tokens[^1].Kind == kind)
                tokens[^1] = tokens[^1] with { Length = tokens[^1].Length + position - start };
            else tokens.Add(new JsonToken(start, position - start, kind));
        }
        return tokens;
    }
}
