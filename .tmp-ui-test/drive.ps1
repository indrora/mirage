param([string]$Action = "dump", [string]$WindowClass = "MainWindow", [string]$Pattern = ".")
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
if (-not ("Win32Snap" -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Snap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
}
"@
}
$pid2 = [int](Get-Content C:\Users\indrora\src\mirage\.tmp-ui-test\pid.txt)
$rootAE = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pid2)
$win = $null
foreach ($w in $rootAE.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
    if ($w.Current.ClassName -eq $WindowClass) { $win = $w; break }
}
if ($win -eq $null) { throw "window class '$WindowClass' not found" }
switch ($Action) {
  "dump" {
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
      $c = $e.Current
      $line = "$($c.ControlType.ProgrammaticName -replace 'ControlType.','') | '$($c.Name)'"
      if ($line -match $Pattern) { Write-Output $line }
    }
  }
  "snap" {
    Start-Sleep -Milliseconds 300
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [Win32Snap]::PrintWindow($hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save("C:\Users\indrora\src\mirage\.tmp-ui-test\shot.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Output "snapped"
  }
}
