# FileSorter v2 设计文档

> v1:基础文件分类器(28 commit, 16 单元测试, 公开仓库 teddy4556/filesorter)
> v2:**SendTo 修复 + Rule + Mappings 新概念 + GUI 规则编辑器**

设计者:hermes + 用户协作 | 日期:2026-09-27 | 状态:草稿待审

---

## 1. 需求来源(用户原话)

1. **"没有右键发送"** — 资源管理器右键 → 发送到 → 没有 FileSorter
2. **"一级二级目录可以手动拖动选择(类似滑块结构)"** — 新概念:可视化编辑目录层级映射
3. **"添加规则只有手动改"** — 想在软件上加规则 + **新规则类型**:`name_template`(命名模板) + 显式指定 token 映射到哪一级目录

---

## 2. 范围 & 非范围

### ✅ In scope

- A. **SendTo 集成**:自动创建 `%USERPROFILE%\SendTo\FileSorter.lnk`
- B. **QuickRuleDialog**:SendTo 触发后弹"快速规则选择"窗口
- C. **Rule + Mappings** 新 YAML 字段(`Rule.mappings[]`)
- D. **新规则类型**:
  - `filename_pattern` (starts_with / ends_with)
  - `name_template` (占位符 + 软件生成 regex)
- E. **GUI 规则编辑器**(RuleEditor.xaml):
  - 列表 + 添加/编辑/删除
  - **新概念 Mappings UI**(token → level 下拉)
  - 测试用例预览
- F. **YAML 序列化器扩展**:支持新字段往返

### ❌ Out of scope(显式不做)

- 文件内容匹配(**用户明确禁止**:"永远不读取文件内容")
- SQLite 替换 YAML(保持 YAML 为唯一真值)
- 重写 path_template(只加可视化编辑器,正则不动)
- 复杂的规则导入/导出 UI(纯 YAML 文件就是导入/导出)
- 多语言规则名(暂时只支持中文/英文 rule name)

---

## 3. SendTo 设计

### 3.1 注册机制

- 文件 Sorter 启动时检查 `%USERPROFILE%\SendTo\FileSorter.lnk`
- 不存在 → 创建(指向 `filesorter.exe` + `--sendto` 启动参数)
- 已存在 → 跳过
- 提供命令行参数 `--sendto <file1> <file2> ...` 让 SendTo 路径下快捷方式能用

### 3.2 QuickRuleDialog(弹窗行为)

- 触发:`filesorter.exe --sendto file1.txt file2.pdf ...`(多文件)
- 弹窗内容:
  ```
  ┌─────────────────────────────────────┐
  │ 选择分类规则                  [X]  │
  ├─────────────────────────────────────┤
  │ 文件:file1.txt, file2.pdf (2 个)   │
  │                                     │
  │ ● 按默认规则(全自动)               │
  │ ○ 社交媒体图片(platform>author)    │
  │ ○ 发票                              │
  │ ○ 截图                              │
  │ ○ 压缩包                            │
  │ ○ 图片                              │
  │ ○ 其他                              │
  │                                     │
  │ [确定]            [取消]            │
  └─────────────────────────────────────┘
  ```
- 选规则 → 对所有文件用该规则分类
- 选"默认规则"(全自动)→ 走默认全规则链(用户确认 9-27:此选项必要)
- 取消 → 不动文件
- 完成后弹通知:"已分类 2 个文件 → D:\示例\文档\file2.pdf"

### 3.3 命令行参数

```
filesorter.exe                          # 正常运行(托盘)
filesorter.exe --sendto file1 file2 ... # SendTo 路径调用
filesorter.exe --install                # 注册自启动(已有)
filesorter.exe --uninstall              # 注销自启动(已有)
filesorter.exe --install-sendto         # 单独装 SendTo 快捷方式
```

---

## 4. Rule + Mappings 新概念

### 4.1 动机

当前 `path_template` 字段:
```yaml
- name: 社交媒体
  type: path_template
  filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'  # ← 用户写正则
  path: '{destinations.images}\{1}\@{2}'      # ← 用户手写哪部分到哪级
```

用户痛点:
- (a) **必须懂正则**才能写
- (b) **必须手写 path 模板**,token 哪个是 1/2/3 容易错
- (c) **必须知道 group 编号语义**(`{1}` 是第几个括号?)

### 4.2 新方案:name_template + mappings

```yaml
- name: 社交媒体
  type: name_template
  extensions: [jpg, jpeg, png]
  template: "{platform}_@{author}_{title}_{date:yyyyMMdd}"
  mappings:
    - token: "{platform}"
      level: 1
    - token: "{author}"
      level: 2
  # ↑ 软件自动生成:
  # filename_pattern: ^([a-z]+)_@([^_]+)_([^_]+)_(\d{8})$
  # path: '{destinations.images}\{platform}\@{author}'
```

### 4.3 mappings 字段规则

- `mappings[]`:数组,每个元素:
  - `token`:占位符字符串,必须出现在 `template` 中
  - `level`:整数,**1=一级目录,2=二级目录,3+ 也支持多级**(用户确认 9-27:允许任意整数)
- **不写 mappings** → 软件用启发式:
  - level 1 = template 第 1 个 token
  - level 2 = template 第 2 个 token
  - 其它 token 都不进目录(只作为文件名一部分)

### 4.4 兼容性

- 旧 `path_template` 规则**继续生效**,不破坏 16 个现有测试
- GUI 编辑器**默认隐藏** path_template 高级模式,默认推荐 name_template
- YAML 文件**前向兼容**:旧文件加载到 v2 软件 OK,写回 OK(保留 path_template 字段)

---

## 5. 新规则类型

### 5.1 filename_pattern(替代/补充 filename_keyword)

**为什么加**:`filename_keyword` 是子串匹配(`if (name.Contains(keyword))`),但用户可能要更精确的边界匹配(`StartsWith` / `EndsWith`)。

**YAML 形式**:
```yaml
- name: acme 发票
  type: filename_pattern
  starts_with: ["acme_", "ACME_"]       # 任一匹配即可
  ends_with: ["_invoice.pdf"]            # 任一匹配即可
  case_sensitive: false                   # 默认 false
  extensions: [pdf]                       # 可选:同时限定扩展名
  destination: documents

- name: 命名规范
  type: filename_pattern
  starts_with: ["report_"]
  ends_with: [".md"]
  case_sensitive: true
  destination: documents

- name: 短文件名(无扩展名)
  type: filename_pattern
  ends_with: [""]                         # 空字符串=无扩展名?  或用 filename_pattern 正则模式
```

**GUI 字段**:
- `starts_with` (ListBox, 添加/删除)
- `ends_with` (ListBox, 添加/删除)
- `case_sensitive` (CheckBox)
- `extensions` (多选 ComboBox)

### 5.2 name_template(命名模板)

**完整 YAML**:
```yaml
- name: 社交媒体图片
  type: name_template
  extensions: [jpg, jpeg, png, gif, webp]
  template: "{platform}_@{author}_{title}_{date:yyyyMMdd}"
  case_sensitive: false
  destination: images            # 兜底:没匹配 mappings 时的目标
  mappings:
    - token: "{platform}"
      level: 1
    - token: "{author}"
      level: 2
```

**token 语法**:
- `{name}`:任意非分隔符序列(`[^_\-\s.]+`)
- `{name:type}`:带类型约束
  - `text` / `string`:`[a-zA-Z]+`
  - `int` / `number`:`\d+`
  - `date:yyyyMMdd` / `date:yyyy-MM-dd` / `date:HHmmss`:8 位数字 / 带横线日期
  - `any`:任何字符(贪婪)

> **用户确认 9-27**:`{}` 语法 OK,沿用。

**软件自动生成 regex**:
- 例:`{platform}_@{author}_{title}_{date:yyyyMMdd}`
- → `^([a-zA-Z]+)_@([a-zA-Z0-9]+)_([a-zA-Z0-9]+)_(\d{8})$`
- → `filename_pattern` 字段(可在 GUI "高级" 显示/编辑)

**软件自动生成 path**:
- 拼 `destinations.X` + 按 mappings level 拼目录
- 例:level 1 = platform, level 2 = author
- → `'{destinations.images}\{platform}\@{author}'`
- (注意 `@` 前缀仍然需要用户**显式**写在 template 里,软件不自动加)

### 5.3 完整规则类型表(v2)

| type | 来源 | v2 处理 |
|---|---|---|
| `extension` | v1 | 不变 |
| `filename_keyword` | v1 | 不变(子串匹配) |
| `filename_pattern` | **v2 新** | starts_with / ends_with 边界匹配 |
| `combined` | v1 | 不变(extension + keyword) |
| `path_template` | v1 | 不变(高级,正则) |
| `name_template` | **v2 新** | 占位符 + mappings |
| `default` | v1 | 不变(catch-all) |

---

## 6. GUI 规则编辑器

### 6.1 入口

- 托盘右键菜单新增:**"Edit rules…"** (现有 "Open rules.yaml" 旁边)
- 打开 `RuleEditor.xaml`

### 6.2 主窗口结构

```
┌──────────────────────────────────────────────────────┐
│ FileSorter - Rule Editor                        [X]  │
├──────────────────────────────────────────────────────┤
│ [+ Add] [Delete] [↑] [↓] [Test] [Save] [Reload]    │
│ ┌──────────────────────────────────────────────────┐ │
│ │ #│ Name              │ Type          │ Active  │ │
│ │ 1│ 社交媒体图片       │ name_template │   ☑    │ │
│ │ 2│ 截图               │ combined      │   ☑    │ │
│ │ 3│ 发票               │ filename_keyword│   ☑  │ │
│ │ 4│ 压缩包             │ extension     │   ☑    │ │
│ │ 5│ 图片               │ extension     │   ☑    │ │
│ │ 6│ 其他               │ default       │   ☑    │ │
│ └──────────────────────────────────────────────────┘ │
│                                                      │
│ Selected: 社交媒体图片                                │
│ ┌──────────────────────────────────────────────────┐ │
│ │ Type: [name_template ▾]                          │ │
│ │ Name: [社交媒体图片]                              │ │
│ │ Active: ☑                                         │ │
│ │ Extensions: [jpg][jpeg][png]  [+ Add]            │ │
│ │ Template: [{platform}_@{author}_{title}]          │ │
│ │ Destination: [images ▾]                          │ │
│ │                                                  │ │
│ │ ┌─ Mappings ────────────────────────────────┐   │ │
│ │ │ Token │ Level │  Action                    │   │ │
│ │ │ {platform} │ [1 ▾] │ [× Delete]            │   │ │
│ │ │ {author}   │ [2 ▾] │ [× Delete]            │   │ │
│ │ │ [+ Add mapping]                              │   │ │
│ │ └────────────────────────────────────────────┘   │ │
│ │                                                  │ │
│ │ Test input: [twitter_@hahaoy8_xxx.jpg]           │ │
│ │ → Result path: D:\示例\图片\Twitter\@hahaoy8    │ │
│ └──────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────┘
```

### 6.3 Type 切换的动态 UI

| Type | 显示字段 |
|---|---|
| `extension` | extensions 多选 |
| `filename_keyword` | patterns (ListBox) + case_sensitive + extensions |
| `filename_pattern` | starts_with + ends_with (ListBox × 2) + case_sensitive + extensions |
| `combined` | extensions + filename_keyword |
| `path_template` | filename_pattern (verbatim 字符串输入) + path (verbatim 字符串输入) |
| `name_template` | template + extensions + **mappings UI** |
| `default` | (空,只用 destination) |

### 6.4 Mappings UI 行为

- 添加 mapping:弹小对话框"选 template 里的占位符"(ComboBox 自动从 template 解析)+ Level 下拉(1/2/3)
- 删除 mapping:行内 [×]
- 拖动重排:暂不支持(简化)

### 6.5 测试功能

- 在 "Test input" 文本框输入文件名(只输入文件名,不带路径)
- 软件内部用当前编辑的 rule 跑匹配 + 生成 path
- 显示 "Result path" 预览
- **不实际移动文件**,纯计算
- **要支持 path_template + name_template 两种**,所以测试逻辑需要适配

### 6.6 保存/取消行为

- Save:写回 `%APPDATA%\FileSorter\rules.yaml`(用 PathsEditor 类似的原子写)
  - **写之前**:复制当前 `rules.yaml` 到 `rules.yaml.bak.YYYYMMDD-HHMMSS`(用户确认 9-27:每次 save 留备份)
  - **保留最近 3 份备份**(超过 3 份自动删最旧的)
  - 写流程:写 `.tmp` → `File.Replace(.tmp, rules.yaml)`(原子)
- Reload:从 rules.yaml 重新加载(放弃当前编辑)
- Cancel:关闭窗口(不保存)
- 关闭按钮 [X]:等同 Cancel,弹确认"有未保存的更改"

---

## 7. YAML Schema 扩展

### 7.1 新字段汇总

```yaml
# 顶级(无变化)
version: 1
default_action: move
conflict_strategy: rename
log_level: info

destinations:                       # 无变化
  images: D:\示例\图片
  # ...

rules:                              # 扩展
  - name: ...                       # 必须
    type: <see 5.3>                # 必须
    active: true                    # 新增(可选,默认 true),GUI 可切换
    destination: ...                # 现有,大多数 type 必填
    extensions: [...]               # 新增,可选(可多个 type 共用)
    mappings:                       # 新增,仅 name_template 用
      - token: "{name}"
        level: 1
```

### 7.2 向后兼容

- 旧 rules.yaml 加载到 v2 软件 → 100% 兼容
- v2 软件保存 → 保留所有字段,**可能** 添加新字段(`active: true`,但只在用户添加新 rule 时)
- v2 不**自动迁移**旧 path_template → name_template(用户可选)

### 7.3 序列化器修改

- `Rule` 模型加 `Active: bool = true`
- `Rule` 模型加 `Mappings: List<Mapping>? = null`
- 新增 `Mapping` 模型:`Token: string`, `Level: int`
- `[YamlMember(Alias = "...")]` 全字段映射(已有经验)
- 现有 16 个测试**全部要继续通过**

---

## 8. 数据流

```
GUI (RuleEditor)
  ↓ 用户点击 Save
RuleSerializer.Serialize(rules, destinations)
  ↓ 写 .tmp
File.Replace(.tmp, rules.yaml)   ← 原子写
  ↓
RuleWatcher 检测文件变化
  ↓
RuleEngine.LoadFromString
  ↓
通知已打开的悬浮窗/FloatingDisk 重新加载
```

---

## 9. 测试计划

| 测试 | 验证 |
|---|---|
| 单元:Rule 序列化新字段 | active + mappings 往返 OK |
| 单元:name_template 自动生成 regex | `{platform}_@{author}_{title}` → `^([a-zA-Z]+)_@([^_]+)_([^_]+)$` |
| 单元:path 自动生成(level 1+2) | mappings 排序 + path 拼接正确 |
| 单元:SendTo 启动参数解析 | `--sendto file1 file2` → List<string> |
| 单元:backup 轮转 | save 4 次 → 只有最近 3 份 .bak(最旧的删) |
| 集成:QuickRuleDialog 行为 | 选规则 → 走 ClassifierService |
| GUI:RuleEditor 切换 type | 字段动态显示/隐藏 |
| GUI:Mappings 添加/删除 | 列表更新,template 校验 |
| GUI:Test input 预览 | path_template + name_template 都工作 |
| E2E:SendTo 触发分类 | 实际移动文件到正确路径 |

**16 个现有测试必须 100% 通过**(零回归)。

---

## 10. 风险与缓解

| 风险 | 缓解 |
|---|---|
| v1 公开仓库 README 提到的 path_template 写法,用户可能没升级意识 | README 加 v2 章节说"v1 规则继续生效" |
| GUI 编辑器错误改坏 rules.yaml | 写之前备份 `rules.yaml.bak.YYYYMMDD-HHMMSS`(保留最近 3 份,超过自动删最旧的) |
| name_template 自动生成 regex 错位 | 单元测试覆盖 10+ 模板 + 默认 demo |
| SendTo 快捷方式被用户删了 | 每次启动检测并自动重建 |
| 多文件 SendTo(选规则)只跑一条规则 | UI 提示"该规则会应用于所有选中文件" |
| YAML 序列化破坏中文字符 | UTF-8 + 现有序列化器已验证 |

---

## 11. 里程碑

| M | 任务 | 估时 |
|---|---|---|
| **M14** | Rule 模型扩展(`Active` + `Mappings` 字段)+ 单元测试 | 30 min |
| **M15** | name_template 自动 regex 生成器 + 单元测试 | 1 h |
| **M16** | filename_pattern 类型(extension/filename_pattern)+ 单元测试 | 30 min |
| **M17** | QuickRuleDialog + SendTo 集成 + 启动参数 | 1.5 h |
| **M18** | RuleEditor 主窗口(列表/添加/编辑/删除/Save)+ save 时 backup 轮转(保留 3 份) | 2 h |
| **M19** | Mappings UI 子组件 | 1 h |
| **M20** | Test input 预览功能 | 1 h |
| **M21** | README + docs/design.md 同步 + plan 文档 | 30 min |
| **M22** | 端到端验证:SendTo + GUI 编辑器 + 分类 | 1 h |
| **M23** | dotnet publish + exe 验证 | 10 min |

总计 ~9 小时实际工作 = 派 1-2 个子代理(Phase D + Phase E)。

---

## 12. 不做的事(YAGNI)

- ❌ 规则导入/导出向导(YAML 文件本身就是导入导出)
- ❌ 规则分组/标签(rule 数量预计 < 50,不需要)
- ❌ 规则搜索/过滤(list 控件够用)
- ❌ 规则历史/版本控制(git 就是版本控制)
- ❌ 规则模板市场(自用工具)
- ❌ 暗色主题(可后续加)
- ❌ 文件内容预览
- ❌ 多语言(中英 rule name 已经支持)

---

## 13. 验收标准

1. ✅ `dotnet test` **17+ / 17**(16 现有 + 至少 1 新)
2. ✅ `dotnet build` 0 错误
3. ✅ `dotnet publish` 产出 `dist\filesorter.exe` < 2 MB
4. ✅ 实际跑 exe:
   - 托盘出现(已有)
   - 右键 → Edit rules… 弹 GUI 窗口
   - 加 1 条新 name_template 规则,Save
   - 拖个 jpg → 走新规则分类(按 mappings level 1/2 分级)
   - SendTo:右键 jpg → 发送到 FileSorter → 弹 QuickRuleDialog → 选规则 → 文件移动
5. ✅ README 更新 + docs/design.md 加 v2 章节
6. ✅ GitHub push(28 commit → 35+ commit)