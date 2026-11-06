using System.IO;
using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;
using Microsoft.Win32;

namespace LiveCG.App;

public sealed class BroadcastManagerWindow : Window
{
    private readonly TextBox _root = new() { IsReadOnly = true };
    private readonly ListBox _items = new() { MinHeight = 170 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly CheckBox _immediate = new() { Content = "즉시 교체 · 기본은 반복 종료 후 교체", Margin = new Thickness(4) };
    private BroadcastRevision? _prepared;
    private readonly string _settings = Path.Combine(DocumentStore.DataDirectory, "broadcast-folder.txt");

    public BroadcastManagerWindow()
    {
        Title = "방송 자료 관리 · " + ProductInfo.Credit;
        Background = System.Windows.Media.Brushes.Black;
        Foreground = System.Windows.Media.Brushes.White;
        Width = 780; Height = 540; MinWidth = 650; MinHeight = 430;
        var panel = new DockPanel { Margin = new Thickness(14) };
        Content = panel;
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "방송용 PC에서 ZIP을 가져와 미리 확인한 뒤 반영하세요.", Margin = new Thickness(4) });
        top.Children.Add(_root);
        var actions = new WrapPanel(); top.Children.Add(actions);
        Add(actions, "보관 폴더 선택", ChooseRoot);
        Add(actions, "갱신 ZIP 가져오기", Import);
        Add(actions, "준비 자료 미리보기", Preview);
        Add(actions, "방송에 반영", Apply);
        Add(actions, "이전 버전 복구", Rollback);
        Add(actions, "OBS HTML 경로 복사", CopyEntry);
        top.Children.Add(_immediate);
        top.Children.Add(new TextBlock { Text = "OBS에는 ID별 entry.html을 한 번 등록하세요. 너비·높이는 아래 안내를 따릅니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        panel.Children.Add(_items);
        _items.SelectionChanged += (_, _) => ShowSelection();
        if (File.Exists(_settings)) _root.Text = File.ReadAllText(_settings);
        Refresh();
    }

    private void Add(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(9, 6, 9, 6) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) { _status.Text = error.Message; }
        };
        panel.Children.Add(button);
    }

    private void ChooseRoot()
    {
        var dialog = new OpenFolderDialog { Title = "방송용 자료 보관 폴더" };
        if (dialog.ShowDialog(this) != true) return;
        _root.Text = dialog.FolderName; _prepared = null;
        Directory.CreateDirectory(DocumentStore.DataDirectory);
        File.WriteAllText(_settings, _root.Text);
        Refresh();
    }

    private void Import()
    {
        RequireRoot();
        var dialog = new OpenFileDialog { Filter = "방송 갱신 자료|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        _prepared = null;
        _prepared = BroadcastUpdates.Prepare(_root.Text, dialog.FileName);
        _status.Text = $"준비 완료 · {_prepared.Name} · {_prepared.Width} × {_prepared.Height}\nID {_prepared.Id}\n미리보기 확인 후 방송에 반영하세요.";
    }

    private void Preview()
    {
        RequireRoot();
        if (_prepared is null) throw new InvalidOperationException("갱신 ZIP을 먼저 가져오세요.");
        var path = Path.Combine(BroadcastUpdates.Slot(_root.Text, _prepared.Id), "revisions", _prepared.Revision, "preview.html");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Apply()
    {
        RequireRoot();
        if (_prepared is null) throw new InvalidOperationException("갱신 ZIP을 먼저 가져오세요.");
        var id = _prepared.Id;
        BroadcastUpdates.Apply(_root.Text, _prepared, _immediate.IsChecked == true);
        Refresh();
        _status.Text = "반영 요청 완료 · " + (_immediate.IsChecked == true ? "자료 준비 후 즉시 교체" : "재생 중인 반복 종료 후 교체") +
                       "\nOBS HTML · " + BroadcastUpdates.Entry(_root.Text, id);
    }

    private void Rollback()
    {
        RequireRoot();
        if (_items.SelectedItem is not BindingItem item) throw new InvalidOperationException("복구할 방송 자료를 선택하세요.");
        BroadcastUpdates.Rollback(_root.Text, item.Binding.Active.Id);
        Refresh(); _status.Text = "이전 버전으로 복구 요청했습니다.";
    }

    private void CopyEntry()
    {
        RequireRoot();
        var id = (_items.SelectedItem as BindingItem)?.Binding.Active.Id ?? _prepared?.Id;
        if (id is null) throw new InvalidOperationException("방송 자료를 선택하세요.");
        Clipboard.SetText(BroadcastUpdates.Entry(_root.Text, id.Value));
        _status.Text = "OBS 로컬 파일 경로를 복사했습니다.";
    }

    private void Refresh()
    {
        _items.ItemsSource = BroadcastUpdates.List(_root.Text).Select(binding => new BindingItem(binding)).ToList();
    }

    private void ShowSelection()
    {
        if (_items.SelectedItem is BindingItem item)
            _status.Text = $"{item.Binding.Active.Name} · OBS 크기 {item.Binding.Active.Width} × {item.Binding.Active.Height}\n" +
                           BroadcastUpdates.Entry(_root.Text, item.Binding.Active.Id);
    }

    private void RequireRoot()
    {
        if (!Directory.Exists(_root.Text)) throw new InvalidOperationException("방송 자료 보관 폴더를 먼저 선택하세요.");
    }

    private sealed record BindingItem(BroadcastBinding Binding)
    {
        public override string ToString() => Binding.Active.Name + " · " + Binding.Active.Id;
    }
}
