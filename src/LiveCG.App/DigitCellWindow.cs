using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.WPF;
namespace LiveCG.App;
public sealed class DigitCellWindow:Window
{
    public List<double> Dividers { get; }=[];private readonly SKElement _canvas=new();private readonly PreviewViewport _view=new();private readonly SKImage _image;private int _drag=-1;
    public DigitCellWindow(SKBitmap image,IEnumerable<double> dividers,SKColor? sourceBackground=null)
    {
        Title="숫자 칸 편집 · "+ProductInfo.Credit;Width=960;Height=500;MinWidth=650;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=System.Windows.Media.Brushes.Black;Foreground=System.Windows.Media.Brushes.White;_image=SKImage.FromBitmap(image);Dividers.AddRange(dividers);
        void Auto(){Dividers.Clear();var cells=DigitCells.Find(image,DigitCells.DetectionBackground(image,sourceBackground??SKColors.Transparent));for(var i=1;i<cells.Count;i++)Dividers.Add((cells[i-1].MidX+cells[i].MidX)/2/image.Width);_canvas.InvalidateVisual();}
        if(Dividers.Count==0)Auto();var root=new DockPanel{Margin=new Thickness(10)};Content=root;var row=new WrapPanel();DockPanel.SetDock(row,Dock.Top);root.Children.Add(row);
        void Button(string text,Action action){var b=new Button{Content=text};b.Click+=(_,_)=>action();row.Children.Add(b);}
        Button("자동 칸 다시 찾기",Auto);Button("화면 맞춤",()=>{_view.Fit(false);_canvas.InvalidateVisual();});Button("적용",()=>{DialogResult=true;});Button("취소",Close);row.Children.Add(new TextBlock{Text="경계선 드래그 · 더블클릭 추가 · 우클릭 삭제 · 휠 확대",Margin=new Thickness(6)});root.Children.Add(_canvas);
        _canvas.SizeChanged+=(_,_)=>{_view.Update(_canvas.ActualWidth,_canvas.ActualHeight,image.Width,image.Height,SKRect.Create(image.Width,image.Height));_canvas.InvalidateVisual();};
        _canvas.PaintSurface+=(_,e)=>{var c=e.Surface.Canvas;c.Clear(new(23,35,49));c.Scale(e.Info.Width/(float)Math.Max(1,_canvas.ActualWidth),e.Info.Height/(float)Math.Max(1,_canvas.ActualHeight));var r=_view.ImageRect;c.Translate(r.Left,r.Top);c.Scale((float)_view.Scale);c.DrawImage(_image,0,0);using var p=new SKPaint{Color=SKColors.Cyan,StrokeWidth=(float)(2/_view.Scale),IsAntialias=true};using var font=new SKFont(SKTypeface.Default,(float)(15/_view.Scale));var bounds=Dividers.Prepend(0).Append(1).Order().ToArray();for(var i=0;i<bounds.Length-1;i++){p.Color=SKColors.Yellow;c.DrawText((i+1).ToString(),(float)((bounds[i]+bounds[i+1])/2*image.Width),Math.Max(18,(float)(20/_view.Scale)),font,p);}p.Color=SKColors.Cyan;foreach(var x in Dividers)c.DrawLine((float)x*image.Width,0,(float)x*image.Width,image.Height,p);};
        _canvas.MouseDown+=(_,e)=>{var at=e.GetPosition(_canvas);var x=_view.ToImage(new((float)at.X,(float)at.Y)).X/image.Width;if(x<=0||x>=1)return;var near=Dividers.Select((v,i)=>(v,i)).OrderBy(v=>Math.Abs(v.v-x)).FirstOrDefault((v:100d,i:-1));if(e.ChangedButton==MouseButton.Right){if(Math.Abs(near.v-x)*image.Width*_view.Scale<12)Dividers.RemoveAt(near.i);}else if(e.ClickCount==2){if(Dividers.Count<30)Dividers.Add(x);}else if(Math.Abs(near.v-x)*image.Width*_view.Scale<12){_drag=near.i;_canvas.CaptureMouse();}_canvas.InvalidateVisual();};
        _canvas.MouseMove+=(_,e)=>{if(_drag<0)return;var at=e.GetPosition(_canvas);Dividers[_drag]=Math.Clamp(_view.ToImage(new((float)at.X,(float)at.Y)).X/image.Width,.001,.999);_canvas.InvalidateVisual();};_canvas.MouseUp+=(_,_)=>{_drag=-1;_canvas.ReleaseMouseCapture();};_canvas.MouseWheel+=(_,e)=>{var at=e.GetPosition(_canvas);_view.ZoomAt(e.Delta>0?1.25:.8,new((float)at.X,(float)at.Y));_canvas.InvalidateVisual();};Closed+=(_,_)=>_image.Dispose();
    }
}
