using System.Windows;

namespace HttpTester.App;

public partial class WorkspaceNameDialog : Window
{
    private readonly Func<string, string?> _validate;
    public string WorkspaceNameValue => WorkspaceName.Text.Trim();
    public WorkspaceNameDialog(string title, string initialName, Func<string, string?> validate)
    {
        InitializeComponent(); Title = title; WorkspaceName.Text = initialName; _validate = validate;
        Loaded += (_, _) => { WorkspaceName.Focus(); WorkspaceName.SelectAll(); };
    }
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var error = _validate(WorkspaceNameValue);
        if (error is not null) { MessageBox.Show(this, error, "工作区名称", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        DialogResult = true;
    }
}
