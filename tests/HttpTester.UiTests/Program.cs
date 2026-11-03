using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HttpTester.App;
using HttpTester.App.Controls;
using HttpTester.Core;

internal static class Program
{
    private static int _passed;
    [STAThread]
    public static int Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HttpDetectorUiTest-" + Guid.NewGuid());
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var timer = Stopwatch.StartNew();
            var app = new App(); app.InitializeComponent();
            var store = new ProfileStore(Path.Combine(directory, "requests.json"));
            var window = new MainWindow(store);
            window.Measure(new Size(1240, 820)); window.Arrange(new Rect(0, 0, 1240, 820)); window.UpdateLayout();
            Console.WriteLine($"首次窗口布局 {timer.ElapsedMilliseconds} ms");
            Check("初始方法 GET 与空白工作区", () => Assert((string)Find<ComboBox>(window, "MethodBox").SelectedItem == "GET" && Find<TextBox>(window, "UrlBox").Text == ""));
            Find<TextBox>(window, "NameBox").Text = "本地回显测试";
            Find<TextBox>(window, "UrlBox").Text = "http://localhost:8765/echo";
            var query = (ObservableCollection<Field>)Find<DataGrid>(window, "QueryGrid").ItemsSource;
            query.Add(new() { Key = "message", Value = "你好，Http探测器" });
            Check("首次命名保存与侧栏显示", () => { Assert((bool)Call(window, "SaveCurrent")!); Assert(store.Load().Single().Name == "本地回显测试"); Assert(Find<ListBox>(window, "ProfileList").Items.Count == 1); });
            var headers = (ObservableCollection<Field>)Find<DataGrid>(window, "HeaderGrid").ItemsSource;
            headers.Add(new() { Key = "Accept", Value = "application/json" });
            Check("保存后继续编辑表格不会丢失", () => { Assert((bool)Call(window, "SaveCurrent")!); Assert(store.Load().Single().Headers.Single().Value == "application/json"); });
            Find<ComboBox>(window, "BodyKindBox").SelectedItem = "JSON";
            Find<TextBox>(window, "BodyEditor").Text = "{\"message\":\"你好\"}";
            Find<ComboBox>(window, "MethodBox").SelectedItem = "POST";
            Check("JSON 编辑模式与配置保存", () => { Assert(Find<TextBox>(window, "BodyEditor").Visibility == Visibility.Visible); Assert((bool)Call(window, "SaveCurrent")!); Assert(store.Load().Single().Body.Contains("你好")); });
            Find<ComboBox>(window, "BodyKindBox").SelectedItem = "文件 / Multipart";
            Check("上传模式显示表格和文件选择", () => Assert(Find<DataGrid>(window, "FormGrid").Visibility == Visibility.Visible && Find<Button>(window, "PickFileButton").Visibility == Visibility.Visible));
            Find<ComboBox>(window, "AuthKindBox").SelectedItem = "Basic Auth";
            Check("Basic 认证显示输入框", () => Assert(Find<Grid>(window, "BasicPanel").Visibility == Visibility.Visible && Find<TextBox>(window, "TokenBox").Visibility == Visibility.Collapsed));
            Call(window, "LoadEditor", store.Load().Single());
            Check("重新加载恢复请求体与参数", () => Assert(Find<TextBox>(window, "BodyEditor").Text.Contains("你好") && ((ObservableCollection<Field>)Find<DataGrid>(window, "QueryGrid").ItemsSource).Single().Value == "你好，Http探测器"));
            Find<TextBox>(window, "SearchBox").Text = "does-not-exist";
            Check("侧栏搜索过滤", () => Assert(Find<ListBox>(window, "ProfileList").Items.Count == 0));
            Find<TextBox>(window, "SearchBox").Text = "";
            Call(window, "SetRequestEditing", false);
            Check("请求期间防止切换配置", () => Assert(!Find<ListBox>(window, "ProfileList").IsEnabled && !Find<TextBox>(window, "UrlBox").IsEnabled && !Find<ComboBox>(window, "WorkspaceBox").IsEnabled && !Find<Button>(window, "NewWorkspaceButton").IsEnabled && Find<Button>(window, "SendButton").IsEnabled));
            Call(window, "SetRequestEditing", true);
            Check("默认工作区加载旧版请求文件", () => Assert((Guid)Find<ComboBox>(window, "WorkspaceBox").SelectedValue == Guid.Empty && Find<TextBlock>(window, "WorkspaceHint").Text.Contains("默认工作区")));
            Call(window, "CreateWorkspace", "开发环境");
            var development = (Guid)Find<ComboBox>(window, "WorkspaceBox").SelectedValue;
            Check("新建工作区显示空列表且保留旧请求", () => Assert(Find<ListBox>(window, "ProfileList").Items.Count == 0 && store.Load().Count == 1 && Find<TextBox>(window, "UrlBox").Text == ""));
            Find<TextBox>(window, "NameBox").Text = "开发接口";
            Find<TextBox>(window, "UrlBox").Text = "http://localhost/development";
            Call(window, "SaveCurrent");
            var workspaceStore = new WorkspaceStore(store.Path);
            Check("保存只写入当前工作区", () => Assert(workspaceStore.GetProfileStore(development).Load().Single().Url.EndsWith("/development") && store.Load().Single().Name == "本地回显测试"));
            Call(window, "CreateWorkspace", "生产环境");
            var production = (Guid)Find<ComboBox>(window, "WorkspaceBox").SelectedValue;
            Find<TextBox>(window, "NameBox").Text = "开发接口";
            Find<TextBox>(window, "UrlBox").Text = "http://localhost/production";
            Call(window, "SaveCurrent");
            Find<ComboBox>(window, "WorkspaceBox").SelectedValue = development;
            Find<ListBox>(window, "ProfileList").SelectedIndex = 0;
            Check("界面切换后只加载对应工作区请求", () => Assert(Find<TextBox>(window, "UrlBox").Text.EndsWith("/development") && workspaceStore.GetProfileStore(production).Load().Single().Url.EndsWith("/production")));
            Call(window, "RenameCurrentWorkspace", "开发与联调");
            Check("重命名工作区不丢失内容", () => Assert(Find<TextBlock>(window, "WorkspaceHint").Text.Contains("开发与联调") && Find<TextBox>(window, "UrlBox").Text.EndsWith("/development")));
            var reopened = new MainWindow(store);
            Check("重新打开记住选中工作区", () => Assert((Guid)Find<ComboBox>(reopened, "WorkspaceBox").SelectedValue == development && Find<ListBox>(reopened, "ProfileList").Items.Count == 1));
            reopened.Close();
            Find<ComboBox>(window, "WorkspaceBox").SelectedValue = Guid.Empty;
            Find<ListBox>(window, "ProfileList").SelectedIndex = 0;
            Check("切回默认工作区恢复原请求", () => Assert(Find<TextBox>(window, "NameBox").Text == "本地回显测试" && Find<TextBox>(window, "BodyEditor").Text.Contains("你好")));
            var before = Find<TextBox>(window, "NameBox").Text;
            var broken = WorkspaceStore.Add(workspaceStore.Load(), "损坏文件测试");
            var brokenStore = workspaceStore.GetProfileStore(broken.ActiveWorkspaceId);
            Directory.CreateDirectory(Path.GetDirectoryName(brokenStore.Path)!);
            File.WriteAllText(brokenStore.Path, "INVALID");
            Check("目标工作区损坏时保留当前工作区及编辑内容", () =>
            {
                try { Call(window, "ChangeWorkspace", broken); throw new Exception("应拒绝损坏文件"); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
                Assert((Guid)Find<ComboBox>(window, "WorkspaceBox").SelectedValue == Guid.Empty && Find<TextBox>(window, "NameBox").Text == before && workspaceStore.Load().ActiveWorkspaceId == Guid.Empty);
            });
            var sample = new ResponseResult(200, "OK", "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8", "{\"message\":\"你好，Http探测器\",\"success\":true,\"count\":42,\"empty\":null,\"items\":[1,2,3]}", [], 24, false, false);
            typeof(MainWindow).GetField("_response", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, sample);
            Find<JsonResponseView>(window, "ResponseBody").Text = sample.Body;
            Call(window, "Format_Click", window, new RoutedEventArgs());
            Check("格式化 JSON 开启高亮且保留中文", () => Assert(Find<JsonResponseView>(window, "ResponseBody").IsSyntaxHighlighted && Find<JsonResponseView>(window, "ResponseBody").Text.Contains("你好，Http探测器") && Find<JsonResponseView>(window, "ResponseBody").Text.Contains('\n')));
            Call(window, "Raw_Click", window, new RoutedEventArgs());
            Check("原始文本还原未经格式化的响应", () => Assert(!Find<JsonResponseView>(window, "ResponseBody").IsSyntaxHighlighted && Find<JsonResponseView>(window, "ResponseBody").Text == sample.Body));
            Find<CheckBox>(window, "WrapBox").IsChecked = true;
            Check("高亮查看器支持自动换行", () => Assert(Find<JsonResponseView>(window, "ResponseBody").TextWrapping == TextWrapping.Wrap));
            Find<CheckBox>(window, "WrapBox").IsChecked = false;
            Check("请求名与参数编辑区域有明显边框", () => Assert(Find<TextBox>(window, "NameBox").BorderThickness.Left > 0 && Find<DataGrid>(window, "QueryGrid").Columns[1].CellStyle is not null));
            Console.WriteLine($"界面逻辑检查后工作集 {Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024:0.0} MB");
            // 直接渲染 WPF 布局，不驱动用户桌面，也不读取其他应用。
            if (args.Length > 0)
            {
                var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
                Call(window, "LoadEditor", new RequestProfile());
                Render(window, Path.Combine(output, "window-empty.png"));
                Call(window, "LoadEditor", store.Load().Single());
                var response = sample;
                typeof(MainWindow).GetField("_response", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, response);
                Find<FrameworkElement>(window, "EmptyState").Visibility = Visibility.Collapsed;
                Find<Border>(window, "StatusBadge").Visibility = Visibility.Visible;
                Find<TextBlock>(window, "StatusLabel").Text = "200 OK";
                Find<TextBlock>(window, "MetricsLabel").Text = "24 ms   ·   0.12 KB";
                Find<JsonResponseView>(window, "ResponseBody").Text = response.Body;
                Call(window, "Format_Click", window, new RoutedEventArgs());
                Find<TextBox>(window, "ResponseHeaders").Text = response.Headers;
                Find<TextBlock>(window, "FooterLabel").Text = "请求完成。";
                Find<TabControl>(window, "RequestTabs").SelectedIndex = 2;
                Render(window, Path.Combine(output, "window-example.png"));
                Find<TabControl>(window, "RequestTabs").SelectedIndex = 0;
                Render(window, Path.Combine(output, "window-parameters.png"));
                Console.WriteLine("布局渲染输出：" + output);
            }
            Console.WriteLine($"{_passed} UI checks passed; 含布局渲染的工作集 {Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024:0.0} MB");
            Call(window, "LoadEditor", store.Load().Single());
            window.Close();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static object? Call(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    private static void Assert(bool value) { if (!value) throw new Exception("断言失败"); }
    private static void Check(string name, Action action) { action(); _passed++; Console.WriteLine("PASS " + name); }
    private static void Render(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
        root.SetValue(TextElement.FontSizeProperty, window.FontSize);
        root.SetValue(TextElement.ForegroundProperty, window.Foreground);
        window.Content = null;
        using var source = new HwndSource(new HwndSourceParameters("HttpDetector isolated layout render")
        { Width = 1240, Height = 780, WindowStyle = unchecked((int)0x80000000) });
        source.RootVisual = root;
        if (root is Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));
        root.Measure(new Size(1240, 780)); root.Arrange(new Rect(0, 0, 1240, 780)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
        Check("工作区下拉框显示名称而非类型名", () => Assert(Descendants<TextBlock>(Find<ComboBox>((MainWindow)window, "WorkspaceBox")).Any(t => t.Text == "默认工作区")));
        var queryGrid = Find<DataGrid>((MainWindow)window, "QueryGrid");
        if (queryGrid.IsVisible)
        {
            Assert(queryGrid.Columns.All(c => c.ActualWidth > 0));
            Console.WriteLine("参数表列宽：" + string.Join(", ", queryGrid.Columns.Select(c => c.ActualWidth.ToString("0"))));
        }
        var bitmap = new RenderTargetBitmap(1240, 780, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
        if (path.EndsWith("window-parameters.png"))
        {
            var field = ((ObservableCollection<Field>)queryGrid.ItemsSource).First();
            var row = (DataGridRow)queryGrid.ItemContainerGenerator.ContainerFromItem(field);
            var cell = Descendants<DataGridCell>(row).First(c => c.Column == queryGrid.Columns[1]);
            var originalKey = field.Key;
            Check("参数单元格单击进入编辑并更新数据", () =>
            {
                var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = cell };
                Call((MainWindow)window, "Grid_EditCell", queryGrid, mouse);
                Assert(cell.IsEditing && cell.Content is TextBox);
                ((TextBox)cell.Content).Text = "edited-once";
                queryGrid.CommitEdit(DataGridEditingUnit.Cell, true); queryGrid.CommitEdit(DataGridEditingUnit.Row, true);
                Assert(field.Key == "edited-once");
                field.Key = originalKey;
            });
            var view = Find<JsonResponseView>((MainWindow)window, "ResponseBody");
            var editor = Descendants<TextBox>(view).Single();
            var original = view.Text;
            Check("高亮长字符串支持横向滚动与换行渲染", () =>
            {
                view.ShowJson(JsonSyntax.Format("{\"text\":\"" + new string('x', 10000) + "😀\"}"));
                root.UpdateLayout();
                editor.ScrollToHorizontalOffset(3000);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); root.UpdateLayout();
                bitmap.Render(root);
                Assert(view.IsSyntaxHighlighted && editor.HorizontalOffset > 0);
                view.TextWrapping = TextWrapping.Wrap;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); root.UpdateLayout();
                editor.ScrollToEnd();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); root.UpdateLayout();
                bitmap.Render(root);
                Assert(view.IsSyntaxHighlighted && editor.VerticalOffset > 0);
                view.TextWrapping = TextWrapping.NoWrap;
                view.ShowJson(original);
            });
        }
        source.RootVisual = null;
        window.Content = root;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
