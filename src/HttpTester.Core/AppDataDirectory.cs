namespace HttpTester.Core;

public static class AppDataDirectory
{
    public const string DirectoryName = "data";
    private const string PreviousDirectoryName = "Http探测器";
    // 此旧名称仅用于识别并迁移已发布版本的数据，不再作为保存目录。
    private const string LegacyDirectoryName = "QingzhouHttp";
    public static string CurrentPath => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    public static string Prepare(string applicationDirectory, string? localApplicationData = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory));
        var destination = Path.Combine(root, DirectoryName);
        // 新目录一旦存在，始终以新目录为准，避免再次导入旧数据或覆盖用户的新修改。
        if (Directory.Exists(destination)) return destination;
        if (File.Exists(destination)) throw new IOException($"数据目录路径已被文件占用：{destination}");
        var previousRoot = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var previousDirectory = Path.Combine(previousRoot, PreviousDirectoryName);
        var legacy = Directory.Exists(previousDirectory) ? previousDirectory : Path.Combine(previousRoot, LegacyDirectoryName);
        if (!Directory.Exists(legacy)) { Directory.CreateDirectory(destination); return destination; }

        var staging = Path.Combine(root, "HttpDetector.migration-" + Guid.NewGuid().ToString("N"));
        // 暂存目录与目标目录必须直接位于指定数据根目录内。
        if (!string.Equals(Path.GetDirectoryName(staging), root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(destination), root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("数据迁移目录超出了指定范围。");
        try
        {
            var before = Snapshot(legacy);
            Directory.CreateDirectory(staging);
            foreach (var entry in before)
            {
                var target = Path.Combine(staging, entry.RelativePath);
                if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var source = new FileStream(Path.Combine(legacy, entry.RelativePath), FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { source.CopyTo(output); output.Flush(flushToDisk: true); }
                File.SetLastWriteTimeUtc(target, entry.LastWriteTimeUtc);
            }
            if (!before.SequenceEqual(Snapshot(legacy)))
                throw new IOException("迁移期间旧版数据发生变化，请关闭旧版程序后重新启动。原数据未被修改。");
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Directory.Exists(destination))
            { /* 另一个进程先完成了迁移，保留已就绪的新目录。 */ }
            return destination;
        }
        finally
        {
            // 只清理经过父目录检查、由本次调用生成的暂存目录，不删除旧数据目录。
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static List<DataEntry> Snapshot(string directory)
    {
        var entries = new List<DataEntry>();
        Visit(directory);
        return entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToList();

        void Visit(string current)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"数据迁移不跟随符号链接或目录联接：{current}");
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"数据迁移不跟随符号链接或目录联接：{path}");
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var info = isDirectory ? null : new FileInfo(path);
                entries.Add(new DataEntry(Path.GetRelativePath(directory, path), isDirectory,
                    info?.Length ?? 0, info?.LastWriteTimeUtc ?? default));
                if (isDirectory) Visit(path);
            }
        }
    }

    private readonly record struct DataEntry(string RelativePath, bool IsDirectory, long Length, DateTime LastWriteTimeUtc);
}
