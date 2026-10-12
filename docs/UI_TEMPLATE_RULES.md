# UI 模板设计规范

本文规定 `DiagnosticFlashTool.App` 中页面、控件、对话框、导航和样式的设计与实现流程。它是 UI 改动的强制前置规范，目标是让新增功能延续当前 WPF/MVVM 界面，而不是为每个需求重新发明一套布局。

## 1. 适用范围与事实来源

以下改动必须遵循本文：

- 新建或修改 `Views/**/*.xaml`、`MainWindow.xaml` 及其代码隐藏。
- 新建或修改 `Styles/**/*.xaml`、应用资源、图标、主题颜色和控件模板。
- 修改导航、对话框、可见文案、状态反馈、布局尺寸或交互行为。
- ViewModel 改动会改变页面结构、控件状态或用户操作流程。

规范的事实来源按下列顺序判断：

1. 用户对当前任务的明确要求。
2. 当前 `src/DiagnosticFlashTool.App/Styles/`、复用控件和已挂载页面源码。
3. 本文规定的选择与检查流程。
4. `docs/UI间距与统一设计统计.md` 等审计或统计文档。

精确颜色、尺寸、间距和状态值必须从当前资源字典读取，不得从统计文档复制后另行硬编码。统计文档用于理解现状；与源码不一致时以源码为准，并在本次改动涉及该内容时同步修正文档。

## 2. 修改前的强制流程

AI 在编辑 UI 文件前必须完成以下步骤：

1. 明确任务属于哪一种页面模板：数据列表、列表加编辑区、表单设置、运行监控、流程编辑、日志工具或对话框。
2. 阅读本次会使用的资源字典，并至少检查一个最接近的现有页面。不要只根据样式名称猜测行为。
3. 先形成简短设计说明，再开始编辑。设计说明至少包含：
   - 页面类型和主要工作流。
   - 参考页面及选择原因。
   - 准备复用的布局、卡片、输入、按钮和状态资源。
   - 正常、空数据、忙碌/禁用、错误等适用状态。
   - 与既有模板不同的地方及其业务原因。
4. 确认绑定、命令和状态归属。业务状态放 ViewModel，代码隐藏仅承载必须依赖视图对象的交互。
5. 实现后执行第 10 节的验收清单。

可使用下面的简短格式汇报设计：

```text
页面类型：表单设置页
参考页面：SystemSettingsView
复用资源：PageRootGridStyle、Card.Form、Card.FormContent、FieldLabelStyle
关键状态：加载、可编辑、保存中、校验失败
模板偏离：无；或说明偏离原因
```

用户明确要求只修复局部视觉问题时，可以缩短设计说明，但仍需检查目标控件的父级布局、相邻页面和对应资源样式。

## 3. 资源入口与参考页面

### 3.1 资源入口

`Styles/AppStyles.xaml` 是应用样式合并入口。按职责查找和新增资源：

| 职责 | 文件 |
|---|---|
| 颜色令牌和语义画刷 | `Styles/Colors.xaml`、`Styles/Brushes.xaml` |
| 字体和文本层级 | `Styles/Typography.xaml` |
| 按钮与图标 | `Styles/Controls/ButtonStyles.xaml` |
| 输入框、下拉框、开关、进度条 | `Styles/Controls/InputStyles.xaml` |
| 表格 | `Styles/Controls/DataGridStyles.xaml` |
| 页面页头 | `Styles/Controls/PageHeaderStyles.xaml` |
| 卡片和字段 | `Styles/Layout/CardStyles.xaml` |
| 页面骨架与间距令牌 | `Styles/Layout/PageStyles.xaml` |
| 面板标题与内容 | `Styles/Layout/PanelStyles.xaml` |
| Shell 导航与内容区 | `Styles/Shell/ShellStyles.xaml` |
| 业务状态和日志 | `Styles/Domain/StatusStyles.xaml`、`Styles/Domain/LogStyles.xaml` |

新增共享资源时放入职责最接近的字典，并通过 `AppStyles.xaml` 合并；不要在单个页面复制通用 `ControlTemplate`。只有页面独有且不具复用价值的样式才可放在页面资源中。

### 3.2 模板选择

| 页面类型 | 首选参考 | 适用场景 |
|---|---|---|
| 简单只读列表 | `AlgorithmConfigView.xaml` | 单卡片、只读表格、少量页头元信息 |
| 列表加侧边编辑 | `ProjectConfigView.xaml` | 主表格、选择项编辑、新增/保存/删除工作流 |
| 多组表单设置 | `SystemSettingsView.xaml` | 可滚动设置卡、标签列、路径操作、开关和说明 |
| 运行监控/状态工作台 | `FirmwareFlashView.xaml` | 状态卡、设备操作、配置摘要和进度反馈 |
| 复杂流程编辑 | `FlowConfigView.xaml` | 工具区、可编辑表格、排序/拖放和校验结果 |
| 日志与过滤工具 | `SystemLogView.xaml` | 工具栏、过滤条件、深色日志内容区 |
| 空状态或待接入列表 | `FlashHistoryView.xaml` | 暂无数据、说明和后续列表承载区 |
| 诊断工具工作台 | `DeveloperOptionsView.xaml` | 多区域操作、消息发送、实时表格和进度 |
| 模态确认/输入 | `SystemSettingsView.xaml` 中管理员密码遮罩 | Shell 内遮罩、输入、确认和取消 |

选择最接近用户工作流的页面，而不是只比较视觉相似度。若没有合适模板，先组合已有骨架和控件；仅在模式预计复用两处以上时新增全局模板。

## 4. 标准页面骨架

普通 Shell 页面从以下结构开始，再按业务增加主体行列：

```xml
<Grid Style="{StaticResource PageRootGridStyle}">
    <Grid.RowDefinitions>
        <RowDefinition Style="{StaticResource PageHeaderRowStyle}" />
        <RowDefinition Style="{StaticResource PageHeaderGapRowStyle}" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <controls:PageHeader Grid.Row="0">
        <controls:PageHeader.Actions>
            <controls:PageHelpDocumentButton />
        </controls:PageHeader.Actions>
    </controls:PageHeader>

    <Border Grid.Row="2" Style="{StaticResource Card.Compact}">
        <!-- 页面主体 -->
    </Border>
</Grid>
```

骨架规则：

- 根布局使用 `PageRootGridStyle`；页头行和间隔分别使用 `PageHeaderRowStyle`、`PageHeaderGapRowStyle`。
- `PageHeader` 的上下文默认绑定 `MainViewModel.CurrentPageContextText`。不要在页头重复左侧导航中已有的页面名称。
- 多区块间距优先使用 `PageSectionGapRowStyle`、`PageColumnGapStyle`、`PageCardBottomGap` 等资源。
- 内容超出可用高度时，在主体内容层使用纵向 `ScrollViewer`；不要让整个 Shell 或固定页头一起滚动。
- `Auto` 用于由内容决定的尺寸，`*` 用于分配剩余空间。固定宽高只用于稳定工具栏、状态卡、协议字段等确有格式要求的区域。
- 避免卡片嵌套卡片。卡片内部需要分组时使用标题、Grid、分隔线或 `SettingGroupPanelStyle`。

## 5. 常用内容模板

### 5.1 表单与设置

- 表单卡片使用 `Card.Form` 或页面级的 `PageLeadStandardCardStyle` / `PageTrailingStandardCardStyle`。
- 卡片内容优先使用 `CardContentPanel` + `Card.FormContent`，由控件统一标题、行间和末行间距。
- 标题使用 `CardContentPanelTitleTextStyle`；字段标签使用 `FieldLabelStyle`，标签列优先使用 `FormLabelColumnStyle`。
- 相同卡片中的标签列宽必须一致。输入内容需要占满余量时覆盖 ComboBox 默认宽度为 `Width="Auto"` 并设 `HorizontalAlignment="Stretch"`。
- 路径显示使用 `PathDisplayBorderStyle` 和 `PathDisplayTextStyle`，配套操作使用 `Button.PathAction`。
- 设置项需要补充解释时使用 `SettingAssistTextStyle`，不要用醒目标题代替辅助说明。

### 5.2 数据列表与编辑区

- 表格沿用隐式 DataGrid 样式或显式 `DataGrid.Base`，只在业务需要时定义列、单元格模板和列宽。
- 长表格和长列表默认隐藏非必要的垂直滚动条，使用鼠标滚轮、触控板或键盘滚动；WPF 设置 `ScrollViewer.VerticalScrollBarVisibility="Hidden"`。`Hidden` 只隐藏滚动条，不禁用滚动；不得用 `Disabled` 代替。若用户需要拖动滑块快速定位、滚动条承担必要的可发现性提示，或主要输入方式不便滚轮滚动，可保留可见滚动条，并说明原因。
- 主信息列可使用 `*` 占据剩余空间；标识、时间、状态等格式稳定的列使用有依据的固定宽度。
- 表格操作放在卡片标题行或紧邻表格的工具栏，使用 `Button.ToolBar`。
- 侧边编辑区应有明确的打开、保存、取消和必要的删除路径；编辑区显示状态由 ViewModel 控制。
- 空数据不能只留下空白表格，应提供简洁空状态以及可执行的下一步操作。

### 5.3 运行监控与诊断工作台

- 优先展示连接状态、当前配置、主操作、进度和结果，避免营销式大标题或装饰性区域。
- 状态使用 `Status*Style`、图标和文字共同表达，不能只依赖颜色。
- 长时间操作必须显示忙碌或进度状态，并禁用会造成重复执行或状态冲突的操作。
- 日志、报文和协议字段采用稳定尺寸，动态内容使用裁切、省略、滚动或等宽字体，不能推动工具栏和主要操作发生布局跳动。

### 5.4 对话框与遮罩

- Shell 内模态交互复用 `DialogOverlayStyle`、`DialogPanelStyle`、`Button.DialogPrimary` 和 `Button.DialogSecondary`。
- 默认操作、取消操作和危险操作必须在语义与视觉上区分；危险操作使用确认文案，不得伪装为普通主操作。
- 支持适用的 Enter/Escape 键，打开后应聚焦到首个可操作控件，关闭后恢复合理焦点。
- 对话框必须覆盖确认、取消、关闭窗口和命令执行失败路径，不能只实现成功路径。

## 6. 控件和视觉规则

- 普通、主操作、危险、工具栏、纯图标、路径和对话框按钮分别选用现有 `Button.*` 样式。
- 有对应 MaterialDesign `PackIcon` 时使用现有图标库。图标加文字按钮使用 `Button.InlineContent`、`Button.PackIcon.Current` 和 `Button.IconLabel`。
- 纯图标按钮必须提供 `ToolTip`；相同作用在不同页面应使用相同图标和文案。
- 文本输入、密码、下拉、开关和进度条复用 `SoftTextBoxStyle`、`SoftPasswordBoxStyle`、`StandardComboBoxStyle`、`SoftSwitchStyle`、`SoftProgressBarStyle`。
- 颜色只能通过语义画刷资源引用。业务页面不得新增十六进制颜色、重复阴影、圆角或控件模板。
- 文字遵循 `Typography.xaml` 的层级。紧凑面板内使用卡片标题和字段标题，不得使用页面级大字号。
- UI 文案使用中文；协议名、标准名、文件格式和必要的字段标识可保留英文。操作文案使用清晰动词。
- 长路径、日志和动态文本应使用 `TextTrimming`、换行、滚动或完整内容 ToolTip，不能覆盖相邻控件。

## 7. 交互、状态与 MVVM

- 用户操作通过命令绑定进入 ViewModel。同步操作用 `RelayCommand`，异步操作用 `AsyncRelayCommand` 和 `CancellationToken`。
- 不得在 UI 线程执行文件解析、设备通信、刷写或大批量数据处理。
- 代码隐藏只处理拖放、焦点、窗口、滚动、控件专属事件等视图行为；业务判断、数据修改和流程编排放入 ViewModel/Core。
- 每个页面按实际工作流检查以下状态：初始、正常、无数据、加载/执行中、禁用、校验失败、操作失败、取消、完成。仅实现适用状态，但不得遗漏失败和重复提交防护。
- 命令执行期间保持布局稳定。状态文字、进度值和按钮文案变化不能导致关键控件跳位或遮挡。
- 错误反馈应说明发生了什么以及用户可以采取的下一步，不显示无上下文的异常堆栈。

## 8. 布局与可用性

- 以 `MainWindow` 的最小尺寸为下限检查页面；页面不得依赖默认启动尺寸才可使用。
- 工具栏和表单在空间不足时应通过合理列布局、换行、滚动或内容省略处理，不允许相互覆盖。
- 数字、CAN ID、地址、进度和状态区域应保留稳定宽度；数据内容变化不应重排整页。
- 可点击区域需要明确的悬停、按下、禁用和焦点状态，优先由共享样式提供。
- 状态和错误不能仅通过颜色传递，应同时使用文字、图标或状态点。
- 不添加纯装饰渐变、光斑、悬浮营销卡片或与诊断工具场景无关的视觉元素。

## 9. 新页面接入

新页面除创建 `Views/<Name>View.xaml` 和对应代码隐藏外，还必须同步检查：

- 在 `MainWindow.xaml` 的 Shell 导航和 `TabControl` 中按同一顺序接入。
- 更新 `MainViewModel.SelectedShellIndex` 相关命令、索引分支和 `CurrentPageContextText`。
- 为页面上下文、帮助入口、权限或可见性提供所需状态。
- 复用现有 ViewModel 装配方式，不引入 DI 容器。
- 若页面引入可复用控件或资源，将其放入 `Views/Controls/` 或对应 `Styles/` 分类并更新本文索引。

导航索引是位置契约。插入或删除页面时必须检查所有硬编码索引、命令跳转和上下文文案，不能只修改可见导航项。

## 10. 完成验收清单

提交 UI 改动前逐项确认：

- 已说明页面类型、参考页面、复用资源、关键状态和模板偏离。
- 页面使用标准根骨架、页头和适合的内容模板。
- 未在业务页面硬编码颜色、阴影、圆角或重复通用控件模板。
- 新共享模式已抽取到正确的样式或复用控件；页面专属布局没有被错误全局化。
- 正常、空数据、忙碌/禁用、错误、取消和完成等适用状态均有清晰表现。
- 图标按钮有 ToolTip，状态不只依赖颜色，危险操作有明确确认语义。
- 隐藏滚动条的长表格/列表仍可通过滚轮、触控板和键盘滚动；需要可见滚动条的例外有明确理由。
- 最小窗口尺寸下无裁切、重叠、文本溢出或不可达操作；长文本和动态数据不会引发布局跳动。
- 异步操作不阻塞 UI，命令有防重入或禁用状态，取消令牌得到传递。
- 新页面的导航顺序、Tab 顺序、`SelectedShellIndex` 和上下文文案保持一致。
- 已执行 `dotnet build DiagnosticFlashTool.slnx -c Debug`。
- 对用户可见的布局或交互改动已尽可能运行应用检查；未能检查的项目在交付说明中明确列出。
