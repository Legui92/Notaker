param([string]$Executable = 'artifacts/Notaker/Notaker.exe')
$ErrorActionPreference = 'Stop'
$binary = (Resolve-Path -LiteralPath $Executable).Path
$root = Join-Path (Get-Location) ('artifacts/qa/update-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
foreach ($scenario in @('success','rollback','hash-mismatch')) {
    $case = Join-Path $root $scenario
    $stage = Join-Path $case 'stage'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $target = Join-Path $case 'Notaker.exe'
    $candidate = Join-Path $stage 'Notaker.exe'
    Copy-Item -LiteralPath $binary -Destination $target
    Copy-Item -LiteralPath $binary -Destination $candidate
    $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    if ($scenario -eq 'hash-mismatch') { $hash = '0' * 64 }
    $parent = Start-Process -FilePath $env:ComSpec -ArgumentList '/c exit 0' -WindowStyle Hidden -PassThru
    $parent.WaitForExit()
    $plan = @{ Target=$target; Staged=$candidate; Sha256=$hash; ParentPid=$parent.Id; ParentStartTicks=0; TestOnly=$true; SimulateStartupFailure=($scenario -eq 'rollback') }
    $planPath = Join-Path $stage 'update.json'
    $plan | ConvertTo-Json | Set-Content -LiteralPath $planPath
    $helper = Join-Path $stage 'Notaker.Update.exe'
    Copy-Item -LiteralPath $binary -Destination $helper
    $process = Start-Process -FilePath $helper -ArgumentList @('--apply-update', ('"' + $planPath + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { throw "Update test timed out: $scenario" }
    $process.Refresh()
    if ($scenario -eq 'success') {
        if ($process.ExitCode -ne 0 -or -not (Test-Path (Join-Path $stage 'started.ok'))) { throw 'Update/relaunch failed' }
        Write-Output 'PASS: Update replaces executable and new process confirms startup'
    } else {
        if ($process.ExitCode -eq 0) { throw "Invalid update was accepted: $scenario" }
        if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $binary).Hash) { throw 'Original executable was not preserved' }
        Write-Output "PASS: Original executable preserved after $scenario"
    }
}
Write-Output "Artifacts: $root"
