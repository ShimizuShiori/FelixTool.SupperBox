using System.Windows.Forms;
using System.Windows;

namespace FelixTool.SupperBox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private MainWindow? _launcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _launcher = new MainWindow();
            MainWindow = _launcher;
            _launcher.InitializeBackground();

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开 (&O)", null, (_, _) => _launcher.ShowLauncher());
            menu.Items.Add("退出 (&X)", null, (_, _) => Shutdown());

            _trayIconImage = TrayIconFactory.Create();
            _trayIcon = new NotifyIcon
            {
                Icon = _trayIconImage,
                Text = "SupperBox - Ctrl+F1",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.DoubleClick += (_, _) => _launcher.ShowLauncher();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"SupperBox 启动失败：{exception.GetBaseException().Message}",
                "SupperBox",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _launcher?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _trayIconImage?.Dispose();
        base.OnExit(e);
    }
}
