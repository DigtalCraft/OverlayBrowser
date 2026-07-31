param(
    [string]$SourcePath = (Join-Path $PSScriptRoot "..\OverlayBrowser\Assets\OverlayBrowser.png"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\OverlayBrowser\Assets\OverlayBrowser.ico")
)

Add-Type -AssemblyName System.Drawing

<#
ICO内へ格納する非圧縮BMP画像のサイズを取得する。
#>
function Get-IconImageSize {
    param([int]$Size)

    $maskRowBytes = [int]([math]::Ceiling($Size / 32.0) * 4)
    return 40 + ($Size * $Size * 4) + ($maskRowBytes * $Size)
}

<#
16bit符号なし整数をリトルエンディアンで書き込む。
#>
function Write-UInt16 {
    param(
        [System.IO.BinaryWriter]$Writer,
        [int]$Value
    )

    $Writer.Write([uint16]$Value)
}

<#
32bit符号なし整数をリトルエンディアンで書き込む。
#>
function Write-UInt32 {
    param(
        [System.IO.BinaryWriter]$Writer,
        [long]$Value
    )

    $Writer.Write([uint32]$Value)
}

<#
元画像を指定サイズへ縮小する。
#>
function New-IconBitmap {
    param(
        [System.Drawing.Bitmap]$SourceBitmap,
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($SourceBitmap, 0, 0, $Size, $Size)
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

<#
32bit BGRA画像と透過マスクをICOへ書き込む。
#>
function Write-IconImage {
    param(
        [System.IO.BinaryWriter]$Writer,
        [System.Drawing.Bitmap]$Bitmap,
        [int]$Size
    )

    $pixelBytes = $Size * $Size * 4
    $maskRowBytes = [int]([math]::Ceiling($Size / 32.0) * 4)
    $rectangle = [System.Drawing.Rectangle]::new(0, 0, $Size, $Size)
    $bitmapData = $Bitmap.LockBits(
        $rectangle,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $maskRows = New-Object object[] $Size

    try {
        # BITMAPINFOHEADER。高さは色画像と透過マスクの合計値を指定する。
        Write-UInt32 $Writer 40
        $Writer.Write([int32]$Size)
        $Writer.Write([int32]($Size * 2))
        Write-UInt16 $Writer 1
        Write-UInt16 $Writer 32
        Write-UInt32 $Writer 0
        Write-UInt32 $Writer $pixelBytes
        $Writer.Write([int32]0)
        $Writer.Write([int32]0)
        Write-UInt32 $Writer 0
        Write-UInt32 $Writer 0

        $row = [byte[]]::new($Size * 4)
        for ($y = $Size - 1; $y -ge 0; $y--) {
            [Array]::Clear($row, 0, $row.Length)
            [System.Runtime.InteropServices.Marshal]::Copy(
                [IntPtr]::Add($bitmapData.Scan0, $y * $bitmapData.Stride),
                $row,
                0,
                $row.Length)
            $Writer.Write($row)

            $maskRow = [byte[]]::new($maskRowBytes)
            for ($x = 0; $x -lt $Size; $x++) {
                if ($row[($x * 4) + 3] -lt 128) {
                    $maskIndex = [int]([math]::Floor($x / 8.0))
                    $maskRow[$maskIndex] = [byte](
                        $maskRow[$maskIndex] -bor (1 -shl (7 - ($x % 8))))
                }
            }
            $maskRows[$y] = $maskRow
        }

        for ($y = $Size - 1; $y -ge 0; $y--) {
            $Writer.Write([byte[]]$maskRows[$y])
        }
    }
    finally {
        $Bitmap.UnlockBits($bitmapData)
    }
}

$sourceBitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePath))
$sizes = @(256, 128, 64, 48, 32, 24, 16)
$images = foreach ($size in $sizes) {
    [pscustomobject]@{
        Size = $size
        Bitmap = New-IconBitmap -SourceBitmap $sourceBitmap -Size $size
    }
}

$directoryPath = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directoryPath -Force | Out-Null
$fileStream = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($fileStream)

try {
    Write-UInt16 $writer 0
    Write-UInt16 $writer 1
    Write-UInt16 $writer $images.Count

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        Write-UInt16 $writer 1
        Write-UInt16 $writer 32
        Write-UInt32 $writer (Get-IconImageSize -Size $image.Size)
        Write-UInt32 $writer $offset
        $offset += Get-IconImageSize -Size $image.Size
    }

    foreach ($image in $images) {
        Write-IconImage -Writer $writer -Bitmap $image.Bitmap -Size $image.Size
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
    $sourceBitmap.Dispose()
    foreach ($image in $images) {
        $image.Bitmap.Dispose()
    }
}

Write-Output "Created $OutputPath"
