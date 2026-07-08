using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace Racks.Core
{
    public class FileData
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string SemanticText { get; set; } = string.Empty;
    }

    public class FileClusterPrediction
    {
        [ColumnName("PredictedLabel")]
        public uint PredictedClusterId { get; set; }
        [ColumnName("Score")]
        public float[] Distances { get; set; } = Array.Empty<float>();
    }

    public class ClusterGroup
    {
        public string Name { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new List<string>();
    }

    public static class AutoOrganizer
    {
        // Hybrid strategy that reads as "smart" for any desktop size:
        //   1. Bucket everything by type category (Images, Documents, Videos, ...). This is
        //      what people actually expect and it's deterministic - no weird 2-file clusters.
        //   2. For a big bucket (> SubdivideThreshold), use ML to split it by filename theme
        //      (e.g. "Invoices" vs "Screenshots" inside Images), so large piles get useful
        //      sub-racks instead of one giant one.
        //   3. Fold tiny leftover buckets (1 item) into a single "Misc" rack so we don't
        //      spawn a rack per stray file.
        private const int SubdivideThreshold = 8; // buckets larger than this get ML sub-clustering
        private const int MinBucketToKeep = 2;    // buckets smaller than this go to Misc

        public static List<ClusterGroup> AnalyzeDesktop(string desktopPath)
        {
            if (!Directory.Exists(desktopPath))
                return new List<ClusterGroup>();

            var files = Directory.GetFileSystemEntries(desktopPath)
                .Where(f => !new FileInfo(f).Attributes.HasFlag(FileAttributes.Hidden)
                            && !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Select(ToFileData)
                .ToList();

            if (files.Count == 0) return new List<ClusterGroup>();

            // Step 1: bucket by human category.
            var byCategory = files
                .GroupBy(f => GetCategoryNameForExtension(f.Extension))
                .ToList();

            var result = new List<ClusterGroup>();
            var misc = new List<FileData>();

            foreach (var bucket in byCategory)
            {
                var items = bucket.ToList();

                // Tiny buckets aren't worth their own rack - collect them into Misc.
                if (items.Count < MinBucketToKeep)
                {
                    misc.AddRange(items);
                    continue;
                }

                // Big buckets: try to split by filename theme.
                if (items.Count > SubdivideThreshold)
                {
                    var sub = SubdivideByTheme(items);
                    if (sub.Count > 1)
                    {
                        foreach (var group in sub)
                            AddGroup(result, $"{bucket.Key} · {group.Key}", group.Value);
                        continue;
                    }
                }

                AddGroup(result, bucket.Key, items);
            }

            if (misc.Count > 0)
                AddGroup(result, misc.Count == 1 ? SingleName(misc[0]) : "Misc", misc);

            // Largest racks first so the preview leads with the most useful ones.
            return result.OrderByDescending(r => r.FilePaths.Count).ToList();
        }

        private static FileData ToFileData(string f)
        {
            bool isFolder = Directory.Exists(f);
            var name = isFolder ? Path.GetFileName(f) : Path.GetFileNameWithoutExtension(f);
            var ext = isFolder ? "folder" : Path.GetExtension(f).ToLowerInvariant().TrimStart('.');
            return new FileData
            {
                FilePath = f,
                FileName = name,
                Extension = ext,
                SemanticText = $"{name} {GetSemanticTagsForExtension(ext)}"
            };
        }

        // Split one large category into filename-theme groups via ML KMeans over the file
        // names. Returns theme-name -> files. Falls back to a single group if ML can't find
        // a meaningful split.
        private static Dictionary<string, List<FileData>> SubdivideByTheme(List<FileData> items)
        {
            var outGroups = new Dictionary<string, List<FileData>>();
            try
            {
                int k = Math.Min(4, Math.Max(2, items.Count / 6));
                var mlContext = new MLContext(seed: 0);
                var dataView = mlContext.Data.LoadFromEnumerable(items);
                var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", nameof(FileData.FileName))
                    .Append(mlContext.Clustering.Trainers.KMeans("Features", numberOfClusters: k));
                var model = pipeline.Fit(dataView);
                var preds = mlContext.Data
                    .CreateEnumerable<FileClusterPrediction>(model.Transform(dataView), reuseRowObject: false)
                    .ToList();

                var byCluster = new Dictionary<uint, List<FileData>>();
                for (int i = 0; i < items.Count; i++)
                {
                    var id = preds[i].PredictedClusterId;
                    if (!byCluster.ContainsKey(id)) byCluster[id] = new List<FileData>();
                    byCluster[id].Add(items[i]);
                }

                foreach (var c in byCluster.Values)
                {
                    string theme = CommonThemeWord(c) ?? "Other";
                    // Merge same-theme clusters.
                    if (outGroups.ContainsKey(theme)) outGroups[theme].AddRange(c);
                    else outGroups[theme] = c;
                }
            }
            catch
            {
                outGroups.Clear();
            }
            return outGroups.Count > 0 ? outGroups : new Dictionary<string, List<FileData>> { ["All"] = items };
        }

        // Most common meaningful word across a set of filenames, Title-cased. Null if none.
        private static string? CommonThemeWord(List<FileData> items)
        {
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "the", "and", "for", "new", "copy", "final", "version", "file", "document", "img", "image", "screenshot" };
            var word = items
                .SelectMany(f => f.FileName.Split(new[] { ' ', '_', '-', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length > 3 && !stop.Contains(w) && !w.All(char.IsDigit))
                .GroupBy(w => w.ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
            return word == null ? null : char.ToUpper(word[0]) + word.Substring(1);
        }

        private static string SingleName(FileData f)
        {
            var cat = GetCategoryNameForExtension(f.Extension);
            return cat == "Misc" ? "Misc" : cat;
        }

        private static void AddGroup(List<ClusterGroup> result, string name, List<FileData> items)
        {
            int suffix = 1;
            string finalName = name;
            while (result.Any(r => r.Name == finalName)) finalName = $"{name} {++suffix}";
            result.Add(new ClusterGroup { Name = finalName, FilePaths = items.Select(f => f.FilePath).ToList() });
        }

        private static string GetSemanticTagsForExtension(string ext)
        {
            switch (ext)
            {
                case "png": case "jpg": case "jpeg": case "gif": case "bmp": case "svg": case "webp":
                    return "image photo picture graphic media visual art design";
                case "mp4": case "mkv": case "avi": case "mov": case "webm":
                    return "video movie clip media motion film animation capcut";
                case "mp3": case "wav": case "flac": case "ogg":
                    return "audio sound music track voice media";
                case "pdf": case "docx": case "doc": case "txt": case "rtf": case "odt":
                    return "document text book article letter report read writing paper";
                case "xlsx": case "xls": case "csv":
                    return "spreadsheet data table excel finance money accounting numbers invoice receipt contas taxes bill";
                case "pptx": case "ppt":
                    return "presentation slides pitch deck";
                case "zip": case "rar": case "7z": case "tar": case "gz":
                    return "archive compressed zip bundle files package";
                case "exe": case "msi": case "bat": case "cmd": case "ps1":
                    return "executable program app installer script software run software";
                case "cs": case "js": case "py": case "html": case "css": case "cpp": case "h": case "json": case "xml": case "sln": case "csproj":
                    return "code programming script developer engineering source git project";
                case "lnk": case "url":
                    return "shortcut link web application app start game launcher";
                case "folder":
                    return "folder directory project files group collection";
                default:
                    return "misc file data unknown other";
            }
        }

        private static string GetCategoryNameForExtension(string ext)
        {
            switch (ext)
            {
                case "png": case "jpg": case "jpeg": case "gif": case "bmp": case "svg": case "webp": return "Images";
                case "mp4": case "mkv": case "avi": case "mov": case "webm": return "Videos";
                case "mp3": case "wav": case "flac": case "ogg": return "Audio";
                case "pdf": case "docx": case "doc": case "txt": case "rtf": case "odt": return "Documents";
                case "xlsx": case "xls": case "csv": return "Spreadsheets";
                case "pptx": case "ppt": return "Presentations";
                case "zip": case "rar": case "7z": case "tar": case "gz": return "Archives";
                case "exe": case "msi": case "bat": case "cmd": case "ps1": return "Programs";
                case "cs": case "js": case "py": case "html": case "css": case "cpp": case "h": case "json": case "xml": case "sln": case "csproj": return "Development";
                case "lnk": case "url": return "Apps";
                case "folder": return "Folders";
                default: return "Misc";
            }
        }
    }
}
