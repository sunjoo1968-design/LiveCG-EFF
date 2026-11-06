using System.IO;
using System.Windows;
using LiveCG.Core;
using Microsoft.Win32;

namespace LiveCG.App;

public partial class MainWindow
{
    private void OpenBroadcastManager(object sender, RoutedEventArgs e)
    {
        new BroadcastManagerWindow { Owner = this }.Show();
    }

    private async void ExportBroadcastUpdates(object sender, RoutedEventArgs e)
    {
        if (_busy || !CommitClip(true) || !CommitRegion(true) || !CommitMask(true) || !CommitVideo(true)) return;
        var jobs = SelectedJobs();
        if (jobs.Count == 0) { Notify("갱신할 자료를 선택하세요."); return; }
        var dialog = new OpenFolderDialog { Title = "방송용 PC로 전달할 갱신 ZIP 저장 위치" };
        if (dialog.ShowDialog(this) != true) return;
        if (_projectPath is null && !SaveProjectInternal()) return;
        try
        {
            DocumentStore.Save(_projectPath!, ProjectSources.ForSave(CaptureProject(), _projectPath!));
            _dirty = false;
            _busy = true;
            foreach (var job in jobs)
            {
                var name = Path.GetFileNameWithoutExtension(job.SourcePath) + "-" + job.BroadcastId.ToString("N") + ".broadcast.zip";
                await BroadcastUpdates.ExportAsync(job.Snapshot(), _video with { }, Path.Combine(dialog.FolderName, name), CancellationToken.None);
            }
            _dirty = false;
            Notify("방송 갱신 ZIP을 저장했습니다. 방송용 PC의 방송 자료 관리에서 가져오세요.");
        }
        catch (Exception error) { Error(error); }
        finally { _busy = false; }
    }

    private void DuplicateBroadcastSource(object sender, RoutedEventArgs e)
    {
        if (_busy || _current is null || !CommitClip(true) || !CommitRegion(true) || !CommitMask(true)) return;
        var duplicate = _current.Snapshot();
        duplicate.BroadcastId = Guid.NewGuid();
        duplicate.OutputPath = ""; duplicate.OutputAliases.Clear();
        duplicate.Status = "새 방송 자료 복제";
        _jobs.Add(duplicate);
        FileList.SelectedItem = duplicate;
        MarkEdited(true);
        RefreshFiles();
        Notify("새 방송 자료 ID로 복제했습니다. 원본 이미지 교체 후 작업을 저장하세요.");
    }
}
