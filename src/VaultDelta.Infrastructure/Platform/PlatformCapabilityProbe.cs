using System.Text;
using VaultDelta.Application.Abstractions;

namespace VaultDelta.Infrastructure.Platform;

public sealed class PlatformCapabilityProbe : IApplyCapabilityValidator
{
    private readonly IFileSystemSemantics _semantics;

    public PlatformCapabilityProbe()
        : this(SelectSemantics())
    {
    }

    public PlatformCapabilityProbe(IFileSystemSemantics semantics)
    {
        _semantics = semantics ?? throw new ArgumentNullException(nameof(semantics));
        if (!_semantics.IsCurrentPlatform)
        {
            throw new PlatformNotSupportedException($"{_semantics.PlatformName} filesystem semantics do not match the current operating system.");
        }
    }

    public async ValueTask ValidateAsync(
        string targetRoot,
        string transactionRoot,
        CancellationToken cancellationToken = default)
    {
        PlatformCapabilities capabilities = await ProbeAsync(targetRoot, transactionRoot, cancellationToken).ConfigureAwait(false);
        capabilities.EnsureSafeForApply();
    }

    public async ValueTask<PlatformCapabilities> ProbeAsync(
        string targetRoot,
        string transactionRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionRoot);
        cancellationToken.ThrowIfCancellationRequested();

        string target = Path.GetFullPath(targetRoot);
        string transaction = Path.GetFullPath(transactionRoot);
        List<string> issues = [];
        if (!Directory.Exists(target))
        {
            throw new DirectoryNotFoundException($"Target root does not exist: {target}");
        }

        bool targetRootLinkFree = IsRootLinkFree(target, issues);
        bool transactionRootCreated = !Directory.Exists(transaction);
        string id = Guid.NewGuid().ToString("N");
        string targetProbe = Path.Combine(target, $".vaultdelta-capability-{id}");
        string transactionProbe = Path.Combine(transaction, $".vaultdelta-capability-{id}");
        bool targetWritable = false;
        bool transactionWritable = false;
        bool sameVolumeMove = false;
        bool atomicReplace = false;
        bool caseSensitive = true;
        UnicodeFileNameBehavior unicodeBehavior = UnicodeFileNameBehavior.PreservesDistinctForms;

        try
        {
            try
            {
                Directory.CreateDirectory(targetProbe);
                await WriteThroughAsync(Path.Combine(targetProbe, "write-probe"), "target", cancellationToken).ConfigureAwait(false);
                targetWritable = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Target root is not writable: {exception.Message}");
            }

            try
            {
                Directory.CreateDirectory(transactionProbe);
                await WriteThroughAsync(Path.Combine(transactionProbe, "write-probe"), "transaction", cancellationToken).ConfigureAwait(false);
                transactionWritable = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add($"Transaction root is not writable: {exception.Message}");
            }

            if (targetWritable)
            {
                caseSensitive = ProbeCaseSensitivity(targetProbe, id);
                unicodeBehavior = ProbeUnicodeBehavior(targetProbe, id);
                atomicReplace = await ProbeAtomicReplaceAsync(targetProbe, cancellationToken).ConfigureAwait(false);
                if (!atomicReplace)
                {
                    issues.Add("Atomic file replacement could not be verified in the target root.");
                }
            }

            if (targetWritable && transactionWritable)
            {
                sameVolumeMove = ProbeSameVolumeDirectoryMove(targetProbe, transactionProbe, id);
                if (!sameVolumeMove)
                {
                    issues.Add("The transaction root and target root do not support a same-volume directory move.");
                }
            }

            return new PlatformCapabilities(
                _semantics.PlatformName,
                targetWritable,
                transactionWritable,
                sameVolumeMove,
                atomicReplace,
                caseSensitive,
                unicodeBehavior,
                targetRootLinkFree,
                issues);
        }
        finally
        {
            DeleteProbe(targetProbe);
            DeleteProbe(transactionProbe);
            if (transactionRootCreated && Directory.Exists(transaction) && !Directory.EnumerateFileSystemEntries(transaction).Any())
            {
                Directory.Delete(transaction);
            }
        }
    }

    private bool IsRootLinkFree(string targetRoot, List<string> issues)
    {
        DirectoryInfo root = new(targetRoot);
        if (_semantics.IsLink(root))
        {
            issues.Add("The target root is a symbolic link, junction, or reparse point.");
            return false;
        }

        return true;
    }

    private static bool ProbeCaseSensitivity(string probeRoot, string id)
    {
        string upper = Path.Combine(probeRoot, $"Case-{id}");
        File.WriteAllText(upper, "case");
        return !File.Exists(Path.Combine(probeRoot, $"case-{id}"));
    }

    private static UnicodeFileNameBehavior ProbeUnicodeBehavior(string probeRoot, string id)
    {
        string composedName = $"é-{id}".Normalize(NormalizationForm.FormC);
        string decomposedName = composedName.Normalize(NormalizationForm.FormD);
        string composedPath = Path.Combine(probeRoot, composedName);
        File.WriteAllText(composedPath, "unicode");
        string observedName = Path.GetFileName(
            Directory.EnumerateFiles(probeRoot)
                .Single(path => Path.GetFileName(path).Contains('é', StringComparison.Ordinal)
                    || Path.GetFileName(path).Contains("e\u0301", StringComparison.Ordinal)));
        if (!StringComparer.Ordinal.Equals(observedName, composedName))
        {
            return UnicodeFileNameBehavior.NormalizesStoredNames;
        }

        return File.Exists(Path.Combine(probeRoot, decomposedName))
            ? UnicodeFileNameBehavior.TreatsCanonicalFormsAsEquivalent
            : UnicodeFileNameBehavior.PreservesDistinctForms;
    }

    private static async ValueTask<bool> ProbeAtomicReplaceAsync(
        string probeRoot,
        CancellationToken cancellationToken)
    {
        string destination = Path.Combine(probeRoot, "replace-destination");
        string source = Path.Combine(probeRoot, "replace-source");
        try
        {
            await WriteThroughAsync(destination, "old", cancellationToken).ConfigureAwait(false);
            await WriteThroughAsync(source, "new", cancellationToken).ConfigureAwait(false);
            File.Move(source, destination, overwrite: true);
            return StringComparer.Ordinal.Equals(
                await File.ReadAllTextAsync(destination, cancellationToken).ConfigureAwait(false),
                "new");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool ProbeSameVolumeDirectoryMove(string targetProbe, string transactionProbe, string id)
    {
        string source = Path.Combine(transactionProbe, $"move-{id}");
        string destination = Path.Combine(targetProbe, $"move-{id}");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "marker"), "move");
            Directory.Move(source, destination);
            return File.Exists(Path.Combine(destination, "marker"));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async ValueTask WriteThroughAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteProbe(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static IFileSystemSemantics SelectSemantics()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsFileSystemSemantics();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsFileSystemSemantics();
        }

        throw new PlatformNotSupportedException("Vault Delta MVP supports Windows and macOS filesystems.");
    }
}
