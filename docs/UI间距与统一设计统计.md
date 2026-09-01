# WPF UI 间距与统一设计统计

> 审计范围：`src/DiagnosticFlashTool.App` 下全部 WPF XAML（页面、复用控件、资源字典）。
> 单位：WPF device-independent pixel（通常可视为 px）。除非特别注明，值来自 `StaticResource` 样式继承；“显式覆盖”表示页面 XAML 在控件上直接设置。
> 说明：`*` 表示按剩余空间比例分配，`Auto` 表示按内容/控件期望尺寸分配，不是固定像素。

## 1. 全局布局基线

| 区域/令牌 | 值 | 来源与说明 |
|---|---:|---|
| 页面根外边距 `PageMargin` | 24 四边 | `PageRootGridStyle` 继承 |
| 页面页头行 `PageHeaderRowStyle` | 高 88 | 所有 11 个页面统一 |
| 页头后间距 `PageHeaderGapRowStyle` | 高 12 | 所有页面统一 |
| 页面区块间距 `PageSectionGapRowStyle` | 高 12 | 多卡片页面使用 |
| 页面列间距 `PageColumnGapStyle` | 宽 12 | 两列/三列布局使用 |
| 页面摘要行 `PageSummaryRowStyle` | 高 122 | 固件刷写、刷写监控 |
| 表单标签列 `FormLabelColumnStyle` | 宽 90 | 标签右对齐，样式 `FieldLabelStyle` 另有右边距 12 |
| 页面卡片底部间距 `PageCardBottomGap` | 0,0,0,12 | `PageLeadStandardCardStyle` |
| 页面卡片顶部间距 `PageCardTopGap` | 0,12,0,0 | `PageTrailingCompactCardStyle` |
| 内容顶部/底部令牌 | `0,12,0,0` / `0,0,0,12` | `PageContentTopGap` / `PageContentBottomGap` |
| 标签后辅助文字偏移 | `90,0,0,0` | `FormAssistTextAfterLabelMargin`，与标签列对齐 |

## 2. 统一控件与模板

### 2.1 卡片、面板与标题

| 样式/模板 | 尺寸、内外边距、位置规则 | 统一程度 |
|---|---|---|
| `CardStyle` | 背景面板色；边框 1；圆角 8；默认阴影；悬停使用增强阴影 | 统一模板 |
| `Card.Compact` | 继承 `CardStyle`，内边距 14（四边） | 统一模板，表格/紧凑内容 |
| `Card.Standard` | 继承 `CardStyle`，内边距 18 | 统一模板，标准内容 |
| `Card.Elevated` | 继承 `CardStyle`，固定增强阴影 | 统一模板，页头 |
| `DialogPanelStyle` | 继承卡片，内边距 24，圆角 8 | 统一模板 |
| `PageHeaderCardStyle` | 继承 `Card.Elevated`，左右内边距 22 | 统一模板 |
| `PanelHeaderRowStyle` | 标题行高 50 | 统一模板 |
| `PanelHeaderStyle` | 左右内边距 22（`CardHeaderContentPadding`），无边框 | 统一模板 |
| `PanelContentGridStyle` | `Margin=22,12,22,12` | 统一模板 |
| `PanelContentGroupStyle` | 继承设置组，外边距 `22,12,22,12`，内边距 `12,10` | 统一模板 |
| `CardHeaderTextStyle` | 字号 16、半粗、垂直居中 | 统一模板 |
| `CardContentTitleTextStyle` | 继承卡片标题，下边距 10 | 统一模板 |
| `CardContentTitleGapRowStyle` | 行高 10 | 统一模板 |
| `FieldLabelStyle` | 字号 12、右对齐、右边距 12、垂直居中 | 统一模板 |
| `SettingGroupPanelStyle` | 内边距 14；边框 1；圆角 6 | 统一模板 |

### 2.2 按钮与图标

| 样式 | 高/宽 | Padding / Margin | 备注 |
|---|---:|---|---|
| `Button.Base` | 高 36 | `Padding=16,0`；`Margin=0,4,8,4` | 所有普通按钮基线，圆角 6 |
| `Button.Normal` | 继承基线 | 同上 | 默认隐式 `Button` 也继承此样式 |
| `Button.Primary` / `Button.Danger` | 高 36 | 继承基线 | 主操作/危险操作颜色令牌 |
| `Button.ToolBar` | 高 32 | `Padding=12,0`；`Margin=0,0,8,0` | 工具栏按钮 |
| `Button.Compact` | `72 x 32` | `Padding=8,0`；`Margin=8,0,0,0` | 紧凑操作 |
| `Button.ConnectionAction` | 高 32 | `Padding=12,0`；`Margin=8,0,0,0` | 固件页连接按钮 |
| `Button.ConnectionPrimaryAction` | 高 36 | `Padding=14,0`；`Margin=0` | 系统设置连接按钮 |
| `Button.HelpDocument` | 宽 112、高 32 | `Margin=0` | 页头帮助按钮 |
| `Button.PathAction` | 高 30，最小宽 52 | `Padding=8,0`；`Margin=6,0,0,0` | 路径打开/选择 |
| `Button.DialogSecondary` | `78 x 32` | `Margin=0,0,8,0` | 对话框取消 |
| `Button.DialogPrimary` | 高 32 | `Margin=0` | 对话框确认 |
| `Button.Icon` | `36 x 36` | `Padding=0`；`Margin=0` | 纯图标按钮 |
| `BaseChromeButtonStyle` | `46 x 46` | 继承图标按钮 | 窗口控制 |
| `Button.PackIcon` | `18 x 18` | 居中 | `Current` 覆盖为 `16 x 16` |
| `Button.IconLabel` | 内容字号继承 | 左边距 8 | 图标与文字间距 |
| `Button.PathActionLabel` | 内容字号继承 | 左边距 5 | 路径按钮图标与文字间距 |
| `Button.InlineContent` | 横向 StackPanel | 垂直居中 | 图标+文字统一模板 |

补充资源：`Button.PackIcon.Accent` / `Button.PackIcon.Inverse` 只覆盖图标前景色；`Button.IconLabel.Inverse` 只覆盖文字前景色；`Button.PathActionIcon` 继承当前按钮前景色图标；`ChromeButtonStyle` 与 `CloseChromeButtonStyle` 继承 `BaseChromeButtonStyle`，均不改变 `46 x 46` 的基线。

### 2.3 输入、开关、进度与表格

| 控件/样式 | 尺寸与间距 |
|---|---|
| `SoftTextBoxStyle` | 高 36；`Padding=10,4`；`Margin=0`；边框 1；圆角 6 |
| 隐式 `TextBox` | 高 30；`Margin=0,4,0,8`；`Padding=8,4` |
| `PathTextBoxStyle` | 继承软输入框；高 32；只读 |
| `PathDisplayBorderStyle` | 高 32；`Padding=10,0`；边框 1；圆角 6 |
| `StandardComboBoxStyle` | 默认 `280 x 32`；`Padding=10,0,34,0`；`Margin=0`；下拉最大高 260 |
| `StandardComboBoxItemStyle` | 最小高 30；`Padding=10,5`；模板内部边距 `2,1`；圆角 4 |
| `SoftSwitchStyle` | `44 x 24`；轨道圆角 6；滑块 `18 x 18`，外边距 3 |
| `SoftProgressBarStyle` | 高 24；轨道/指示器圆角 12 |
| `InlineStatusValueBorderStyle` | 高 32；`Padding=10,0`；边框 1；圆角 6 |
| `DataGrid.Base` | 行高 40；表头高 38；行头宽 0；字号 13；单行选择 |
| DataGrid 表头 | 左内边距 10；下/右边框 1 |
| DataGrid 单元格 | 左右内边距 10；底边框 1 |
| `FunctionCheckDataGridStyle` | 继承表格基线，网格线改为 All，只读 |
| `LogListBoxStyle` | `Padding=12,10`；列表项 `Padding=0,4`；横向内容拉伸 |
| `LogTextBoxStyle` | `Padding=12,10`；等宽 Consolas；自动换行/滚动 |

### 2.4 状态与文字令牌

| 样式 | 值 |
|---|---|
| 隐式 `TextBlock` | 字号 13、垂直居中、正文色 |
| `PageTitleTextStyle` | 字号 22、半粗 |
| `PageSubtitleTextStyle` / `HintTextStyle` | 字号 13；辅助色；页头副标题上边距 10 |
| `SettingAssistTextStyle` | 继承提示文字，字号 12、透明度 0.78 |
| `ConfigLabelTextStyle` | 字号 12、右对齐 |
| `ConfigValueTextStyle` | 继承数值文字，字号 13、行高 19.6 |
| `PageSummaryContentStyle` | 左右外边距 18，垂直居中 |
| `StatusCardPackIconStyle` | `28 x 28` |
| `StatusBadgeBorderStyle` | `Padding=10,4`；圆角 4；边框 1 |
| `StatusDotStyle` | `8 x 8` |
| `StatusValueTextStyle` | 中等字重，颜色按状态枚举触发 |

模板资源 `SoftSwitchTemplate`、`StandardComboBoxTemplate` 和 `StandardComboBoxItemStyle` 分别承载开关、下拉框和选项的视觉结构；`CardShadowEffect` / `CardElevatedShadowEffect` 承载普通/抬升卡片阴影，页面不直接重复定义这些效果。

### 2.5 复用控件、壳层与颜色令牌

| 对象 | 内部布局与间距 |
|---|---|
| `PageHeader` | 外层 `PageHeaderCardStyle`；内部 Grid 为标题区 `*`、动作区 `Auto`。标题/副标题纵向 StackPanel 垂直居中；标题字号 22，副标题字号 13、上边距 10；副标题为空时折叠。右侧动作内容右对齐、垂直居中。 |
| `PageHelpDocumentButton` | 控件本身右对齐、垂直居中；内部 `Button.HelpDocument` 为 `112 x 32`、无外边距；图标实际 `16 x 16`，文字左边距 8。 |
| `ShellContentTabControlStyle` | 无边框；内容 Presenter 只呈现当前 Tab 的 `SelectedContent`，页面自身负责 24 的外边距。 |
| `ShellNavGroupTextStyle` | 宽 256、高 28；外边距 `0,14,0,4`；内边距 `22,0,16,0`。 |
| `ShellNavRadioButtonStyle` | 宽 256、高 42；模板列 `4 / 26 / 34 / *`；图标 `18 x 18`，活动轨为第 0 列宽 4。 |
| `SettingToggleTitleTextStyle` | 字号 13、半粗、右对齐；右边距 10。当前页面未直接引用，保留为可复用设置标题样式。 |
| `SoftComboBoxStyle` | `StandardComboBoxStyle` 的别名。当前页面使用标准下拉样式，未直接引用此别名。 |

颜色只通过角色资源引用，不在业务页面硬编码。`Brushes.xaml` 为下表 `Colors.xaml` 令牌提供语义画刷别名：

| 角色 | 颜色令牌和值 |
|---|---|
| 背景与边框 | App `#F6F8FB`；Card `#FFFFFF`；Surface `#F8FAFC`；Card/Divider `#E5EAF1`；Input `#D7DEE8`；Primary Soft Border `#B8CAE5` |
| 主操作 | Primary `#3568B8`；Hover `#2E5B9E`；Pressed `#274D86`；Accent `#6F95C8`；Soft `#EDF3FC`；Soft Pressed `#DCE7F6`；Navigation Brand `#3F67A3` |
| 文本 | Primary `#334155`；Card Title `#3B4A5F`；Secondary `#475569`；Label `#64748B`；Muted `#8A97A8`；Disabled `#B6C0CC`；Inverse `#FFFFFF` |
| 图标 | Default `#64748B`；Primary `#3568B8`；Success `#16A34A`；Warning `#D97706`；Error `#DC2626`；Muted `#94A3B8` |
| 侧栏 | Background `#182335`；Hover `#202D42`；Active `#4D3F67A3`；Active Rail `#6F95C8`；Active Icon `#C6D5E8`；Text `#A8B3C4`；Muted Text `#7F8CA1` |
| 表格与日志 | Table Header/Alternate `#F8FAFC`；Progress Track `#E5EAF1`；Log Panel `#0F172A`；Log Hover `#111827`；Log Selected `#1E293B`；Log Text `#CBD5E1` |
| 状态 | Success `#16A34A` / Soft `#DCFCE7` / Border `#86EFAC`；Warning `#D97706` / Soft `#FEF3C7` / Border `#FCD34D`；Error `#DC2626` / Soft `#FEE2E2` / Border `#FCA5A5`；Neutral Soft `#F6F8FB` / Border `#D7DEE8` |

`Status*Style` 按 `DiagnosticStatusKind` 切换上述状态画刷；状态信息始终同时以文字、图标或状态点表达，不仅依赖颜色。MaterialDesignThemes 5.3.0 在当前项目中主要提供 `PackIcon` 枚举和图标内容，尺寸、间距与按钮模板由本项目资源字典定义。

## 3. 页面逐项间距与位置清单

以下“卡片内”均以卡片左上角内容区域为参照；未列出的属性遵循第 2 节样式继承。

### 3.1 `FirmwareFlashView.xaml` 固件刷写

- 根：`PageRootGridStyle`，外边距 24；行依次为页头 88、间距 12、主体 `*`、间距 12、摘要 122。
- 主体（第 2 行）：三列 `1* / 12 / 1*`，左右两张卡片等宽，列间 12。
- 左卡“设备配置”：`CardStyle`；标题行高 50；标题左右 22；内容 `Margin=22,12,22,12`。
  - 内容行：高 74 + 间距 14 + 当前配置 `Auto` + 余量 `*`。
  - 设备状态区内两行 `32 / 10 / 32`；每行标签列 90，值列 `*`。
  - 第二行值列后接连接按钮，按钮显式宽 112，样式 `Button.ConnectionAction`（高 32、左边距 8）。
  - “当前配置”标题为 `SectionHeaderTextStyle`；四个字段采用 `Auto / 10 / Auto / 10 / ...` 行间距；标签显式覆盖 `Margin=0,0,16,0`，值为 `ConfigValueTextStyle`。
- 右卡“BTMS 刷写监视器”：同样标题 50、内容边距 22/12；状态区高 74、下方间隔 14。
  - 两张状态卡各 `144 x 74`，第一张右边距 12；图标样式目标 28 x 28（页面同时显式 24 x 24，属于页面显式覆盖）。
  - 图标与徽章间距 6；徽章 `Padding=10,4`。
  - 下方摘要列 `144 / 12 / 144`；标题字号 12，值上边距 3。
- 底部“下载进度”卡：摘要行高 122；标题 50；内容 `Margin=22,12,22,12`；进度条高 24，进度文字字号 16 居中覆盖。

### 3.2 `FlashMonitorView.xaml` 刷写监控

- 根：外边距 24；页头 88 + 12；摘要行 122；摘要与表格间 12；表格占余量。
- 摘要区三列 `1* / 12 / 1* / 12 / 1*`；每张 `PageSummaryCardStyle` 无额外内边距，内容 StackPanel 左右 18、垂直居中。
- 每张摘要卡内部：标签；主状态值上边距 10、字号 22；次级文字上边距 6。进度卡的进度网格使用 `PageContentTopGap=0,12,0,0`，进度条高 24。
- 底部 DataGrid：`Card.Compact` 内边距 14；表格基线行高 40、表头 38。列宽 `Time=120, Dir=70, Ch=70, ID=140, DLC=70, Data=*`。

### 3.3 `FlashHistoryView.xaml` 历史记录

- 根：外边距 24；页头 88 + 12；主体 `*`。
- 主卡 `Card.Standard`，内边距 18。
- 空状态 Border 页面显式：内边距 20、边框 1、圆角 8，填充 Surface；居中 StackPanel。
- 空状态标题 `EmptyStateTitleTextStyle`（字号 22）；说明文字上边距 8，水平居中。

### 3.4 `FunctionCheckView.xaml` 功能检测

- 根：外边距 24；页头 88 + 12；配方卡固定高 120；间距 12；检测卡占余量。
- 页头动作区含主题按钮（高 36、右边距 12）及三个窗口控制按钮 `46 x 46`，均为页面专用组合。
- 配方选择卡：标题行 50、标题左右 22；内容 `Margin=22,12,22,12`，标签列 90，选择列显式宽 300，ComboBox 显式宽 270（左对齐），剩余列 `*`。
- 检测卡：标题行 50；内容边距 22/12；内部行 `Auto / * / 44`，底部操作区高 44。
- DataGrid 使用 `FunctionCheckDataGridStyle`（行 40、表头 38、全网格、只读）；列宽 `序号=100, 检测项=300, 期望值=130, 当前值=130, 检测日期=200, 检测结果=*`；单元格文字样式左右外边距 10。
- “开始检测”按钮页面显式 `130 x 36`，右下对齐，样式 `Button.Primary`。

### 3.5 `ProjectConfigView.xaml` 项目配置

- 根：外边距 24；页头 88 + 12；主体 `*`。
- 页头操作：Add `80`、Delete `80`、Save `90`；均横向排列，工具栏按钮间默认右边距 8（Save 仍继承主按钮基线，页面未额外改 Margin）。
- 主卡 `Card.Compact`（内边距 14）包住 DataGrid；表格基线行高 40、表头 38、左右内边距 10。
- 列宽：`Project=160, No=70, Baud=80, Physical ID=130, Functional ID=130, Response ID=130, BOOT=120, Driver file=220, Application file=*`。
- 页面显式启用不可新增/删除、列头显示、水平网格；其余遵循 `DataGrid.Base`。

### 3.6 `RecipeConfigView.xaml` 配方配置

- 布局与 `ProjectConfigView` 完全相同：根外边距 24、页头 88 + 12、主卡 `Card.Compact` 内边距 14。
- 页头操作宽度 `80 / 80 / 90`，按钮间距遵循工具栏/主按钮样式。
- DataGrid 列宽同样为 `160 / 70 / 80 / 130 / 130 / 130 / 120 / 220 / *`；页面未定义独立配方列模板，属于复用项目表格模板。

### 3.7 `FlowConfigView.xaml` 流程配置

- 根：外边距 24；页头 88 + 12；工具卡 `Auto`；表格 `*`；底部验证卡固定高 110。
- 工具卡 `PageLeadStandardCardStyle`（继承 `Card.Standard`，内边距 18，底部间距 12），内部 DockPanel 左右两组。
  - 左组：标签右边距 8；ComboBox 显式宽 180；按钮 `Load=80, Validate=90, Save=90`；工具栏按钮默认右边距 8。
  - 右组：`Add Step=100, Delete=86, Up=70, Down=70`；横向排列，最后按钮仍可能继承工具栏右边距 8。
- 中部 DataGrid `Card.Compact`；列宽 `ID=60, Name=180, Step Type=140, Service=90, Sub=80, Extend=140, Addressing=100, Security=140, CRC=120, Timeout=90, Pending=90, Algorithm Params=*`。
- 验证卡 `PageTrailingCompactCardStyle`（内边距 14，顶部间距 12，固定高 110）；标题 `CardContentTitleTextStyle` 下边距 10；日志框 `LogTextBoxStyle`，内边距 12/10。

### 3.8 `AlgorithmConfigView.xaml` 算法配置

- 根：外边距 24；页头 88 + 12；主体 `*`。
- 页头副标题绑定脚本根路径；右侧元信息 `PageHeaderMetaTextStyle`（字号 13、半粗）。
- 主卡 `Card.Compact` 内边距 14，DataGrid 只读；基线行高 40、表头 38。
- 列宽：`Category=150, Name=180, Status=110, Source=*`；单元格左右内边距 10。

### 3.9 `DeveloperOptionsView.xaml` 开发者选项

- 根：外边距 24；页头 88 + 12；内容区先为固定高 112 的刷写配置卡，再间距 12，再占余量主体。
- 刷写配置卡：标题行 50；内容边距 22/12；标签列 90，ComboBox 列 `*`（页面显式 `Width=Auto`、拉伸），列间固定 12，开始刷写按钮显式宽 118、样式 `Button.Primary`。
- 主体左右比例 `7* / 12 / 13*`，中间间距 12（星号为比例而非像素）。
- 左列：上部下载进度卡固定高 96（标题 50、内容边距 22/12、进度条 24）；下部下载信息卡占余量，标题 50；内容使用 `PanelContentGroupStyle`（外边距 22/12，内边距 12/10），内嵌 TextBox 页面显式 `Height=Auto, Margin=0, Padding=0`。
- 右列：上部报文发送卡固定高 148；下部报文接收卡占余量；两卡间 12。
  - 发送卡标题 50；内容边距 22/12；两行 `32 / 10 / 32`。
  - 第一行列结构：标签 `Auto`，下拉 `104`，间隔 16，标签 `Auto`，下拉 `104`，间隔 16，标签 `Auto`，下拉 `104`，尾列 `*`。三个 ComboBox 均页面显式拉伸覆盖默认 280 宽。
  - 第二行：标签 `Auto`，ID 输入 `104`，间隔 16，数据标签 `Auto`，数据输入 `*`，间隔 12，发送按钮 `90`。输入框高 32、Margin 0；发送按钮高 32、Margin 0。
  - 接收卡标题行仍高 50；标题右侧按钮宽 `98 / 126 / 104`，前两个沿用工具栏右边距 8，导出按钮页面显式 Margin 0；DataGrid 使用 `Margin=22,12,22,12`（`PanelContentMargin`）、只读、全网格；列宽 `方向=110, ID=140, 格式=140, 长度=100, 数据=*`。
- 该页的 7:13 比例、固定卡高、104/90 等列宽均为页面显式自定义，非通用模板。

### 3.10 `SystemSettingsView.xaml` 系统设置

- 根：外边距 24；页头 88 + 12；内容为垂直 ScrollViewer，卡片顺序固定，卡片均 `PageLeadStandardCardStyle`（标准内边距 18，底部间距 12）。
- “基础设置”卡：标题 `CardContentTitleTextStyle`（下边距 10）；内容两列 `* / 24 / *`，外层底部 8。每列内部标签列 90、开关 `44 x 24`、开关后间距 12、状态辅助文字字号 12。
- “CAN 连接”卡：标题/状态/按钮行与内容行之间使用标题间隙 10；状态徽章最小宽 176，内边距 10/4；状态点 `8 x 8`，点与文字页面显式间距 8；连接按钮宽 126、高 36、无外边距。
  - 下方两组设置列 `* / 24 / *`；每组 `SettingGroupPanelStyle` 内边距 14、边框 1、圆角 6。
  - 设备设置组：标题后 12；普通行间距 10；三行分别为设备类型下拉、自动连接开关（开关后间距 14）、设备通道下拉；标签列 90。
  - 波特率组：标题后 12；自动搜索行与波特率行间 10；提示行与波特率行间 6；提示使用 `FormAssistTextAfterLabelMargin=90` 对齐标签列。
- “运行配置管理”卡：标题操作区使用 `CardContentHeaderGridStyle`（标题下边距 10）；导入按钮高 32、右边距 8，清空按钮高 32、Margin 0。三条路径行，前两条底部间距 8；标签列 90，路径框高 32，路径操作统一 `Button.PathAction`（高 30、左边距 6、最小宽 52）。
- “日志存储”卡：标题下边距 10；前两行均 `PageContentBottomGap`（底部 12）；保存开关后间距 12；日志文件路径操作沿用路径按钮；数值行两组之间固定间隔 24，数值输入框 `64 x 32`，单位间隔 4。
- “管理员模式”卡：网格列 `90 / Auto / 12 / Auto / 12 / *`；标题行后 `CardContentTitleGapRowStyle=10`；开关 `44 x 24`；状态值继承 `ConfigValueTextStyle` 并按启用状态改色；右侧提示自动换行。
- 管理员密码弹窗：遮罩继承 `DialogOverlayStyle`（默认折叠，显示时覆盖三行）；面板宽 380、`DialogPanelStyle` 内边距 24；标题下边距 10，提示下边距 12；PasswordBox 高 32、默认下边距 16；取消按钮 `78 x 32` 且右边距 8，确认按钮高 32。

### 3.11 `SystemLogView.xaml` 日志

- 根：外边距 24；页头 88 + 12；主体为 `PageLeadStandardCardStyle`（标准内边距 18）。
- 主卡内部行：标题区 `Auto`、间距 12、工具栏 `Auto`、间距 10、日志列表 `*`。
- 标题区：标题使用 `CardHeaderTextStyle`；标题与状态/路径行之间 `CardContentTitleGapRowStyle=10`。
- 状态/路径行列：状态框最小宽 148；状态框与路径框间 10；路径框与右侧过滤汇总间 12；路径框高度 32、内边距 10/0。
- 工具栏：所有动作按钮 `Button.ToolBar`（高 32、右边距 8）；过滤组横向排列。
  - 级别组外边距 `8,0,12,0`，标签宽 34，ComboBox 宽 86。
  - 关键词组标签宽 46，TextBox 宽 168、高 32，组右边距 12。
  - 时间范围组标签宽 58，ComboBox 宽 104，组右边距 12。
  - 自动滚动组外边距 `0,0,8,0`，开关后文字间距 8。
- 日志列表 `LogListBoxStyle`：内边距 12/10，列表项上下 4；每条日志两列，级别列宽 54，正文列 `*`，正文 Consolas、超长省略。

### 3.12 `MainWindow.xaml` 壳层

- 窗口默认 `1440 x 860`，最小 `1180 x 760`；无系统标题栏，WindowChrome 圆角 10、可调整边框 6。
- 根三行：标题栏 46、内容 `*`、底部状态栏 36。
- 标题栏：左侧 StackPanel `Margin=16,0,0,0`；Logo `28 x 28`、圆角 7；Logo 与标题间 10；标题字号 14。右侧三个窗口按钮各 `46 x 46`，使用 Chrome 模板，关闭按钮复用危险悬停状态。
- 内容区列：左导航宽 256；右侧 TabControl 占剩余空间（当前 XAML 以 `1079* / 105*` 两个星号并列且 `Grid.ColumnSpan=2`，实际按比例分配，不能视为固定 1079/105 像素）。导航容器外边距 `0,22,0,8`。
- 导航：分组标题高 28、默认 `Margin=0,14,0,4`、`Padding=22,0,16,0`；导航项高 42、宽 256、无外边距。模板列宽 `4 / 26 / 34 / *`，图标 `18 x 18` 位于第 2 列，文字在第 3 列；活动轨宽 4。
- 系统分组前分隔线高 1，外边距 `18,14,18,10`；系统组标题页面显式下边距 4。
- 底部状态栏：第一段文字 `Margin=14,0,10,0`；分隔线宽 1、高 16、左右 18；日志图标 `16 x 16`，图标与文字间 6，日志组右边距 8；最新日志计数 `Margin=14,0,10,0`；查看按钮工具栏样式但页面覆盖 `Padding=10,0`、右边距 10。

## 4. 统一模板与页面自定义统计

### 4.1 统一设计（模板/令牌）

| 类别 | 统一资源 |
|---|---|
| 页面骨架 | `PageRootGridStyle`、`PageHeaderRowStyle`、`PageHeaderGapRowStyle`、`PageSectionGapRowStyle`、`PageSummaryRowStyle`、`PageColumnGapStyle`、`FormLabelColumnStyle` |
| 页头与复用控件 | `PageHeader`、`PageHeaderCardStyle`、`PageTitleTextStyle`、`PageSubtitleTextStyle`、`PageHeaderSubtitleTextStyle`、`PageHeaderMetaTextStyle`、`PageHelpDocumentButton` |
| 卡片与面板 | `CardStyle`、`Card.Compact`、`Card.Standard`、`Card.Elevated`、`DialogPanelStyle`、`SettingGroupPanelStyle`、`PanelHeaderStyle`、`PanelContentGridStyle`、`PanelContentGroupStyle` |
| 按钮 | `Button.Base`、`Button.Normal`、`Button.Primary`、`Button.Danger`、`Button.ToolBar`、`Button.Compact`、`Button.PathAction`、`Button.ConnectionAction`、`Button.ConnectionPrimaryAction`、`Button.HelpDocument`、`Button.Icon`、对话框/窗口按钮样式 |
| 输入与反馈 | `SoftTextBoxStyle`、`PathTextBoxStyle`、`PathDisplayBorderStyle`、`StandardComboBoxStyle`、ComboBoxItem 模板、`SoftSwitchStyle`、`SoftProgressBarStyle`、`InlineStatusValueBorderStyle` |
| 数据展示 | `DataGrid.Base`、隐式 DataGrid/Header/Cell 样式、`FunctionCheckDataGridStyle`、`LogListBoxStyle`、`LogTextBoxStyle` |
| 状态与文字 | `Status*Style`、`Typography.xaml` 的隐式 TextBlock、页标题/卡片标题/字段标签/配置值样式 |
| 色彩令牌 | `Colors.xaml` 定义的角色色及 `Brushes.xaml` 对应画刷，页面通过 `StaticResource` 使用 |
| 壳层 | `ShellContentTabControlStyle`、`ShellNavGroupTextStyle`、`ShellNavRadioButtonStyle` 及导航模板 |

### 4.2 页面自定义内容

以下内容在页面 XAML 中直接定义，不能仅靠统一模板推导：

- 每个页面的标题、副标题、字段文案、绑定命令、状态文案和业务数据列。
- 页面显式行高/列宽/比例：例如固件页状态卡 `144 x 74`、功能检测配方卡高 120、开发者页 `7*:12:13*`、系统设置两列间距 24、流程验证卡高 110。
- 专用 DataGrid 列及列宽：项目/配方、流程、算法、功能检测、刷写监控、开发者报文接收。
- `FunctionCheckView` 页头主题按钮与窗口控制按钮组合，以及开始检测按钮 `130 x 36`。
- `DeveloperOptionsView` 报文发送/接收区域、104 宽输入/下拉列、90 宽发送按钮、接收区三按钮及自定义全网格表格。
- `SystemSettingsView` 的五张设置卡、设置组内容、路径操作行、管理员状态绑定、密码遮罩和 380 宽弹窗。
- `FlashHistoryView` 的空状态内嵌 Border（Padding 20）及空态文案间距 8。
- `MainWindow` 的自定义标题栏、侧栏分组、窗口按钮、底部状态栏字段与布局。

### 4.3 复用度结论

下表统计的是页面/壳层/复用控件 XAML 中显式 `Style="{StaticResource ...}"` 的引用次数；隐式样式（例如所有 DataGrid 的基础样式）不计入该列。

| 统一资源 | 显式引用次数 | 覆盖范围 |
|---|---:|---|
| `PageRootGridStyle`、`PageHeaderRowStyle`、`PageHeaderGapRowStyle` | 各 11 | 全部 11 个业务页面 |
| `Button.ToolBar` | 17 | 7 个页面/壳层 |
| `FieldLabelStyle` | 27 | 5 个表单型页面 |
| `FormLabelColumnStyle` | 19 | 4 个复杂表单页面 |
| `CardStyle`（直接引用） | 10 | 固件、功能检测、开发者页；衍生卡片另行统计 |
| `Card.Compact`（直接引用） | 5 | 算法、监控、流程、项目、配方 |
| `PageLeadStandardCardStyle` | 7 | 流程、系统设置、系统日志 |
| `PanelHeaderRowStyle` / `PanelHeaderStyle` | 各 10 | 固件、功能检测、开发者页 |
| `StandardComboBoxStyle` | 10 | 5 个页面 |
| `SoftSwitchStyle` | 7 | 系统设置、系统日志 |
| `PageHeader` 控件 | 11 | 全部业务页面 |
| `ShellNavRadioButtonStyle` | 10 | 主窗口 10 个导航入口 |

- 11 个页面全部复用 `PageRootGridStyle`、页头行、页头间距和 `PageHeader`，页面外框一致。
- 卡片、面板标题、表单标签、按钮、输入框、ComboBox、开关、进度条和 DataGrid 均已有资源级基线；新页面优先组合这些资源。
- 自定义主要集中在业务列定义、页面专用固定高度/列宽、复杂嵌套网格和管理员弹窗；这些是内容层差异，不应回写为全局模板，除非出现至少两个页面的同形复用需求。

## 5. 文件索引与审计边界

| 文件 | 角色 |
|---|---|
| `src/DiagnosticFlashTool.App/App.xaml` | 合并 `Styles/AppStyles.xaml`，应用资源入口 |
| `src/DiagnosticFlashTool.App/MainWindow.xaml` | 窗口标题栏、导航、Tab 壳、状态栏 |
| `src/DiagnosticFlashTool.App/Views/Controls/PageHeader.xaml` | 页头复用控件 |
| `src/DiagnosticFlashTool.App/Views/Controls/PageHelpDocumentButton.xaml` | 帮助按钮复用控件 |
| `src/DiagnosticFlashTool.App/Views/*.xaml` | 11 个业务页面，详见第 3 节；其中 `RecipeConfigView.xaml` 当前未在 `MainWindow` 的 TabControl 中实例化，但仍已纳入审计 |
| `src/DiagnosticFlashTool.App/Styles/AppStyles.xaml` | 统一资源字典合并入口 |
| `src/DiagnosticFlashTool.App/Styles/Colors.xaml` | 颜色值令牌 |
| `src/DiagnosticFlashTool.App/Styles/Brushes.xaml` | 角色画刷 |
| `src/DiagnosticFlashTool.App/Styles/Typography.xaml` | 文字样式 |
| `src/DiagnosticFlashTool.App/Styles/Controls/ButtonStyles.xaml` | 按钮、图标、窗口按钮 |
| `src/DiagnosticFlashTool.App/Styles/Controls/InputStyles.xaml` | 输入框、下拉框、开关、进度条 |
| `src/DiagnosticFlashTool.App/Styles/Controls/DataGridStyles.xaml` | 表格、表头、单元格 |
| `src/DiagnosticFlashTool.App/Styles/Layout/CardStyles.xaml` | 卡片、字段、标题 |
| `src/DiagnosticFlashTool.App/Styles/Layout/PanelStyles.xaml` | 面板标题和内容间距 |
| `src/DiagnosticFlashTool.App/Styles/Layout/PageStyles.xaml` | 页面令牌和骨架 |
| `src/DiagnosticFlashTool.App/Styles/Shell/ShellStyles.xaml` | 导航与 Tab 壳 |
| `src/DiagnosticFlashTool.App/Styles/Domain/FunctionCheckStyles.xaml` | 功能检测单元格 |
| `src/DiagnosticFlashTool.App/Styles/Domain/StatusStyles.xaml` | 状态卡、徽章、状态点 |
| `src/DiagnosticFlashTool.App/Styles/Domain/LogStyles.xaml` | 日志列表与日志文本框 |

共核对 29 个 XAML 文件；所有 XAML 文件均已通过 XML 解析校验。除 `MainWindow.xaml.cs` 动态创建的 Windows Forms 托盘图标、右键菜单及其两个菜单项外，未发现 C# 动态创建的 WPF 控件；该托盘菜单没有定义可统计的 `Width`、`Height`、`Margin`、`Padding` 或 WPF 样式。本文件的间距统计覆盖当前静态 WPF UI 定义及全部可追溯的 XAML 样式。
