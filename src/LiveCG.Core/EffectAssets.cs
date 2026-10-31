using SkiaSharp;
using System.Text.Json.Serialization;

namespace LiveCG.Core;

public class EffectAsset
{
    public int SchemaVersion { get; set; }=1;
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Name { get; set; }="새 효과";
    public string Category { get; set; }="내 효과";
    public string Description { get; set; }="";
    public bool Favorite { get; set; }
    [JsonIgnore] public string DisplayName=>(Favorite?"★ ":"")+Name;
    public string ThumbnailPng { get; set; }="";
    public List<EffectClip> Clips { get; set; }=[];
    public double Duration=>Clips.Select(c=>c.End).DefaultIfEmpty(0).Max();
    public void Validate()
    {
        if(SchemaVersion!=1||Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||Name.Length>100||Category.Length>100||Description.Length>4000||ThumbnailPng.Length>1000000||Clips is null||Clips.Count is <1 or >1000||Duration>300)throw new InvalidDataException("효과 에셋의 이름·길이·내용을 확인하세요.");
        if(ThumbnailPng.Length>0)try{using var codec=SKCodec.Create(new SKMemoryStream(Convert.FromBase64String(ThumbnailPng)));if(codec is null||codec.Info.Width>1024||codec.Info.Height>1024)throw new InvalidDataException("에셋 썸네일을 확인하세요.");}catch(FormatException ex){throw new InvalidDataException("에셋 썸네일 데이터가 손상됐습니다.",ex);}
        foreach(var clip in Clips)clip.Validate();
    }
    public EffectAsset Copy(bool newId=false)=>new(){Id=newId?Guid.NewGuid():Id,Name=Name,Category=Category,Description=Description,Favorite=Favorite,ThumbnailPng=ThumbnailPng,Clips=Clips.Select(c=>c.Copy()).ToList()};
    public static EffectAsset NewMotion()=>new(){Name="새 키프레임 효과",Clips=[new(){Duration=2,Options=new(){Kind=EffectKind.UserMotion},Motion=new()}]};
}
public static class AssetStore
{
    public static string DirectoryPath=>Path.Combine(DocumentStore.DataDirectory,"assets");
    public static string PathFor(Guid id)=>Path.Combine(DirectoryPath,id.ToString("N")+".lcgeffect.json");
    public static IReadOnlyList<EffectAsset> List(out List<string> errors)
    {
        errors=[];var list=new List<EffectAsset>();if(!Directory.Exists(DirectoryPath))return list;
        foreach(var path in Directory.EnumerateFiles(DirectoryPath,"*.lcgeffect.json"))
            try{list.Add(DocumentStore.Load<EffectAsset>(path));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){errors.Add(Path.GetFileName(path)+": "+ex.Message);}
        return list.OrderByDescending(a=>a.Favorite).ThenBy(a=>a.Name).ToList();
    }
    public static void Save(EffectAsset asset){asset.Validate();DocumentStore.Save(PathFor(asset.Id),asset);}
    public static EffectAsset Import(string path){var asset=DocumentStore.Load<EffectAsset>(path).Copy(true);Save(asset);return asset;}
    public static void Remove(Guid id)
    {
        var path=PathFor(id);if(!File.Exists(path))return;var trash=Path.Combine(DirectoryPath,"deleted");Directory.CreateDirectory(trash);
        File.Move(path,Path.Combine(trash,id.ToString("N")+"-"+DateTime.UtcNow.Ticks+".json"));
    }
    public static List<EffectClip> Instantiate(EffectAsset asset,double start,int fps)
    {
        asset.Validate();var result=new List<EffectClip>();var instance=Guid.NewGuid();var sequences=new Dictionary<Guid,Guid>();
        foreach(var item in asset.Clips){var clip=item.Copy();TimelineRules.SetTiming(clip,start+item.Start,item.Duration,fps);if(clip.SequenceId is { } id){if(!sequences.TryGetValue(id,out var next))sequences[id]=next=Guid.NewGuid();clip.SequenceId=next;}clip.AssetId=asset.Id;clip.AssetInstanceId=instance;clip.AssetName=asset.Name;result.Add(clip);}
        return result;
    }
}

public static class EffectSample
{
    public static SKBitmap DefaultObject()
    {
        var bitmap=new SKBitmap(300,150);using var c=new SKCanvas(bitmap);c.Clear(SKColors.Transparent);
        using var p=new SKPaint{IsAntialias=true,Color=new SKColor(93,226,177)};c.DrawRoundRect(SKRect.Create(8,20,284,112),16,16,p);
        using var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),54);p.Color=new(22,42,53);c.DrawText("87,070",48,96,font,p);return bitmap;
    }
    public static (SKBitmap Bitmap,NormalizedRect Bounds) Stage(SKBitmap? sample=null,int width=800,int height=450)
    {
        using var fallback=sample is null?DefaultObject():null;var source=sample??fallback!;var bitmap=new SKBitmap(width,height);
        var scale=Math.Min(width*.43/source.Width,height*.55/source.Height);var w=(float)(source.Width*scale);var h=(float)(source.Height*scale);var rect=SKRect.Create((width-w)/2,(height-h)/2,w,h);
        using var c=new SKCanvas(bitmap);c.Clear(SKColors.Transparent);using var image=SKImage.FromBitmap(source);c.DrawImage(image,rect,new SKSamplingOptions(SKCubicResampler.CatmullRom));return(bitmap,NormalizedRect.FromPixels(rect,width,height));
    }
    public static string Thumbnail(EffectAsset asset)
    {
        var (bitmap,bounds)=Stage(null,400,225);using(bitmap)using(var scene=new EffectScene(bitmap))
        using(var frame=scene.Render([new(){Bounds=bounds,Background=BackgroundMode.Transparent,Clips=asset.Clips.Select(c=>c.Copy(false)).ToList()}],new(),asset.Duration*.4))
        using(var image=SKImage.FromBitmap(frame))using(var data=image.Encode(SKEncodedImageFormat.Png,100))return Convert.ToBase64String(data.ToArray());
    }
}
