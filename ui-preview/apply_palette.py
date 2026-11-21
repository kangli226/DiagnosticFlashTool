# -*- coding: utf-8 -*-
import re, sys, io, os
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COLORS = os.path.join(ROOT, "src/DiagnosticFlashTool.App/Styles/Colors.xaml")
FLUENT = os.path.join(ROOT, "src/DiagnosticFlashTool.App/Styles/FluentTheme.xaml")
BACKUP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "backup")

A = {
 "Color.App.Background": "F4F6F8", "Color.Chrome.Background": "EDEFF2",
 "Color.Chrome.Border": "DDE1E5", "Color.Surface.Background": "F6F8FA",
 "Color.Border.Card": "D8DCE1", "Color.Border.Divider": "E9ECEF",
 "Color.Border.Input": "C6CBD1", "Color.Border.PrimarySoft": "B9D3EE",
 "Color.Primary.Soft": "E7F0FA", "Color.Primary.SoftPressed": "D9E7F7",
 "Color.Text.Primary": "17191B", "Color.Text.CardTitle": "17191B",
 "Color.Text.Secondary": "3F4750", "Color.Text.Label": "545C65",
 "Color.Text.Muted": "697078", "Color.Icon.Default": "545C65",
 "Color.Sidebar.Background": "EDEFF2", "Color.Sidebar.Hover": "E2E6EA",
 "Color.Sidebar.Active": "DDE9F5", "Color.Sidebar.Text": "3F4750",
 "Color.Sidebar.TextMuted": "697078", "Color.Table.Header": "E9EDF1",
 "Color.Log.Panel": "F7F9FA", "Color.Log.Text": "2A3036",
 "Color.Warning": "96620A",
}
A_FLUENT = ["1668C4", "1259A8", "0E4A8C", "0A3B70", "4C93E0", "9CC4EE", "E8F1FB"]

C = {
 "Color.App.Background": "F2F3F4", "Color.Chrome.Background": "E9EBED",
 "Color.Chrome.Border": "D6D9DC", "Color.Surface.Background": "EEF0F1",
 "Color.Border.Card": "D6D9DC", "Color.Border.Divider": "E6E8EA",
 "Color.Border.Input": "C2C6CA", "Color.Border.PrimarySoft": "C3C8CD",
 "Color.Primary": "2B3238", "Color.Primary.Hover": "1F2429",
 "Color.Primary.Pressed": "15191D", "Color.Primary.Accent": "6E777F",
 "Color.Primary.Soft": "E6E9EC", "Color.Primary.SoftPressed": "DADDE0",
 "Color.Brand.Navigation": "2B3238",
 "Color.Text.Primary": "17191B", "Color.Text.CardTitle": "17191B",
 "Color.Text.Secondary": "3D4349", "Color.Text.Label": "525860",
 "Color.Text.Muted": "666C73", "Color.Icon.Default": "525860",
 "Color.Icon.Primary": "2B3238", "Color.Sidebar.ActiveRail": "2B3238",
 "Color.Sidebar.IconActive": "1F2429",
 "Color.Sidebar.Background": "EAECEE", "Color.Sidebar.Hover": "DFE2E5",
 "Color.Sidebar.Active": "DFE3E7", "Color.Sidebar.Text": "3D4349",
 "Color.Sidebar.TextMuted": "666C73", "Color.Table.Header": "E8EAED",
 "Color.Log.Panel": "F6F7F8", "Color.Log.Text": "2A3036",
 "Color.Warning": "9A6400", "Color.Warning.Soft": "FBF2E1",
}
C_FLUENT = ["2B3238", "1F2429", "15191D", "101317", "6E777F", "B9BFC4", "E6E9EC"]


def set_colors(mapping):
    s = io.open(os.path.join(BACKUP, "Colors.xaml"), encoding="utf-8").read()
    for k, v in mapping.items():
        s, n = re.subn(r'(<Color x:Key="%s">)#[0-9A-Fa-f]{6}(</Color>)' % re.escape(k),
                       r'\g<1>#%s\g<2>' % v, s)
        if n != 1:
            print("WARN key", k, n)
    io.open(COLORS, "w", encoding="utf-8", newline="\n").write(s)


def set_fluent(accents):
    s = io.open(os.path.join(BACKUP, "FluentTheme.xaml"), encoding="utf-8").read()
    keys = ["AccentColorKey", "AccentColorDark1Key", "AccentColorDark2Key", "AccentColorDark3Key",
            "AccentColorLight1Key", "AccentColorLight2Key", "AccentColorLight3Key"]
    for k, v in zip(keys, accents):
        s, n = re.subn(r'(<Color x:Key="\{x:Static SystemColors.%s\}">)#[0-9A-Fa-f]{6}(</Color>)' % k,
                       r'\g<1>#%s\g<2>' % v, s)
        if n != 1:
            print("WARN fluent", k, n)
    io.open(FLUENT, "w", encoding="utf-8", newline="\n").write(s)


def restore():
    for f in ("Colors.xaml", "Brushes.xaml", "FluentTheme.xaml"):
        dst = os.path.join(ROOT, "src/DiagnosticFlashTool.App/Styles", f)
        src = os.path.join(BACKUP, f)
        io.open(dst, "w", encoding="utf-8", newline="\n").write(io.open(src, encoding="utf-8").read())


mode = sys.argv[1]
if mode == "A":
    set_colors(A); set_fluent(A_FLUENT)
elif mode == "C":
    set_colors(C); set_fluent(C_FLUENT)
else:
    restore()
print("applied", mode)
