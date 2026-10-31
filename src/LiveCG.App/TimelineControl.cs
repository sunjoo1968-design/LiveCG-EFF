using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;

public sealed class TimelineControl : FrameworkElement
{
    public const double LabelWidth=250, RulerHeight=54, RowHeight=48, SectionHeight=22;
    private int _sectionDrag;private PlaybackSections? _sectionBefore;
    private ImageJob? _job;
    private VideoOptions _video=new();
    private EffectClip? _dragClip;
    private double _startX,_oldStart,_oldDuration;
    private int _resize;
    private bool _scrub;
    private EffectRegion? _rowDrag;private int _dropRow;private MaskTrack? _dragMask;private double _maskStart,_maskDuration;
    public MaskTrack? SelectedMask { get; set; }
    public List<LayerRow> Rows=>LayerGraph.Rows(_job?.Regions??[]);
    public event Action<EffectRegion,MaskTrack>? MaskSelected;
    public event Action<EffectRegion,MaskTrack>? MaskToggleRequested;
    public event Action<EffectRegion,MaskTrack,int>? MaskMoveRequested;
    public event Action<EffectRegion>? CollapseRequested;
    private Dictionary<Guid,(double Start,double Duration)> _before=[];
    public bool SnapEnabled { get; set; } = true;
    public bool RippleEnabled { get; set; } = true;
    public bool FollowMagnet { get; set; }
    public double? SnapGuide { get; private set; }
    public double PixelsPerSecond { get; private set; }=90;
    public double CursorTime { get; set; }
    public EffectRegion? SelectedRegion { get; set; }
    private EffectClip? _selectedClip;private bool _publishing;
    public HashSet<Guid> SelectedClipIds { get; }=[];
    public EffectClip? SelectedClip { get=>_selectedClip;set{_selectedClip=value;if(!_publishing){SelectedClipIds.Clear();if(value is not null)SelectedClipIds.Add(value.Id);}InvalidateVisual();} }
    public IReadOnlyList<(EffectRegion Region,EffectClip Clip)> SelectedClips=>_job?.Regions.SelectMany(r=>r.Clips.Where(c=>SelectedClipIds.Contains(c.Id)).Select(c=>(r,c))).ToList()??[];
    public void SetSelection(IEnumerable<Guid> ids,EffectClip? primary=null)
    {
        SelectedClipIds.Clear();SelectedClipIds.UnionWith(ids);var items=SelectedClips;SelectedClipIds.IntersectWith(items.Select(x=>x.Clip.Id));
        _selectedClip=primary is not null&&SelectedClipIds.Contains(primary.Id)?primary:items.LastOrDefault().Clip;
        SelectedRegion=items.FirstOrDefault(x=>x.Clip==_selectedClip).Region??SelectedRegion;PublishSelection();
    }
    private void PublishSelection(){_publishing=true;try{SelectionChanged?.Invoke(SelectedRegion,_selectedClip);}finally{_publishing=false;}InvalidateVisual();}
    public event Action<EffectRegion?,EffectClip?>? SelectionChanged;
    public event Action<double>? Seeked;
    public event Action? Edited;
    public event Action? GestureStarted;
    public event Action<bool>? GestureEnded;
    public event Action? DeleteRequested;
    public event Action<EffectRegion,int>? MoveLayerRequested;
    public event Action<EffectRegion>? ToggleLayerRequested;
    public event Action<string>? Message;
    public ImageJob? Job => _job;
    private static readonly Brush[] ClipBrushes=[Brush("#367E71"),Brush("#465C96"),Brush("#987537"),Brush("#794D90"),Brush("#9C5162"),Brush("#527E45")];
    private static Brush Brush(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
    public TimelineControl()
    {
        Focusable=true;ClipToBounds=true;ToolTip="눈: 레이어 숨김/표시 · ↑↓: 레이어 순서 · 행 이름 드래그: 레이어 이동 · Ctrl+클릭: 클립 다중 선택";
        MouseLeftButtonDown+=(_,e)=>{Focus();BeginPointer(e.GetPosition(this),Keyboard.Modifiers.HasFlag(ModifierKeys.Control));CaptureMouse();e.Handled=true;};
        MouseMove+=(_,e)=>MovePointer(e.GetPosition(this),Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        MouseLeftButtonUp+=(_,e)=>{EndPointer();ReleaseMouseCapture();e.Handled=true;};
        MouseRightButtonDown+=(_,e)=>
        {
            CancelGesture();var point=e.GetPosition(this);var row=(int)((point.Y-RulerHeight)/RowHeight);
            if(point.Y>=RulerHeight&&_job is not null&&row>=0&&row<Rows.Count)
            {var info=Rows[row];var region=info.Region;if(info.Mask is { } mask){MaskSelected?.Invoke(region,mask);return;}var clip=region.Clips.OrderBy(c=>c.Start).Reverse().FirstOrDefault(c=>ClipRect(region,c).Contains(point));SelectedRegion=region;if(clip is null||!SelectedClipIds.Contains(clip.Id))SelectedClip=clip;else _selectedClip=clip;PublishSelection();}
        };
        LostMouseCapture+=(_,_)=>{if(_sectionDrag!=0||_dragClip is not null||_dragMask is not null||_scrub||_rowDrag is not null)CancelGesture();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){CancelGesture();ReleaseMouseCapture();e.Handled=true;}if(e.Key==Key.Delete){DeleteRequested?.Invoke();e.Handled=true;}};
        MouseWheel+=(_,e)=>{if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){Zoom(e.Delta>0?1.25:.8);e.Handled=true;}};
    }
    public void SetJob(ImageJob? job,VideoOptions video){_job=job;_video=video;SelectedRegion=null;SelectedClip=null;SelectedMask=null;Refresh();}
    public void SetVideo(VideoOptions video){_video=video;Refresh();}
    public void Zoom(double factor){PixelsPerSecond=Math.Clamp(PixelsPerSecond*factor,25,360);Refresh();}
    public void Refresh()
    {
        var allEnd=_job?.Regions.SelectMany(r=>r.Clips).Select(c=>c.End).DefaultIfEmpty(0).Max()??0;
        var end=_job is null?1:TimelineRules.Resolve(_job,_video).ActualDuration;
        Width=Math.Max(680,LabelWidth+(Math.Max(6,Math.Ceiling(Math.Max(allEnd,end)+1)))*PixelsPerSecond+30);
        Height=RulerHeight+Math.Max(1,Rows.Count)*RowHeight+8;
        InvalidateVisual();
    }
    private void Text(DrawingContext dc,string text,Point at,Brush color,double size=11,double maxWidth=0)
    {
        var t=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if(maxWidth>0){t.MaxTextWidth=maxWidth;t.MaxTextHeight=30;t.Trimming=TextTrimming.CharacterEllipsis;}
        dc.DrawText(t,at);
    }
    public Rect ClipRect(EffectRegion region,EffectClip clip)
    {
        var index=Rows.FindIndex(r=>r.Region.Id==region.Id&&r.Mask is null);
        return new(LabelWidth+clip.Start*PixelsPerSecond,RulerHeight+index*RowHeight+10,Math.Max(4,clip.Duration*PixelsPerSecond),28);
    }
    public Rect MaskRect(MaskTrack mask)
    {var index=Rows.FindIndex(r=>r.Mask?.Id==mask.Id);return new(LabelWidth+mask.Start*PixelsPerSecond,RulerHeight+index*RowHeight+10,Math.Max(4,mask.Duration*PixelsPerSecond),28);}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brush("#101B27"),null,new Rect(0,0,Width,Height));
        var pen=new Pen(Brush("#2D3E50"),1);var tick=PixelsPerSecond<45?2:1;
        for(var second=0;LabelWidth+second*PixelsPerSecond<Width;second+=tick)
        {
            var x=LabelWidth+second*PixelsPerSecond;dc.DrawLine(pen,new(x,RulerHeight-8),new(x,Height));
            Text(dc,$"{second}s",new(x+3,SectionHeight+4),Brush("#A8BDCC"),10);
        }
        Text(dc,"영역 / 효과 클립",new(10,SectionHeight+6),Brush("#BCD2DF"));DrawSections(dc);
        if(_job is null||_job.Regions.Count==0)Text(dc,"마우스로 영역을 지정한 뒤 효과를 추가하세요.",new(LabelWidth+12,45),Brush("#7891A5"));
        var rows=Rows;if(_job is not null)for(var i=0;i<rows.Count;i++)
        {
            var info=rows[i];var region=info.Region;var y=RulerHeight+i*RowHeight;
            if(info.Mask is { } mask){DrawMaskRow(dc,info,mask,i,y);continue;}
            if(SelectedRegion?.Id==region.Id)dc.DrawRectangle(Brush("#18322F"),null,new Rect(0,y,Width,RowHeight));
            dc.DrawLine(pen,new(0,y+RowHeight),new(Width,y+RowHeight));
            foreach(var clip in region.Clips.OrderBy(c=>c.Start))
            {
                var r=ClipRect(region,clip);var selected=SelectedClipIds.Contains(clip.Id);
                var exitOff=_job?.Playback.ExitEnabled==false&&clip.Section==ClipSection.Exit;
                dc.DrawRoundedRectangle(clip.Enabled&&region.Enabled&&!exitOff?ClipBrushes[(int)clip.Options.Kind%ClipBrushes.Length]:Brush("#3A4550"),new Pen(selected?Brush("#75FFCD"):Brush("#64818C"),selected?2.5:1),r,4,4);
                dc.PushClip(new RectangleGeometry(new Rect(r.Left+5,r.Top,r.Width-8>0?r.Width-8:1,r.Height)));
                Text(dc,(_job?.Playback.ByEffects==true?"["+SectionRules.Name(SectionRules.Effective(clip))+(exitOff?" 꺼짐":"")+"] ":"")+clip.DisplayName+(clip.Loop?" ↻":""),new(r.Left+7,r.Top+5),Brushes.White,11);
                dc.Pop();
                if(selected&&SelectedClipIds.Count==1)
                {
                    dc.DrawRectangle(Brushes.White,null,new Rect(r.Left+2,r.Top+7,3,14));
                    dc.DrawRectangle(Brushes.White,null,new Rect(r.Right-5,r.Top+7,3,14));
                }
            }
            dc.DrawRectangle(Brush("#192936"),null,new Rect(0,y,LabelWidth,RowHeight));
            var eyePen=new Pen(region.Enabled?Brush("#5DE2B1"):Brush("#708391"),1.5);dc.DrawEllipse(null,eyePen,new(14,y+24),7,4);
            if(region.Enabled)dc.DrawEllipse(eyePen.Brush,null,new(14,y+24),2,2);else dc.DrawLine(eyePen,new(7,y+18),new(21,y+30));
            Text(dc,$"{(region.Role==RegionRole.Group?(region.Collapsed?"▸ ":"▾ "):info.Depth>0?"↳ ":"")}{region.Name}",new(HeaderIndent(info),y+15),region.Enabled?Brush("#C3D5E1"):Brush("#708391"),11,LabelWidth-HeaderIndent(info)-42);DrawTreeGuide(dc,info,y);Text(dc,"↑",new(LabelWidth-34,y+14),Brush("#ABC3D5"));Text(dc,"↓",new(LabelWidth-18,y+14),Brush("#ABC3D5"));
        }
        if(_job is not null)
        {
            var outputEnd=LabelWidth+TimelineRules.Resolve(_job,_video).ActualDuration*PixelsPerSecond;
            dc.DrawLine(new Pen(Brush("#D4AA60"),1.5){DashStyle=DashStyles.Dash},new(outputEnd,RulerHeight),new(outputEnd,Height));
        }
        var cursor=LabelWidth+CursorTime*PixelsPerSecond;
        dc.DrawLine(new Pen(Brush("#FF7F95"),2),new(cursor,0),new(cursor,Height));
        dc.DrawRectangle(Brush("#FF7F95"),null,new Rect(cursor-5,0,10,7));
        if(SnapGuide is { } guide)
        {var x=LabelWidth+guide*PixelsPerSecond;dc.DrawLine(new Pen(Brush("#5DE2B1"),2),new(x,0),new(x,Height));}
        if(_rowDrag is not null)dc.DrawLine(new Pen(Brush("#5DE2B1"),3),new(0,RulerHeight+_dropRow*RowHeight),new(Width,RulerHeight+_dropRow*RowHeight));
    }
    public static double HeaderIndent(LayerRow row)=>29+row.Depth*20;
    private static string MaskOperationName(MaskOperation op)=>op switch{MaskOperation.Subtract=>"가리기",MaskOperation.Intersect=>"겹친 곳",_=>"보일 곳"};
    private void DrawTreeGuide(DrawingContext dc,LayerRow row,double y){if(row.Depth==0)return;var x=HeaderIndent(row)-10;var p=new Pen(Brush(row.Mask is null?"#4C8273":"#77709F"),1.5);dc.DrawLine(p,new(x,y),new(x,y+24));dc.DrawLine(p,new(x,y+24),new(x+7,y+24));dc.DrawRectangle(Brush(row.Mask is null?"#235044":"#4A456C"),null,new Rect(LabelWidth-3,y,3,RowHeight));}
    private void DrawMaskRow(DrawingContext dc,LayerRow row,MaskTrack mask,int index,double y)
    {
        var selected=SelectedMask?.Id==mask.Id;dc.DrawRectangle(Brush(selected?"#243752":"#15202D"),null,new Rect(0,y,Width,RowHeight));
        var r=MaskRect(mask);dc.DrawRoundedRectangle(Brush(mask.Enabled?"#555182":"#37414E"),new Pen(selected?Brushes.White:Brush("#8F8CB9"),selected?2:1),r,4,4);
        if(selected){dc.DrawRectangle(Brushes.White,null,new Rect(r.Left+1,r.Top+5,3,18));dc.DrawRectangle(Brushes.White,null,new Rect(r.Right-4,r.Top+5,3,18));}
        Text(dc,mask.Name+(mask.Invert?" · 반전":"")+" · "+MaskOperationName(mask.Operation),new(r.Left+7,r.Top+5),Brushes.White,10,Math.Max(1,r.Width-10));
        dc.DrawRectangle(Brush("#1B2535"),null,new Rect(0,y,LabelWidth,RowHeight));Text(dc,mask.Enabled?"◩":"—",new(7,y+14),Brush("#ABA6F0"));
        DrawTreeGuide(dc,row,y);Text(dc,"◩ "+mask.Name,new(HeaderIndent(row),y+14),Brush("#ABA6F0"),11,LabelWidth-HeaderIndent(row)-42);Text(dc,"↑",new(LabelWidth-34,y+14),Brush("#ABA6F0"));Text(dc,"↓",new(LabelWidth-18,y+14),Brush("#ABA6F0"));
        if(mask.Motion is { } motion)foreach(var frame in motion.Tracks.SelectMany(t=>t.Keys).Select(k=>k.Frame).Distinct()){var x=LabelWidth+(mask.Start+mask.Duration*frame/motion.LengthFrames)*PixelsPerSecond;dc.DrawEllipse(Brush("#C7BFFF"),null,new(x,y+RowHeight-4),2,2);}
    }
    private double TimeAt(double x)=>Math.Max(0,TimelineRules.Snap((x-LabelWidth)/PixelsPerSecond,_video.Fps));
    private void DrawSections(DrawingContext dc)
    {
        Text(dc,_job?.Playback.ExitEnabled==false?"등장 · 반복":"등장 · 반복 · 퇴장",new(10,3),Brush("#ABC3D5"),10);
        if(_job?.Playback.Enabled!=true){Text(dc,"구간 설정에서 사용 · 경계는 드래그",new(LabelWidth+7,3),Brush("#7891A5"),10);return;}
        var plan=PlaybackPlan.Create(_job,_video);var a=LabelWidth+(double)plan.IntroFrames/plan.Fps*PixelsPerSecond;var b=LabelWidth+(double)plan.LoopEndFrame/plan.Fps*PixelsPerSecond;var end=LabelWidth+plan.Duration*PixelsPerSecond;
        void Part(double left,double right,string text,string color){if(right<=left)return;dc.DrawRectangle(Brush(color),null,new Rect(left,0,right-left,SectionHeight-1));if(right-left>30)Text(dc,text,new(left+5,3),Brushes.White,10,Math.Max(1,right-left-8));}
        Part(LabelWidth,a,"등장 1회","#386D80");Part(a,b,"↻ 반복","#286D58");Part(b,end,"퇴장","#805368");
        foreach(var x in new[]{a,b,end}){dc.DrawRectangle(Brush("#E7EEE4"),null,new Rect(x-3,0,6,SectionHeight-1));dc.DrawLine(new Pen(Brush("#9AB7C7"),1){DashStyle=DashStyles.Dot},new(x,SectionHeight),new(x,Height));}
    }
    public void BeginPointer(Point point,bool control=false)
    {
        _startX=point.X;_resize=0;
        if(point.Y<SectionHeight&&point.X>=LabelWidth-6&&_job?.Playback.Enabled==true&&!_job.Playback.ByEffects)
        {
            var plan=PlaybackPlan.Create(_job,_video);var edges=new[]{(double)plan.IntroFrames/plan.Fps,(double)plan.LoopEndFrame/plan.Fps,plan.Duration};
            var hit=edges.Select((t,i)=>(Distance:Math.Abs(point.X-(LabelWidth+t*PixelsPerSecond)),Index:i+1)).OrderBy(x=>x.Distance).First();
            if(hit.Distance<=7){GestureStarted?.Invoke();_sectionBefore=_job.Playback with{};_sectionDrag=hit.Index;InvalidateVisual();return;}
        }
        var index=(int)((point.Y-RulerHeight)/RowHeight);
        var region=point.Y>=RulerHeight&&_job is not null&&index>=0&&index<Rows.Count?Rows[index].Region:null;
        if(region is not null&&Rows[index].Mask is { } mask)
        {
            if(point.X<28){MaskToggleRequested?.Invoke(region,mask);return;}if(point.X>=LabelWidth-38&&point.X<LabelWidth-7){MaskMoveRequested?.Invoke(region,mask,point.X<LabelWidth-22?-1:1);return;}
            MaskSelected?.Invoke(region,mask);SelectedMask=mask;SelectedClipIds.Clear();
            var rect=MaskRect(mask);var hit=rect;hit.Inflate(6,4);if(point.X>=LabelWidth-6&&hit.Contains(point)){GestureStarted?.Invoke();_dragMask=mask;_maskStart=mask.Start;_maskDuration=mask.Duration;_resize=Math.Abs(point.X-rect.Left)<=6&&Math.Abs(point.X-rect.Left)<=Math.Abs(rect.Right-point.X)?-1:Math.Abs(rect.Right-point.X)<=6?1:0;}
            else if(point.X>=LabelWidth){_scrub=true;CursorTime=TimeAt(point.X);Seeked?.Invoke(CursorTime);}InvalidateVisual();return;
        }
        SelectedMask=null;
        if(region is not null&&point.X<LabelWidth)
        {
            if(point.X<28){ToggleLayerRequested?.Invoke(region);return;}if(region.Role==RegionRole.Group&&point.X<44){CollapseRequested?.Invoke(region);return;}
            if(point.X>=LabelWidth-38){MoveLayerRequested?.Invoke(region,point.X<LabelWidth-22?-1:1);return;}
            SelectedRegion=region;SelectedClip=null;PublishSelection();_rowDrag=region;_dropRow=index;InvalidateVisual();return;
        }
        var clip=point.X>=LabelWidth?region?.Clips.OrderBy(c=>c.Start).Reverse().FirstOrDefault(c=>ClipRect(region,c).Contains(point)):null;
        if(region is not null)
        {
            SelectedRegion=region;
            if(control&&clip is not null)
            {
                if(!SelectedClipIds.Add(clip.Id)){SelectedClipIds.Remove(clip.Id);_selectedClip=SelectedClips.LastOrDefault().Clip;PublishSelection();return;}
                _selectedClip=clip;
            }
            else if(clip is not null&&SelectedClipIds.Contains(clip.Id))_selectedClip=clip;
            else SelectedClip=clip;
            PublishSelection();
        }
        else if(!control){SelectedClip=null;PublishSelection();}
        if(clip is not null && region is not null)
        {
            GestureStarted?.Invoke();_before=_job!.Regions.SelectMany(r=>r.Clips).ToDictionary(c=>c.Id,c=>(c.Start,c.Duration));
            _dragClip=clip;_oldStart=clip.Start;_oldDuration=clip.Duration;var r=ClipRect(region,clip);
            _resize=SelectedClipIds.Count>1?0:point.X-r.Left<7?-1:r.Right-point.X<7?1:0;
        }
        else if(point.X>=LabelWidth){_scrub=true;CursorTime=TimeAt(point.X);Seeked?.Invoke(CursorTime);}
        InvalidateVisual();
    }
    public void MovePointer(Point point,bool bypassSnap=false)
    {
        if(_sectionDrag!=0&&_job is not null)
        {
            var t=TimeAt(point.X);SnapGuide=null;
            if(SnapEnabled&&!bypassSnap){var targets=_job.Regions.SelectMany(r=>r.Clips.SelectMany(c=>new[]{c.Start,c.End}).Concat(r.Masks.SelectMany(m=>new[]{m.Start,m.End}))).Append(CursorTime).Append(0);var near=targets.OrderBy(v=>Math.Abs(v-t)).First();if(Math.Abs(near-t)*PixelsPerSecond<=8){t=TimelineRules.Snap(near,_video.Fps);SnapGuide=t;}}
            var p=_job.Playback;var step=1d/_video.Fps;
            if(_sectionDrag==1)p.IntroEnd=Math.Clamp(t,0,Math.Max(0,p.LoopEnd-step));
            else if(_sectionDrag==2)p.LoopEnd=Math.Clamp(t,p.IntroEnd+step,3600);
            else p.ExitDuration=Math.Clamp(t-p.LoopEnd,0,30);
            Edited?.Invoke();Refresh();return;
        }
        if(_rowDrag is not null){_dropRow=Math.Clamp((int)((point.Y-RulerHeight)/RowHeight),0,Math.Max(0,Rows.Count-1));InvalidateVisual();return;}
        if(_scrub){CursorTime=TimeAt(point.X);Seeked?.Invoke(CursorTime);InvalidateVisual();return;}
        if(_dragMask is { } mask)
        {
            var maskDelta=(point.X-_startX)/PixelsPerSecond;var start=_maskStart;var duration=_maskDuration;
            if(_resize==0)start=Math.Max(0,start+maskDelta);else if(_resize>0)duration=Math.Max(1d/_video.Fps,duration+maskDelta);else{start=Math.Clamp(start+maskDelta,0,start+duration-1d/_video.Fps);duration=_maskStart+_maskDuration-start;}
            var tracks=_job!.Regions.Concat([new EffectRegion{Clips=_job.Regions.SelectMany(r=>r.Masks).Select(m=>new EffectClip{Id=m.Id,Start=m.Start,Duration=m.Duration}).ToList()}]);
            var fake=new EffectClip{Id=mask.Id,Start=_maskStart,Duration=_maskDuration};var snap=TimelineRules.Magnet(tracks,fake,start,duration,_resize,_video.Fps,PixelsPerSecond,CursorTime,SnapEnabled&&!bypassSnap);
            var candidate=mask with{Start=snap.Start,Duration=snap.Duration};try{candidate.Validate();mask.Start=candidate.Start;mask.Duration=candidate.Duration;SnapGuide=snap.Guide;Edited?.Invoke();Refresh();}catch(InvalidDataException ex){Message?.Invoke(ex.Message);}return;
        }
        if(_dragClip is null)return;
        var delta=(point.X-_startX)/PixelsPerSecond;
        try
        {
            var region=_job!.Regions.First(r=>r.Clips.Contains(_dragClip));
            RestoreTimings();
            if(SelectedClipIds.Count>1)
            {
                var selected=SelectedClips;var min=selected.Min(x=>_before[x.Clip.Id].Start);var max=selected.Max(x=>_before[x.Clip.Id].Start+_before[x.Clip.Id].Duration);
                delta=Math.Clamp(TimelineRules.Snap(delta,_video.Fps),-min,3600-max);
                var magnet=TimelineRules.Magnet(_job.Regions,_dragClip,_oldStart+delta,_oldDuration,0,_video.Fps,PixelsPerSecond,CursorTime,SnapEnabled&&!bypassSnap,SelectedClipIds);
                delta=Math.Clamp(TimelineRules.Snap(magnet.Start-_oldStart,_video.Fps),-min,3600-max);
                foreach(var item in selected)item.Clip.Start=_before[item.Clip.Id].Start+delta;
                SnapGuide=magnet.Guide;Edited?.Invoke();Refresh();return;
            }
            var start=_oldStart;var duration=_oldDuration;
            if(_resize==0)start=Math.Max(0,_oldStart+delta);
            else if(_resize>0)duration=Math.Max(1d/_video.Fps,_oldDuration+delta);
            else
            {
                start=Math.Clamp(_oldStart+delta,0,_oldStart+_oldDuration-1d/_video.Fps);
                duration=_oldStart+_oldDuration-start;
            }
            var excluded=(RippleEnabled||FollowMagnet)&&_resize>0?region.Clips.Where(c=>c.Id!=_dragClip.Id&&c.Start>=_oldStart+_oldDuration-1e-8).Select(c=>c.Id).ToHashSet():null;
            var next=TimelineRules.Magnet(_job.Regions,_dragClip,start,duration,_resize,_video.Fps,PixelsPerSecond,CursorTime,SnapEnabled&&!bypassSnap,excluded);
            TimelineRules.SetTimingWithFollowers(region,_dragClip,next.Start,next.Duration,_video.Fps,RippleEnabled&&_resize>0,FollowMagnet&&_resize>0);SnapGuide=next.Guide;
            Edited?.Invoke();Refresh();
        }
        catch(InvalidDataException ex){Message?.Invoke(ex.Message);}
    }
    private void RestoreTimings()
    {if(_job is not null)foreach(var clip in _job.Regions.SelectMany(r=>r.Clips))if(_before.TryGetValue(clip.Id,out var old)){clip.Start=old.Start;clip.Duration=old.Duration;}}
    public void EndPointer(){if(_sectionDrag!=0){_sectionDrag=0;_sectionBefore=null;SnapGuide=null;Edited?.Invoke();GestureEnded?.Invoke(true);Refresh();return;}if(_rowDrag is { } row){_rowDrag=null;var target=Rows[Math.Clamp(_dropRow,0,Rows.Count-1)].Region;var siblings=_job!.Regions.Where(r=>r.ParentId==row.ParentId).ToList();var at=siblings.IndexOf(target);if(at>=0)MoveLayerRequested?.Invoke(row,at-siblings.IndexOf(row));Refresh();return;}var edited=_dragClip is not null||_dragMask is not null;_dragMask=null;_dragClip=null;_scrub=false;SnapGuide=null;_before.Clear();if(edited){Edited?.Invoke();GestureEnded?.Invoke(true);}Refresh();}
    public void CancelGesture(){if(_sectionDrag!=0&&_job is not null&&_sectionBefore is not null){_job.Playback=_sectionBefore;_sectionDrag=0;_sectionBefore=null;SnapGuide=null;Edited?.Invoke();GestureEnded?.Invoke(false);Refresh();return;}_rowDrag=null;var edited=_dragClip is not null||_dragMask is not null;if(_dragMask is { } mask){mask.Start=_maskStart;mask.Duration=_maskDuration;_dragMask=null;Edited?.Invoke();}if(_dragClip is not null){RestoreTimings();Edited?.Invoke();}_dragClip=null;_scrub=false;SnapGuide=null;_before.Clear();if(edited)GestureEnded?.Invoke(false);Refresh();}
}
