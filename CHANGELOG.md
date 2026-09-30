# Changelog

所有 notable 改动记录在这里。版本号遵循 [Semantic Versioning](https://semver.org/)。

> **🎯 速览**:v2.7.11 是当前版本(2026-09-29),含 Pruner v6"删了就是删了"+ FloatingDisk DPI-aware + NTC unicode-dash 扩展 + RuleEditor 自动派生 alias。
>
> 历史 → [git log](https://github.com/teddy4556/filesorter/commits/main)

---

## [Unreleased] — 待发布

待开发功能待定。

---

## [2.7.11] — 2026-09-29

### 🚀 新增 / 功能

- **`DestinationPruner` v6**("删了就是删了"):PASS 0/2 移除,PASS 3 唯一条件 `!referenced.Contains(key) → Remove`。无 `inbox` 保留,无 `userDeclaredKeys` 例外。`ClassifierService` 无规则命中返回 `false`(不再回退到 inbox)。
- **`RuleEditor.OnAdd` 自动派生 alias**:新增规则时不再硬编码 `Destination="inbox"`(v6 已删),而是 `MakeUniqueAlias` + `cfg.Destinations[alias] = Documents\FileSorter\alias` + 同步创建目录。
- **`FloatingDisk` 150% DPI 可拖动**:新增 `app.manifest`(`<dpiAwareness>PerMonitorV2</dpiAwareness>`)+ csproj `<ApplicationManifest>` 引用 + `SetProcessDpiAwarenessContext(-4)` Win32 调用。
- **`FloatingDisk.xaml` hit-test 修法**:`<Image IsHitTestVisible="False">` + `<Border Background="#01000000">`(1/255 alpha,**不**用 `Transparent` 关键字 — 那是 null Brush 不接受 hit-test)。
- **NTC `unicode-dash` 字符类扩展(v2.7.11)**:加 `\p{Po}`(OtherPunctuation)覆盖 `＊` (U+FF0A) 等;显式加入装饰字符 `★☆♥❤❀❄✿✦✧`。
- **`PathsEditor.SavePaths` 调用 Pruner**:EditPaths 保存时也清理未引用的 alias(原来只有 RuleEditor 保存会跑 Pruner,EditPaths 写 yaml 时跳过导致 orphan alias)。

### 🐛 修复

- **Bug #59**:`twitter#(@chacooooo0s)#Belle＊⑅♥#...jpg` 未命中 Twitter rule — `＊` U+FF0A 属 `OtherPunctuation` 类,不在原字符类。修法见 v2.7.11 NTC 扩展。
- **Bug #60**:EditPaths 窗口删除路径后 alias 残留 — `PathsEditor.SavePaths` 没调 Pruner。
- **Bug #61**:FloatingDisk 鼠标拖动不响应(150% DPI)— 见 DPI 修法。
- **Bug #62**:用户删掉的 rule 对应 alias 还显示在 EditPaths 窗口 — Pruner v6 PASS 3 修。
- **Bug #63**:新建规则后 Match 永远不命中 — v6 删了 inbox,但 `OnAdd` 仍 `Destination="inbox"`。

### ⚠️ 行为变更

- `destinations.inbox` 会被 Pruner 自动删除,**除非**有规则 `destination: inbox` 引用它。
- `default_action: ...` 顶层字段不再影响"未命中时回退到 inbox"行为 — 现在未命中时文件**不动**。

### 🧪 测试

- 新增 `tests/FileSorter.Tests/DestinationPrunerTests.cs`(Pruner v4/v5/v6 行为断言)。
- 4 个老 Pruner 测试自动 pass(行为变更但 assertion 同步更新)。
- 1 个新测试 `Prune_Removes_Inbox_When_No_References` 验证 inbox 删除。

### 📦 部署验证

- `dist\filesorter.dll` SHA `A2FAC7DE90D5FD93` (v2.7.11 NTC v6 Pruner v6 RuleEditor.OnAdd)
- `dist\filesorter.exe` SHA `854BA381` (DPI-aware apphost,UTF-8 grep `dpiAwareness` 确认)

---

## [2.7.10] — 2026-09-29 (superseded by 2.7.11)

> 初版尝试 NTC 加全角中文标点,后被 2.7.11 覆盖(unicode-dash 类扩展更彻底)。

---

## [2.6] — 2026-09-27

### 🚀 新增

- **NTC unicode-dash 字符类扩展(Bug #47 + #48)**:加 `\p{S}`(Symbol)`\p{M}`(Mark)`\p{Cs}`(Surrogate) 接受 emoji + 符号 + 全角括号 + 标记字符。

### 🐛 修复

- **Bug #47**:Twitter user_name 含 emoji(如 `🌸樱花`)不匹配。
- **Bug #48**:Twitter user_name 含全角括号 `（）` 不匹配。

### 🧪 测试

- unicode-dash 测试覆盖 emoji + 全角括号 + 标记字符。

---

## [2.4] — 2026-09-27

### 🚀 新增

- **Explorer 风格 `-N_N_N` dedup 后缀**(Bug #46):冲突文件按 Windows Explorer 行为命名(`file.jpg` → `file-2.jpg` → `file-3.jpg`),而非 `_1` `_2` 后缀。

### 🐛 修复

- **Bug #46**:原 `_1` 后缀跟某些工具不一致;改用 `-N_N_N` 跟 Explorer 对齐。
- 3 个新测试 + 3 个 hardcode 修正。

---

## [2.3] — 2026-09-27

### 🚀 新增

- **Unicode / Chinese / CJK token 类型**:NTC 支持 `{user_name:unicode}` `{name:chinese}` `{name:cjk}` 等显式字符类。
- **`OrderMattersTests` + `using` fix**:Instagram rule 在 generic `图片` rule 之后会被短路,测试覆盖顺序敏感场景。

---

## [2.2] — 2026-09-27

### 🐛 修复

- **`RuleEngine.MatchNameTemplate` 剥 `.jpg` 扩展名后再 match**:regex `^...$` 永远不匹配带扩展名的文件名,Instagram 文件名命中失败的根因。
- 合并 `(N)` 后缀:Instagram 文件名末尾的 `(1)` `(2)` 被剥除后再匹配。

### 🧪 测试

- 2 个新测试覆盖真实 Instagram 文件名场景。

---

## [2.1] — 2026-09-27

### 🚀 新增

- **NTC `any` 类型支持 `.` `_` `-`**:Instagram username(如 `user.name_123-foo`)能被 token 字符类匹配。
- **`MakeListBoxRow` add button 修复**:Add Row 不再丢数据。

---

## [2.0] — 2026-09-27

### 🚀 新增 / 功能

#### 规则引擎扩展
- **`name_template` 规则类型**:语义模板,自动编译成 regex。
  - 例:`{platform}_@{author}_{date:yyyyMMdd}.{ext}` → 自动编译为 `^([a-z]+)_@([^_]+)_(\d{8})\.([^.]+)$`
  - 支持 token 类型: `{word}` `{date}` `{date-time}` `{number}` `{unicode-dash}` `{filename}` `{stem}` `{ext}`
- **`filename_pattern` 规则类型**:`starts_with` / `ends_with` 边匹配 + 多扩展名。比 `combined` 更轻量。
- **`mappings` 字段**:`name_template` 可声明 `{token, level}` 控制多级目录生成。

#### GUI 规则编辑器
- **托盘菜单 → Open rules…**:可视化编辑规则(增删改 + 重排)。
- **按 rule.type 动态显示字段**:extension / filename_pattern / path_template / name_template 各自专属字段。
- **Mappings UI**:token 下拉框(从模板自动抽取)+ level 下拉框(1-5)+ Add/Delete 行。
- **实时预览**:底部"测试输入"框,实时显示分类路径(走完整 `RuleEngine.Match` 流水线)。
- **保存时自动留 backup**:保留最近 3 份 `rules.yaml.bak.NNN`。

#### Active 开关
- 单条规则可临时禁用(不删):`active: false` 在加载和编辑里都保留。

#### SendTo 集成
- **`--install-sendto`**:自动写 `%APPDATA%\Microsoft\Windows\SendTo\FileSorter.lnk`。
- 资源管理器右键 → 发送到 → FileSorter 触发分类。

### 🧪 测试

- 33 个新 v2 测试(共 49 个)。

### 📦 部署验证

- `dist\filesorter.exe` 单文件 1.07 MB,53/53 tests pass。

---

## [1.0] — 2026-09-27

### 🚀 初始版本

#### 规则类型
- **`extension`**:按扩展名分类。
- **`filename_keyword`**:子串匹配文件名关键字。
- **`combined`**:扩展名 + 关键字同时满足。
- **`path_template`**:正则抽取 + 多级目录(`path: '{destinations.X}\{1}\{2}'`)。

#### 核心引擎
- `RuleEngine.Match` 顺序匹配(自上而下,第一条胜出)。
- `ClassifierService` 实现 `move` / `copy` / `dry-run` + 三种冲突策略(`rename` / `overwrite` / `skip`)。
- `RuleWatcher` YAML 2 秒防抖热加载,parse error 时保留旧规则。
- `RulesFileWriter` 原子写 + 3 份 backup 保留。

#### UI
- **FloatingDisk** 悬浮圆盘拖入(原版未做 DPI-aware,v2.7.11 修)。
- **TrayIcon** + 中文菜单("开机启动"、"打开规则"、"打开路径"、"退出")。
- **EditPathsWindow** 最小化路径编辑 GUI。
- **FolderModeDialog**:拖入文件夹时弹"递归 / 顶层 / 取消"三选项。
- **AutoStart**:`HKCU\Run\FileSorter` 注册表项 + `--install` / `--uninstall`。

#### YAML 兼容
- `YamlDotNet 15.x` round-trip。
- `WithCaseInsensitivePropertyMatching` 修小写 YAML 字段不匹配。
- 每次 `LoadFromString` 重建 `Deserializer`(规避 alias + Dictionary 状态污染)。

### 🧪 测试

- 16 个 v1 单元测试。

---

## 类型参考

- 📖 完整规则示例 → [`examples/rules.yaml`](./examples/rules.yaml)
- 🏗 架构设计 → [`docs/design.md`](./docs/design.md)
- 📋 README → [`README.md`](./README.md)
- 🔧 构建/部署踩坑 → Hermes skill `filesorter-windows-build` (用户私有)

---

## 版本命名约定

- **Major (1.x → 2.x)**:规则类型变更 / 引擎架构改动 / YAML schema breaking change。
- **Minor (x.1 → x.2)**:新规则字段 / 新 UI 功能 / 新命令。
- **Patch (x.x.1 → x.x.2)**:bug 修复 / 字符类扩展 / 测试补充。

每个版本号对应一个 git commit(commit message 前缀 `v2.7.11:`),详情见 [git log](https://github.com/teddy4556/filesorter/commits/main)。
