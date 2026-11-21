Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr ins, int x, int y, int cx, int cy, uint f);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
$exe = "D:\Work\1.Product\04_Boot刷写工具\主程序\src\DiagnosticFlashTool\src\DiagnosticFlashTool.App\bin\Debug\net10.0-windows\DiagnosticFlashTool.exe"
$out = "D:\Work\1.Product\04_Boot刷写工具\主程序\src\DiagnosticFlashTool\ui-preview"
$p = Start-Process -FilePath $exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break } }
if ($h -eq [IntPtr]::Zero) { "no window"; exit 1 }
Start-Sleep -Milliseconds 1200
$dpi = [W.U]::GetDpiForWindow($h); $sc = $dpi / 96.0
$w = [int](1180 * $sc); $hh = [int](760 * $sc)
[W.U]::SetWindowPos($h, [IntPtr]::Zero, 20, 20, $w, $hh, 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
$r = New-Object W.U+RECT; [W.U]::GetWindowRect($h, [ref]$r) | Out-Null
"dpi=$dpi target=${w}x${hh} actual=$($r.Right-$r.Left)x$($r.Bottom-$r.Top)"
function Capture($name) {
    $r = New-Object W.U+RECT; [W.U]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap(($r.Right-$r.Left), ($r.Bottom-$r.Top))
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [W.U]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save("$out\min-$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$pages = @("流程配置","系统设置","全部项目配置")
foreach ($name in $pages) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)))
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { "missing $name"; continue }
    ($el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 900
    Capture $name
    "captured min $name"
}
Stop-Process -Id $p.Id -Force
"done"
