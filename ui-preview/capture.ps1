Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
$exe = "D:\Work\1.Product\04_Boot刷写工具\主程序\src\DiagnosticFlashTool\src\DiagnosticFlashTool.App\bin\Debug\net10.0-windows\DiagnosticFlashTool.exe"
$out = "D:\Work\1.Product\04_Boot刷写工具\主程序\src\DiagnosticFlashTool\ui-preview"
$p = Start-Process -FilePath $exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { "no window"; exit 1 }
Start-Sleep -Milliseconds 1200

function Capture($name) {
    $r = New-Object W.U+RECT
    [W.U]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [W.U]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save("$out\shot-$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$pages = @("固件刷写","开发者选项","操作日志","项目配置","流程配置","算法配置","系统设置")
Capture "00-start"
foreach ($name in $pages) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)))
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { "missing: $name"; continue }
    $pat = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pat.Select()
    Start-Sleep -Milliseconds 900
    Capture $name
    "captured $name"
}
# 新增项目遮罩件
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NewProjectButton")
$b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$dump = ""
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) {
  if ($e.Current.Name -match "新增|新建") { $dump += ("[" + $e.Current.ControlType.ProgrammaticName + "] " + $e.Current.Name + " id=" + $e.Current.AutomationId + "`n") }
}
$dump | Out-File "$out\buttons.txt" -Encoding UTF8
Stop-Process -Id $p.Id -Force
"done"
