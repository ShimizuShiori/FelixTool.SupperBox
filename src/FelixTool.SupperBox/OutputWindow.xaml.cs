using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FelixTool.SupperBox;

public partial class OutputWindow : Window
{
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromMinutes(5);
    private readonly DispatcherTimer _autoCloseTimer;

    public OutputWindow(string content)
    {
        InitializeComponent();
        OutputText.Text = content;
        _autoCloseTimer = new DispatcherTimer { Interval = AutoCloseDelay };
        _autoCloseTimer.Tick += (_, _) => Close();
        Loaded += Window_Loaded;
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CopyContent();
        _autoCloseTimer.Start();
        OutputText.Focus();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        CopyContent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CopyContent()
    {
        try
        {
            System.Windows.Clipboard.SetText(OutputText.Text);
            StatusText.Text = "已复制到剪贴板。";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(174, 183, 198));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"复制失败：{exception.GetBaseException().Message}";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 128, 128));
        }
    }
}
