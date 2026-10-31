using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace LiveCG.Core;

public static class RuntimePaths
{
    public static string FindFile(string relative)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for(var depth=0; current is not null && depth<8; depth++,current=current.Parent)
        {
            var candidate=Path.Combine(current.FullName,relative);
            if(File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"필수 파일이 없습니다: {relative}. 배포 폴더 전체를 함께 사용하세요.");
    }
    public static string Ffmpeg => FindExecutable("ffmpeg.exe");
    public static string Ffprobe => FindExecutable("ffprobe.exe");
    private static string FindExecutable(string name)
    {
        try { return FindFile(Path.Combine("tools","ffmpeg",name)); } catch(FileNotFoundException) { }
        var current=new DirectoryInfo(AppContext.BaseDirectory);
        for(var i=0;current is not null && i<8;i++,current=current.Parent)
        {
            var root=Path.Combine(current.FullName,".tools","ffmpeg");
            if(Directory.Exists(root))
            { var found=Directory.EnumerateFiles(root,name,SearchOption.AllDirectories).FirstOrDefault(); if(found is not null)return found; }
        }
        throw new FileNotFoundException($"{name}를 찾을 수 없습니다. tools/bootstrap.ps1을 실행하거나 완전한 배포본을 사용하세요.");
    }
}

public sealed class WebmExporter(string executable)
{
    public static void ValidateOutputNames(IEnumerable<ImageJob> jobs)
    {
        var collision=jobs.GroupBy(x=>x.OutputName,StringComparer.OrdinalIgnoreCase).FirstOrDefault(x=>x.Count()>1);
        if(collision is not null)throw new InvalidDataException($"같은 출력 이름이 두 개 이상입니다: {collision.Key}\n해당 원본들을 다른 출력 폴더에 나누어 저장하세요.");
    }

    public async Task ExportAsync(ImageJob job, VideoOptions video, string target, bool overwrite, IProgress<double>? progress, CancellationToken cancellation)
    {
        var plan=PlaybackPlan.Create(job,video);plan.ValidateWebm();
        video = TimelineRules.Resolve(job, video) with {Duration=plan.WebmDuration};
        video.Validate(); foreach(var r in job.Regions)r.Validate();
        target=Path.GetFullPath(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if(File.Exists(target)&&!overwrite)throw new IOException("같은 이름의 출력 파일이 있습니다.");
        var temporary=Path.Combine(Path.GetDirectoryName(target)!, "."+Path.GetFileNameWithoutExtension(target)+"."+Guid.NewGuid().ToString("N")+".partial.webm");
        using var source=SourceImage.Read(job)??throw new InvalidDataException("이미지를 읽을 수 없습니다.");
        using var scene = new EffectScene(source);
        var renderRegions=SectionRules.RenderRegions(job);scene.Validate(renderRegions);
        using var process=new Process { StartInfo=new ProcessStartInfo(executable) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true} };
        string[] args=["-hide_banner","-loglevel","error","-y","-f","rawvideo","-pixel_format","bgra","-video_size",$"{source.Width}x{source.Height}",
            "-framerate",video.Fps.ToString(),"-i","pipe:0","-an","-c:v","libvpx-vp9","-pix_fmt","yuva420p","-b:v","0","-crf",video.Crf.ToString(),
            "-deadline","good","-cpu-used","4","-row-mt","1","-threads","4","-metadata:s:v:0","alpha_mode=1",temporary];
        foreach(var arg in args)process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start();
            var stderr=process.StandardError.ReadToEndAsync();
            using var cancelRegistration=cancellation.Register(()=>{try { if(!process.HasExited)process.Kill(true); }catch(InvalidOperationException){}catch(Win32Exception){} });
            try
            {
                using var straight=new SKBitmap(new SKImageInfo(source.Width,source.Height,SKColorType.Bgra8888,SKAlphaType.Unpremul));
                var bytes=new byte[checked(source.Width*source.Height*4)];
                for(var frame=0;frame<video.FrameCount;frame++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var sourceTime=(double)plan.WebmSourceFrame(frame)/video.Fps;
                    using var rendered=plan.RenderFrame(scene,renderRegions,video,sourceTime,plan.WebmRest(frame));
                    using var image=SKImage.FromBitmap(rendered);
                    if(!image.ReadPixels(straight.Info,straight.GetPixels(),straight.RowBytes,0,0))throw new IOException("영상 프레임 알파 변환 실패");
                    Marshal.Copy(straight.GetPixels(),bytes,0,bytes.Length);
                    await process.StandardInput.BaseStream.WriteAsync(bytes,cancellation).ConfigureAwait(false);
                    progress?.Report((double)(frame+1)/video.FrameCount*.9);
                }
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
                var error=await stderr.ConfigureAwait(false);
                if(process.ExitCode!=0)throw new IOException("FFmpeg 변환 실패: "+error);
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary,target,overwrite);progress?.Report(1);
            }
            catch(Exception e) when(cancellation.IsCancellationRequested && e is not OperationCanceledException)
            { throw new OperationCanceledException("출력을 취소했습니다.",e,cancellation); }
            finally
            {
                try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(Win32Exception)when(process.HasExited){}
                await process.WaitForExitAsync().ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
            }
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
