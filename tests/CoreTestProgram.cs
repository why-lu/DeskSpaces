using DeskSpaces;

var failures = new List<string>();
var checks = 0;

void Check(bool condition, string name)
{
    checks++;
    if (!condition) failures.Add(name);
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}");
}

var root = Path.Combine(AppContext.BaseDirectory, "test-output", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new LayoutStore(root);
    var document = LayoutStore.CreateDefault();
    var first = document.Workspaces[0];
    var entry = new DesktopEntry { TargetPath = @"D:\example\demo.txt", DisplayName = "示例", X = 321.5, Y = 178.25 };
    entry.Normalize(1707, 1019);
    first.Entries.Add(entry);
    store.Save(document);
    var loaded = store.Load();
    var persisted = loaded.Workspaces[0].Entries.Single();
    Check(persisted.Id == entry.Id, "保存后图标 ID 不变");
    Check(persisted.NormalizedX == entry.NormalizedX && persisted.NormalizedY == entry.NormalizedY, "保存后坐标完全不变");

    var second = new WorkspaceLayout { Name = "学习" };
    loaded.Workspaces.Add(second);
    var copy = LayoutStore.CopyEntry(persisted, 1707, 1019);
    second.Entries.Add(copy);
    Check(copy.Id != persisted.Id && copy.TargetPath == persisted.TargetPath, "复制入口创建新 ID 且指向同一文件");
    Check(first.Entries.Count == 1 && second.Entries.Count == 1, "复制不移除原桌面入口");

    LayoutStore.MoveEntry(first, second, persisted.Id);
    Check(first.Entries.Count == 0 && second.Entries.Count == 2, "移动入口从原桌面移除");
    Check(File.Exists(Path.Combine(root, "layout.json")), "布局文件已保存到 D 盘测试目录");
    Check(Directory.GetFiles(Path.Combine(root, "backups"), "layout-*.json").Length <= 10, "备份数量受限");

    persisted.Denormalize(1707, 1019);
    persisted.Denormalize(853.5, 509.5);
    persisted.Normalize(853.5, 509.5);
    persisted.Denormalize(1707, 1019);
    Check(Math.Abs(persisted.X - 321.5) < 0.001 && Math.Abs(persisted.Y - 178.25) < 0.001, "往返缩放恢复原始坐标");

    store.Save(loaded);
    File.WriteAllText(Path.Combine(root, "layout.json"), "{broken");
    var recovered = store.Load();
    Check(recovered.Workspaces.Count > 0, "主文件损坏时自动从备份恢复");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
Console.WriteLine($"通过 {checks - failures.Count}/{checks}");
return failures.Count == 0 ? 0 : 1;
