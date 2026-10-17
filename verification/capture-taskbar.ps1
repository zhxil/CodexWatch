param([int]$TargetProcess,[string]$Destination)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class CapsuleCapture {
 public delegate bool CB(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rt,B;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr h,CB cb,IntPtr p);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool GetClientRect(IntPtr h,out R r);
 [DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static IntPtr Widget(uint pid){var found=IntPtr.Zero;EnumChildWindows(FindWindow("Shell_TrayWnd",null),(h,p)=>{GetWindowThreadProcessId(h,out var id);if(id==pid && IsWindowVisible(h)){found=h;return false;}return true;},IntPtr.Zero);return found;}
}
"@
$deadline=(Get-Date).AddSeconds(15)
do{$hwnd=[CapsuleCapture]::Widget($TargetProcess);if($hwnd -ne [IntPtr]::Zero){break};Start-Sleep -Milliseconds 200}while((Get-Date)-lt $deadline)
if($hwnd -eq [IntPtr]::Zero){throw 'Taskbar capsule not found'}
$r=New-Object CapsuleCapture+R;[CapsuleCapture]::GetClientRect($hwnd,[ref]$r)|Out-Null
$bmp=New-Object Drawing.Bitmap $r.Rt,$r.B
$g=[Drawing.Graphics]::FromImage($bmp)
try{$dc=$g.GetHdc();try{[CapsuleCapture]::PrintWindow($hwnd,$dc,2)|Out-Null}finally{$g.ReleaseHdc($dc)};$bmp.Save($Destination,[Drawing.Imaging.ImageFormat]::Png)}finally{$g.Dispose();$bmp.Dispose()}
Write-Output "Taskbar=$hwnd Size=$($r.Rt)x$($r.B) Capture=$Destination"
