using VaultDelta.Application.Abstractions;
using VaultDelta.Domain.Paths;
using VaultDelta.Infrastructure.Apply;
using VaultDelta.Infrastructure.Hashing;

namespace VaultDelta.Infrastructure.Tests.Apply;

public sealed class LocalTargetStateReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vaultdelta-baseline-{Guid.NewGuid():N}");

    public LocalTargetStateReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ReadAsync_returns_file_directory_and_missing_states()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "note.md"), "hello", CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_root, "folder"));
        LocalTargetStateReader reader = new(new Sha256ContentHasher());

        TargetEntryState file = await reader.ReadAsync(_root, RelativePath.Parse("note.md"), CancellationToken.None);
        TargetEntryState directory = await reader.ReadAsync(_root, RelativePath.Parse("folder"), CancellationToken.None);
        TargetEntryState missing = await reader.ReadAsync(_root, RelativePath.Parse("missing.md"), CancellationToken.None);

        Assert.Equal(TargetEntryStateKind.File, file.Kind);
        Assert.Equal(5, file.Fingerprint!.Length);
        Assert.Equal(TargetEntryStateKind.Directory, directory.Kind);
        Assert.Equal(TargetEntryStateKind.Missing, missing.Kind);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
