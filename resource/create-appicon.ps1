param([string]$PngPath, [string]$IcoPath)

Add-Type -AssemblyName System.Drawing

$bmp = [System.Drawing.Bitmap]::FromFile($PngPath)
$sizes = 256, 128, 64, 48, 32, 16

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$imgStreams = New-Object System.Collections.Generic.List[System.IO.MemoryStream]
foreach ($sz in $sizes) {
    $resized = New-Object System.Drawing.Bitmap($bmp, $sz, $sz)
    $imgMs = New-Object System.IO.MemoryStream
    $resized.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $imgStreams.Add($imgMs)
    $resized.Dispose()
}

$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $dim = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
    $bw.Write($dim)
    $bw.Write($dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$imgStreams[$i].Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $imgStreams[$i].Length
}

foreach ($imgMs in $imgStreams) {
    $bw.Write($imgMs.ToArray())
    $imgMs.Dispose()
}

$bmp.Dispose()
[System.IO.File]::WriteAllBytes($IcoPath, $ms.ToArray())
Write-Host "Generated: $IcoPath"
