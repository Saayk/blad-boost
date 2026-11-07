# GameBoost - BLAD Studios
# Uso:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Deploy]
#   Genera en .\dist: GameBoost.exe, GameBoost_Setup.exe y version.json
#   -Deploy copia GameBoost_Setup.exe al Escritorio.
param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$dist = Join-Path $dir 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$version = '2.2.0'

# ---------- icono (PNG dentro de ICO, 256 px) ----------
if (-not (Test-Path "$dir\app.ico")) {
  Add-Type -AssemblyName System.Drawing
  $bmp = New-Object System.Drawing.Bitmap 256, 256
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $r = 60
  $path.AddArc(8, 8, $r, $r, 180, 90); $path.AddArc(248 - $r, 8, $r, $r, 270, 90)
  $path.AddArc(248 - $r, 248 - $r, $r, $r, 0, 90); $path.AddArc(8, 248 - $r, $r, $r, 90, 90); $path.CloseFigure()
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,0)), ([System.Drawing.Point]::new(256,256)), ([System.Drawing.Color]::FromArgb(138,107,255)), ([System.Drawing.Color]::FromArgb(70,100,240))
  $g.FillPath($brush, $path)
  $bolt = [System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(146,36), [System.Drawing.Point]::new(72,140), [System.Drawing.Point]::new(122,140),
    [System.Drawing.Point]::new(104,220), [System.Drawing.Point]::new(184,112), [System.Drawing.Point]::new(132,112))
  $g.FillPolygon([System.Drawing.Brushes]::White, $bolt)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $png = $ms.ToArray()
  $fs = [System.IO.File]::Create("$dir\app.ico")
  $bw = New-Object System.IO.BinaryWriter $fs
  $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
  $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
  $bw.Write($png); $bw.Close()
}

# ---------- compilar ----------
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = "$fw\csc.exe"
$refs = @("$fw\WPF\PresentationFramework.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll", "$fw\System.Xaml.dll",
          "$fw\System.Web.Extensions.dll", "$fw\System.Management.dll", "$fw\System.ServiceProcess.dll", "$fw\System.Drawing.dll",
          "$fw\System.Core.dll", "$fw\System.Windows.Forms.dll") | % { "/r:$_" }
$base = @('/nologo', '/target:winexe', '/optimize+', '/platform:x64', '/codepage:65001', "/win32icon:$dir\app.ico", "/resource:$dir\app.ico,app.ico") + $refs
$shared = @("$dir\Core.cs", "$dir\Services.cs", "$dir\Lang.cs")

function Build($out, $extra, $src) {
  & $csc @base @extra "/out:$out" @src
  if ($LASTEXITCODE -ne 0) { throw "build failed: $out" }
}

$appRes = @("/resource:$dir\MainWindow.xaml,MainWindow.xaml")
Build "$dir\GameBoost_test.exe" $appRes ($shared + "$dir\App.cs")
Build "$dist\GameBoost.exe" ($appRes + "/win32manifest:$dir\app.manifest") ($shared + "$dir\App.cs")

$setupRes = @("/resource:$dir\Setup.xaml,Setup.xaml", "/resource:$dist\GameBoost.exe,GameBoost.exe")
Build "$dir\Setup_test.exe" $setupRes ($shared + "$dir\Setup.cs")
Build "$dist\GameBoost_Setup.exe" ($setupRes + "/win32manifest:$dir\app.manifest") ($shared + "$dir\Setup.cs")

# ---------- version.json (subelo junto al instalador para publicar una actualizacion) ----------
@{ version = $version; url = 'https://github.com/bladstudios/gameboost/releases/latest/download/GameBoost_Setup.exe'; notes = 'Mejoras de rendimiento y correcciones.' } |
  ConvertTo-Json | Set-Content "$dist\version.json" -Encoding utf8

if ($Deploy) {
  $desk = [Environment]::GetFolderPath('Desktop')
  Copy-Item "$dist\GameBoost_Setup.exe" "$desk\GameBoost_Setup.exe" -Force
}
"BUILD OK $version"

