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
        }
    }

    public static ProjectDocument ForSave(ProjectDocument snapshot, string projectPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        foreach (var job in snapshot.Jobs)
        {
            Fingerprint(job);
            var relative = Path.GetRelativePath(folder, job.SourcePath);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                job.SourcePath = relative;
        }
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
