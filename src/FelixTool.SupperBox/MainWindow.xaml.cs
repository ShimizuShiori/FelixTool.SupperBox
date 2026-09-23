using System.Windows.Input;
using System.IO;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public partial class MainWindow : System.Windows.Window, IDisposable
{
    private readonly CommandRegistry _registry;
    private readonly SystemConfiguration _configuration;
    private readonly IReadOnlyList<string> _loadErrors;
    private GlobalHotKey? _hotKey;
    private bool _allowClose;
    private bool _executing;

    public MainWindow()
    {
        InitializeComponent();
        _configuration = new SystemConfiguration();
        _configuration.Reloaded += Configuration_Reloaded;
        var pluginDirectory = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var itemSelection = new ItemSelectionService(this);
        var pluginContext = new PluginContext(itemSelection);
        var loadResult = PluginLoader.Load(pluginDirectory, pluginContext);
        var launcher = new ExternalLauncher();
        const string repositoryRoot = @"C:\git\GitHub\WiseTechGlobal";
        var commands = loadResult.Plugins.Concat<ICommandPlugin>(
        [
            new WebSearchCommand(launcher),
            new SystemConfigCommand(_configuration, launcher),
            new OpenWorkspaceRepositoryCommand(
                repositoryRoot,
                new RepositoryLauncher()),
            new NewWiBranchCommand(repositoryRoot, new GitRepositoryService()),
            new OpenSolutionCommand(
                itemSelection,
                new VisualStudioLocator(),
                new VisualStudioSolutionLauncher())
        ]).ToList();
        _configuration.Bind(commands);
        _registry = new CommandRegistry(commands);
        _loadErrors = loadResult.Errors;
        UpdateSuggestions();
    }

    public void InitializeBackground()
    {
        _hotKey = new GlobalHotKey(this, ShowLauncher);
        Hide();
    }

    public void ShowLauncher()
    {
        if (!IsVisible)
        {
            CommandInput.Clear();
            Show();
        }

        Left = System.Windows.SystemParameters.WorkArea.Left
            + (System.Windows.SystemParameters.WorkArea.Width - ActualWidth) / 2;
        Top = System.Windows.SystemParameters.WorkArea.Top
            + System.Windows.SystemParameters.WorkArea.Height * 0.18;
        Activate();
        CommandInput.Focus();
        Keyboard.Focus(CommandInput);

        if (_loadErrors.Count > 0)
        {
            ShowStatus($"有 {_loadErrors.Count} 个插件加载失败：{_loadErrors[0]}", true);
        }
        else if (_registry.Commands.Count == 0)
        {
            ShowStatus($"未找到插件。请将插件 DLL 放入 {Path.Combine(AppContext.BaseDirectory, "Plugins")}。", true);
        }
        else
        {
            ShowStatus($"已加载 {_registry.Commands.Count} 个命令。按 Enter 执行，按 Esc 关闭。");
        }
    }

    public void Dispose()
    {
        _allowClose = true;
        _hotKey?.Dispose();
        _configuration.Dispose();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private async void CommandInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_executing)
        {
            e.Handled = true;
            await ExecuteCommandAsync();
        }
        else if (e.Key == Key.Down && SuggestionList.Items.Count > 0)
        {
            e.Handled = true;
            SuggestionList.SelectedIndex = Math.Min(SuggestionList.SelectedIndex + 1, SuggestionList.Items.Count - 1);
        }
        else if (e.Key == Key.Up && SuggestionList.Items.Count > 0)
        {
            e.Handled = true;
            SuggestionList.SelectedIndex = Math.Max(SuggestionList.SelectedIndex - 1, 0);
        }
        else if (e.Key == Key.Tab && SuggestionList.SelectedItem is CommandSuggestion suggestion)
        {
            e.Handled = true;
            CompleteSuggestion(suggestion);
        }
    }

    private async Task ExecuteCommandAsync()
    {
        _executing = true;
        CommandInput.IsEnabled = false;
        ShowStatus("正在执行...");

        try
        {
            var result = await _registry.ExecuteAsync(CommandInput.Text, CancellationToken.None);
            ShowStatus(result.Message, !result.Success);
        }
        catch (CommandInputException exception)
        {
            ShowStatus(exception.Message, true);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("命令已取消。", true);
        }
        catch (Exception exception)
        {
            ShowStatus($"插件执行失败：{exception.GetBaseException().Message}", true);
        }
        finally
        {
            CommandInput.IsEnabled = true;
            CommandInput.Focus();
            _executing = false;
            Hide();
        }
    }

    private void CommandInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateSuggestions();
    }

    private void UpdateSuggestions()
    {
        var input = CommandInput?.Text ?? string.Empty;
        var suggestions = BuildSuggestions(input);
        SuggestionList.ItemsSource = suggestions;
        SuggestionList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
    }

    private IReadOnlyList<CommandSuggestion> BuildSuggestions(string input)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = CommandParser.Tokenize(input);
        }
        catch (CommandInputException)
        {
            return [];
        }

        var endsWithWhitespace = input.Length > 0 && char.IsWhiteSpace(input[^1]);
        if (tokens.Count == 0 || (tokens.Count == 1 && !endsWithWhitespace))
        {
            var query = tokens.FirstOrDefault() ?? string.Empty;
            return _registry.Find(query)
                .Select(command => new CommandSuggestion(
                    command.Name,
                    BuildSignature(command),
                    command.Description,
                    true))
                .ToList();
        }

        if (!_registry.TryGet(tokens[0], out var command)
            || command is not ICommandValueSuggestionProvider provider)
        {
            return [];
        }

        var parameterIndex = endsWithWhitespace ? tokens.Count - 1 : tokens.Count - 2;
        var queryValue = endsWithWhitespace ? string.Empty : tokens[^1];
        return provider.GetSuggestions(parameterIndex, queryValue)
            .Select(suggestion => new CommandSuggestion(
                suggestion.Value,
                suggestion.Value,
                suggestion.Description,
                false))
            .ToList();
    }

    private static string BuildSignature(ICommandPlugin command)
    {
        var parameters = command.Parameters.Select(parameter =>
        {
            var text = $"{parameter.Name}:{CommandRegistry.GetTypeName(parameter.Type)}";
            return parameter.Required ? $"<{text}>" : $"[{text}]";
        });
        if (command is IVariadicCommandPlugin variadic)
        {
            parameters = parameters.Append($"[{variadic.RemainingParameter.Name}:文本...]");
        }

        return $"{command.Name} {string.Join(' ', parameters)}".TrimEnd();
    }

    private void SuggestionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SuggestionList.SelectedItem is CommandSuggestion suggestion)
        {
            if (!suggestion.IsCommand
                || !_registry.TryGet(suggestion.Completion, out var command)
                || command is null)
            {
                ShowStatus(suggestion.Description);
                return;
            }

            var details = command.Parameters.Count == 0
                ? command.Description
                : string.Join("；", command.Parameters.Select(parameter =>
                    $"{parameter.Name}：{parameter.Description}"));
            ShowStatus(details);
        }
    }

    private void CompleteSuggestion(CommandSuggestion suggestion)
    {
        if (suggestion.IsCommand)
        {
            CommandInput.Text = $"{suggestion.Completion} ";
        }
        else
        {
            var lastWhitespace = CommandInput.Text.LastIndexOfAny([' ', '\t', '\r', '\n']);
            CommandInput.Text = $"{CommandInput.Text[..(lastWhitespace + 1)]}{suggestion.Completion} ";
        }

        CommandInput.CaretIndex = CommandInput.Text.Length;
        CommandInput.Focus();
    }

    private void Configuration_Reloaded(object? sender, ConfigurationReloadedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateSuggestions();
            ShowStatus(e.Message, !e.Success);
        });
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_executing)
        {
            Hide();
        }
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            isError
                ? System.Windows.Media.Color.FromRgb(255, 128, 128)
                : System.Windows.Media.Color.FromRgb(174, 183, 198));
    }

    private sealed record CommandSuggestion(
        string Completion,
        string Signature,
        string Description,
        bool IsCommand);
}
