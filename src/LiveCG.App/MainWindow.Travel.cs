using System.Windows;
using LiveCG.Core;
using SkiaSharp;
namespace LiveCG.App;
public partial class MainWindow
{
    private void EditDigitCells(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null||_region is null){Notify("먼저 숫자 영역을 선택하세요.");return;}try{using var source=SourceImage.Read(_current.SourcePath);using var crop=ObjectExtractor.CropObject(source!,_region);var window=new DigitCellWindow(crop,_region.DigitDividers){Owner=this};if(window.ShowDialog()==true){_region.DigitDividers=window.Dividers.Distinct().Order().ToList();MarkEdited();Preview.InvalidateVisual();Notify($"숫자 칸 {window.Dividers.Count+1}개 · 원본 숫자로 정착합니다.");}}catch(Exception ex){Error(ex);}
    }
    private void OpenTravelRoute(object sender,RoutedEventArgs e)=>OpenRouteEditor(_clip?.Options.Kind==EffectKind.TravelRoute?_clip:null,null);
    private void OpenRouteEditor(EffectClip? edit,EffectAsset? asset)
    {
        if(_busy||_current is null){Notify("먼저 지도 이미지를 추가하세요.");return;}
        if(_region is null){SetDrawMode(true);Notify("먼저 지도에 직접 지정으로 영역을 그린 뒤 여행 경로를 선택하세요.");return;}
        if(!CommitClip(true)||!CommitRegion(true))return;
        if(edit is null&&_current.Regions.Count>=100){Notify("영역은 파일당 최대100개입니다.");return;}
        try
        {
            using var source=SourceImage.Read(_current.SourcePath)!;var bounds=_region.Bounds;using var crop=new SKBitmap();source.ExtractSubset(crop,ObjectExtractor.PixelRect(bounds,source.Width,source.Height));
            var draft=(edit?.Route??asset?.Clips.FirstOrDefault()?.Route??new()).Copy();if(draft.DurationBudget is { } budget)RouteEditing.AllocateDuration(draft,budget,crop.Width,crop.Height,_video.Fps);
            var window=new TravelRouteWindow(crop,draft,_video.Fps){Owner=this};
            window.SaveAssetRequested+=route=>{var saved=new EffectAsset{Name=window.AssetName,Category="여행 경로",Clips=[new(){Duration=route.Duration,Section=route.Repeat?ClipSection.Loop:ClipSection.Intro,Loop=route.Repeat,LoopPeriod=route.Duration,Route=route.Copy(),Options=new(){Kind=EffectKind.TravelRoute}}]};saved.ThumbnailPng=EffectSample.Thumbnail(saved);AssetStore.Save(saved);RefreshAssets();Notify("내 여행 경로에 저장했습니다.");};
            if(window.ShowDialog()!=true)return;var route=window.Route.Copy();route.Validate();EffectClip clip;var targetRegion=_region;
            if(edit is not null){clip=edit;clip.Route=route;clip.Duration=TimelineRules.Snap(route.Duration,_video.Fps);clip.SourceOffset=0;clip.SourceDuration=null;}
            else{var area=new EffectRegion{Name="여행 경로",Bounds=bounds,OverlayOnly=true,ContentOnly=false,Background=BackgroundMode.KeepOriginal};_current.Regions.Insert(0,area);targetRegion=area;clip=new(){Start=TimelineRules.Snap(_time,_video.Fps),Duration=TimelineRules.Snap(route.Duration,_video.Fps),Route=route,Options=new(){Kind=EffectKind.TravelRoute},Section=ClipSection.Intro};area.Clips.Add(clip);}
            clip.Section=route.Repeat?ClipSection.Loop:ClipSection.Intro;if(route.Repeat){var introEnd=_current.Regions.SelectMany(r=>r.Clips).Where(c=>c.Id!=clip.Id&&SectionRules.Effective(c)==ClipSection.Intro).Select(c=>c.End).DefaultIfEmpty(0).Max();clip.Start=Math.Max(clip.Start,introEnd);}clip.Loop=route.Repeat;clip.LoopPeriod=route.Duration;_current.Playback.Enabled=true;_current.Playback.ByEffects=true;_clip=null;SelectRegion(targetRegion,clip);MarkEdited();RefreshRegionList();RefreshTimeline();Seek(clip.Start,true);
        }catch(Exception ex){Error(ex);}
    }
}
