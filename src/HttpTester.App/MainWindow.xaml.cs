using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HttpTester.Core;
using Microsoft.Win32;

namespace HttpTester.App;

public partial class MainWindow : Window
{
    private ProfileStore _store;
    private readonly WorkspaceStore _workspaceStore;
    private WorkspaceCatalog _catalog = WorkspaceCatalog.CreateDefault();
    private bool _catalogAvailable;
    private string WorkspaceName => _catalog.Workspaces.First(w => w.Id == _catalog.ActiveWorkspaceId).Name;
    private List<RequestProfile> _profiles = [];
    private RequestProfile _current = new();
    private CancellationTokenSource? _requestCancellation;
    private ResponseResult? _response;
    private string _cleanSnapshot = "";
    private bool _loading = true;
    private bool _storageAvailable = true;

    public MainWindow() : this(new ProfileStore(Path.Combine(AppDataDirectory.CurrentPath, "requests.json"))) { }

    public MainWindow(ProfileStore store)
    {
        _store = store;
        _workspaceStore = new WorkspaceStore(store.Path);
        InitializeComponent();
        MethodBox.ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE" };
        BodyKindBox.ItemsSource = new[] { "无", "JSON", "纯文本", "表单 URL 编码", "文件 / Multipart" };
        AuthKindBox.ItemsSource = new[] { "无", "Bearer Token", "Basic Auth" };
        try
        {
            _catalog = _workspaceStore.Load();
            _catalogAvailable = true;
            _store = _workspaceStore.GetProfileStore(_catalog.ActiveWorkspaceId);
            _profiles = _store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _storageAvailable = false;
            Loaded += (_, _) => MessageBox.Show(this, ex.Message + "\n请备份并修复或移走该文件后重新打开程序。当前仍可测试请求和导出。", "无法读取保存文件", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshWorkspaces();
        RefreshProfiles();
        LoadEditor(new RequestProfile());
    }

    private void RefreshWorkspaces()
    {
        var wasLoading = _loading;
        _loading = true;
        WorkspaceBox.ItemsSource = _catalog.Workspaces;
        WorkspaceBox.SelectedValue = _catalog.ActiveWorkspaceId;
        WorkspaceHint.Text = $"保存到：{WorkspaceName}";
        WorkspaceHint.ToolTip = WorkspaceHint.Text;
        WorkspaceBox.IsEnabled = NewWorkspaceButton.IsEnabled = RenameWorkspaceButton.IsEnabled = _catalogAvailable && _requestCancellation is null;
        _loading = wasLoading;
    }

    private void ChangeWorkspace(WorkspaceCatalog catalog)
    {
        if (!_catalogAvailable) throw new IOException("工作区目录不可用，请先修复 workspaces.json 后重新打开程序。");
        // 先验证目标文件可读取，再持久化选择；失败时保留当前工作区及编辑内容。
        var store = _workspaceStore.GetProfileStore(catalog.ActiveWorkspaceId);
        var profiles = store.Load();
        _workspaceStore.Save(catalog);
        _catalog = catalog;
        _store = store;
        _profiles = profiles;
        _storageAvailable = true;
        _loading = true;
        SearchBox.Clear();
        RefreshWorkspaces();
        RefreshProfiles();
        LoadEditor(new RequestProfile());
        ClearResponse();
        FooterLabel.Text = $"当前工作区：{WorkspaceName} · {_profiles.Count} 个请求";
    }

    private void Workspace_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || WorkspaceBox.SelectedValue is not Guid id || id == _catalog.ActiveWorkspaceId) return;
        if (_requestCancellation != null || !ConfirmLeave()) { RefreshWorkspaces(); return; }
        try { ChangeWorkspace(WorkspaceStore.Select(_catalog, id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { RefreshWorkspaces(); ShowError(ex.Message); }
    }

    private void CreateWorkspace(string name) => ChangeWorkspace(WorkspaceStore.Add(_catalog, name));

    private void RenameCurrentWorkspace(string name)
    {
        if (!_catalogAvailable) throw new IOException("工作区目录不可用。");
        var catalog = WorkspaceStore.Rename(_catalog, _catalog.ActiveWorkspaceId, name);
        _workspaceStore.Save(catalog);
        _catalog = catalog;
        RefreshWorkspaces();
        UpdateTitle();
        FooterLabel.Text = $"工作区已重命名为：{WorkspaceName}";
    }

    private string? ValidateWorkspaceName(string name, bool renaming)
    {
        try
        {
            if (renaming) WorkspaceStore.Rename(_catalog, _catalog.ActiveWorkspaceId, name);
            else WorkspaceStore.Add(_catalog, name);
            return null;
        }
        catch (ArgumentException ex) { return ex.Message; }
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (!_catalogAvailable || _requestCancellation != null) return;
        var dialog = new WorkspaceNameDialog("新建工作区", "", name => ValidateWorkspaceName(name, false)) { Owner = this };
        if (dialog.ShowDialog() != true || !ConfirmLeave()) return;
        try { CreateWorkspace(dialog.WorkspaceNameValue); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(ex.Message); }
    }

    private void RenameWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (!_catalogAvailable || _requestCancellation != null) return;
        var dialog = new WorkspaceNameDialog("重命名工作区", WorkspaceName, name => ValidateWorkspaceName(name, true)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { RenameCurrentWorkspace(dialog.WorkspaceNameValue); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(ex.Message); }
    }

    private void LoadEditor(RequestProfile profile)
    {
        _loading = true;
        _current = ProfileStore.Clone(profile);
        NameBox.Text = _current.Name;
        MethodBox.SelectedItem = _current.Method;
        UrlBox.Text = _current.Url;
        QueryGrid.ItemsSource = _current.Query;
        HeaderGrid.ItemsSource = _current.Headers;
        FormGrid.ItemsSource = _current.Form;
        BodyKindBox.SelectedItem = _current.BodyKind;
        BodyEditor.Text = _current.Body;
        TimeoutBox.Text = _current.TimeoutSeconds.ToString();
        RedirectBox.IsChecked = _current.FollowRedirects;
        AuthKindBox.SelectedItem = _current.AuthKind;
        TokenBox.Text = _current.Token;
        UsernameBox.Text = _current.Username;
        PasswordBox.Text = _current.Password;
        _loading = false;
        _cleanSnapshot = Snapshot();
        UpdateTitle();
    }

    private RequestProfile ReadEditor(bool validate = true)
    {
        foreach (var grid in new[] { QueryGrid, HeaderGrid, FormGrid })
        { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
        var profile = ProfileStore.Clone(_current);
        profile.Name = NameBox.Text.Trim();
        profile.Method = MethodBox.SelectedItem as string ?? "GET";
        profile.Url = UrlBox.Text.Trim();
        profile.BodyKind = BodyKindBox.SelectedItem as string ?? "无";
        profile.Body = BodyEditor.Text;
        if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout is < 1 or > 600)
        {
            if (validate) throw new ArgumentException("超时时间需要是 1～600 之间的整数。");
            timeout = 30;
        }
        profile.TimeoutSeconds = timeout;
        profile.FollowRedirects = RedirectBox.IsChecked == true;
        profile.AuthKind = AuthKindBox.SelectedItem as string ?? "无";
        profile.Token = TokenBox.Text;
        profile.Username = UsernameBox.Text;
        profile.Password = PasswordBox.Text;
        return profile;
    }

    private string Snapshot() => JsonSerializer.Serialize(ReadEditor(false)) + "|" + TimeoutBox.Text;
    private bool IsDirty => !_loading && Snapshot() != _cleanSnapshot;
    private void UpdateTitle() => Title = $"{NameBox.Text} · {WorkspaceName} · Http探测器";
    private void Editor_Changed(object sender, RoutedEventArgs e) { if (!_loading) UpdateTitle(); }

    private void RefreshProfiles(Guid? selected = null)
    {
        var filter = SearchBox.Text.Trim();
        _loading = true;
        ProfileList.ItemsSource = _profiles.Where(p => string.IsNullOrEmpty(filter) ||
            $"{p.Name} {p.Url} {p.Method}".Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        ProfileList.SelectedItem = _profiles.FirstOrDefault(p => p.Id == selected);
        CountLabel.Text = _profiles.Count.ToString();
        _loading = false;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    { if (!_loading) RefreshProfiles(_current.Id); }

    private bool ConfirmLeave()
    {
        if (!IsDirty) return true;
        var answer = MessageBox.Show(this, "当前请求有未保存的修改，是否保存？", "未保存的请求", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && SaveCurrent());
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_requestCancellation != null) return;
        if (!ConfirmLeave()) return;
        RefreshProfiles();
        LoadEditor(new RequestProfile());
        ClearResponse();
        UrlBox.Focus();
    }

    private void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileList.SelectedItem is not RequestProfile selected || selected.Id == _current.Id) return;
        var id = selected.Id;
        if (!ConfirmLeave()) { RefreshProfiles(_current.Id); return; }
        var profile = _profiles.First(p => p.Id == id);
        RefreshProfiles(id);
        LoadEditor(profile);
        ClearResponse();
        FooterLabel.Text = $"已加载：{profile.Name}";
    }

    private bool SaveCurrent()
    {
        try
        {
            if (!_storageAvailable) throw new IOException("保存文件读取失败。请先修复保存文件，或使用“导出”保留当前请求。");
            var profile = ReadEditor();
            if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("请先为请求填写名称。");
            var all = _profiles.Select(ProfileStore.Clone).ToList();
            var index = all.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) all[index] = profile;
            else all.Add(profile);
            _store.Save(all);
            _profiles = all;
            _current = ProfileStore.Clone(profile);
            // 保持表格仍绑定到当前模型，避免后续编辑读到旧集合。
            QueryGrid.ItemsSource = _current.Query;
            HeaderGrid.ItemsSource = _current.Headers;
            FormGrid.ItemsSource = _current.Form;
            _cleanSnapshot = Snapshot();
            RefreshProfiles(profile.Id);
            FooterLabel.Text = $"已保存到“{WorkspaceName}”：{profile.Name}";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { ShowError(ex.Message); return false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_requestCancellation != null) return;
        if (ProfileList.SelectedItem is not RequestProfile profile) { FooterLabel.Text = "请先选择一个已保存请求。"; return; }
        if (MessageBox.Show(this, $"删除“{profile.Name}”？此操作不可撤销。", "删除请求", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (!_storageAvailable) throw new IOException("保存文件不可用。");
            var all = _profiles.Where(p => p.Id != profile.Id).ToList();
            _store.Save(all);
            _profiles = all;
            RefreshProfiles();
            LoadEditor(new RequestProfile());
            ClearResponse();
            FooterLabel.Text = "已删除请求。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "请求集合 (*.json)|*.json", Title = $"导入请求到“{WorkspaceName}”" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (!_storageAvailable) throw new IOException("保存文件不可用，请先修复后再导入。");
            var imported = ProfileStore.Parse(File.ReadAllText(dialog.FileName));
            var all = _profiles.Select(ProfileStore.Clone).ToList();
            foreach (var profile in imported) { profile.Id = Guid.NewGuid(); all.Add(profile); }
            _store.Save(all);
            _profiles = all;
            RefreshProfiles(_current.Id);
            FooterLabel.Text = $"已导入 {imported.Count} 个请求到“{WorkspaceName}”。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { ShowError(ex.Message); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = ReadEditor();
            if (string.IsNullOrWhiteSpace(current.Name)) current.Name = "未命名请求";
            var all = _profiles.Where(p => p.Id != current.Id).Select(ProfileStore.Clone).ToList();
            if (!string.IsNullOrWhiteSpace(current.Url) || _profiles.Any(p => p.Id == current.Id) || IsDirty) all.Add(current);
            if (all.Count == 0) { FooterLabel.Text = "暂无可导出的请求。"; return; }
            var safeName = string.Concat(WorkspaceName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var dialog = new SaveFileDialog { Filter = "请求集合 (*.json)|*.json", FileName = $"Http探测器-{safeName}.json", Title = $"导出“{WorkspaceName}”全部请求（包含当前编辑内容及认证信息）" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(all, ProfileStore.JsonOptions));
            FooterLabel.Text = $"已导出 {all.Count} 个请求。";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_requestCancellation != null) { _requestCancellation.Cancel(); return; }
        try
        {
            var profile = ReadEditor();
            RequestRunner.BuildUri(profile);
            _requestCancellation = new CancellationTokenSource();
            SetRequestEditing(false);
            SendButton.Content = "取消  ×";
            ClearResponse();
            EmptyState.Visibility = Visibility.Collapsed;
            MetricsLabel.Text = "请求中…";
            FooterLabel.Text = $"正在请求 {profile.Method} {profile.Url}";
            _response = await new RequestRunner().SendAsync(profile, _requestCancellation.Token);
            StatusBadge.Visibility = Visibility.Visible;
            StatusLabel.Text = $"{_response.StatusCode} {_response.Reason}";
            var good = _response.StatusCode < 400;
            StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(good ? "#E6F4EF" : "#FDECEB"));
            StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(good ? "#177250" : "#B74444"));
            MetricsLabel.Text = $"{_response.ElapsedMilliseconds:N0} ms   ·   {_response.Bytes.Length / 1024d:0.##} KB";
            ResponseBody.Text = _response.Body;
            ResponseHeaders.Text = _response.Headers;
            ResponseTabs.SelectedIndex = 0;
            FooterLabel.Text = _response.Truncated ? "响应超过 10 MB，仅保留前 10 MB；导出的响应也是截断内容。" : "请求完成。";
        }
        catch (OperationCanceledException) { MetricsLabel.Text = "已取消"; FooterLabel.Text = "请求已取消。"; }
        catch (Exception ex) when (ex is HttpRequestException or ArgumentException or IOException or UnauthorizedAccessException or TimeoutException or FormatException or InvalidOperationException)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            ResponseBody.Text = ex.Message;
            MetricsLabel.Text = "请求失败";
            FooterLabel.Text = ex is HttpRequestException ? "连接失败，请检查网络、地址及 HTTPS 证书。" : ex.Message;
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            SetRequestEditing(true);
            SendButton.Content = "发送  ↗";
        }
    }

    private void SetRequestEditing(bool enabled)
    {
        foreach (var element in new FrameworkElement[] { NameBox, MethodBox, UrlBox, RequestTabs,
            TimeoutBox, RedirectBox, ProfileList, SearchBox, NewButton }) element.IsEnabled = enabled;
        WorkspaceBox.IsEnabled = NewWorkspaceButton.IsEnabled = RenameWorkspaceButton.IsEnabled = enabled && _catalogAvailable;
    }

    private void ClearResponse()
    {
        _response = null;
        ResponseBody.Clear(); ResponseHeaders.Clear();
        StatusBadge.Visibility = Visibility.Collapsed;
        MetricsLabel.Text = "等待发送";
        EmptyState.Visibility = Visibility.Visible;
    }

    private void BodyKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BodyEditor is null || FormGrid is null || FormPanel is null) return;
        var kind = BodyKindBox.SelectedItem as string;
        BodyEditor.Visibility = kind is "JSON" or "纯文本" ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility = kind is "表单 URL 编码" or "文件 / Multipart" ? Visibility.Visible : Visibility.Collapsed;
        NoBodyLabel.Visibility = kind is null or "无" ? Visibility.Visible : Visibility.Collapsed;
        PickFileButton.Visibility = kind == "文件 / Multipart" ? Visibility.Visible : Visibility.Collapsed;
        FileColumn.Visibility = kind == "文件 / Multipart" ? Visibility.Visible : Visibility.Collapsed;
        Editor_Changed(sender, e);
    }

    private void AuthKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TokenBox is null) return;
        TokenBox.Visibility = AuthKindBox.SelectedItem as string == "Bearer Token" ? Visibility.Visible : Visibility.Collapsed;
        BasicPanel.Visibility = AuthKindBox.SelectedItem as string == "Basic Auth" ? Visibility.Visible : Visibility.Collapsed;
        Editor_Changed(sender, e);
    }

    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择要上传的文件" };
        if (dialog.ShowDialog(this) != true) return;
        FormGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (FormGrid.SelectedItem is Field field)
        { field.IsFile = true; field.Value = dialog.FileName; if (string.IsNullOrWhiteSpace(field.Key)) field.Key = "file"; FormGrid.Items.Refresh(); }
        else _current.Form.Add(new Field { Key = "file", Value = dialog.FileName, IsFile = true });
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        if (_requestCancellation is not null) return;
        var tag = (sender as FrameworkElement)?.Tag as string;
        var grid = tag == "Query" ? QueryGrid : tag == "Headers" ? HeaderGrid : FormGrid;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        var rows = tag == "Query" ? _current.Query : tag == "Headers" ? _current.Headers : _current.Form;
        var field = new Field(); rows.Add(field);
        grid.SelectedItem = field;
        grid.ScrollIntoView(field);
        grid.CurrentCell = new DataGridCellInfo(field, grid.Columns[1]);
        grid.Focus(); grid.BeginEdit();
    }

    private void Grid_EditCell(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridCell)
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        if (source is not DataGridCell { IsEditing: false, IsReadOnly: false, Column: DataGridTextColumn } cell) return;
        var grid = (DataGrid)sender;
        grid.SelectedItem = cell.DataContext;
        grid.CurrentCell = new DataGridCellInfo(cell);
        cell.Focus();
        if (grid.BeginEdit(e))
        {
            if (cell.Content is TextBox editor) editor.Focus();
            e.Handled = true;
        }
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_response is null || _response.IsBinary) return;
        try
        {
            ResponseBody.ShowJson(JsonSyntax.Format(_response.Body));
            FooterLabel.Text = "JSON 已格式化并高亮：蓝色键名 · 绿色字符串 · 橙色数字 · 紫色布尔值；保存响应仍导出原始字节。";
        }
        catch (JsonException) { FooterLabel.Text = "此响应不是有效的 JSON，已保留原始内容。"; }
    }

    private void Raw_Click(object sender, RoutedEventArgs e) { if (_response != null) ResponseBody.Text = _response.Body; }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResponseBody.Text)) return;
        try { Clipboard.SetText(ResponseBody.Text); FooterLabel.Text = "已复制响应文本。"; }
        catch (System.Runtime.InteropServices.COMException) { FooterLabel.Text = "剪贴板暂时被占用，请稍后重试。"; }
    }
    private void SaveResponse_Click(object sender, RoutedEventArgs e)
    {
        if (_response is null) return;
        var dialog = new SaveFileDialog { FileName = _response.IsBinary ? "response.bin" : "response.txt", Filter = "所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, _response.Bytes); FooterLabel.Text = "已保存响应。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
    }
    private void Wrap_Changed(object sender, RoutedEventArgs e)
    { if (ResponseBody != null) ResponseBody.TextWrapping = WrapBox.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap; }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S) { SaveCurrent(); e.Handled = true; }
        else if (e.Key == Key.N) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { Send_Click(sender, e); e.Handled = true; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    { if (!ConfirmLeave()) e.Cancel = true; else _requestCancellation?.Cancel(); }
    private void ShowError(string message) => MessageBox.Show(this, message, "Http探测器", MessageBoxButton.OK, MessageBoxImage.Warning);
}
