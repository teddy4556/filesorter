# FileSorter

> Personal file classifier for Windows. Drag a file onto the floating disk, it moves to where your rules say it belongs.

类似 DropIt / Hazel,但**只为你一个人设计**:配置是手改的 YAML,触发是悬浮圆盘 + 右键"发送到",冲突默认 `rename`(自动按 Explorer 风格 `-N_N_N` dedup 后缀,**绝不丢文件**)。

## ✨ 特性

- **悬浮窗拖入**:把文件拖到屏幕右下角圆盘,按规则分类(150% DPI 下可正常拖动)
- **右键"发送到"**:在资源管理器右键 → 发送到 → FileSorter
- **SendTo 集成**:首次 `--install-sendto` 自动写 `%APPDATA%\Microsoft\Windows\SendTo\FileSorter.lnk`
- **文件夹拖入对话框**:每次弹"递归 / 只处理顶层 / 取消"三选项(避免误操作)
- **多级目录自动建**:`twitter#(@hahaoy8)#...#20260929.jpg` → `D:\示例\图片\twitter\@hahaoy8\20260929\`
- **纯文本 YAML 规则**:手改也要能识别,2 秒内热加载
- **可视化规则编辑器**:托盘菜单 → Open rules… 弹 GUI 编辑器,实时预览分类路径
- **开机自启动**:`filesorter.exe --install` 写注册表,`--uninstall` 删
- **DPI-aware**:`PerMonitorV2` DPI,150% 缩放下悬浮圆盘可正常拖动
- **冲突策略**:默认 `rename`(不丢文件),可在 YAML 顶层改 `overwrite`/`skip`

## 📦 快速开始(从源码)

### 前置
- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### 装 SDK(开发用,不需要发布版本)

```powershell
# 用户级安装,不污染 Program Files
Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
[Environment]::SetEnvironmentVariable("Path", "$env:USERPROFILE\.dotnet;$env:Path", "User")
```

### 克隆 + 编译

```powershell
git clone https://github.com/teddy4556/filesorter.git filesorter
cd filesorter
dotnet build src/FileSorter/FileSorter.csproj -c Release
dotnet publish src/FileSorter/FileSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist\
```

输出是 `dist\filesorter.exe`,单文件 ~1 MB。**目标机器只需要 .NET 8 Desktop Runtime**(不必装 SDK)。

### 跑起来

```powershell
# 首次会创建 %APPDATA%\FileSorter\rules.yaml
.\dist\filesorter.exe

# (可选)开机自启动
.\dist\filesorter.exe --install
.\dist\filesorter.exe --uninstall

# (可选)注册右键"发送到"菜单
.\dist\filesorter.exe --install-sendto
```

## 🛠️ 规则(YAML)

**位置**:`%APPDATA%\FileSorter\rules.yaml`(右键托盘 → Open rules.yaml)

完整示例见 `examples/rules.yaml`。最常用两种规则类型:

### `extension` — 按扩展名分类

```yaml
rules:
  - name: 压缩包
    type: extension
    patterns: [zip, rar, 7z, tar, gz]
    destination: archives
```

### `name_template` — 语义模板(社交媒体 / 截图场景)

```yaml
rules:
  - name: Twitter 图片
    type: name_template
    template: 'twitter#(@{user_id})#{user_name:unicode-dash}#{date-time}#{status_id:number}'
    extensions: [jpg, jpeg, png]
    destination: twitter
```

文件名 `twitter#(@chacooooo0s)#Belle＊⑅♥#20260929-140000#2104934220808491298.jpg` 自动匹配并提取:
- `user_id = chacooooo0s`
- `user_name = Belle＊⑅♥`
- `date = 20260929-140000`
- `status_id = 2104934220808491298`

#### Token 类型速查

| Token | 含义 | 匹配字符类 |
|---|---|---|
| `{word}` | ASCII 单词 | `[A-Za-z0-9_]+` |
| `{date}` | `yyyyMMdd` | `\d{8}` |
| `{date-time}` | `yyyyMMdd-HHmmss` | `\d{8}-\d{6}` |
| `{number}` | 数字 | `\d+` |
| `{unicode-dash}` | Unicode 安全字符(用户名 / Display Name) | `[\p{L}\p{N}\p{S}\p{M}\p{Cs}\p{Po}]` + 装饰字符(★ ☆ ♥ ❤ ❀ 等) |
| `{filename}` / `{stem}` / `{ext}` | 原文件名 / 不含扩展名 / 扩展名 | 全文 / `.+?(?=\.[^.]+$)` / `\.[^.]+$` |

**`unicode-dash`** 字符类包含:所有 Unicode 字母/数字/符号/标点 + 常见中文/日文标点(`、。?!,;:""''~—–`) + 装饰字符(`＊♥❤❀★☆✿✿✦✧`),匹配社交媒体用户名/Display Name 时几乎不会因为特殊字符漏匹配。

### 规则编辑器

托盘菜单 → **Open rules…** 打开 GUI:
- 左侧规则列表(每条带 `Active` 复选框)
- 右侧字段按 rule.type 自动切换
- 底部"测试输入"框:输入文件名,实时显示分类路径(走完整 `RuleEngine.Match` 流水线)
- 保存时自动留最近 3 份 `rules.yaml.bak.NNN` 备份

## 📂 目录结构

```
filesorter/
├── src/FileSorter/
│   ├── Core/                   # 规则解析 + 匹配 + 分类服务
│   │   ├── NameTemplateCompiler.cs   # 模板编译 + token 字符类
│   │   ├── DestinationPruner.cs      # alias 自动清理(v6 = 删了就是删了)
│   │   ├── PathsEditor.cs            # EditPaths 路径编辑
│   │   └── RuleEngine.cs             # 匹配引擎
│   ├── UI/                     # WPF 窗口 + 托盘 + 悬浮窗
│   │   ├── FloatingDisk.xaml(.cs)    # 悬浮圆盘(可拖动,DPI-aware)
│   │   ├── RuleEditor.xaml(.cs)      # 规则 GUI 编辑器
│   │   └── EditPathsWindow.xaml(.cs) # 路径编辑
│   ├── App.xaml(.cs)           # WPF 入口
│   ├── app.manifest            # PerMonitorV2 DPI awareness
│   ├── app.ico                 # 应用图标
│   └── FileSorter.csproj
├── tests/FileSorter.Tests/     # 单元测试
├── examples/
│   └── rules.yaml              # 全规则类型示例
├── docs/
│   └── design.md
├── _archive/                   # 历史 backup + 调试文件(已忽略)
├── CHANGELOG.md                # 版本历史
├── README.md                   # 本文件
└── FileSorter.sln
```

## ⚠️ 已知限制

- **只支持 Windows**(WPF 依赖,Linux/macOS 不行)
- **`inbox` 别名不再保留**:Pruner v6 会清掉未被任何规则引用的 alias(含 `inbox`),需要确保规则 `destination` 引用了 `destinations.inbox`,否则别名会被自动删掉
- **`default_action` 未匹配时不会回退到 inbox**:`ClassifierService` 在无规则命中时直接返回 "no rule matched",文件保持原位不动
- **path_template** 只支持一个 `filename_pattern` 字段,不能多正则复合
- **规则匹配顺序自上而下**,**第一条命中的规则胜出** — 顺序敏感
- 拖入文件夹 → **每次**都弹递归/顶层对话框(不记忆选择)
- **EditPaths 窗口**只编辑 `destinations.*` 路径块,改其它字段需要"Open rules.yaml"手动

## 🧪 开发

```powershell
# 跑单元测试
dotnet test

# 写完改代码 → 重新发布
dotnet publish src/FileSorter/FileSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist\
```

**Cross-platform dev workflow**:可以在 Linux 容器(NAS)上用 .NET SDK 编辑代码,但**编译产物是 .NET 8 Windows 应用**,必须在 Windows 上 `dotnet publish` 或跑 `csc.dll` 直接编译。本仓库的日常开发流程是:代码编辑在 Linux 容器(NAS),cross-compile/deploy 在 Windows(通过 windows-mcp)。

## 📝 License

MIT

---

详细版本历史 → [CHANGELOG.md](./CHANGELOG.md)
