param([int]$TargetProcess,[string]$Destination,[long]$TargetWindow=0)
$ErrorActionPreference='Stop'
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class CaptureDpi {
 [StructLayout(LayoutKind.Sequential)] public struct R {public int L,T,Rt,B;}
 [DllImport("user32.dll")]public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr h,int attr,out R r,int n);
 [DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
}
"@
[CaptureDpi]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
[CaptureDpi]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
Add-Type -AssemblyName System.Drawing
$p=Get-Process -Id $TargetProcess
$captureHandle=if($TargetWindow -ne 0){[IntPtr]$TargetWindow}else{$p.MainWindowHandle}
$r=New-Object CaptureDpi+R
[CaptureDpi]::DwmGetWindowAttribute($captureHandle,9,[ref]$r,16)|Out-Null
Write-Output "Bounds=$($r.L),$($r.T),$($r.Rt),$($r.B) Visible=$([CaptureDpi]::IsWindowVisible($captureHandle))"
$bmp=New-Object Drawing.Bitmap ($r.Rt-$r.L),($r.B-$r.T)
$graphics=[Drawing.Graphics]::FromImage($bmp)
try{$dc=$graphics.GetHdc();try{if(![CaptureDpi]::PrintWindow($captureHandle,$dc,2)){throw 'PrintWindow failed'}}finally{$graphics.ReleaseHdc($dc)};$bmp.Save($Destination,[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bmp.Dispose()}
Write-Output $Destination
