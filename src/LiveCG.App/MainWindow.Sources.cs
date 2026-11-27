using System.IO;
using System.Windows;
using LiveCG.Core;
using Microsoft.Win32;

namespace LiveCG.App;

public partial class MainWindow
{
    private JobEffectBundle? _jobEffectsClipboard;

    private void UpdateJobEffectButtons()
    {
        if (CopyJobEffectsButton is not null) CopyJobEffectsButton.IsEnabled = !_busy;
        var canPaste = !_busy && _jobEffectsClipboard is not null;
        if (PasteJobEffectsButton is not null) PasteJobEffectsButton.IsEnabled = canPaste;
        if (PasteJobEffectsMenu is not null) PasteJobEffectsMenu.IsEnabled = canPaste;
    }

    private void CopyJobEffects(object sender, RoutedEventArgs e)
    {
        var preferred = _current is not null && JobEffectBundle.CanCopy(_current)
            ? _current
            : SelectedJobs().FirstOrDefault(JobEffectBundle.CanCopy) ?? _current ?? SelectedJobs().FirstOrDefault();
        CopyJobEffectsFrom(preferred);
    }

    public bool CopyJobEffectsFrom(ImageJob? source)
    {
        if (_busy) return false;
        if (source is null)
        {
            Notify("효과를 복사할 자료를 먼저 선택하세요.");
            return false;
        }
        if (ReferenceEquals(source, _current) && (!CommitClip(true) || !CommitRegion(true) || !CommitMask(true) || !CommitOutputTiming()))
            return false;
        var bundle = JobEffectBundle.FromJob(source);
        if (bundle is null)
        {
            Notify("선택한 자료에 복사할 효과가 없습니다. 효과가 적용된 자료를 선택해 복사하세요.");
            return false;
        }
        _jobEffectsClipboard = bundle;
        UpdateJobEffectButtons();
        Notify($"효과 묶음 복사 · {bundle.SourceFileName} (효과 {bundle.ClipCount}개) · 붙여넣을 자료를 선택한 뒤 ‘효과 붙여넣기’(Ctrl+V)를 누르세요.");
        return true;
    }

    private void PasteJobEffects(object sender, RoutedEventArgs e) => PasteJobEffectsTo(SelectedJobs());

    public int PasteJobEffectsTo(IReadOnlyList<ImageJob>? targets)
    {
        if (_busy) return 0;
        if (_jobEffectsClipboard is not { } bundle)
        {
            Notify("먼저 효과가 있는 자료를 선택하고 ‘효과 복사’(Ctrl+C)를 실행하세요.");
            return 0;
        }
        var selected = (targets is { Count: > 0 } ? targets : _current is null ? [] : new[] { _current })
            .Where(job => _jobs.Contains(job))
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            Notify("효과를 붙여넣을 대상 자료를 선택하세요.");
            return 0;
        }

        var candidates = selected.Count > 1 && selected.Any(job => !bundle.IsUnchangedSource(job))
            ? selected.Where(job => !bundle.IsUnchangedSource(job)).ToList()
            : selected;
        if (candidates.Count == 1 && bundle.IsUnchangedSource(candidates[0]))
        {
            Notify("현재 복사 원본 자료만 선택되어 있습니다. 붙여넣을 다른 자료를 선택하세요.");
            return 0;
        }

        if (!CommitClip(true) || !CommitRegion(true) || !CommitMask(true) || !CommitOutputTiming())
            return 0;

        StopPlayback();
        Preview.CancelGesture();
        TimelineEditor.CancelGesture();
        RecordEdit();

        var applied = new List<ImageJob>(candidates.Count);
        var createdRegionJobs = 0;
        try
        {
            foreach (var job in candidates)
            {
                var outcome = bundle.ApplyDetailed(job);
                if (outcome.Applied)
                {
                    applied.Add(job);
                    if (outcome.CreatedRegions) createdRegionJobs++;
                }
            }
        }
        catch (Exception ex)
        {
            Error(ex);
            return 0;
        }

        if (applied.Count == 0) return 0;

        _dirty = true;
        foreach (var job in applied)
        {
            job.Progress = 0;
            job.Status = !File.Exists(job.SourcePath) ? "원본 다시 연결 필요" : $"효과 붙여넣기 · 영역 {job.Regions.Count}개";
            job.Detail = string.IsNullOrEmpty(job.OutputPath) ? "" : "연결된 출력 · " + job.OutputPath;
        }

        if (_current is not null && applied.Contains(_current))
        {
            Preview.ModelChanged();
            var keepRegion = _region is not null && _current.Regions.Contains(_region)
                ? _region
                : LayerGraph.Rows(_current.Regions).FirstOrDefault()?.Region;
            _clip = null;
            var priorRestoring = _restoring;
            _restoring = true;
            try
            {
                RegionList.ItemsSource = _current.Regions;
                TimelineEditor.SetJob(_current, _video);
                SelectRegion(keepRegion);
                RefreshRegionList();
                RegionList.Items.Refresh();
                RefreshTimeline();
                if (_clip is not null)
                    Seek(Math.Min(ResolvedVideo.ActualDuration, _clip.Start + _clip.Duration * 0.45), true);
            }
            finally
            {
                _restoring = priorRestoring;
            }
        }

        var previousLoading = _loading;
        _loading = true;
        try
        {
            FileList.Items.Refresh();
            FileList.SelectedItems.Clear();
            foreach (var item in selected)
                FileList.SelectedItems.Add(item);
        }
        finally
        {
            _loading = previousLoading;
        }

        RecordEdit();
        UpdateJobEffectButtons();
        var regionNote = createdRegionJobs > 0
            ? (createdRegionJobs == applied.Count ? " · 미지정 영역 자동 생성" : $" · 기존 영역 유지 / 미지정 {createdRegionJobs}개 영역 자동 생성")
            : " · 이미지·위치·크기·레이어 유지";
        Notify($"{applied.Count}개 자료에 효과 묶음 붙여넣기 완료{regionNote}");
        return applied.Count;
    }

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

    private void ReplaceCurrentSource(object sender, RoutedEventArgs e) => ChangeSource(_current, true);

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

    private void ShowFinalOutput(object sender, RoutedEventArgs e)
    {
        OutputExpander.IsExpanded=true;
        OutputSettingsScroll.ScrollToTop();
        OutputExpander.BringIntoView();
    }

    private void RelinkFolder()
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = "원본 이미지가 있는 폴더" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var job in _jobs.Where(j => !File.Exists(j.SourcePath)))
            {
                var candidate = ProjectSources.Find(dialog.FolderName, job);
                if (candidate is not null) ApplySource(job, candidate, false);
            }
            Notify("원본 연결 완료 · 미연결 " + _jobs.Count(j => !File.Exists(j.SourcePath)) + "개");
        }
        catch (Exception ex) { Error(ex); }
    }
}
