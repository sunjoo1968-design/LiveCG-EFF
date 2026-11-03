using System.IO;
using System.Windows;
using LiveCG.Core;
using Microsoft.Win32;

namespace LiveCG.App;

public partial class MainWindow
{
    private ProjectDocument CaptureProject() => new()
    {
        Jobs = _jobs.Select(job => job.Snapshot()).ToList(),
        Video = _video with { },
        OutputDirectory = OutputFolderBox.Text.Trim()
    };

    private void SavePortableProject(object sender, RoutedEventArgs e)
    {
        if (_busy || !CommitClip(true) || !CommitRegion(true) || !CommitMask(true) || !CommitVideo(true)) return;
        var dialog = new OpenFolderDialog { Title = "이동용 작업을 만들 위치" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Notify("이동용 작업 저장 완료 · " + ProjectSources.SavePortable(CaptureProject(), dialog.FolderName));
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ChangeSource(ImageJob? job, bool replace)
    {
        if (_busy || job is null) return;
        var dialog = new OpenFileDialog { Title = replace ? "교체할 이미지" : "다시 연결할 원본", Filter = SourceImage.Filter };
        if (dialog.ShowDialog(this) != true) return;
        try { ApplySource(job, dialog.FileName, replace); }
        catch (Exception ex) { Error(ex); }
    }

    private void ApplySource(ImageJob job, string path, bool replace)
    {
        using var image = SourceImage.Read(path) ?? throw new InvalidDataException("이미지를 읽을 수 없습니다.");
        if ((image.Width != job.Width || image.Height != job.Height) &&
            MessageBox.Show(this, "이미지 크기가 다릅니다. 영역을 비율 좌표로 유지합니다. 연결 후 미리보기에서 위치를 확인하세요.", "이미지 크기 확인", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        if (!CommitClip(true) || !CommitRegion(true) || !CommitMask(true)) return;
        ProjectSources.Update(job, path, image.Width, image.Height, replace);
        job.Status = replace ? "이미지 교체" : "원본 연결";
        job.Progress = 0;
        FileList.Items.Refresh();
        if (ReferenceEquals(job, _current)) DisplayJob(job);
        MarkEdited(true);
    }

    private void ShowFinalOutput(object sender, RoutedEventArgs e) => OutputExpander.IsExpanded = true;

    private void RelinkFolder()
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = "원본 이미지가 있는 폴더" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var job in _jobs.Where(j => !File.Exists(j.SourcePath)))
            {
                var candidate = Path.Combine(dialog.FolderName, job.FileName);
                if (File.Exists(candidate)) ApplySource(job, candidate, false);
            }
            Notify("원본 연결 완료 · 미연결 " + _jobs.Count(j => !File.Exists(j.SourcePath)) + "개");
        }
        catch (Exception ex) { Error(ex); }
    }
}
