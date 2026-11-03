using System.Text.Json;

namespace HttpTester.Core;

public sealed class WorkspaceInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class WorkspaceCatalog
{
    public Guid ActiveWorkspaceId { get; set; }
    public List<WorkspaceInfo> Workspaces { get; set; } = [];

    public static WorkspaceCatalog CreateDefault() => new()
    { Workspaces = [new WorkspaceInfo { Id = Guid.Empty, Name = "默认工作区" }] };
}

/// <summary>默认工作区继续使用原请求文件；其他工作区使用独立的 GUID 目录。</summary>
public sealed class WorkspaceStore(string defaultProfilePath)
{
    private readonly string _defaultPath = System.IO.Path.GetFullPath(defaultProfilePath);
    public string Path => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_defaultPath)!, "workspaces.json");

    public WorkspaceCatalog Load()
    {
        if (!File.Exists(Path)) return WorkspaceCatalog.CreateDefault();
        try
        {
            var catalog = JsonSerializer.Deserialize<WorkspaceCatalog>(File.ReadAllText(Path))
                ?? throw new JsonException("工作区目录不能为空。");
            Validate(catalog);
            return catalog;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"工作区目录文件损坏，原文件未被覆盖：{Path}", ex); }
    }

    public ProfileStore GetProfileStore(Guid id) => new(id == Guid.Empty ? _defaultPath :
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_defaultPath)!, "workspaces", id.ToString("N"), "requests.json"));

    public void Save(WorkspaceCatalog catalog)
    {
        Validate(catalog);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporaryPath = Path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(catalog, ProfileStore.JsonOptions));
        if (File.Exists(Path)) File.Replace(temporaryPath, Path, Path + ".bak", true);
        else File.Move(temporaryPath, Path);
    }

    public static WorkspaceCatalog Add(WorkspaceCatalog catalog, string name)
    {
        var copy = Copy(catalog);
        var id = Guid.NewGuid();
        copy.Workspaces.Add(new WorkspaceInfo { Id = id, Name = name.Trim() });
        copy.ActiveWorkspaceId = id;
        Validate(copy);
        return copy;
    }

    public static WorkspaceCatalog Rename(WorkspaceCatalog catalog, Guid id, string name)
    {
        var copy = Copy(catalog);
        var workspace = copy.Workspaces.FirstOrDefault(w => w.Id == id)
            ?? throw new ArgumentException("工作区不存在。");
        workspace.Name = name.Trim();
        Validate(copy);
        return copy;
    }

    public static WorkspaceCatalog Select(WorkspaceCatalog catalog, Guid id)
    {
        var copy = Copy(catalog);
        copy.ActiveWorkspaceId = id;
        Validate(copy);
        return copy;
    }

    private static WorkspaceCatalog Copy(WorkspaceCatalog catalog) => new()
    {
        ActiveWorkspaceId = catalog.ActiveWorkspaceId,
        Workspaces = catalog.Workspaces.Select(w => new WorkspaceInfo { Id = w.Id, Name = w.Name }).ToList()
    };

    private static void Validate(WorkspaceCatalog catalog)
    {
        if (catalog.Workspaces is null || catalog.Workspaces.Count == 0 ||
            catalog.Workspaces.Any(w => w is null || string.IsNullOrWhiteSpace(w.Name) || w.Name.Length > 40 || w.Name != w.Name.Trim()))
            throw new ArgumentException("工作区名称需要为 1～40 个字符，且不能仅包含空格。");
        if (catalog.Workspaces.Select(w => w.Id).Distinct().Count() != catalog.Workspaces.Count ||
            catalog.Workspaces.Select(w => w.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Workspaces.Count)
            throw new ArgumentException("工作区名称不能重复。");
        if (!catalog.Workspaces.Any(w => w.Id == Guid.Empty) || !catalog.Workspaces.Any(w => w.Id == catalog.ActiveWorkspaceId))
            throw new ArgumentException("默认工作区或当前工作区不存在。");
    }
}
