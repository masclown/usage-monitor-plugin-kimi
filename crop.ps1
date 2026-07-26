Add-Type -AssemblyName System.Drawing
$inputPath = 'C:\Users\Watchin\AppData\Local\Temp\qoder-computer-use-images\57a5379a\img-1785056582640378400-858844.png'
$outputPath = 'd:\应用开发\UsageMonitor\deepseek_account_overview.png'
$originalImg = [System.Drawing.Image]::FromFile($inputPath)
$cropHeight = [int]($originalImg.Height * 0.52)
$cropTop = [int]($originalImg.Height * 0.38)
$sourceRect = New-Object System.Drawing.Rectangle(0, $cropTop, $originalImg.Width, $cropHeight)
$croppedBmp = New-Object System.Drawing.Bitmap($sourceRect.Width, $sourceRect.Height)
$graphics = [System.Drawing.Graphics]::FromImage($croppedBmp)
$graphics.DrawImage($originalImg, (New-Object System.Drawing.Rectangle(0, 0, $croppedBmp.Width, $croppedBmp.Height)), $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
$graphics.Dispose()
$croppedBmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$croppedBmp.Dispose()
$originalImg.Dispose()
Write-Host 'Cropped successfully'
