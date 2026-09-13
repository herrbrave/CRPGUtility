param([string]$Action='capture',[int]$TargetPid,[int]$X=0,[int]$Y=0,[string]$Path='C:\Users\Peter\wkspaces\CRPGUnity\TestProject\AgentScripts\desktop.png')
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DesktopNative {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
$p=Get-Process -Id $TargetPid -ErrorAction Stop
$h=$p.MainWindowHandle
if($h -eq 0) { throw 'Target has no window' }
[void][DesktopNative]::ShowWindow($h,9)
[void][DesktopNative]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 250
if ([DesktopNative]::GetForegroundWindow() -ne $h) {
 $shell=New-Object -ComObject WScript.Shell
 [void]$shell.AppActivate($TargetPid)
 Start-Sleep -Milliseconds 250
 if ([DesktopNative]::GetForegroundWindow() -ne $h) { throw 'Focus denied: no input or screenshot captured' }
}
$r=New-Object DesktopNative+RECT
$o=New-Object DesktopNative+POINT
[void][DesktopNative]::GetClientRect($h,[ref]$r)
[void][DesktopNative]::ClientToScreen($h,[ref]$o)
if($Action -eq 'click') {
 [void][DesktopNative]::SetCursorPos($o.X+$X,$o.Y+$Y)
 Start-Sleep -Milliseconds 100
 [DesktopNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 100
 [DesktopNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
}
if($Action -eq 'wheel') {
 [void][DesktopNative]::SetCursorPos($o.X+$X,$o.Y+$Y)
 [DesktopNative]::mouse_event(2048,0,0,120,[UIntPtr]::Zero)
}
if($Action -eq 'capture') {
 $bmp=New-Object System.Drawing.Bitmap ($r.Right-$r.Left),($r.Bottom-$r.Top)
 $g=[System.Drawing.Graphics]::FromImage($bmp)
 try { $g.CopyFromScreen($o.X,$o.Y,0,0,$bmp.Size);$bmp.Save($Path,[System.Drawing.Imaging.ImageFormat]::Png) } finally {$g.Dispose();$bmp.Dispose()}
}
@{pid=$TargetPid; action=$Action;width=$r.Right;height=$r.Bottom;path=$Path} | ConvertTo-Json
