$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
& $taskDotnet run --project (Join-Path $taskRoot 'tests\TypeBlocks.Core.Tests\TypeBlocks.Core.Tests.csproj') -c Release -- --pure-text-preview (Join-Path $taskRoot 'artifacts\pure-text-review\cases.json')
if ($LASTEXITCODE -ne 0) { throw 'Encryption tests failed.' }
& $taskDotnet build (Join-Path $taskRoot 'src\TypeBlocks\TypeBlocks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
$taskApp = Join-Path $taskRoot 'src\TypeBlocks\bin\Release\net8.0-windows\TypeBlocks.exe'
$taskQa = Join-Path $taskRoot 'artifacts\qa'
$taskRun = Start-Process -FilePath $taskApp -ArgumentList @('--smoke-test', ('"' + $taskQa + '"')) -WindowStyle Hidden -PassThru
if (-not $taskRun.WaitForExit(45000)) { $taskRun.Kill(); throw "UI/native smoke test timed out. See $taskQa\progress.txt" }
if ($taskRun.ExitCode -ne 0) { throw "UI/native smoke test failed. See $taskQa\result.txt" }
$taskResult = Get-Content -LiteralPath (Join-Path $taskQa 'result.txt') -Raw
if (-not $taskResult.StartsWith('PASS:') -or (Get-Content -LiteralPath (Join-Path $taskQa 'progress.txt') -Tail 1) -ne 'Completed') { throw "UI/native smoke test did not finish. See $taskQa\progress.txt" }
Write-Output $taskResult
