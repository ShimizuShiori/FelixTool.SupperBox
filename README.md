# FelixTool.SupperBox

一个常驻 Windows 托盘的命令启动器。按 `Ctrl+F1` 打开输入框，输入
`command arg1 arg2 ...` 后按 Enter 执行插件命令。

## 运行

```powershell
dotnet run --project .\src\FelixTool.SupperBox
```

程序启动后隐藏到托盘。双击托盘图标也可打开；右键图标可退出。

内置示例：

```text
echo "hello world"
add 1.5 2
websearch google "SupperBox launcher"
sysconfig
```

输入命令名时会显示匹配命令、参数类型和用途。参数可用单引号或双引号包裹；
反斜杠路径（如 `C:\Temp\a.txt`）会被原样保留。

`sysconfig` 会打开 `%APPDATA%\FelixTool.SupperBox\config.json`。其中
`websearch.engine` 定义搜索引擎名称和 URL 格式，`{0}`、`{1}` 等零基占位符按顺序对应
`websearch <engine> <kw1> <kw2> ...` 的关键词，并会在打开浏览器前进行 URL 编码。
保存配置后程序会自动重新加载；输入 `websearch ` 时可用方向键选择 engine，并按
Tab 补全。

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
