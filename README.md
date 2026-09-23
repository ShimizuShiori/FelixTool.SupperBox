# FelixTool.SupperBox

一个常驻 Windows 托盘的命令启动器。按 `Ctrl+F1` 打开输入框，输入
`command arg1 arg2 ...` 后按 Enter 执行插件命令。

## AI 开发约定

> **给 AI：每次修改完本项目后都必须运行根目录的 `.\build.ps1`。只有该脚本成功完成，
> 才能认为修改已完成。不要只运行 `dotnet build` 代替此步骤。**

该脚本会停止正在运行的 SupperBox，以 Release 配置将程序发布到
`C:\Tools\FelixTool.SupperBox`，写入当前用户的 Windows `Run` 注册表项以启用登录后
自启动，最后重新启动发布后的程序。该方式不需要管理员权限。

## 运行

```powershell
dotnet run --project .\src\FelixTool.SupperBox
```

程序启动后隐藏到托盘。双击托盘图标也可打开；右键图标可退出。

内置示例：

```text
websearch google "SupperBox launcher"
openrepo code MyRepository
openrepo cmd MyRepository
openrepo copilot MyRepository
openrepo exp MyRepository
opensln C:\git\MyRepository\src
sysconfig
```

输入命令名时会显示匹配命令、参数类型和用途。参数可用单引号或双引号包裹；
反斜杠路径（如 `C:\Temp\a.txt`）会被原样保留。

`sysconfig` 会打开 `%APPDATA%\FelixTool.SupperBox\config.json`。其中
`websearch.engine` 定义搜索引擎名称和 URL 格式，`{0}`、`{1}` 等零基占位符按顺序对应
`websearch <engine> <kw1> <kw2> ...` 的关键词，并会在打开浏览器前进行 URL 编码。
保存配置后程序会自动重新加载；输入 `websearch ` 时可用方向键选择 engine，并按
Tab 补全。

`openrepo <runner> <repoName>` 使用指定程序打开
`C:\git\GitHub\WiseTechGlobal` 下的同名一级目录。`runner` 可选 `code`（VS Code）、
`cmd`（命令提示符）、`copilot`（在仓库目录直接启动 Copilot）或 `exp`（文件资源
管理器）。runner 和仓库名称均支持 Tab 补全；仓库名称不存在时不会执行任何操作。

`opensln <filepath>` 从指定文件或目录开始，逐级向上查找当前目录中的 `.sln` 和
`.slnx` 文件。只找到一个时直接打开；找到多个时显示通用选择窗口。程序通过 Visual
Studio Installer 附带的 `vswhere.exe` 查找最新的 Visual Studio（包含 Preview），
也支持通过 `VSINSTALLDIR` 环境变量定位。

配置文件的根属性名会自动对应同名命令插件（不区分大小写）。实现
`IConfigurableCommandPlugin` 的插件会在启动和配置热重载时收到其专属 JSON 节点，
因此新增插件配置无需修改宿主的配置结构。

```json
{
  "websearch": {
    "engine": {
      "foo": "https://www.foo.com?q={0}&type={1}&dd={2}"
    }
  }
}
```

## 编写插件

1. 新建目标框架为 `net10.0` 的类库。
2. 引用 `src\FelixTool.SupperBox.Abstractions`。
3. 创建具有公开无参构造函数的 `ICommandPlugin` 实现。
4. 编译后将插件 DLL 及其私有依赖复制到主程序输出目录的 `Plugins` 文件夹。
5. 重启 SupperBox。单个插件加载或执行失败时，错误会显示在输入框下方，不会导致程序退出。

```csharp
public sealed class HelloCommand : ICommandPlugin
{
    public string Name => "hello";
    public string Description => "问候指定用户";
    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("name", CommandParameterType.String, "用户名称")
    ];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(CommandResult.Ok($"Hello, {arguments["name"]}!"));
    }
}
```

命令名不区分大小写，且必须唯一。宿主会完成参数数量检查以及文本、整数、数字、
布尔值的类型转换；插件只需实现业务逻辑并返回 `CommandResult`。

插件需要用户从多个条目中选择一个时，可额外实现 `IPluginContextAware`。宿主会在
加载插件后注入 `IPluginContext`，插件调用 `context.ItemSelection.SelectAsync(...)`
即可复用统一的选择窗口；用户按 Esc 取消时会抛出 `OperationCanceledException`。

```csharp
public sealed class ChooseCommand : ICommandPlugin, IPluginContextAware
{
    private IPluginContext? _context;

    public void Initialize(IPluginContext context) => _context = context;

    // 在 ExecuteAsync 中：
    // var selected = await _context!.ItemSelection.SelectAsync(
    //     "选择条目",
    //     values.Select(value => new SelectionItem<string>(value, value)).ToList(),
    //     cancellationToken);
}
```
