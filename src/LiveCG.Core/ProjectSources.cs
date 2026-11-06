namespace LiveCG.Core;

public static class ProjectSources
{
    public static void Resolve(ProjectDocument project, string projectPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        foreach (var job in project.Jobs)
        {
            var candidate = Path.GetFullPath(job.SourcePath, folder);
            job.SourcePath = File.Exists(candidate) ? candidate : Find(folder, job) ?? candidate;
            var output = string.IsNullOrEmpty(job.OutputPath) ? "" : Path.GetFullPath(job.OutputPath, folder);
            if (!string.IsNullOrEmpty(output) && BrowserExporter.IsPackage(output)) job.OutputPath = output;
            else
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                var manifests = Directory.EnumerateFiles(folder, "manifest.json", options).ToArray();
                job.OutputPath = manifests.Select(Path.GetDirectoryName).FirstOrDefault(path => LinkedOutput.Matches(path!, job.BroadcastId)) ?? "";
                if (job.OutputPath.Length == 0)
                {
                    var legacyMatches = manifests.Select(Path.GetDirectoryName).Where(path => LinkedOutput.MatchesLegacy(path!, job)).Take(2).ToArray();
                    if (legacyMatches.Length == 1) job.OutputPath = legacyMatches[0]!;
                }
                if (job.OutputPath.Length == 0 && !string.IsNullOrEmpty(project.OutputDirectory))
                {
                    var legacy = Path.Combine(Path.GetFullPath(project.OutputDirectory, folder), BrowserExporter.OutputName(job));
                    if (BrowserExporter.IsPackage(legacy)) job.OutputPath = legacy;
                }
            }
        }
        if (!string.IsNullOrEmpty(project.OutputDirectory)) project.OutputDirectory = Path.GetFullPath(project.OutputDirectory, folder);
    }

    public static ProjectDocument ForSave(ProjectDocument snapshot, string projectPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        foreach (var job in snapshot.Jobs)
        {
            Fingerprint(job);
            if (!string.IsNullOrEmpty(job.OutputPath))
            {
                var absolute = Path.GetFullPath(job.OutputPath);
                if (!job.OutputAliases.Contains(absolute, StringComparer.OrdinalIgnoreCase)) job.OutputAliases.Add(absolute);
                job.OutputPath = Path.GetRelativePath(folder, absolute);
            }
            var relative = Path.GetRelativePath(folder, job.SourcePath);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                job.SourcePath = relative;
        }
        if (!string.IsNullOrEmpty(snapshot.OutputDirectory))
            snapshot.OutputDirectory = Path.GetRelativePath(folder, Path.GetFullPath(snapshot.OutputDirectory));
        return snapshot;
    }

    public static string SavePortable(ProjectDocument project, string destination)
    {
        var folder = Path.Combine(destination, "LiveCG-" + Guid.NewGuid().ToString("N"));
        foreach (var job in project.Jobs)
            if (!File.Exists(job.SourcePath))
                throw new FileNotFoundException("원본을 먼저 다시 연결하세요.", job.SourcePath);

        var portable = new ProjectDocument { Video = project.Video, Jobs = project.Jobs.Select(job => job.Snapshot()).ToList() };
        for (var i = 0; i < portable.Jobs.Count; i++)
        {
            var job = portable.Jobs[i];
            var path = Path.Combine(folder, "자료", (i + 1).ToString("D3"), job.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Fingerprint(job);
            File.Copy(job.SourcePath, path);
            job.SourcePath = Path.GetRelativePath(folder, path);
            if (!string.IsNullOrEmpty(job.OutputPath) && BrowserExporter.IsPackage(job.OutputPath))
            {
                var output = Path.Combine(folder, "출력", (i + 1).ToString("D3"), Path.GetFileName(job.OutputPath));
                if (!job.OutputAliases.Contains(job.OutputPath, StringComparer.OrdinalIgnoreCase)) job.OutputAliases.Add(job.OutputPath);
                foreach (var file in Directory.GetFiles(job.OutputPath, "*", SearchOption.AllDirectories))
                {
                    var copy = Path.Combine(output, Path.GetRelativePath(job.OutputPath, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(file, copy);
                }
                job.OutputPath = Path.GetRelativePath(folder, output);
            }
        }
        var projectPath = Path.Combine(folder, "방송자막.livecg.json");
        DocumentStore.Save(projectPath, portable);
        return projectPath;
    }

    public static string? Find(string root, ImageJob job)
    {
        if (!Directory.Exists(root)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var candidates = Directory.EnumerateFiles(root, "*", options).Where(SourceImage.Supported);
        if (string.IsNullOrEmpty(job.SourceFingerprint))
        {
            var names = candidates.Where(path => Path.GetFileName(path).Equals(job.FileName, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            return names.Length == 1 ? names[0] : null;
        }
        foreach (var path in candidates)
        {
            try
            {
                if (new FileInfo(path).Length != job.SourceBytes) continue;
                using var stream = File.OpenRead(path);
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)) == job.SourceFingerprint)
                    return path;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static void Fingerprint(ImageJob job)
    {
        if (!File.Exists(job.SourcePath)) return;
        using var stream = File.OpenRead(job.SourcePath);
        job.SourceBytes = stream.Length;
        job.SourceFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
    public static void Update(ImageJob job, string path, int width, int height, bool replace)
    {
        job.SourcePath = path;
        job.Width = width;
        job.Height = height;
        Fingerprint(job);
        if (!replace) return;
        foreach (var region in job.Regions)
        {
            region.Cutout = null;
            region.DigitDividers.Clear();
        }
    }
}
