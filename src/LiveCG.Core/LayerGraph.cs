namespace LiveCG.Core;

public sealed record LayerRow(EffectRegion Region,MaskTrack? Mask,int Depth);
public static class LayerGraph
{
    public static List<LayerRow> Rows(IReadOnlyList<EffectRegion> regions)
    {
        var rows=new List<LayerRow>();
        void Add(EffectRegion r,int depth){rows.Add(new(r,null,depth));if(r.Collapsed)return;foreach(var mask in r.Masks)rows.Add(new(r,mask,depth+1));if(r.Role==RegionRole.Group)foreach(var child in regions.Where(c=>c.ParentId==r.Id))Add(child,depth+1);}
        foreach(var r in regions.Where(r=>r.ParentId is null))Add(r,0);return rows;
    }
    public static bool Visible(EffectRegion region,IReadOnlyList<EffectRegion> regions)=>region.Enabled&&(region.ParentId is null||regions.Any(p=>p.Id==region.ParentId&&p.Enabled));
    public static void Validate(IReadOnlyList<EffectRegion> regions)
    {
        if(regions.Select(r=>r.Id).Distinct().Count()!=regions.Count)throw new InvalidDataException("중복된 영역 ID입니다.");
        foreach(var r in regions)
        {
            if(r.Role==RegionRole.EffectArea&&(r.ParentId is null||!regions.Any(p=>p.Id==r.ParentId&&p.Role==RegionRole.Group&&p.ParentId is null)))throw new InvalidDataException("효과 영역에 유효한 부모 그룹이 필요합니다.");
            if(r.Role!=RegionRole.EffectArea&&r.ParentId is not null)throw new InvalidDataException("그룹 안 효과 영역만 부모를 지정할 수 있습니다.");
        }
        var ids=regions.SelectMany(r=>r.Masks).Select(m=>m.Id).ToArray();if(ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("중복된 마스크 ID입니다.");
    }
    public static List<EffectRegion> Copy(IEnumerable<EffectRegion> regions,bool newIds=true)
    {
        var source=regions.ToList();var copies=source.Select(r=>r.Copy(newIds)).ToList();var map=source.Select((r,i)=>(r.Id,Next:copies[i].Id)).ToDictionary(x=>x.Id,x=>x.Next);
        foreach(var copy in copies)if(copy.ParentId is { } id)copy.ParentId=map.TryGetValue(id,out var next)?next:null;
        foreach(var copy in copies)if(copy.Role==RegionRole.EffectArea&&copy.ParentId is null)copy.Role=RegionRole.Object;return copies;
    }
}
