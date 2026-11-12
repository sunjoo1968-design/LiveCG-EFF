using System.Text.Json.Nodes;

namespace LiveCG.Core;

public static class ObsScenePaths
{
    public static int Relink(string source, string destination, ProjectDocument project)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in project.Jobs.Where(job => Directory.Exists(job.OutputPath) || LinkedOutput.MatchesFlat(job.OutputPath, job.BroadcastId)))
        {
            if (LinkedOutput.IsFlat(job.OutputPath))
            {
                foreach (var old in job.OutputAliases.Append(job.OutputPath).Where(path => Path.IsPathRooted(path) && LinkedOutput.IsFlat(path)))
                    map[Path.GetFullPath(old)] = job.OutputPath;
                continue;
            }
            var names = Directory.GetFiles(job.OutputPath, "*index.html", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).ToArray();
            foreach (var old in job.OutputAliases.Append(job.OutputPath).Where(Path.IsPathRooted))
                foreach (var name in names)
                    map[Path.GetFullPath(Path.Combine(old, name!))] = Path.Combine(job.OutputPath, name!);
        }
        var node = JsonNode.Parse(File.ReadAllText(source)) ?? throw new InvalidDataException("OBS 장면 파일이 비어 있습니다.");
        var changed = 0;
        void Visit(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var property in obj.ToArray())
                {
                    if (property.Key == "local_file" && property.Value is JsonValue value && value.TryGetValue<string>(out var path) && Path.IsPathRooted(path) &&
                        map.TryGetValue(Path.GetFullPath(path), out var resolved))
                    { obj[property.Key] = resolved.Replace(Path.DirectorySeparatorChar, '/'); changed++; }
                    else Visit(property.Value);
                }
            }
            else if (current is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(node);
        File.WriteAllText(destination, node.ToJsonString(new() { WriteIndented = true }));
        return changed;
    }
}
