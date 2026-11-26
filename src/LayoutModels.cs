using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskSpaces;

public sealed class WorkspaceLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "主桌面";
    public List<DesktopEntry> Entries { get; set; } = [];

    public WorkspaceLayout Clone(string? name = null)
    {
        return new WorkspaceLayout
        {
            Id = Guid.NewGuid(),
            Name = name ?? Name,
            Entries = Entries.Select(entry => entry.Clone()).ToList(),
        };
    }
}

public sealed class DesktopEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "File";
    public double NormalizedX { get; set; }
    public double NormalizedY { get; set; }
    public string? CustomIconPath { get; set; }
    public int CustomIconIndex { get; set; }

    [JsonIgnore]
    public double X { get; set; }

    [JsonIgnore]
    public double Y { get; set; }

    public void Normalize(double width, double height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        X = Math.Max(0, X);
        Y = Math.Max(0, Y);
        NormalizedX = Math.Clamp(X / width, 0, 1);
        NormalizedY = Math.Clamp(Y / height, 0, 1);
    }

    public void Denormalize(double width, double height)
    {
        X = Math.Max(0, NormalizedX * width);
        Y = Math.Max(0, NormalizedY * height);
    }

    public DesktopEntry Clone() => new()
    {
        Id = Guid.NewGuid(),
        TargetPath = TargetPath,
        DisplayName = DisplayName,
        Kind = Kind,
        NormalizedX = NormalizedX,
        NormalizedY = NormalizedY,
        X = X,
        Y = Y,
        CustomIconPath = CustomIconPath,
        CustomIconIndex = CustomIconIndex,
    };
}

public sealed class LayoutDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ActiveWorkspaceId { get; set; }
    public List<WorkspaceLayout> Workspaces { get; set; } = [];
}

public sealed class LayoutStore
{
    private readonly string _filePath;
    private readonly string _backupDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public LayoutStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _filePath = Path.Combine(rootDirectory, "layout.json");
        _backupDirectory = Path.Combine(rootDirectory, "backups");
        Directory.CreateDirectory(_backupDirectory);
    }

    public LayoutDocument Load()
    {
        if (!File.Exists(_filePath)) return new LayoutDocument();
        try
        {
            var document = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(_filePath), _jsonOptions);
            Validate(document);
            return document!;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            var latestBackup = new DirectoryInfo(_backupDirectory).GetFiles("layout-*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
            if (latestBackup is null) throw new InvalidDataException("布局文件无法读取，且没有可用备份。", error);
            var document = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(latestBackup.FullName), _jsonOptions);
            Validate(document);
            return document!;
        }
    }

    public void Save(LayoutDocument document)
    {
        if (document.Workspaces.Count == 0) return;
        Validate(document);
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, _jsonOptions));
        if (File.Exists(_filePath))
        {
            try
            {
                var previous = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(_filePath), _jsonOptions);
                Validate(previous);
                var backup = Path.Combine(_backupDirectory, $"layout-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                File.Copy(_filePath, backup);
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
            {
                // 损坏的主文件不得覆盖或混入有效备份。
            }
            // 同一目录、同一卷上的覆盖重命名避免长时间留空，兼容当前机器的文件策略。
            File.Move(temporary, _filePath, true);
        }
        else
        {
            File.Move(temporary, _filePath);
        }
        foreach (var old in new DirectoryInfo(_backupDirectory).GetFiles("layout-*.json")
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(10))
        {
            old.Delete();
        }
    }

    public static LayoutDocument CreateDefault()
    {
        var workspace = new WorkspaceLayout();
        return new LayoutDocument { ActiveWorkspaceId = workspace.Id, Workspaces = [workspace] };
    }

    public static DesktopEntry CopyEntry(DesktopEntry source, double availableWidth, double availableHeight)
    {
        var copy = source.Clone();
        copy.Denormalize(availableWidth, availableHeight);
        copy.Normalize(availableWidth, availableHeight);
        return copy;
    }

    public static void MoveEntry(WorkspaceLayout source, WorkspaceLayout target, Guid entryId)
    {
        var entry = source.Entries.Single(item => item.Id == entryId);
        target.Entries.Add(entry.Clone());
        source.Entries.Remove(entry);
    }

    private static void Validate(LayoutDocument? document)
    {
        if (document is null || document.SchemaVersion != 1 || document.Workspaces.Count == 0)
            throw new InvalidDataException("布局文件版本或工作区列表无效。");
        if (document.Workspaces.Select(item => item.Id).Distinct().Count() != document.Workspaces.Count)
            throw new InvalidDataException("布局文件包含重复工作区 ID。");
        if (document.Workspaces.SelectMany(item => item.Entries).Select(item => item.Id).Distinct().Count() !=
            document.Workspaces.Sum(item => item.Entries.Count))
            throw new InvalidDataException("布局文件包含重复图标 ID。");
        if (!document.Workspaces.Any(item => item.Id == document.ActiveWorkspaceId))
            document.ActiveWorkspaceId = document.Workspaces[0].Id;
    }
}
