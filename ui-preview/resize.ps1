Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr ins, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
$h = [IntPtr]([long]$env:DSH_HWND)
$dpi = [W.U]::GetDpiForWindow($h)
$scale = $dpi / 96.0
$w = [int](1180 * $scale); $hh = [int](760 * $scale)
[W.U]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, $w, $hh, 0x0004) | Out-Null
Start-Sleep -Milliseconds 700
$r = New-Object W.U+RECT
[W.U]::GetWindowRect($h, [ref]$r) | Out-Null
"dpi=$dpi scale=$scale size=$($r.Right-$r.Left)x$($r.Bottom-$r.Top) at $($r.Left),$($r.Top)"
