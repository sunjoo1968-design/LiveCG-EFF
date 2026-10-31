$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskAssetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LiveCG.App\Assets'
New-Item -ItemType Directory -Path $taskAssetDirectory -Force | Out-Null
$taskIconStream = [IO.MemoryStream]::new()
$taskWriter = [IO.BinaryWriter]::new($taskIconStream)
$taskSizes = @(16,24,32,48,64,128,256)
$taskPngs = @()
foreach ($taskSize in $taskSizes) {
    $taskBitmap = [Drawing.Bitmap]::new($taskSize,$taskSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.ScaleTransform($taskSize/256.0,$taskSize/256.0)
    function Draw-IconRound([single]$x,[single]$y,[single]$w,[single]$h,[single]$r,[Drawing.Brush]$brush,[Drawing.Pen]$pen) {
        $taskPath=[Drawing.Drawing2D.GraphicsPath]::new();$d=$r*2
        try {$taskPath.AddArc($x,$y,$d,$d,180,90);$taskPath.AddArc($x+$w-$d,$y,$d,$d,270,90);$taskPath.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90);$taskPath.AddArc($x,$y+$h-$d,$d,$d,90,90);$taskPath.CloseFigure();if($brush){$taskGraphics.FillPath($brush,$taskPath)};if($pen){$taskGraphics.DrawPath($pen,$taskPath)}} finally {$taskPath.Dispose()}
    }
    $taskBg=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(10,10),[Drawing.Point]::new(246,246),[Drawing.ColorTranslator]::FromHtml('#203a50'),[Drawing.ColorTranslator]::FromHtml('#0d1727'))
    $taskAccent=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(40,44),[Drawing.Point]::new(216,168),[Drawing.ColorTranslator]::FromHtml('#7dffd2'),[Drawing.ColorTranslator]::FromHtml('#25bbd1'))
    $taskBorder=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#456073'),3)
    $taskAccentPen=[Drawing.Pen]::new($taskAccent,10)
    $taskInside=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#142c3e'))
    $taskTrack=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#435f74'),8)
    $taskWhite=[Drawing.Pen]::new([Drawing.Color]::White,6)
    $taskMint=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#60e5ba'))
    $taskBlue=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#5cb5ec'))
    try {
        Draw-IconRound 10 10 236 236 52 $taskBg $taskBorder
        Draw-IconRound 40 44 176 124 22 $taskInside $taskAccentPen
        $taskGraphics.FillPolygon($taskAccent,[Drawing.PointF[]]@([Drawing.PointF]::new(108,75),[Drawing.PointF]::new(108,136),[Drawing.PointF]::new(158,106)))
        $taskGraphics.DrawLine($taskTrack,44,194,212,194)
        Draw-IconRound 44 185 62 18 6 $taskMint $null
        Draw-IconRound 116 185 42 18 6 $taskBlue $null
        $taskGraphics.DrawLine($taskWhite,179,176,179,214);$taskGraphics.FillEllipse([Drawing.Brushes]::White,172,168,14,14)
        $taskPngStream=[IO.MemoryStream]::new()
        try {$taskBitmap.Save($taskPngStream,[Drawing.Imaging.ImageFormat]::Png);$taskPngs+=,$taskPngStream.ToArray()} finally {$taskPngStream.Dispose()}
        if ($taskSize -eq 256) {$taskBitmap.Save((Join-Path $taskAssetDirectory 'LiveCG.png'),[Drawing.Imaging.ImageFormat]::Png)}
    } finally {foreach($taskResource in @($taskBg,$taskAccent,$taskBorder,$taskAccentPen,$taskInside,$taskTrack,$taskWhite,$taskMint,$taskBlue,$taskGraphics,$taskBitmap)){$taskResource.Dispose()}}
}
$taskWriter.Write([uint16]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]$taskSizes.Count)
$taskOffset=6+16*$taskSizes.Count
for($i=0;$i -lt $taskSizes.Count;$i++) {$dimension=if($taskSizes[$i] -eq 256){0}else{$taskSizes[$i]};$taskWriter.Write([byte]$dimension);$taskWriter.Write([byte]$dimension);$taskWriter.Write([byte]0);$taskWriter.Write([byte]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]32);$taskWriter.Write([uint32]$taskPngs[$i].Length);$taskWriter.Write([uint32]$taskOffset);$taskOffset+=$taskPngs[$i].Length}
foreach($taskPng in $taskPngs){$taskWriter.Write([byte[]]$taskPng)}
[IO.File]::WriteAllBytes((Join-Path $taskAssetDirectory 'LiveCG.ico'),$taskIconStream.ToArray())
$taskWriter.Dispose();$taskIconStream.Dispose()
Write-Output 'LiveCG icon: 7 sizes generated.'
