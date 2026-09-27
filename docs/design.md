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
- ❌ 不做图形配置界面(GUI 配置面板)——规则改 YAML 即可
- ❌ 不做账号系统、不做多用户
- ❌ 不做 OCR / 脚本调用等高级动作(只做"按规则移动/复制/重命名")

### 1.3 成功标准

| 维度 | 标准 |
|---|---|
| 启动时间 | < 1 秒(从双击到托盘可见) |
| 单文件分类耗时 | < 100ms(规则匹配 + 文件操作) |
| 规则改完到生效 | < 2 秒(热加载,无需重启软件) |
| 内存占用 | < 50MB(常驻) |
| exe 体积 | < 15MB(单文件,无外部依赖) |

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

**实现**:在 `C:\Users\yaoyx\SendTo\` 目录创建快捷方式指向 `filesorter.exe`。SendTo 是 Windows 系统识别的"发送到"菜单目录,放快捷方式即可,无需写 shell extension。

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
  # 规则 1:扩展名匹配
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

  # 规则 4:catch-all
  - name: 其他
    type: default
    destination: inbox
```

### 3.3 匹配优先级

按 rules 列表**自上而下**匹配,**第一个命中即停止**。catch-all 规则放最后。

### 3.4 热加载机制

- 程序启动时加载 rules.yaml
- FileSystemWatcher 监听 rules.yaml 文件变更
- 文件变更后 **2 秒内重载**(防编辑器保存中途读取)
- 重载失败时保留旧规则 + 弹气泡"规则加载失败,已保留旧版本"

### 3.5 手改友好性

- YAML 注释 `#` 开头
- 缩进 2 空格
- 字段名固定 6 个:type / patterns / extension / filename_keyword / destination / case_sensitive
- 错误信息带行号(用 YamlDotNet 内置报错)

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
│   └── TrayIcon.cs             # 托盘图标 + 菜单
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

---

## 5. 错误处理

| 场景 | 处理 |
|---|---|
| 规则 YAML 解析失败 | 保留旧规则 + 气泡通知 + 日志详情 |
| 目标目录不存在 | 自动创建(单层),多层失败则报错跳过 |
| 源文件被占用 | 跳过,记录日志,提示用户 |
| 目标已有同名文件 | 按 conflict_strategy(skip/overwrite/rename)处理 |
| 拖入文件夹 | 递归遍历内部文件,逐个匹配规则 |
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

---

## 9. 未来扩展(本期不做,留口子)

- 规则按时间段(如:本月 → 临时,更早 → 归档)
- 简单 GUI 配置面板(目前手改 YAML)
- 多设备同步(目前自己拷 YAML 到每台设备)

---

## 变更记录

- 2026-09-27 初版(M1 完成,待用户最终评审)
- 2026-09-27 修订:加入 §2.4 开机自启动(HKCU\...\Run)+ M9,移除原"不做开机自启动"项