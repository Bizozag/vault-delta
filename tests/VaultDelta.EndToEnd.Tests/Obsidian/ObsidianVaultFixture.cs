namespace VaultDelta.EndToEnd.Tests.Obsidian;

internal sealed class ObsidianVaultFixture : IDisposable
{
    private static readonly DateTime FixedWriteTimeUtc = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-obsidian-fixture-{Guid.NewGuid():N}");

    public ObsidianVaultFixture()
    {
        string blueprintRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ObsidianVault");
        if (!Directory.Exists(blueprintRoot))
        {
            throw new DirectoryNotFoundException($"Fixture blueprint was not copied: {blueprintRoot}");
        }

        BaselineRoot = Path.Combine(_root, "baseline");
        TargetRoot = Path.Combine(_root, "target");
        AppliedRoot = Path.Combine(_root, "applied");
        PackagePath = Path.Combine(_root, "transfer", "obsidian-delta.zip");
        TransactionRoot = Path.Combine(_root, "transactions");
        Materialize(Path.Combine(blueprintRoot, "Baseline"), BaselineRoot);
        Materialize(Path.Combine(blueprintRoot, "Target"), TargetRoot);
        CopyTree(BaselineRoot, AppliedRoot);
    }

    public string BaselineRoot { get; }
    public string TargetRoot { get; }
    public string AppliedRoot { get; }
    public string PackagePath { get; }
    public string TransactionRoot { get; }

    public static Dictionary<string, byte[]> ReadManagedTree(string root)
    {
        Dictionary<string, byte[]> result = new(StringComparer.Ordinal);
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            string relative = Relative(root, directory);
            if (IsExcluded(relative))
            {
                continue;
            }

            result[$"D:{relative}"] = [];
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Relative(root, file);
            if (IsExcluded(relative) || relative == ".vaultdelta.lock")
            {
                continue;
            }

            result[$"F:{relative}"] = File.ReadAllBytes(file);
        }

        return result;
    }

    private static void Materialize(string blueprintRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string directory in Directory.EnumerateDirectories(blueprintRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(blueprintRoot, directory)));
        }

        foreach (string source in Directory.EnumerateFiles(blueprintRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(blueprintRoot, source);
            if (relative.EndsWith(".base64", StringComparison.Ordinal))
            {
                relative = relative[..^".base64".Length];
                string binaryPath = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
                File.WriteAllBytes(binaryPath, Convert.FromBase64String(File.ReadAllText(source).Trim()));
                File.SetLastWriteTimeUtc(binaryPath, FixedWriteTimeUtc);
            }
            else
            {
                string destination = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
                File.SetLastWriteTimeUtc(destination, FixedWriteTimeUtc);
            }
        }
    }

    private static void CopyTree(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
            File.SetLastWriteTimeUtc(destination, FixedWriteTimeUtc);
        }
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsExcluded(string relative) =>
        relative.Equals(".trash", StringComparison.Ordinal)
        || relative.StartsWith(".trash/", StringComparison.Ordinal)
        || relative.Equals(".obsidian/workspace.json", StringComparison.Ordinal)
        || relative.Equals(".obsidian/workspace-mobile.json", StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
