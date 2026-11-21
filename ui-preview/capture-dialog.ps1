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
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break } }
if ($h -eq [IntPtr]::Zero) { "no window"; exit 1 }
Start-Sleep -Milliseconds 1500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$target = $null
foreach ($e in $all) {
    if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $e.Current.HelpText -match "新增项目") { $target = $e; break }
}
if ($null -eq $target) { "no new-project button"; Stop-Process -Id $p.Id -Force; exit 1 }
($target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
Start-Sleep -Milliseconds 1200
$r = New-Object W.U+RECT; [W.U]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap(($r.Right-$r.Left), ($r.Bottom-$r.Top))
$g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
[W.U]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save("$out\dialog-new-project.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"captured dialog"
Start-Process -FilePath "cmd.exe" -ArgumentList "/c","taskkill /F /PID $($p.Id)" -WindowStyle Hidden
"done"
