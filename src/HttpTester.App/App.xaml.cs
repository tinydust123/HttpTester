using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using HttpTester.Core;

namespace HttpTester.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 小型工具无需 GPU 合成，避免为简单界面保留较大的图形缓存。
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            AppDataDirectory.Prepare(AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"无法准备程序目录下的 data 文件夹：{ex.Message}\n\n原数据已保留。请将程序放在可写目录中，关闭旧版程序后重新启动。",
                "Http探测器 · 数据目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
    }
}
