# FileSorter

> Personal file classifier for Windows. Drag a file onto the floating disk, it moves to where your rules say it belongs.

类似 DropIt / Hazel,但**只为你一个人设计**:配置是手改的 YAML,触发是悬浮圆盘 + 右键"发送到",冲突默认 `rename`(重命名加 `_1` `_2` 后缀,绝不丢文件)。

## ✨ 特性

- **悬浮窗拖入**:把文件拖到屏幕上的圆盘,按规则分类
- **右键"发送到"**:在资源管理器右键 → 发送到 → FileSorter
- **文件夹拖入对话框**:每次弹"递归 / 只处理顶层 / 取消"三选项(避免误操作)
- **多级目录自动建**:`twitter_(@hahaoy8)_xxx.jpg` → `D:\示例\图片\twitter\@hahaoy8\`
- **纯文本 YAML 规则**:手改也要能识别,2 秒内热加载
- **开机自启动**:`filesorter.exe --install` 写注册表,`--uninstall` 删
- **最小化路径编辑**:右键托盘 → Open paths… 弹一个简单窗口,只改 `destinations.*` 路径
- **冲突策略**:默认 `rename`(不丢文件),可在 YAML 顶层改 `overwrite`/`skip`

## 📦 快速开始(从源码)

```powershell
# 1. 装 .NET 8 SDK(用户级即可)
Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
[Environment]::SetEnvironmentVariable("Path", "$env:USERPROFILE\.dotnet;$env:Path", "User")

# 2. clone & build & publish
git clone <this-repo>.git filesorter
cd filesorter
dotnet publish src/FileSorter/FileSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist\

# 3. 跑(首次会创建 %APPDATA%\FileSorter\rules.yaml)
.\dist\filesorter.exe

# 4. (可选)开机自启动
.\dist\filesorter.exe --install
.\dist\filesorter.exe --uninstall
```

输出是 `dist\filesorter.exe`,单文件 ~1 MB。需要 `.NET 8 Desktop Runtime` 在目标机器。

## 🛠️ 规则(YAML)

**位置**:`%APPDATA%\FileSorter\rules.yaml`(右键托盘 → Open rules.yaml)

**示例**(全规则示例见 `examples/rules.yaml`):

```yaml
version: 1
default_action: move
conflict_strategy: rename   # rename | overwrite | skip

destinations:
  images: D:\示例\图片
  documents: D:\示例\文档
  archives: D:\示例\压缩包
  inbox: D:\示例\未分类

rules:
  # 扩展名 → 单一目录
  - name: 压缩包
    type: extension
    patterns: [zip, rar, 7z, tar, gz]
    destination: archives

  # 文件名关键字(子串匹配)
  - name: 发票
    type: filename_keyword
    patterns: ["发票", "invoice", "receipt"]
    case_sensitive: false
    extensions: [pdf, jpg]
    destination: documents

  # 扩展名 + 关键字同时满足
  - name: 截图
    type: combined
    extension: [png, jpg]
    filename_keyword: ["screenshot", "截图", "screen"]
    destination: images

  # 正则抽取 + 多级目录(path_template)
  - name: 社交媒体图片
    type: path_template
    extensions: [jpg, jpeg, png]
    filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'   # group 1=平台  group 2=作者(不含 @)
    path: '{destinations.images}\{1}\@{2}'         # 一级=平台  二级=@作者
```

### path_template token 速查

| Token | 含义 |
|---|---|
| `{destinations.X}` | 引用 `destinations.X` 路径 |
| `{1}` `{2}` ... | 正则捕获组 1、2... |
| `{filename}` | 完整原文件名 |
| `{stem}` | 不含扩展名 |
| `{ext}` | 含点扩展名 |
| `{date:yyyy-MM}` | 当前日期,可换格式 |

### path_template regex 注意事项

C# 的 verbatim 字符串里 `\(` `\)` 是真正的转义括号(等同于 `\\(` `\\)` 在 YAML 文本里):

```yaml
filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
```

上面正则的 group 1 = `twitter`,group 2 = `hahaoy8`(不含 `@`),所以 path 模板里**手动加 `\@`**:

```yaml
path: '{destinations.images}\{1}\@{2}'  # → ...\twitter\@hahaoy8
```

## 📂 目录结构

```
filesorter/
├── src/FileSorter/
│   ├── Core/              # 规则解析 + 匹配 + 分类服务
│   ├── UI/                # WPF 窗口 + 托盘 + 悬浮窗
│   ├── App.xaml(.cs)      # WPF 入口
│   ├── rules.yaml         # 默认规则模板
│   └── FileSorter.csproj
├── tests/FileSorter.Tests/
├── examples/
│   └── rules.yaml         # 全规则类型示例
├── docs/
│   ├── design.md
│   └── superpowers/plans/2026-09-27-filesorter.md
└── README.md
```

## ⚠️ 已知限制

- **只支持 Windows**(WPF 依赖,Linux/macOS 不行)
- **tray 图标用系统默认**(`SystemIcons.Application`)——未做自定义 .ico
- **path_template** 只支持一个 `filename_pattern` 字段,不能多正则复合
- **托盘菜单的"Open paths…"** 当前实现只编辑 `destinations` 块;改其它字段需要"Open rules.yaml"手动
- **规则匹配顺序自上而下**,**第一**条命中的规则胜出
- 拖入文件夹 → **每次**都弹递归/顶层对话框(不记忆选择)

## 🆕 v2 新特性(2026-09+)

| 特性 | 说明 |
|---|---|
| SendTo 集成 | 自动写 `SendTo\FileSorter.lnk`,资源管理器右键 → 发送到 → FileSorter |
| Rule + Mappings | name_template 规则可声明 `mappings: [{token, level}]` 控制多级目录生成 |
| name_template 规则 | `{platform}_@{author}_{date:yyyyMMdd}.{ext}` 这种语义模板,自动编译成 regex |
| filename_pattern 规则 | `starts_with` / `ends_with` 边匹配 + 多扩展名,比 `combined` 更轻量 |
| GUI 规则编辑器 | 托盘菜单 → Open rules…,可视化增删改 + 重排 + 实时预览 |
| 热加载 + backup 保留 | YAML 保存时自动保留最近 3 份 `rules.bak.N` 备份 |
| Active 开关 | 单条规则可临时禁用(不删),`active: false` 在加载和编辑里都保留 |

### v2 规则示例

```yaml
# name_template:从模板自动生成多级目录
- name: 社交媒体图片
  type: name_template
  template: "{platform}_@{author}_{date:yyyyMMdd}.{ext}"
  destination: images
  mappings:
    - { token: "{platform}", level: 1 }   # 第 1 级 = 平台
    - { token: "{author}",   level: 2 }   # 第 2 级 = 作者
```

`twitter_@hahaoy8_20260927.jpg` → `D:\示例\图片\twitter\@hahaoy8\`

```yaml
# filename_pattern:边匹配 + 多扩展名
- name: 截图
  type: filename_pattern
  extensions: [png, jpg]
  starts_with: [screenshot_, ScreenShot]
  case_sensitive: false
  destination: images
```

```yaml
# 临时禁用某条规则(不删)
- name: 测试规则
  type: extension
  patterns: [tmp]
  destination: inbox
  active: false      # v2 新字段;默认 true
```

### GUI 规则编辑器

托盘菜单 → **Open rules…** 打开可视化编辑器:

- 左侧规则列表(每条带 `Active` 复选框)
- 右侧字段自动按 rule.type 切换:extension / filename_pattern / path_template / name_template 各有专属字段
- Mappings 用 token 下拉框(自动从模板抽取)+ level 下拉框(1-5)
- 底部"测试输入"框实时计算某文件名会落到哪个目录
- 保存时自动留最近 3 份 backup

### SendTo 一键启用

首次跑 `filesorter.exe --install-sendto`,会在 `%APPDATA%\Microsoft\Windows\SendTo\FileSorter.lnk` 写快捷方式。资源管理器右键 → 发送到 → FileSorter 即可触发分类。

## 🧪 开发

```powershell
# 跑单元测试(49 个:16 v1 + 33 v2)
dotnet test

# 写完改代码 → 重新发布
dotnet publish src/FileSorter/FileSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist\
```

## 📝 License

MIT