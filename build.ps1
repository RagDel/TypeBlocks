param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$taskProject = Join-Path $taskRoot 'src\TypeBlocks\TypeBlocks.csproj'
$taskOutput = Join-Path $taskRoot 'artifacts\app'
& $taskDotnet publish $taskProject -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $taskOutput\TypeBlocks.exe"
