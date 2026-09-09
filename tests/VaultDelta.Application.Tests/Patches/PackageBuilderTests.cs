using VaultDelta.Application.Abstractions;
using VaultDelta.Application.Patches;
using VaultDelta.Domain.Patches;

namespace VaultDelta.Application.Tests.Patches;

public sealed class PackageBuilderTests
{
    [Fact]
    public async Task BuildAsync_delegates_to_the_selected_writer()
    {
        RecordingWriter writer = new();
        PackageBuilder builder = new(writer);
        PatchManifest manifest = EmptyManifest();

        await builder.BuildAsync(manifest, "/source", "/output", cancellationToken: CancellationToken.None);

        Assert.Same(manifest, writer.Manifest);
        Assert.Equal("/source", writer.SourceRoot);
        Assert.Equal("/output", writer.OutputPath);
    }

    [Fact]
    public async Task BuildAsync_honors_pre_cancelled_tokens_before_writing()
    {
        RecordingWriter writer = new();
        PackageBuilder builder = new(writer);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await builder.BuildAsync(EmptyManifest(), "/source", "/output", cancellationToken: cancellation.Token));

        Assert.Null(writer.Manifest);
    }

    private static PatchManifest EmptyManifest() =>
        new(
            "1.0",
            "patch-001",
            DateTimeOffset.UnixEpoch,
            "0.1.0",
            "rules-v1",
            "sha256:base",
            "sha256:target",
            new PatchSummary(0, 0, 0, 0, 0),
            []);

    private sealed class RecordingWriter : IPatchPackageWriter
    {
        public PatchManifest? Manifest { get; private set; }

        public string? SourceRoot { get; private set; }

        public string? OutputPath { get; private set; }

        public ValueTask WriteAsync(
            PatchManifest manifest,
            string sourceRoot,
            string outputPath,
            IProgress<PackageBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Manifest = manifest;
            SourceRoot = sourceRoot;
            OutputPath = outputPath;
            return ValueTask.CompletedTask;
        }
    }
}
