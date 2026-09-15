using VaultDelta.Domain.Snapshots;

namespace VaultDelta.Domain.Patches;

public static class PatchOperationExecutionPlanner
{
    public static IReadOnlyList<PatchOperation> Order(IReadOnlyList<PatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count < 2)
        {
            return operations.ToArray();
        }

        Dictionary<string, List<int>> basePaths = IndexPaths(operations, operation => operation.BasePath?.Value);
        Dictionary<string, List<int>> targetPaths = IndexPaths(operations, operation => operation.TargetPath?.Value);
        HashSet<int>[] successors = Enumerable.Range(0, operations.Count)
            .Select(_ => new HashSet<int>())
            .ToArray();
        int[] predecessorCounts = new int[operations.Count];

        for (int index = 0; index < operations.Count; index++)
        {
            PatchOperation operation = operations[index];
            if (operation.BasePath is not null)
            {
                foreach (string ancestor in EnumerateAncestors(operation.BasePath.Value, includeSelf: false))
                {
                    foreach (int parentIndex in GetAtPath(basePaths, ancestor))
                    {
                        PatchOperation parent = operations[parentIndex];
                        if (parent.EntryKind == SnapshotEntryKind.Directory
                            && parent.Type is PatchOperationType.Delete or PatchOperationType.Rename)
                        {
                            AddDependency(index, parentIndex, successors, predecessorCounts);
                        }
                    }
                }
            }

            if (operation.TargetPath is null)
            {
                continue;
            }

            foreach (string path in EnumerateAncestors(operation.TargetPath.Value, includeSelf: true))
            {
                foreach (int blockerIndex in GetAtPath(basePaths, path))
                {
                    if (blockerIndex != index && operations[blockerIndex].Type == PatchOperationType.Delete)
                    {
                        AddDependency(blockerIndex, index, successors, predecessorCounts);
                    }
                }

                foreach (int parentIndex in GetAtPath(targetPaths, path))
                {
                    PatchOperation parent = operations[parentIndex];
                    if (parentIndex != index
                        && parent.EntryKind == SnapshotEntryKind.Directory
                        && parent.Type is PatchOperationType.Add or PatchOperationType.Rename)
                    {
                        AddDependency(parentIndex, index, successors, predecessorCounts);
                    }
                }
            }
        }

        PriorityQueue<int, int> ready = new();
        for (int index = 0; index < predecessorCounts.Length; index++)
        {
            if (predecessorCounts[index] == 0)
            {
                ready.Enqueue(index, index);
            }
        }

        List<PatchOperation> ordered = new(operations.Count);
        while (ready.TryDequeue(out int index, out _))
        {
            ordered.Add(operations[index]);
            foreach (int successor in successors[index])
            {
                predecessorCounts[successor]--;
                if (predecessorCounts[successor] == 0)
                {
                    ready.Enqueue(successor, successor);
                }
            }
        }

        if (ordered.Count != operations.Count)
        {
            throw new InvalidDataException("Patch operations contain cyclic path dependencies and cannot be applied safely.");
        }

        return ordered;
    }

    public static IReadOnlyList<PatchOperation> OrderAndResequence(
        IReadOnlyList<PatchOperation> operations,
        int sequenceStep = 10)
    {
        if (sequenceStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceStep), sequenceStep, "Sequence step must be positive.");
        }

        IReadOnlyList<PatchOperation> ordered = Order(operations);
        PatchOperation[] resequenced = new PatchOperation[ordered.Count];
        for (int index = 0; index < ordered.Count; index++)
        {
            PatchOperation operation = ordered[index];
            int sequence = checked((index + 1) * sequenceStep);
            resequenced[index] = PatchOperation.Restore(
                sequence,
                operation.Type,
                operation.EntryKind,
                operation.BasePath,
                operation.TargetPath,
                operation.PayloadPath,
                operation.OldFingerprint,
                operation.NewFingerprint);
        }

        return resequenced;
    }

    private static Dictionary<string, List<int>> IndexPaths(
        IReadOnlyList<PatchOperation> operations,
        Func<PatchOperation, string?> selectPath)
    {
        Dictionary<string, List<int>> index = new(StringComparer.OrdinalIgnoreCase);
        for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            string? path = selectPath(operations[operationIndex]);
            if (path is null)
            {
                continue;
            }

            if (!index.TryGetValue(path, out List<int>? matchingOperations))
            {
                matchingOperations = [];
                index.Add(path, matchingOperations);
            }

            matchingOperations.Add(operationIndex);
        }

        return index;
    }

    private static List<int> GetAtPath(Dictionary<string, List<int>> index, string path) =>
        index.TryGetValue(path, out List<int>? operations) ? operations : [];

    private static IEnumerable<string> EnumerateAncestors(string path, bool includeSelf)
    {
        int separator = includeSelf ? path.Length : path.LastIndexOf('/');
        while (separator > 0)
        {
            string current = separator == path.Length ? path : path[..separator];
            yield return current;
            separator = current.LastIndexOf('/');
        }
    }

    private static void AddDependency(
        int predecessor,
        int successor,
        HashSet<int>[] successors,
        int[] predecessorCounts)
    {
        if (predecessor != successor && successors[predecessor].Add(successor))
        {
            predecessorCounts[successor]++;
        }
    }
}
