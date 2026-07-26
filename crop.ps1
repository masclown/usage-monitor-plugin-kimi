Add-Type -AssemblyName System.Drawing
 = 'C:\Users\Watchin\AppData\Local\Temp\qoder-computer-use-images\57a5379a\img-1785056582640378400-858844.png'
 = 'd:\应用开发\UsageMonitor\deepseek_account_overview.png'
 = [System.Drawing.Image]::FromFile()
 = [int](.Height * 0.52)
 = [int](.Height * 0.38)
 = New-Object System.Drawing.Rectangle(0, , .Width, )
 = New-Object System.Drawing.Bitmap(.Width, .Height)
 = [System.Drawing.Graphics]::FromImage()
.DrawImage(, (New-Object System.Drawing.Rectangle(0,0,.Width,.Height)), , [System.Drawing.GraphicsUnit]::Pixel)
.Dispose()
.Save(, [System.Drawing.Imaging.ImageFormat]::Png)
.Dispose()
.Dispose()
Write-Host 'Cropped successfully'
