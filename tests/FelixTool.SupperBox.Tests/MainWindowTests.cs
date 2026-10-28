using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace FelixTool.SupperBox.Tests;

public sealed class MainWindowTests
{
    [Theory]
    [InlineData("", "websearch ")]
    [InlineData("websearch ", "websearch google ")]
    public void Suggestions_ArrowKeysSelectWithoutChangingInput_AndTabCompletes(
        string text, string completedText)
    {
        RunOnStaThread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using var configuration = new SystemConfiguration(directory);
                using var window = new MainWindow(configuration);
                using var source = new HwndSource(new HwndSourceParameters("SupperBox keyboard test"));
                var input = (TextBox)window.FindName("CommandInput");
                var suggestions = (ListBox)window.FindName("SuggestionList");
                input.Text = text;
                Assert.True(suggestions.Items.Count > 1);
                Assert.Equal(0, suggestions.SelectedIndex);

                Assert.True(PressKey(input, source, Key.Up).Handled);
                Assert.Equal(0, suggestions.SelectedIndex);
                Assert.True(PressKey(input, source, Key.Down).Handled);
                Assert.Equal(1, suggestions.SelectedIndex);
                Assert.Equal(text, input.Text);

                Assert.True(PressKey(input, source, Key.Up).Handled);
                Assert.Equal(0, suggestions.SelectedIndex);
                for (var index = 0; index < suggestions.Items.Count + 1; index++)
                {
                    PressKey(input, source, Key.Down);
                }

                Assert.Equal(suggestions.Items.Count - 1, suggestions.SelectedIndex);
                Assert.True(PressKey(input, source, Key.Tab).Handled);
                Assert.Equal(completedText, input.Text);
                Assert.Equal(input.Text.Length, input.CaretIndex);

                input.Text = "nonexistent-command ";
                Assert.Empty(suggestions.Items);
                Assert.False(PressKey(input, source, Key.Down).Handled);
                Assert.False(PressKey(input, source, Key.Up).Handled);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static KeyEventArgs PressKey(TextBox input, PresentationSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        input.RaiseEvent(args);
        return args;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
