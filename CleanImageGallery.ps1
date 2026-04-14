Get-ChildItem * -Include _meta.json -Recurse | Remove-Item
$files = Get-ChildItem -recurse | % { gi $_.FullName -stream * } | Where stream -ne ':$Data';
Foreach ($file in $files)
{
    Remove-Item -Path $file.FileName -Stream $file.Stream;
    Write-Output "Deleted $($file.FileName):$($file.Stream)";
}