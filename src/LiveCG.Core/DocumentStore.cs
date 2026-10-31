using System.Text.Json;

namespace LiveCG.Core;

public static class DocumentStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string DefaultsPath => Path.Combine(Environment.GetEnvironmentVariable("LIVECG_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name), "defaults.json");
    public static string DataDirectory => Path.GetDirectoryName(DefaultsPath)!;
    public static void Save<T>(string path, T document)
    {
        Validate(document); path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static T Load<T>(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("작업 파일이 너무 큽니다.");
            var document = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("빈 설정 파일입니다.");
            Upgrade(document); Validate(document); return document;
        }
        catch (JsonException e) { throw new InvalidDataException("설정 파일이 손상되었거나 형식이 올바르지 않습니다.", e); }
        catch (NullReferenceException e) { throw new InvalidDataException("설정 파일의 필수 정보가 없습니다.", e); }
    }
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
    private static void Upgrade<T>(T document)
    {
        switch (document)
        {
            case ProjectDocument d when d.SchemaVersion == 1:
                foreach (var job in d.Jobs) UpgradeRegions(job.Regions, d.Video.Duration);
                var end = d.Jobs.Select(j => TimelineRules.LastEnd(j.Regions)).DefaultIfEmpty(0).Max();
                d.Video.EndHold = Math.Clamp(d.Video.Duration - end, 0, 30); d.SchemaVersion = 2; break;
            case TemplateDocument d when d.SchemaVersion == 1:
                UpgradeRegions(d.Regions, d.Video.Duration);
                d.Video.EndHold = Math.Clamp(d.Video.Duration - TimelineRules.LastEnd(d.Regions), 0, 30); d.SchemaVersion = 2; break;
            case AppDefaults d when d.SchemaVersion == 1:
                d.ClipDuration = Math.Clamp((d.Effect.Duration ?? 1) / Math.Max(.25, d.Effect.Speed), .05, 300);
                d.Effect = d.Effect.Current(); d.SchemaVersion = 2; break;
        }
        switch (document)
        {
            case ProjectDocument d when d.SchemaVersion == 2: d.SchemaVersion = 3; break;
            case TemplateDocument d when d.SchemaVersion == 2: d.SchemaVersion = 3; break;
            case AppDefaults d when d.SchemaVersion == 2: d.SchemaVersion = 3; break;
        }
        switch(document)
        {
            case ProjectDocument d when d.SchemaVersion == 3:d.SchemaVersion=4;break;
            case TemplateDocument d when d.SchemaVersion == 3:d.SchemaVersion=4;break;
            case AppDefaults d when d.SchemaVersion == 3:d.SchemaVersion=4;break;
        }
        switch(document)
        {
            case ProjectDocument d when d.SchemaVersion==4:foreach(var j in d.Jobs)j.Regions.Reverse();d.SchemaVersion=5;break;
            case TemplateDocument d when d.SchemaVersion==4:d.Regions.Reverse();d.SchemaVersion=5;break;
            case AppDefaults d when d.SchemaVersion==4:d.SchemaVersion=5;break;
        }
        switch(document)
        {
            case ProjectDocument d when d.SchemaVersion==5:d.SchemaVersion=6;break;
            case TemplateDocument d when d.SchemaVersion==5:d.SchemaVersion=6;break;
            case AppDefaults d when d.SchemaVersion==5:d.SchemaVersion=6;break;
        }
    }
    private static void UpgradeRegions(List<EffectRegion> regions, double length)
    {
        if (!double.IsFinite(length) || length is <= 0 or > 300) throw new InvalidDataException("이전 작업의 영상 길이가 올바르지 않습니다.");
        foreach (var region in regions)
        {
            if (region.LegacyEffect is not { } old) continue;
            var duration = (old.Duration ?? 1) / Math.Max(.25, old.Speed);
            var period = old.Period ?? 3;
            if (!double.IsFinite(duration + period) || duration <= 0 || period < .1) throw new InvalidDataException("이전 효과 시간이 올바르지 않습니다.");
            if (region.Clips.Count == 0)
                for (double start = 0; start < length - .016; start += period)
                {
                    region.Clips.Add(new() { Start = start, Duration = Math.Max(.016, Math.Min(duration, length - start)), Options = old.Current() with { Speed = 1 } });
                    if (old.Repeat == false) break;
                }
            region.LegacyEffect = null;
        }
    }
    private static void Validate<T>(T document)
    {
        switch (document)
        {
            case AppDefaults d: d.Validate(); break;
            case ProjectDocument d: d.Validate(); break;
            case TemplateDocument d: d.Validate(); break;
            case EffectAsset d: d.Validate(); break;
            default: throw new InvalidDataException("지원하지 않는 문서입니다.");
        }
    }
}
