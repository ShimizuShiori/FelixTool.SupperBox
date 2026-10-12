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
quicktext translate Chinese "Hello world"
openpage baidu
run foo
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

配置文件通过 `$schema` 引用同目录的 `config.schema.json`，支持编辑器校验、字段说明和
自动补全。架构源文件位于 `src\FelixTool.SupperBox\config.schema.json`，纳入代码托管并
随程序发布。每次加载配置（包括热重载）时，如果用户目录中的架构文件不存在，或其
`x-schema-version` 小于随程序发布的版本，就自动复制更新；没有版本字段的架构按旧版
处理，同版本或更高版本不会覆盖。修改架构时应递增这个正整数版本。
程序启动时会为新旧配置自动添加或更新 `$schema` 引用，保留其他配置内容。
自动写入配置时保留可读的汉字和符号，只对 JSON 必需的引号、反斜杠和控制字符转义。
架构允许额外根属性，兼容外部插件配置；架构或复制错误会通过现有错误机制报告。

`quicktext <type> <arg0> <arg1> ...` 从配置文件的 `quicktexts` section 读取快捷文本，
并按序替换 `{0}`、`{1}` 等占位符。未提供对应参数的占位符会原样保留。生成结果会
显示在可复用的 Output Box 中，并在显示时自动复制到剪贴板；也可手动复制、关闭，
窗口在显示五分钟后会自动关闭。输入 `quicktext ` 时可用方向键选择 type，并按 Tab 补全。
已有配置需要将根属性 `prompts` 重命名为 `quicktexts`，旧配置节不再读取。

`openpage <pageName>` 使用系统默认浏览器打开配置文件 `pages` section 中对应的
HTTP 或 HTTPS 地址。输入 `openpage ` 时可用方向键选择页面名称，并按 Tab 补全。

`run <target>` 从配置文件 `commands` section 读取对应目标的 `command`，
在新 CMD 窗口中执行，执行结束后保留窗口以查看输出。命令按 CMD 语法解释，支持
`npm run ...`、`dotnet test ...`、引号、管道以及 `&&` 等，不会额外拆分参数。
可选的 `workingDir` 必须是绝对目录路径，省略时使用当前用户目录
（如 `C:\Users\CurrentUser`）。目标或工作目录不存在时会报错。
目标名称不区分大小写，支持 Tab 补全和配置热重载。仅配置可信命令，命令会以当前
用户权限执行。旧配置无需添加 `commands` 节即可启动，新配置默认包含空对象。

`openrepo <runner> <repoName>` 使用指定程序打开
配置文件 `repositories.roots` 中各个仓库根目录下的同名一级目录。`runner` 可选 `code`（VS Code）、
`cmd`（命令提示符）、`copilot`（在仓库目录直接启动 Copilot）或 `exp`（文件资源
管理器）。runner 和仓库名称均支持 Tab 补全；仓库名称不存在时不会执行任何操作。
`openrepo` 和 `newwibranch` 共用此配置，保存后自动刷新。不同根目录下存在同名仓库时，
会显示选择窗口，列出完整路径；取消选择不会打开仓库或创建分支。补全列表中的同名仓库
只显示一次。目录无需包含 `.git`，不会递归搜索；不存在的根目录会跳过。
`roots` 必须是绝对路径数组，空数组表示不搜索任何仓库。旧配置没有 `repositories`
节时仍使用 `C:\git\GitHub\WiseTechGlobal`，新配置默认包含该路径。

`opensln <filepath>` 从指定文件或目录开始，逐级向上查找当前目录中的 `.sln` 和
`.slnx` 文件。只找到一个时直接打开；找到多个时显示通用选择窗口。程序通过 Visual
Studio Installer 附带的 `vswhere.exe` 查找最新的 Visual Studio（包含 Preview），
也支持通过 `VSINSTALLDIR` 环境变量定位。

配置文件的根属性名默认对应同名命令插件（不区分大小写），插件也可显式指定其他
section 名。实现
`IConfigurableCommandPlugin` 的插件会在启动和配置热重载时收到其专属 JSON 节点，
因此新增插件配置无需修改宿主的配置结构。

```json
{
  "$schema": "config.schema.json",
  "commands": {
    "foo": {
      "workingDir": "C:\\FelixProjects\\MyApp",
      "command": "npm run dev"
    },
    "hello": {
      "command": "echo Hello"
    }
  },
  "repositories": {
    "roots": [
      "C:\\git\\GitHub\\WiseTechGlobal",
      "C:\\FelixProjects"
    ]
  },
  "websearch": {
    "engine": {
      "foo": "https://www.foo.com?q={0}&type={1}&dd={2}"
    }
  },
  "quicktexts": {
    "translate": "Translate the following text into {0}: {1}"
  },
  "pages": {
    "baidu": "https://www.baidu.com/"
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
插件也可调用 `context.Output.ShowAsync(...)`，通过统一的 Output Box 展示并复制文本。

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
