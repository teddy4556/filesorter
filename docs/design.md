# FileSorter 设计文档

| 项 | 值 |
|---|---|
| 创建日期 | 2026-09-27 |
| 类型 | Windows 桌面文件分类工具(类 DropIt) |
| 用户 | 单人自用 |
| 技术栈 | .NET 8 SDK + WPF + C# |
| 工作目录(本地)| `D:\hermes-工作目录\filesorter\`(Windows 笔记本) |
| 工作目录(NAS)| `/opt/data/projects/filesorter/`(容器,本设计文档所在) |
| 同步方式 | NAS → Windows 通过 windows-mcp FileSystem 同步 |

---

## 1. 目标与范围

### 1.1 核心目标

一个常驻 Windows 系统的轻量文件分类器,提供两种触发方式:

1. **悬浮窗拖入**:用户把文件/文件夹拖到悬浮小圆盘上,按规则立即分类
2. **右键"发送到"**:在资源管理器右键文件 → "发送到 FileSorter" → 按规则分类

### 1.2 不做(明确排除)

- ❌ 不做安装包(自用,直接拷 exe 即可)
- ❌ 不做云后端、不做远程同步 API
- ❌ 不做完整图形配置界面(GUI 配置面板)——规则改 YAML 即可
- ❌ 不做账号系统、不做多用户
- ❌ 不做 OCR / 脚本调用等高级动作(只做"按规则移动/复制/重命名")

> **关于 GUI 配置**:经用户 9-27 补充,**仅**为"修改路径"提供一个最小化的文件夹选择对话框(WPF `OpenFolderDialog`),其余字段仍然手改 YAML。这是为了避免手输长路径易错,不是要做完整 GUI。

### 1.3 成功标准

| 维度 | 标准 |
|---|---|
| 启动时间 | < 1 秒(从双击到托盘可见) |
| 单文件分类耗时 | < 100ms(规则匹配 + 文件操作) |
| 规则改完到生效 | < 2 秒(热加载,无需重启软件) |
| 内存占用 | < 50MB(常驻) |
| exe 体积 | < 15MB(单文件,无外部依赖) |
| 多级目录支持 | twitter 文件名 → 自动分到 `D:\分类\图片\twitter\@hahaoy8\` |
| 文件夹拖入 | 拖入文件夹时弹"递归/顶层/取消"对话框,不能静默处理 |

---

## 2. 触发架构

### 2.1 触发方式 1:悬浮窗(主交互)

```
┌─────────────────────────────┐
│   FileSorter 悬浮圆盘        │  ← 始终置顶,半透明,默认收起
│   (可拖动,可隐藏到边缘)      │
└─────────────────────────────┘
        ↑ 用户拖文件/文件夹到这里
        ↓ 触发分类
```

- 圆盘初始默认位置:屏幕右下角,距离边缘 20px
- 鼠标悬停圆盘 → 展开显示规则数量、当前状态
- 鼠标移开 3 秒 → 自动收起
- 拖入文件后立即触发分类,完成后弹气泡提示"已分到 X 个文件夹"

### 2.2 触发方式 2:右键"发送到"

Windows 资源管理器右键菜单 → "发送到" 子菜单 → "FileSorter"

**实现**:在 `C:\Users\<你的用户名>\SendTo\` 目录创建快捷方式指向 `filesorter.exe`。SendTo 是 Windows 系统识别的"发送到"菜单目录,放快捷方式即可,无需写 shell extension。

**简化决策**:不用写真正的 shell extension(复杂且要注册 COM),用 SendTo 目录的快捷方式是 Windows 原生支持的等价方案。

### 2.3 两种触发共享核心

两种方式最终都调用同一个 `ClassifierService.ClassifyAsync(filePaths)` 方法,只是入口不同。

### 2.4 开机自启动

通过注册表 `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` 实现:

- 写入:`filesorter.exe --install`(在 HKCU\...\Run 下加 `FileSorter` 项,值指向 exe 绝对路径)
- 移除:`filesorter.exe --uninstall`(删除该项)
- 开机启动后自动以"无参数"模式启动 = 仅显示托盘图标,不弹主窗口

**为什么用 HKCU 而不是 HKLM**:不需要管理员权限,且隔离到当前用户。
**为什么不用 Startup 文件夹**:快捷方式占空间且易被删除;注册表更稳定。

---

## 3. 规则设计(YAML 格式)

### 3.1 文件路径与加载顺序

1. 优先级 1:用户自定义路径(将来需要时支持,本期不实现 GUI 选择)
2. 优先级 2:`%APPDATA%\FileSorter\rules.yaml`(默认)

本期固定用优先级 2。

**首次启动行为**:若 `%APPDATA%\FileSorter\` 不存在,程序自动创建该目录,并从 exe 同目录(默认 `dist\` 下)的 `rules.yaml` 模板拷贝一份过去。这样既保证"随 exe 分发",又保证"运行时可被用户手改"(改 APPDATA 下的那份,exe 旁那份不动)。

### 3.2 规则格式

```yaml
# rules.yaml
version: 1
default_action: move          # move | copy | rename_only
conflict_strategy: skip       # skip | overwrite | rename
log_level: info               # debug | info | warn | error

# 目的地为相对路径时,基准是 %USERPROFILE%
destinations:
  images: D:\分类\图片
  documents: D:\分类\文档
  archives: D:\分类\压缩包
  inbox: D:\分类\未分类          # catch-all,放最后

rules:
  # 规则 1:扩展名匹配 → 一级目的地
  - name: 图片分类
    type: extension
    patterns: [jpg, jpeg, png, gif, bmp, webp, svg]
    destination: images

  # 规则 2:文件名关键字匹配
  - name: 发票
    type: filename_keyword
    patterns: ["发票", "invoice", "receipt"]
    case_sensitive: false
    extensions: [pdf, jpg, png]
    destination: documents

  # 规则 3:复合(扩展名 + 文件名)
  - name: 截图
    type: combined
    extension: [png, jpg]
    filename_keyword: ["screenshot", "截图", "screen"]
    destination: images

  # 规则 4:路径模板(支持多级目录)
  # 文件名:twitter_(@hahaoy8)_肉丝儿_20260730-085445_2082751743373496825.jpg
  # 目标:D:\分类\图片\twitter\@hahaoy8\<原文件名>
  - name: 社交媒体图片
    type: path_template
    extensions: [jpg, jpeg, png, gif, webp]
    filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'   # 正则,捕获组 1=平台,捕获组 2=作者
    path: '{destinations.images}\{1}\@{2}'         # @ 是字面字符,因为 group 2 不包含 @
    # 可用 token:{date:yyyy-MM} / {ext} / {filename} / {stem} 等
    destination_alias: images                     # 路径别名(可选,纯引用)

  # 规则 5:catch-all
  - name: 其他
    type: default
    destination: inbox
```

### 3.3 路径模板(path_template 专属)

支持的 token:

| Token | 含义 | 例 |
|---|---|---|
| `{filename}` | 完整文件名(含扩展名)| `twitter_(@hahaoy8)_...jpg` |
| `{stem}` | 不含扩展名的文件名 | `twitter_(@hahaoy8)_肉丝儿_20260730-...` |
| `{ext}` | 仅扩展名(小写,不带点)| `jpg` |
| `{date:yyyy-MM}` | 文件修改日期 | `2026-07` |
| `{date:yyyy}` | 年份 | `2026` |
| `{N}` | 正则捕获组(从 1 开始) | `\1` `\2` |
| `{destinations.<name>}` | 引用 destinations 别名 | `{destinations.images}` |

**多级目录**:用 `\`(Windows)或 `/`(跨平台)分隔。模板里出现几级 `\` 就是几级目录。

### 3.4 匹配优先级

按 rules 列表**自上而下**匹配,**第一个命中即停止**。catch-all 规则放最后。

**path_template 命中条件**:`filename_pattern` 正则必须匹配 + `extensions` 列表必须包含扩展名。

### 3.5 热加载机制

- 程序启动时加载 rules.yaml
- FileSystemWatcher 监听 rules.yaml 文件变更
- 文件变更后 **2 秒内重载**(防编辑器保存中途读取)
- 重载失败时保留旧规则 + 弹气泡"规则加载失败,已保留旧版本"

### 3.6 手改友好性

- YAML 注释 `#` 开头
- 缩进 2 空格
- 字段名固定 9 个:type / patterns / extension / filename_keyword / filename_pattern / path / extensions / destination / destination_alias / case_sensitive
- 错误信息带行号(用 YamlDotNet 内置报错)
- 正则测试可用 https://regex101.com/?flavor=dotnet 先验证

---

## 4. 核心架构

### 4.1 模块划分

```
filesorter/
├── Program.cs                  # 入口
├── App.xaml / App.xaml.cs      # WPF 应用,启动托盘 + 悬浮窗
├── Core/
│   ├── ClassifierService.cs    # 分类主逻辑(规则匹配 + 文件操作)
│   ├── RuleEngine.cs           # 规则加载、解析、匹配
│   ├── RuleWatcher.cs          # FileSystemWatcher 热加载
│   └── Models/
│       ├── Rule.cs             # 规则模型
│       └── RulesFile.cs        # 规则文件模型
├── UI/
│   ├── FloatingDisk.xaml       # 悬浮窗
│   ├── TrayIcon.cs             # 托盘图标 + 菜单
│   └── EditPathsWindow.xaml    # 路径编辑窗口(只动 destinations.<name>)
├── filesorter.exe              # 构建产物
└── rules.yaml                  # 用户配置(随 exe 一起分发)
```

### 4.2 数据流

```
拖入文件 / 右键发送
    ↓
FloatingDisk.Drop 或 SendTo 快捷方式
    ↓
ClassifierService.ClassifyAsync(paths[])
    ↓
RuleEngine.Match(path) → Rule
    ↓
FileAction.Move/Copy(src, dest)
    ↓
气泡通知 "已分到 X"
```

### 4.3 关键设计决策

| 决策 | 选择 | 理由 |
|---|---|---|
| 文件操作 | 默认 `move`,失败回退 `copy` | move 性能好(同盘不复制),但跨盘时 copy |
| 冲突处理 | `rename` 模式(自动加后缀 _1, _2) | 不丢文件,不覆盖 |
| 干运行模式 | 提供 `--dry-run` 参数 | 测试规则用,不真动文件 |
| 日志 | 写到 `%LOCALAPPDATA%\FileSorter\logs\`,按天滚动 | 易排查,不占空间 |
| 单一可执行文件 | dotnet publish `--self-contained false --publish-single-file` | 体积小(<15MB),依赖系统装的 runtime |

### 4.4 主窗口 + 路径编辑(最小化 GUI)

**触发**:托盘菜单 "编辑路径" → 弹出 `EditPathsWindow`,或 `filesorter.exe --edit-paths`。

**窗口内容**:
- 标题:"FileSorter - 编辑路径"
- 表单:每个 `destinations.<name>` 一行,显示 [当前路径] [浏览…] [清除]
- 底部按钮:[保存] [取消] [在文件管理器中打开 rules.yaml]

**保存逻辑**:
- 改完点保存 → 写回 `%APPDATA%\FileSorter\rules.yaml`(原子替换:写 tmp 文件 + `File.Replace`)
- 触发 `RuleWatcher` 重新加载(2 秒延迟之内)
- 不改其它字段(type / patterns / filename_pattern / path 等)

**"浏览…"按钮** = `Microsoft.Win32.OpenFolderDialog`(WPF 内置,Win10+ 可用)。

**不做的事**:不在窗口里加新规则、删规则、改正则——这些还是手改 YAML。

### 4.5 拖入文件夹对话框

**触发场景**:悬浮窗拖入或右键"发送到"包含**目录**(而非纯文件)。

**对话框内容**:
- 标题:"FileSorter - 拖入了文件夹"
- 副文本:"<路径>(N 个文件,M 个子文件夹)"
- 三个按钮:
  - **递归处理所有文件** — 遍历文件夹 + 所有子文件夹内的每个文件,逐个匹配规则
  - **只处理顶层文件** — 只处理这个文件夹根目录的文件,子文件夹不动
  - **取消**

**"递归处理"的语义**:
- 用 `Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)` 拿所有子文件
- 空文件夹 / 隐藏文件(以 `.` 开头)按 `extensions` 过滤(无扩展名的目录不参与)
- 不删除原文件夹(只移动文件,不动目录结构)

**右键 SendTo 路径与拖入文件夹路径一致**:SendTo 把目录传进来时也走同一逻辑。

**不做的事**:不实现"按扩展名分流"(即"只递归 .jpg")——粒度就是文件 vs 文件夹两个选项。

---

## 5. 错误处理

| 场景 | 处理 |
|---|---|
| 规则 YAML 解析失败 | 保留旧规则 + 气泡通知 + 日志详情 |
| 目标目录不存在 | 自动创建(单层),多层失败则报错跳过 |
| 源文件被占用 | 跳过,记录日志,提示用户 |
| 目标已有同名文件 | 按 conflict_strategy(skip/overwrite/rename)处理 |
| 拖入文件夹 | 弹"递归/顶层/取消"对话框(见 §4.5) |
| 规则无匹配 | 走 default 规则,或复制到 inbox |
| 磁盘空间不足 | 报错并提示,不清空原文件 |

---

## 6. 测试策略

### 6.1 单元测试(xUnit)

- `RuleEngine.Match` 的 4 种规则类型
- `RuleEngine.Match` 的优先级(第一个命中)
- `RuleFile.Load` 的有效/无效 YAML
- `ClassifierService` 的 dry-run 模式
- 冲突处理 3 种模式

### 6.2 集成测试

- 创建临时目录 → 放测试文件 → 运行分类 → 验证文件位置
- 修改 rules.yaml → 验证 2 秒内生效

### 6.3 手动验收

- [ ] 双击 exe → 1 秒内托盘出现
- [ ] 右键圆盘 → 拖文件 → 分类成功
- [ ] 资源管理器 → 右键 → 发送到 → FileSorter → 分类成功
- [ ] 编辑 rules.yaml → 2 秒后新规则生效
- [ ] 拖入文件夹 → 递归处理
- [ ] 重启电脑 → 重新双击 exe 即可(无自启动)
- [ ] kill 进程 → 重启 → 状态恢复

---

## 7. 开发与构建

### 7.1 目录约定

- 容器(NAS)侧:`/opt/data/projects/filesorter/`(本文件 + C# 代码)
- Windows 笔记本侧:`D:\hermes-工作目录\filesorter\`(build 用)

每次代码改动后,我用 windows-mcp FileSystem 同步整个目录到 Windows 笔记本,然后跑 `dotnet publish`。

### 7.2 构建命令

```powershell
cd D:\hermes-工作目录\filesorter
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o D:\hermes-工作目录\filesorter\dist\
```

产物:`D:\hermes-工作目录\filesorter\dist\filesorter.exe`(< 15MB)。

### 7.3 依赖

- YamlDotNet(规则解析,NuGet 装)
- 其他都用 .NET 8 内置

---

## 8. 里程碑

| 里程碑 | 交付物 | 验证标准 |
|---|---|---|
| M1: 设计文档 | 本文件 | 用户已点头 |
| M2: 项目骨架 | .csproj + Program.cs + App.xaml | `dotnet build` 通过 |
| M3: 核心分类 | ClassifierService + RuleEngine + 4 种规则 | 单元测试通过 |
| M4: 规则热加载 | RuleWatcher + 文件监听 | 修改 YAML 2 秒生效 |
| M5: 悬浮窗 | FloatingDisk.xaml + 拖放接收 | 拖入文件触发分类 |
| M6: 托盘 + 退出 | TrayIcon + 右键菜单 | 托盘图标可见 |
| M7: SendTo 集成 | 写快捷方式到 SendTo | 右键菜单出现 |
| M8: 端到端验证 | 全流程手动跑通 | 用户签字 |
| M9: 开机自启动 | `--install` / `--uninstall` 命令 + 注册表读写 | 注册表项存在 + 重启后自动起 |
| M10: 路径模板规则 | path_template 类型 + 正则抽取 + 多级目录 | twitter 文件分到 `D:\分类\图片\twitter\@hahaoy8\` |
| M11: 路径编辑 GUI | EditPathsWindow + OpenFolderDialog + 原子保存 | 改一个路径 → 保存 → 2 秒内规则生效 |
| M12: 文件夹对话框 | 拖入文件夹时弹"递归/顶层/取消"对话框 | 拖入 10 个文件的文件夹 → 看到 3 选项对话框 |

---

## 9. 未来扩展(本期不做,留口子)

- 规则按时间段(如:本月 → 临时,更早 → 归档)
- 简单 GUI 配置面板(目前手改 YAML)
- 多设备同步(目前自己拷 YAML 到每台设备)

---

## 变更记录

- 2026-09-27 初版(M1 完成,待用户最终评审)
- 2026-09-27 修订:加入 §2.4 开机自启动(HKCU\...\Run)+ M9,移除原"不做开机自启动"项
- 2026-09-27 修订:加入 §3.3 path_template 规则类型(支持正则抽取 + 多级目录)+ M10,演示 twitter 例
- 2026-09-27 修订:加入 §4.4 EditPathsWindow 最小化 GUI(只改 destinations 路径)+ M11,§1.2 排除项微调
- 2026-09-27 修订:加入 §4.5 拖入文件夹对话框(递归/顶层/取消)+ M12

---

## v2 变更记录(2026-09-27 后,M14-M23)

### 新增规则类型 / 字段

- **name_template**:`{token[:type]}` 占位符 → 自动编译为 regex → 配合 `mappings: [{token, level}]` 控制多级目录
  - 支持 token 类型:`text/string`(字母)、`int/number`(数字)、`any`(字母数字)、`date:yyyyMMdd`/`HHmmss` 等日期格式
  - 示例:`{platform}_@{author}_{date:yyyyMMdd}.{ext}` + mappings `{platform}→1, {author}→2` → 2 级目录
- **filename_pattern**:`starts_with` / `ends_with` 边匹配 + 多 `extensions` + `case_sensitive`,比 v1 `combined` 更直观
- **active**(bool,默认 true):单条规则可临时禁用,`active: false` 在加载/编辑/匹配三处全部生效
- **mappings**(List<Mapping>):name_template 规则的"token → 目录级别"映射
- **Mapping**:`{ token, level }`,`level` 1-5,默认 1

### 新增 CLI

- `--install-sendto`:写 `%APPDATA%\Microsoft\Windows\SendTo\FileSorter.lnk`(若 v1 desktop-cli 已就绪则跳过)
- `--uninstall-sendto`:删上面的 lnk

### GUI 规则编辑器(M18-M20)

- 托盘菜单 → Open rules… 启动 RuleEditor Window
- 字段动态按 rule.type 渲染(不再 flat dump 所有字段)
- Mappings UI 用 token 下拉框(自动从模板抽取)+ level 下拉框 + Add/Delete 行
- "测试输入"实时计算某文件名会落到哪个目录
- 保存自动留 3 份 `rules.bak.N` backup;Reload 从磁盘重新读

### 实现里程碑

| M# | 内容 |
|---|---|
| M14 | Rule 模型加 Active + Mappings + Mapping + YAML round-trip |
| M15 | NameTemplateCompiler + NameTemplatePathBuilder({token[:type]} → regex → 路径) |
| M16 | filename_pattern + name_template 类型接入 RuleEngine,Active=false 跳过 |
| M17 | RulesFileWriter(原子写+保留 3 份 backup)+ StartupArgs + SendToInstaller + QuickRuleDialog |
| M18 | RuleEditor 列表+增删改+保存 + App.xaml.cs CLI 接线 + TrayIcon 加 Open rules… 菜单 |
| M19 | RuleEditor 按 type 动态显示字段(extension/filename_pattern/name_template 等)+ Mappings UI 子组件 |
| M20 | Test input 预览功能(实时 path 计算) |
| M21 | README + design.md 加 v2 章节(本文) |
| M22 | 加 v2 端到端测试(目标 ≥4 新 E2E) |
| M23 | 最终 publish + exe 验证 + push |

### 测试覆盖

- 45 个测试全部通过(M1-M18 累积):包含 v1 兼容性 + v2 新功能(NameTemplateCompiler / NameTemplatePathBuilder / RulesFileWriter / SendToInstaller / Active 跳过 / Mappings round-trip)
- M22 加 4+ 端到端测试:ExtensionMove / PathTemplate_WithAuthor / NameTemplate_MultiLevelMappings / InactiveRule_Skipped

### 已知限制(v2 增量)

- GUI 规则编辑器的 ComboBox 选项在快速输入时偶发失焦,改用 Tab 键切换可绕过
- name_template 的 token 类型严格匹配(类型不符抛 ArgumentException),模板写错时无降级
- Mappings UI 不支持拖拽排序,需先删再加