using System.IO;
using System.Windows;
using LiveCG.Core;
using Microsoft.Win32;

namespace LiveCG.App;

public partial class MainWindow
{
    private async void SaveAndRefreshBroadcast(object sender, RoutedEventArgs e) => await SaveAndRefreshAsync();

    public async Task SaveAndRefreshAsync()
    {
        if (_busy || !CommitClip(true) || !CommitRegion(true) || !CommitMask(true) || !CommitVideo(true)) return;
        if (_projectPath is null && !SaveProjectInternal()) return;
        var jobs = _jobs.ToList();
        if (jobs.Count == 0) { Notify("갱신할 자료가 없습니다."); return; }
        if (jobs.Any(job => string.IsNullOrEmpty(job.OutputPath)))
        {
            if (string.IsNullOrWhiteSpace(OutputFolderBox.Text)) ChooseOutput(this, new RoutedEventArgs());
            if (string.IsNullOrWhiteSpace(OutputFolderBox.Text)) return;
            foreach (var job in jobs.Where(job => string.IsNullOrEmpty(job.OutputPath)))
                job.OutputPath = Path.Combine(Path.GetFullPath(OutputFolderBox.Text), BrowserExporter.OutputName(job));
        }
        try
        {
            if (jobs.Any(job => !File.Exists(job.SourcePath))) throw new FileNotFoundException("누락된 원본을 다시 연결하세요.");
            if (jobs.Select(job => Path.GetFullPath(job.OutputPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != jobs.Count)
                throw new InvalidOperationException("출력 폴더가 겹칩니다. 자료 이름이나 연결 폴더를 확인하세요.");
            DocumentStore.Save(_projectPath!, ProjectSources.ForSave(CaptureProject(), _projectPath!));
            SetBusy(true);
            _workCts = new();
            for (var index = 0; index < jobs.Count; index++)
            {
                _workCts.Token.ThrowIfCancellationRequested();
                var job = jobs[index];
                var completed = index;
                var progress = new Progress<double>(value => { job.Progress = value * 100; ExportProgress.Value = 100 * (completed + value) / jobs.Count; });
                job.Status = "방송 갱신 중";
                await Task.Run(() => LinkedOutput.ExportAsync(job.Snapshot(), _video with { }, job.OutputPath, _workCts.Token, progress));
                job.Progress = 100; job.Status = "방송 갱신 완료";
                ExportProgress.Value = 100d * (index + 1) / jobs.Count;
            }
            _dirty = false;
            Notify("작업 저장·방송 갱신 완료 · 기존 index.html에서 자동 반영됩니다.");
        }
        catch (OperationCanceledException) { foreach (var job in jobs.Where(job => job.Status == "방송 갱신 중")) job.Status = "취소"; _dirty = true; Notify("방송 갱신을 취소했습니다. 완료된 자료는 유지됩니다."); }
        catch (Exception error) { foreach (var job in jobs.Where(job => job.Status == "방송 갱신 중")) { job.Status = "실패"; job.Detail = error.Message; } _dirty = true; Error(error); }
        finally { _workCts?.Dispose(); _workCts = null; SetBusy(false); }
    }

    private void ConnectObsScene(object sender, RoutedEventArgs e)
    {
        if (_busy || _projectPath is null) { Notify("작업을 먼저 열거나 저장하세요."); return; }
        var dialog = new OpenFileDialog { Title = "OBS에서 내보낸 장면 모음", Filter = "OBS 장면 모음|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var destination = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_연결.json");
            var changed = ObsScenePaths.Relink(dialog.FileName, destination, CaptureProject());
            Notify($"장면 경로 {changed}개 연결 · OBS에서 {destination} 파일을 불러오세요.");
        }
        catch (Exception error) { Error(error); }
    }
}
