using BulkPrint.Core.Infrastructure;
using BulkPrint.Core.Models;

namespace BulkPrint.Core.Services;

public sealed record ImportResult(List<DocumentItem> Items, int UnsupportedCount, int UnreadableCount, List<string> UnsupportedExamples);

public static class QueueOperations
{
    /// <summary>
    /// Expands files and folders into queue items (folders optionally recursive).
    /// Runs on a background thread; does not touch the UI.
    /// </summary>
    public static ImportResult Expand(IEnumerable<string> paths, bool includeSubfolders, CancellationToken ct = default)
    {
        var items = new List<DocumentItem>();
        int unsupported = 0, unreadable = 0;
        var examples = new List<string>();

        void AddFile(string file)
        {
            ct.ThrowIfCancellationRequested();
            if (!FileTypes.IsSupported(file))
            {
                unsupported++;
                if (examples.Count < 5) examples.Add(Path.GetFileName(file));
                return;
            }
            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.Hidden) != 0 && Path.GetFileName(file).StartsWith("~$"))
                    return; // Office lock files such as ~$report.docx
                items.Add(new DocumentItem(info.FullName) { SizeBytes = info.Length, Modified = info.LastWriteTime });
            }
            catch
            {
                unreadable++;
            }
        }

        foreach (var raw in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = raw.Trim().Trim('"');
            if (Directory.Exists(path))
            {
                var option = new EnumerationOptions
                {
                    RecurseSubdirectories = includeSubfolders,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System,
                    MatchType = MatchType.Simple,
                };
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*", option)
                        .OrderBy(f => Path.GetDirectoryName(f), NaturalStringComparer.Instance)
                        .ThenBy(f => Path.GetFileName(f), NaturalStringComparer.Instance)
                        .ToList();
                }
                catch
                {
                    unreadable++;
                    continue;
                }
                foreach (var f in files)
                {
                    if (Path.GetFileName(f).StartsWith("~$")) continue;
                    AddFile(f);
                }
            }
            else if (File.Exists(path))
            {
                AddFile(path);
            }
            else
            {
                unreadable++;
            }
        }
        return new ImportResult(items, unsupported, unreadable, examples);
    }

    /// <summary>Returns the later copies of files that appear more than once (same full path).</summary>
    public static List<DocumentItem> FindDuplicates(IEnumerable<DocumentItem> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<DocumentItem>();
        foreach (var item in items)
            if (!seen.Add(item.Path)) duplicates.Add(item);
        return duplicates;
    }

    public static List<DocumentItem> Sort(IEnumerable<DocumentItem> items, SortMode mode)
    {
        var cmp = NaturalStringComparer.Instance;
        return mode switch
        {
            SortMode.NameAscending => items.OrderBy(i => i.FileName, cmp).ToList(),
            SortMode.NameDescending => items.OrderByDescending(i => i.FileName, cmp).ToList(),
            SortMode.FullPath => items.OrderBy(i => i.Folder, cmp).ThenBy(i => i.FileName, cmp).ToList(),
            SortMode.DateOldestFirst => items.OrderBy(i => i.Modified).ThenBy(i => i.FileName, cmp).ToList(),
            SortMode.DateNewestFirst => items.OrderByDescending(i => i.Modified).ThenBy(i => i.FileName, cmp).ToList(),
            SortMode.FileType => items.OrderBy(i => i.Kind).ThenBy(i => i.Extension).ThenBy(i => i.FileName, cmp).ToList(),
            SortMode.SizeSmallestFirst => items.OrderBy(i => i.SizeBytes).ThenBy(i => i.FileName, cmp).ToList(),
            _ => items.ToList(),
        };
    }

    /// <summary>
    /// Moves the selected items so they start at <paramref name="targetIndex"/> (index in the original list,
    /// as if the selected items were still in place). Keeps the selected items' relative order.
    /// </summary>
    public static List<DocumentItem> MoveTo(IReadOnlyList<DocumentItem> all, IReadOnlyCollection<DocumentItem> selected, int targetIndex)
    {
        var set = new HashSet<DocumentItem>(selected);
        targetIndex = Math.Clamp(targetIndex, 0, all.Count);
        int before = 0;
        for (int i = 0; i < targetIndex; i++)
            if (set.Contains(all[i])) before++;
        var moving = all.Where(set.Contains).ToList();
        var rest = all.Where(i => !set.Contains(i)).ToList();
        int insertAt = Math.Clamp(targetIndex - before, 0, rest.Count);
        rest.InsertRange(insertAt, moving);
        return rest;
    }

    /// <summary>Moves each selected item up (-1) or down (+1) by one place.</summary>
    public static List<DocumentItem> Shift(IReadOnlyList<DocumentItem> all, IReadOnlyCollection<DocumentItem> selected, int direction)
    {
        var list = all.ToList();
        var set = new HashSet<DocumentItem>(selected);
        if (direction < 0)
        {
            for (int i = 1; i < list.Count; i++)
                if (set.Contains(list[i]) && !set.Contains(list[i - 1]))
                    (list[i - 1], list[i]) = (list[i], list[i - 1]);
        }
        else
        {
            for (int i = list.Count - 2; i >= 0; i--)
                if (set.Contains(list[i]) && !set.Contains(list[i + 1]))
                    (list[i + 1], list[i]) = (list[i], list[i + 1]);
        }
        return list;
    }

    public static QueueProject ToProject(IEnumerable<DocumentItem> items, GlobalPrintSettings global, bool batchInProgress) => new()
    {
        BatchInProgress = batchInProgress,
        SavedAt = DateTime.Now,
        Global = global.CloneGlobal(),
        Items = items.Select(i => new QueueEntry
        {
            Path = i.Path,
            Overrides = i.Overrides?.CloneOptions(),
            Status = NormalizeForSave(i.Status),
            StatusDetail = string.IsNullOrEmpty(i.StatusDetail) ? null : i.StatusDetail,
            PageCount = i.PageCount,
        }).ToList(),
    };

    private static PrintStatus NormalizeForSave(PrintStatus s) => s switch
    {
        // A document that was in the middle of printing will be printed again.
        PrintStatus.Converting or PrintStatus.Rendering or PrintStatus.Sending => PrintStatus.Pending,
        // Password state is re-detected when the queue is reopened.
        PrintStatus.NeedsPassword or PrintStatus.Unsupported => PrintStatus.Pending,
        _ => s,
    };

    /// <summary>Recreates queue items from a saved project, keeping statuses if requested.</summary>
    public static List<DocumentItem> FromProject(QueueProject project, bool keepStatuses)
    {
        var list = new List<DocumentItem>();
        foreach (var e in project.Items)
        {
            if (string.IsNullOrWhiteSpace(e.Path)) continue;
            DocumentItem item;
            try { item = new DocumentItem(e.Path); }
            catch { continue; }
            item.Overrides = e.Overrides;
            item.PageCount = e.PageCount;
            try
            {
                var info = new FileInfo(item.Path);
                if (info.Exists)
                {
                    item.SizeBytes = info.Length;
                    item.Modified = info.LastWriteTime;
                    if (keepStatuses)
                    {
                        item.Status = e.Status;
                        item.StatusDetail = e.StatusDetail ?? "";
                    }
                }
                else
                {
                    item.Status = PrintStatus.Failed;
                    item.StatusDetail = "The file no longer exists at this location.";
                }
            }
            catch
            {
                item.Status = PrintStatus.Failed;
                item.StatusDetail = "The file could not be read.";
            }
            list.Add(item);
        }
        return list;
    }
}
