# -*- coding: utf-8 -*-
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "src/DiagnosticFlashTool.App")
S = os.path.join(APP, "Styles")
fails = []


def read(p):
    return io.open(p, encoding="utf-8").read()


def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)


def rep(s, old, new, n=1, tag=""):
    eol = "\r\n" if "\r\n" in s else "\n"
    o = old.replace("\n", eol)
    nw = new.replace("\n", eol)
    if o == "</ResourceDictionary>":
        i = s.rfind(o)
        if i < 0:
            fails.append(tag + ": no close tag")
            return s
        return s[:i] + nw + s[i + len(o):]
    c = s.count(o)
    if c != n:
        fails.append("%s: expected %d got %d" % (tag, n, c))
        return s
    return s.replace(o, nw)


RESTORE = [
    ("Styles/Colors.xaml", "Colors.xaml"),
    ("Styles/Brushes.xaml", "Brushes.xaml"),
    ("Styles/FluentTheme.xaml", "FluentTheme.xaml"),
    ("Styles/Typography.xaml", "Typography.xaml"),
    ("Styles/Controls/ButtonStyles.xaml", "ButtonStyles.xaml"),
    ("Styles/Shell/ShellStyles.xaml", "ShellStyles.xaml"),
    ("MainWindow.xaml", "MainWindow.xaml"),
]


def restore_all():
    for rel, bk in RESTORE:
        src = os.path.join(APP, rel)
        data = io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "backup", bk), "rb").read()
        io.open(src, "wb").write(data)


restore_all()

# ---------------- Colors.xaml ----------------
p = os.path.join(S, "Colors.xaml")
s = read(p)
tokens = {
    "Color.Chrome.Background": "23282E",
    "Color.Chrome.Border": "14181C",
    "Color.App.Background": "EFF2F4",
    "Color.Surface.Background": "F4F6F8",
    "Color.Border.Card": "D5DADF",
    "Color.Border.Divider": "E7EAED",
    "Color.Border.Input": "C3C9CF",
    "Color.Border.PrimarySoft": "B7D2EC",
    "Color.Primary": "1466B8",
    "Color.Primary.Hover": "0F5599",
    "Color.Primary.Pressed": "0B4479",
    "Color.Primary.Accent": "4A8FD0",
    "Color.Primary.Soft": "E6EFF9",
    "Color.Primary.SoftPressed": "D6E5F5",
    "Color.Brand.Navigation": "1466B8",
    "Color.Text.Primary": "17191B",
    "Color.Text.CardTitle": "17191B",
    "Color.Text.Secondary": "3B434B",
    "Color.Text.Label": "515A63",
    "Color.Text.Muted": "666E77",
    "Color.Text.Disabled": "A0A7AE",
    "Color.Text.Placeholder": "A0A7AE",
    "Color.Icon.Default": "515A63",
    "Color.Icon.Primary": "1466B8",
    "Color.Sidebar.Background": "1C2127",
    "Color.Sidebar.Hover": "2A3037",
    "Color.Sidebar.Active": "2E3B47",
    "Color.Sidebar.ActiveRail": "4AA3E8",
    "Color.Sidebar.IconActive": "7FC0F2",
    "Color.Sidebar.Text": "E8ECEF",
    "Color.Sidebar.TextMuted": "98A2AC",
    "Color.Table.Header": "E9ECEF",
    "Color.Log.Panel": "F6F8F9",
    "Color.Warning": "96620A",
}
for k, v in tokens.items():
    s, n = re.subn(r'(<Color x:Key="%s">)#[0-9A-Fa-f]{6}(</Color>)' % re.escape(k),
                   r'\g<1>#%s\g<2>' % v, s)
    if n != 1:
        fails.append("Colors.%s: %d" % (k, n))
s = rep(s, "        桌面工具调色板（浅色为唯一交付主题）。\n        中性石墨灰打底 + 单一仪表青强调；圆角、边框、密度由 Brushes.xaml 与各 Style 承载。\n        色值只在此处定义，业务页面与样式不得再硬编码十六进制颜色。\n",
        "        桌面工具调色板（v3.3 深色框架 + 浅色内容，主题仍固定 Light）。\n        深灰石墨框架（标题栏/菜单栏/侧栏/状态栏）+ 低成本浅色内容区 + 单一工业蓝强调。\n        色值只在此处定义，业务页面与样式不得再硬编码十六进制颜色。\n", tag="colors-comment")
s = rep(s, "</ResourceDictionary>", """    <!-- 深色框架（v3.3）：框架上的文字与图标不得复用内容区文字令牌，
         否则浅色文字会落到浅色内容区、深色文字会落到深色框架上。 -->
    <Color x:Key="Color.Chrome.Text">#E8ECEF</Color>
    <Color x:Key="Color.Chrome.TextMuted">#98A2AC</Color>
    <Color x:Key="Color.Chrome.Accent">#6FB3EE</Color>
    <Color x:Key="Color.Chrome.Hover">#333B44</Color>
    <Color x:Key="Color.Chrome.Pressed">#3E4750</Color>
    <Color x:Key="Color.Chrome.Divider">#3A424B</Color>
</ResourceDictionary>""", tag="colors-append")
write(p, s)

# ---------------- Brushes.xaml ----------------
p = os.path.join(S, "Brushes.xaml")
s = read(p)
s = rep(s, "</ResourceDictionary>", """    <!-- 深色框架语义画刷：只用于标题栏、菜单栏、侧栏与状态栏。 -->
    <SolidColorBrush x:Key="ChromeTextBrush" Color="{StaticResource Color.Chrome.Text}" />
    <SolidColorBrush x:Key="ChromeMutedTextBrush" Color="{StaticResource Color.Chrome.TextMuted}" />
    <SolidColorBrush x:Key="ChromeAccentBrush" Color="{StaticResource Color.Chrome.Accent}" />
    <SolidColorBrush x:Key="ChromeHoverBrush" Color="{StaticResource Color.Chrome.Hover}" />
    <SolidColorBrush x:Key="ChromePressedBrush" Color="{StaticResource Color.Chrome.Pressed}" />
    <SolidColorBrush x:Key="ChromeDividerBrush" Color="{StaticResource Color.Chrome.Divider}" />
</ResourceDictionary>""", tag="brushes-append")
write(p, s)

# ---------------- FluentTheme.xaml ----------------
p = os.path.join(S, "FluentTheme.xaml")
s = read(p)
acc = ["1466B8", "0F5599", "0B4479", "08345C", "4A8FD0", "9CC4EE", "E6EFF9"]
keys = ["AccentColorKey", "AccentColorDark1Key", "AccentColorDark2Key", "AccentColorDark3Key",
        "AccentColorLight1Key", "AccentColorLight2Key", "AccentColorLight3Key"]
for k, v in zip(keys, acc):
    s, n = re.subn(r'(<Color x:Key="\{x:Static SystemColors.%s\}">)#[0-9A-Fa-f]{6}(</Color>)' % k,
                   r'\g<1>#%s\g<2>' % v, s)
    if n != 1:
        fails.append("Fluent.%s: %d" % (k, n))
s = rep(s, "        桌面工具改版：品牌色由通用蓝 #2F6FEB 换为工业蓝 #1668C4。",
        "        v3.3 深色框架：品牌色由 #1668C4 换为更稳的工业蓝 #1466B8，\n        并同步深色框架上的强调色 #4AA3E8（见 Brushes.ChromeAccentBrush）。", tag="fluent-comment")
write(p, s)

# ---------------- ButtonStyles.xaml ----------------
p = os.path.join(S, "Controls/ButtonStyles.xaml")
s = read(p)
s = rep(s, "</ResourceDictionary>", """    <!-- 深色框架区按钮：沿用普通按钮模板，只替换前景与交互态画刷。 -->
    <ResourceDictionary x:Key="Button.OnChromeStateResources">
        <SolidColorBrush x:Key="ButtonBackgroundPointerOver" Color="{StaticResource Color.Chrome.Hover}" />
        <SolidColorBrush x:Key="ButtonBorderBrushPointerOver" Color="Transparent" />
        <SolidColorBrush x:Key="ButtonForegroundPointerOver" Color="{StaticResource Color.Chrome.Text}" />
        <SolidColorBrush x:Key="ButtonBackgroundPressed" Color="{StaticResource Color.Chrome.Pressed}" />
        <SolidColorBrush x:Key="ButtonBorderBrushPressed" Color="Transparent" />
        <SolidColorBrush x:Key="ButtonForegroundPressed" Color="{StaticResource Color.Chrome.Text}" />
        <SolidColorBrush x:Key="ButtonBackgroundDisabled" Color="Transparent" />
        <SolidColorBrush x:Key="ButtonBorderBrushDisabled" Color="Transparent" />
        <SolidColorBrush x:Key="ButtonForegroundDisabled" Color="#6B747D" />
    </ResourceDictionary>

    <Style x:Key="Button.OnChrome" TargetType="Button" BasedOn="{StaticResource Button.Normal}">
        <Setter Property="Foreground" Value="{StaticResource ChromeTextBrush}" />
        <Setter Property="controls:FluentButtonAssist.StateResources" Value="{StaticResource Button.OnChromeStateResources}" />
    </Style>
</ResourceDictionary>""", tag="button-append")
write(p, s)

# ---------------- ShellStyles.xaml ----------------
p = os.path.join(S, "Shell/ShellStyles.xaml")
s = read(p)
s = rep(s, "        桌面工具改版：侧栏由深色块改为与窗口同族的浅灰分区，\n        选中态用 2px 左侧青色轨 + 浅青底 + 主色文字，未选中用中性灰。",
        "        v3.3 深色框架：侧栏与标题栏、状态栏同为深灰分区，\n        选中态用 2px 左侧蓝色轨 + 深蓝底 + 浅蓝文字，未选中用浅灰文字。", tag="shell-comment")
s = rep(s, '<Setter TargetName="NavIcon" Property="Foreground" Value="{StaticResource TextBrush}" />',
        '<Setter TargetName="NavIcon" Property="Foreground" Value="{StaticResource SidebarTextBrush}" />', tag="nav-hover-icon")
s = rep(s, '<Setter TargetName="NavText" Property="Foreground" Value="{StaticResource TextBrush}" />',
        '<Setter TargetName="NavText" Property="Foreground" Value="{StaticResource SidebarTextBrush}" />', tag="nav-hover-text")
s = rep(s, '<Setter TargetName="NavText" Property="Foreground" Value="{StaticResource PrimaryBrush}" />',
        '<Setter TargetName="NavText" Property="Foreground" Value="{StaticResource SidebarActiveIconBrush}" />', tag="nav-checked-text")
write(p, s)

# ---------------- Typography.xaml ----------------
p = os.path.join(S, "Typography.xaml")
s = read(p)
s = rep(s, "</ResourceDictionary>", """    <!-- 深色状态栏：与框架同族的前景，不复用内容区文字令牌。 -->
    <Style x:Key="ShellStatusTextStyle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="{StaticResource FontSize.Caption}" />
        <Setter Property="Foreground" Value="{StaticResource ChromeTextBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>

    <Style x:Key="ShellStatusCodeTextStyle"
           TargetType="TextBlock"
           BasedOn="{StaticResource ShellStatusTextStyle}">
        <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
    </Style>

    <Style x:Key="ShellStatusCountTextStyle"
           TargetType="TextBlock"
           BasedOn="{StaticResource ShellStatusTextStyle}">
        <Setter Property="Foreground" Value="{StaticResource ChromeMutedTextBrush}" />
    </Style>
</ResourceDictionary>""", tag="typography-append")
write(p, s)

# ---------------- MainWindow.xaml ----------------
p = os.path.join(APP, "MainWindow.xaml")
s = read(p)
s = rep(s, """                    <TextBlock Text="BOOT刷写工具"
                               Margin="8,0,0,0"
                               FontSize="{StaticResource FontSize.Assist}"
                               FontWeight="Medium"
                               Foreground="{StaticResource TextBrush}" />""",
        """                    <TextBlock Text="BOOT刷写工具"
                               Margin="8,0,0,0"
                               FontSize="{StaticResource FontSize.Assist}"
                               FontWeight="Medium"
                               Foreground="{StaticResource ChromeTextBrush}" />""", tag="title-text")
s = rep(s, """                        Margin="12,0"
                        VerticalAlignment="Center"
                        Background="{StaticResource DividerBrush}" />""",
        """                        Margin="12,0"
                        VerticalAlignment="Center"
                        Background="{StaticResource ChromeDividerBrush}" />""", tag="title-divider")
s = rep(s, """                           FontSize="{StaticResource FontSize.Assist}"
                           Foreground="{StaticResource SubtleTextBrush}" />""",
        """                           FontSize="{StaticResource FontSize.Assist}"
                           Foreground="{StaticResource ChromeMutedTextBrush}" />""", tag="title-project")
s, n = re.subn(r'(Height="32"\s*\n\s*Padding="0"\s*\n\s*Margin="0"\s*\n\s*Style="\{StaticResource )Button\.Normal', r'\1Button.OnChrome', s)
if n != 3:
    fails.append("winbtn: %d" % n)
for name in ("文件", "视图", "刷写", "帮助"):
    s = rep(s, '<Button Content="' + name + '" Style="{StaticResource Button.Normal}"',
            '<Button Content="' + name + '" Style="{StaticResource Button.OnChrome}"', tag="menu-" + name)
s = rep(s, 'Style="{StaticResource Button.Normal}" ToolTip="新增项目">',
        'Style="{StaticResource Button.OnChrome}" ToolTip="新增项目">', tag="sidebar-add")
s = rep(s, 'Margin="12,0" Background="{StaticResource DividerBrush}" />',
        'Margin="12,0" Background="{StaticResource ChromeDividerBrush}" />', n=3, tag="status-divider")
s = rep(s, 'Style="{StaticResource StatusBarTextStyle}"', 'Style="{StaticResource ShellStatusTextStyle}"', n=2, tag="status-text")
s = rep(s, 'Style="{StaticResource StatusBarCodeTextStyle}"', 'Style="{StaticResource ShellStatusCodeTextStyle}"', n=2, tag="status-code")
s = rep(s, 'Style="{StaticResource StatusBarCountTextStyle}"', 'Style="{StaticResource ShellStatusCountTextStyle}"', n=1, tag="status-count")
s, n = re.subn(r'(Command="\{Binding OpenRuntimeLogPanelCommand\}"\s*\n\s*Style="\{StaticResource )Button\.Normal', r'\1Button.OnChrome', s)
if n != 1:
    fails.append("status-view-btn: %d" % n)
write(p, s)

if fails:
    print("FAILED:")
    for f in fails:
        print(" -", f)
    sys.exit(1)
print("all edits applied")
