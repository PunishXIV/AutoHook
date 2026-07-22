using Newtonsoft.Json;

namespace AutoHook.Utils;

public sealed class PresetImportOptions {
    public bool SkipDuplicateNames { get; init; } // community: true; manual/IPC: false
    public bool MakeAnonymous { get; init; }
    public bool SelectFirst { get; init; }
    public string? SelectReason { get; init; } // used with SelectFirst for FishingPresets.Select
    public Guid? AttachRootToParentId { get; init; }
    public IReadOnlySet<Guid>? SelectedPresetIds { get; init; } // manual checkbox subset; null = import all
}

public sealed class PresetImportResult {
    public int ImportedPresets { get; init; }
    public int SkippedPresets { get; init; }
    public int FoldersAdded { get; init; }
    public CustomPresetConfig? FirstImported { get; init; }
    public PresetFolder? CreatedRootFolder { get; init; }
    public IReadOnlyList<Guid> ImportedPresetIds { get; init; } = [];

    public static PresetImportResult Empty { get; } = new();
}

// always clones with fresh IDs so wiki-shared objects stay untouched.
public static class PresetImport {
    public static CustomPresetConfig ClonePreset(CustomPresetConfig preset) {
        var json = JsonConvert.SerializeObject(preset);
        return JsonConvert.DeserializeObject<CustomPresetConfig>(json)!;
    }

    public static PresetImportResult ImportPresets(FishingPresets target, IEnumerable<CustomPresetConfig> sourcePresets, PresetImportOptions? options = null) {
        options ??= new PresetImportOptions();
        var sources = FilterSources(sourcePresets, options.SelectedPresetIds);
        if (sources.Count == 0)
            return PresetImportResult.Empty;

        var (copies, skipped, _) = ClonePresetsWithMap(target, sources, options);
        if (copies.Count == 0)
            return new PresetImportResult { SkippedPresets = skipped };

        foreach (var copy in copies)
            target.CustomPresets.Add(copy);

        var first = copies[0];
        MaybeSelect(target, first, options);

        return new PresetImportResult {
            ImportedPresets = copies.Count,
            SkippedPresets = skipped,
            FirstImported = first,
            ImportedPresetIds = [.. copies.Select(c => c.UniqueId)]
        };
    }

    public static PresetImportResult ImportPresetsIntoNewFolder(
        FishingPresets target,
        string folderName,
        IEnumerable<CustomPresetConfig> sourcePresets,
        PresetImportOptions? options = null) {
        var result = ImportPresets(target, sourcePresets, options);
        if (result.ImportedPresets == 0)
            return result;

        var folder = new PresetFolder(folderName) {
            ParentFolderId = options?.AttachRootToParentId
        };
        foreach (var id in result.ImportedPresetIds)
            folder.AddPreset(id);

        target.Folders.Add(folder);
        return new PresetImportResult {
            ImportedPresets = result.ImportedPresets,
            SkippedPresets = result.SkippedPresets,
            FoldersAdded = 1,
            FirstImported = result.FirstImported,
            CreatedRootFolder = folder,
            ImportedPresetIds = result.ImportedPresetIds
        };
    }

    public static PresetImportResult ImportFolderTree(FishingPresets target, PresetFolder root, IReadOnlyList<PresetFolder> sourceFolders, IReadOnlyList<CustomPresetConfig> sourcePresets, PresetImportOptions? options = null) {
        options ??= new PresetImportOptions();
        var sources = FilterSources(sourcePresets, options.SelectedPresetIds);
        if (sources.Count == 0)
            return PresetImportResult.Empty;

        var (copies, skipped, presetIdMap) = ClonePresetsWithMap(target, sources, options);
        if (copies.Count == 0)
            return new PresetImportResult { SkippedPresets = skipped };

        foreach (var copy in copies)
            target.CustomPresets.Add(copy);

        var foldersAdded = 0;
        PresetFolder? createdRoot = null;
        CloneFolderRecursive(root, options.AttachRootToParentId);

        var first = copies[0];
        MaybeSelect(target, first, options);

        return new PresetImportResult {
            ImportedPresets = copies.Count,
            SkippedPresets = skipped,
            FoldersAdded = foldersAdded,
            FirstImported = first,
            CreatedRootFolder = createdRoot,
            ImportedPresetIds = [.. copies.Select(c => c.UniqueId)]
        };

        void CloneFolderRecursive(PresetFolder source, Guid? newParentId) {
            if (!FolderTreeHasMappedPresets(source, sourceFolders, presetIdMap))
                return;

            var newFolder = new PresetFolder(source.FolderName) {
                ParentFolderId = newParentId,
                IsExpanded = source.IsExpanded
            };

            foreach (var oldPresetId in source.PresetIds) {
                if (presetIdMap.TryGetValue(oldPresetId, out var newPresetId))
                    newFolder.AddPreset(newPresetId);
            }

            target.Folders.Add(newFolder);
            foldersAdded++;
            createdRoot ??= newFolder;

            foreach (var child in sourceFolders.Where(f => f.ParentFolderId == source.UniqueId))
                CloneFolderRecursive(child, newFolder.UniqueId);
        }
    }

    public static int CountFolderTreeItems(PresetFolder root, IReadOnlyList<PresetFolder> allFolders) {
        var folderCount = 0;
        var presetCount = 0;
        Walk(root);
        return folderCount + presetCount;

        void Walk(PresetFolder folder) {
            folderCount++;
            presetCount += folder.PresetIds.Count;
            foreach (var child in allFolders.Where(f => f.ParentFolderId == folder.UniqueId))
                Walk(child);
        }
    }

    public static bool FolderTreeHasMappedPresets(PresetFolder folder, IReadOnlyList<PresetFolder> allFolders, IReadOnlyDictionary<Guid, Guid> presetIdMap) {
        if (folder.PresetIds.Any(presetIdMap.ContainsKey))
            return true;
        return allFolders.Where(f => f.ParentFolderId == folder.UniqueId).Any(child => FolderTreeHasMappedPresets(child, allFolders, presetIdMap));
    }

    private static List<CustomPresetConfig> FilterSources(
        IEnumerable<CustomPresetConfig> sourcePresets,
        IReadOnlySet<Guid>? selectedPresetIds) {
        var list = sourcePresets.ToList();
        if (selectedPresetIds == null)
            return list;
        return [.. list.Where(p => selectedPresetIds.Contains(p.UniqueId))];
    }

    private static (List<CustomPresetConfig> Copies, int Skipped, Dictionary<Guid, Guid> PresetIdMap) ClonePresetsWithMap(FishingPresets target, List<CustomPresetConfig> sources, PresetImportOptions options) {
        var copies = new List<CustomPresetConfig>(sources.Count);
        var presetIdMap = new Dictionary<Guid, Guid>();

        foreach (var source in sources) {
            var copy = ClonePreset(source);
            copy.UniqueId = Guid.NewGuid();
            copies.Add(copy);
            presetIdMap[source.UniqueId] = copy.UniqueId;
        }

        if (options.MakeAnonymous && copies.Count > 0) {
            var nameMap = CustomPresetConfig.BuildAnonymousNameMap(copies);
            foreach (var copy in copies)
                copy.PresetName = nameMap[copy.PresetName];
            CustomPresetConfig.RemapPresetSwapReferences(copies, nameMap);
        }

        if (!options.SkipDuplicateNames)
            return (copies, 0, presetIdMap);

        var existingNames = new HashSet<string>(target.PresetList.Select(p => p.PresetName), StringComparer.Ordinal);
        var kept = new List<CustomPresetConfig>();
        var keptMap = new Dictionary<Guid, Guid>();
        var skipped = 0;

        foreach (var source in sources) {
            if (!presetIdMap.TryGetValue(source.UniqueId, out var newId))
                continue;

            var copy = copies.First(c => c.UniqueId == newId);
            if (existingNames.Contains(copy.PresetName)) {
                skipped++;
                continue;
            }

            existingNames.Add(copy.PresetName);
            kept.Add(copy);
            keptMap[source.UniqueId] = newId;
        }

        return (kept, skipped, keptMap);
    }

    private static void MaybeSelect(FishingPresets target, CustomPresetConfig first, PresetImportOptions options) {
        if (!options.SelectFirst)
            return;

        if (!string.IsNullOrEmpty(options.SelectReason))
            target.Select(first, options.SelectReason);
        else
            target.SelectedPreset = first;
    }
}
