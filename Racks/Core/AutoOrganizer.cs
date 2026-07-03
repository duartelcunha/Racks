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
        public static List<ClusterGroup> AnalyzeDesktop(string desktopPath)
        {
            if (!Directory.Exists(desktopPath))
                return new List<ClusterGroup>();

            var files = Directory.GetFileSystemEntries(desktopPath)
                .Where(f => !new FileInfo(f).Attributes.HasFlag(FileAttributes.Hidden) && !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0) return new List<ClusterGroup>();
            if (files.Count < 3)
            {
                // Not enough files to cluster meaningfully.
                return new List<ClusterGroup> { new ClusterGroup { Name = "Misc", FilePaths = files } };
            }

            var fileDataList = new List<FileData>();
            foreach (var f in files)
            {
                bool isFolder = Directory.Exists(f);
                var name = isFolder ? Path.GetFileName(f) : Path.GetFileNameWithoutExtension(f);
                var ext = isFolder ? "folder" : Path.GetExtension(f).ToLowerInvariant().TrimStart('.');
                
                string semanticText = $"{name} {GetSemanticTagsForExtension(ext)}";
                
                fileDataList.Add(new FileData 
                { 
                    FilePath = f, 
                    FileName = name, 
                    Extension = ext,
                    SemanticText = semanticText 
                });
            }

            var mlContext = new MLContext(seed: 0);
            var dataView = mlContext.Data.LoadFromEnumerable(fileDataList);

            // TF-IDF pipeline: Tokenize -> Featurize -> PCA -> KMeans
            var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", "SemanticText")
                .Append(mlContext.Clustering.Trainers.KMeans(
                    featureColumnName: "Features",
                    numberOfClusters: Math.Min(5, Math.Max(2, files.Count / 4))));

            var model = pipeline.Fit(dataView);
            var predictions = mlContext.Data.CreateEnumerable<FileClusterPrediction>(model.Transform(dataView), reuseRowObject: false).ToList();

            var clusters = new Dictionary<uint, List<FileData>>();
            for (int i = 0; i < fileDataList.Count; i++)
            {
                var pred = predictions[i];
                if (!clusters.ContainsKey(pred.PredictedClusterId))
                    clusters[pred.PredictedClusterId] = new List<FileData>();
                
                clusters[pred.PredictedClusterId].Add(fileDataList[i]);
            }

            var result = new List<ClusterGroup>();
            foreach (var kvp in clusters)
            {
                var clusterFiles = kvp.Value;
                var commonExt = clusterFiles.GroupBy(f => f.Extension)
                                            .OrderByDescending(g => g.Count())
                                            .First().Key;
                
                string categoryName = GetCategoryNameForExtension(commonExt);
                
                // If it's generic or empty, try to find a common word in the filenames
                if (categoryName == "Misc")
                {
                    var allWords = clusterFiles.SelectMany(f => f.FileName.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries))
                                               .Where(w => w.Length > 3)
                                               .GroupBy(w => w.ToLowerInvariant())
                                               .OrderByDescending(g => g.Count())
                                               .Select(g => g.First())
                                               .ToList();
                    
                    if (allWords.Count > 0)
                    {
                        var bestWord = allWords[0];
                        categoryName = char.ToUpper(bestWord[0]) + bestWord.Substring(1);
                    }
                }

                // Deduplicate names
                int suffix = 1;
                string finalName = categoryName;
                while (result.Any(r => r.Name == finalName))
                {
                    suffix++;
                    finalName = $"{categoryName} {suffix}";
                }

                result.Add(new ClusterGroup 
                { 
                    Name = finalName, 
                    FilePaths = clusterFiles.Select(f => f.FilePath).ToList() 
                });
            }

            return result;
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
