namespace LiveCG.Core;

public static class ProjectSources
{
    public static void Resolve(ProjectDocument project, string projectPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        foreach (var job in project.Jobs)
        {
            var candidate = Path.GetFullPath(job.SourcePath, folder);
            var nearby = Path.Combine(folder, job.FileName);
            job.SourcePath = File.Exists(candidate) || !File.Exists(nearby) ? candidate : nearby;
        }
    }

    public static ProjectDocument ForSave(ProjectDocument snapshot, string projectPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        foreach (var job in snapshot.Jobs)
        {
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
            File.Copy(job.SourcePath, path);
            job.SourcePath = Path.GetRelativePath(folder, path);
        }
        var projectPath = Path.Combine(folder, "방송자막.livecg.json");
        DocumentStore.Save(projectPath, portable);
        return projectPath;
    }

    public static void Update(ImageJob job, string path, int width, int height, bool replace)
    {
        job.SourcePath = path;
        job.Width = width;
        job.Height = height;
        if (!replace) return;
        foreach (var region in job.Regions)
        {
            region.Cutout = null;
            region.DigitDividers.Clear();
        }
    }
}
