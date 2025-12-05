Add-Type -AssemblyName System.Drawing

$pngPath = "C:\Users\badwo\Documents\Wanzhi\Wanzhi\Resources\logo.png"
$icoPath = "C:\Users\badwo\Documents\Wanzhi\Wanzhi\Resources\logo.ico"

$img = [System.Drawing.Image]::FromFile($pngPath)
$thumb = $img.GetThumbnailImage(256, 256, $null, [IntPtr]::Zero)

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# Header
$bw.Write([int16]0)   # Reserved
$bw.Write([int16]1)   # Type (1=ICO)
$bw.Write([int16]1)   # Count

# Image Entry
$bw.Write([byte]0)    # Width (0=256)
$bw.Write([byte]0)    # Height (0=256)
$bw.Write([byte]0)    # ColorCount
$bw.Write([byte]0)    # Reserved
$bw.Write([int16]1)   # Planes
$bw.Write([int16]32)  # BitCount
$bw.Write([int]$ms.Length) # SizeInBytes (placeholder)
$bw.Write([int]22)    # FileOffset

# Image Data (PNG format for 256x256)
$pngMs = New-Object System.IO.MemoryStream
$thumb.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngMs.ToArray()
$bw.Write($pngBytes)

# Update Size
$size = $pngBytes.Length
$ms.Position = 14
$bw.Write([int]$size)

# Save to file
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())

$img.Dispose()
$thumb.Dispose()
$ms.Dispose()
$pngMs.Dispose()
$bw.Dispose()

Write-Host "ICO created at $icoPath"
