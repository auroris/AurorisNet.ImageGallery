# Run against a framework-dependent Windows publish, using its own FFmpeg tools.
param([Parameter(Mandatory)][string] $PublishDirectory)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$scratch = [IO.Directory]::CreateTempSubdirectory('ImageGallery-video-')
$server = $null
try {
    # The app writes its log beside the executable. Keep the release output pristine.
    $publish = Join-Path $scratch 'app'
    Copy-Item -LiteralPath $source -Destination $publish -Recurse
    # Progress uses a named memory-mapped file per gallery directory. Give this run a unique name.
    $gallery = $scratch.CreateSubdirectory($scratch.Name).FullName
    $good = Join-Path $gallery 'good.mp4'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/video.mp4') -Destination $good

    # The synthetic H.264 fixture puts its metadata before the media payload. Both truncated
    # copies retain valid dimensions. One has no packets and the other has the start of its first packet.
    $bytes = [IO.File]::ReadAllBytes($good)
    $mdat = [Text.Encoding]::Latin1.GetString($bytes).IndexOf('mdat', [StringComparison]::Ordinal)
    if ($mdat -lt 0) { throw 'The fixture has no media payload.' }
    [IO.File]::WriteAllBytes((Join-Path $gallery 'empty.mp4'), $bytes[0..($mdat + 3)])
    [IO.File]::WriteAllBytes((Join-Path $gallery 'partial.mp4'), $bytes[0..($mdat + 8)])
    & (Join-Path $publish 'ffmpeg/ffmpeg.exe') -v error -nostdin -i $good -map 0:V:0 -c copy @(
        '-encryption_scheme', 'cenc-aes-ctr', '-encryption_key', '000102030405060708090a0b0c0d0e0f'
        '-encryption_kid', '101112131415161718191a1b1c1d1e1f', '-fflags', '+bitexact', (Join-Path $gallery 'encrypted.mp4')
    )
    if ($LASTEXITCODE -ne 0) { throw 'Could not generate the encrypted test input.' }

    $log = Join-Path $scratch 'server.log'
    $start = @{
        FilePath = (Join-Path $publish 'ImageGallery.exe'); WorkingDirectory = $publish; WindowStyle = 'Hidden'
        Environment = @{ GalleryPath = $gallery; TempDir = (Join-Path $scratch 'work'); ASPNETCORE_URLS = 'http://127.0.0.1:0' }
        RedirectStandardOutput = $log; RedirectStandardError = (Join-Path $scratch 'server.err'); PassThru = $true
    }
    $server = Start-Process @start
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        if ($server.HasExited) { throw "Gallery exited during startup. $(Get-Content $log -Raw)" }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Timed out waiting for the gallery.' }
        Start-Sleep -Milliseconds 100
    } until ((Get-Content -LiteralPath $log -Raw) -match 'Now listening on: (http://127\.0\.0\.1:\d+)')
    $url = $Matches[1]

    $lock = [IO.File]::Open($good, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $failure = Invoke-WebRequest "$url/api/dir/" -SkipHttpErrorCheck -TimeoutSec 20
        if ($failure.StatusCode -ne 500) { throw 'An access error was mistaken for an unsupported video.' }
    } finally {
        $lock.Dispose()
    }
    if ((Invoke-RestMethod "$url/api/dir/status/" -TimeoutSec 10).percent -ne 0) { throw 'Failed scan left progress active.' }

    $index = Invoke-RestMethod "$url/api/dir/" -TimeoutSec 20
    if ($index.'good.mp4'.type -ne 'video' -or $index.'good.mp4'.previewUnavailable) { throw 'Lost playable video.' }
    $null = Invoke-WebRequest "$url/api/media/thumb/good.mp4" -TimeoutSec 10
    foreach ($name in 'empty.mp4', 'partial.mp4', 'encrypted.mp4') {
        if ($index.$name.type -ne 'video' -or !$index.$name.previewUnavailable) { throw "Missing original-file fallback for $name." }
        $original = Invoke-WebRequest "$url/api/media/file/$name" -Headers @{Range = 'bytes=0-7'} -TimeoutSec 10
        if ($original.StatusCode -ne 206) { throw "Could not read the original $name." }
    }
    Write-Host 'Mixed video folder passed.'
} finally {
    if ($server -and !$server.HasExited) {
        $server.Kill($true)
        $server.WaitForExit()
    }
    if ($scratch.Parent.FullName.TrimEnd('\') -eq [IO.Path]::GetTempPath().TrimEnd('\')) {
        Remove-Item -LiteralPath $scratch.FullName -Recurse -Force
    }
}
