# =============================================================================
# [Step 0 · 계측] VerdictLogger 양측 diff — M5(전투 판정 일치율) 산출 스크립트
# 실행계획 v1.1 §0.A.2-2.
#
# 사용법:
#   powershell -File Tools\diff_verdicts.ps1 -HostDir <호스트 세션폴더> -ClientDir <클라 세션폴더>
#   (세션폴더 = %USERPROFILE%\AppData\LocalLow\<회사>\<제품>\NetDiagnostics\<timestamp>_pidN)
#
# 산출:
#   1. 이벤트 전달 일치율 — 양측 RECV 행을 (attacker, victim, phys, serverTime±0.5s)로 매칭,
#      한쪽에만 존재하는 사건(유실/디싱크)을 보고.
#   2. 체인 정합 — DEFENSE_EVAL 판정과 HP_APPLY 실행의 논리 일치:
#        · Hit       → HP_APPLY 1건 존재해야 함
#        · Blocked/Parried/Deflected → HP_APPLY 없어야 함
#   3. R6 카운터 — HP_APPLY가 공격자 머신에서 실행된 건수 (Step 2 이후 0이어야 함).
#   결과는 콘솔 + 같은 폴더의 verdict_diff_report.md 로 저장.
# =============================================================================
param(
    [Parameter(Mandatory = $true)][string]$HostDir,
    [Parameter(Mandatory = $true)][string]$ClientDir,
    [double]$TimeTolerance = 0.5
)

function Load-Verdicts([string]$dir) {
    $path = Join-Path $dir "verdicts.csv"
    if (-not (Test-Path $path)) {
        Write-Error "verdicts.csv 없음: $path"
        exit 1
    }
    Import-Csv $path
}

$hostRows = Load-Verdicts $HostDir
$clientRows = Load-Verdicts $ClientDir

function Get-Key($row) {
    # serverTime 0.5초 버킷 + 공격자/피격자/물리데미지
    $t = [math]::Round([double]$row.serverTime / $TimeTolerance)
    "$($row.attackerId)|$($row.victimId)|$($row.phys)|$t"
}

# --- 1. RECV 이벤트 전달 일치 -------------------------------------------------
$hostRecv = @{}
foreach ($r in ($hostRows | Where-Object { $_.kind -eq "RECV" })) { $hostRecv[(Get-Key $r)] = $r }
$clientRecv = @{}
foreach ($r in ($clientRows | Where-Object { $_.kind -eq "RECV" })) { $clientRecv[(Get-Key $r)] = $r }

$onlyHost = @($hostRecv.Keys | Where-Object { -not $clientRecv.ContainsKey($_) })
$onlyClient = @($clientRecv.Keys | Where-Object { -not $hostRecv.ContainsKey($_) })
$matched = @($hostRecv.Keys | Where-Object { $clientRecv.ContainsKey($_) })

$total = $matched.Count + $onlyHost.Count + $onlyClient.Count
$deliveryRate = if ($total -gt 0) { [math]::Round(100.0 * $matched.Count / $total, 1) } else { 100 }

# --- 2. 체인 정합 (호스트+클라 통합 뷰) ---------------------------------------
$allRows = @($hostRows) + @($clientRows)
$evals = $allRows | Where-Object { $_.kind -eq "DEFENSE_EVAL" }
$applies = @{}
foreach ($r in ($allRows | Where-Object { $_.kind -eq "HP_APPLY" })) {
    $k = Get-Key $r
    if (-not $applies.ContainsKey($k)) { $applies[$k] = 0 }
    $applies[$k]++
}

$chainViolations = @()
foreach ($e in $evals) {
    $k = Get-Key $e
    $hasApply = $applies.ContainsKey($k)
    if ($e.verdict -eq "Hit" -and -not $hasApply) {
        $chainViolations += "Hit인데 HP_APPLY 없음 — atk=$($e.attackerId) vic=$($e.victimId) t=$($e.serverTime) (유실 또는 디싱크)"
    }
    if ($e.verdict -in @("Blocked", "Parried", "Deflected") -and $hasApply) {
        $chainViolations += "$($e.verdict)인데 HP_APPLY 존재 — atk=$($e.attackerId) vic=$($e.victimId) t=$($e.serverTime) (판정 불일치)"
    }
}

# --- 3. R6 카운터 ---------------------------------------------------------------
function Get-AttackerSideCount([string]$dir) {
    $files = Get-ChildItem -Path $dir -Filter "counters_*.csv" -ErrorAction SilentlyContinue
    $max = 0
    foreach ($f in $files) {
        $row = Import-Csv $f.FullName | Where-Object { $_.counter -eq "verdict.hp_apply.attackerSide" }
        if ($row -and [long]$row.value -gt $max) { $max = [long]$row.value }
    }
    $max
}
$r6Host = Get-AttackerSideCount $HostDir
$r6Client = Get-AttackerSideCount $ClientDir

# --- 리포트 ---------------------------------------------------------------------
$report = @()
$report += "# Verdict Diff 리포트 (M5)"
$report += ""
$report += "- 호스트: $HostDir"
$report += "- 클라  : $ClientDir"
$report += "- 시간 허용오차: ${TimeTolerance}s"
$report += ""
$report += "## 1. 이벤트 전달 일치 (RECV 양측 매칭)"
$report += ""
$report += "| 매칭 | 호스트 단독 | 클라 단독 | 전달 일치율 |"
$report += "|---|---|---|---|"
$report += "| $($matched.Count) | $($onlyHost.Count) | $($onlyClient.Count) | $deliveryRate% |"
$report += ""
if ($onlyHost.Count -gt 0) { $report += "### 호스트에만 수신된 사건"; $onlyHost | ForEach-Object { $report += "- $_" }; $report += "" }
if ($onlyClient.Count -gt 0) { $report += "### 클라에만 수신된 사건"; $onlyClient | ForEach-Object { $report += "- $_" }; $report += "" }
$report += "## 2. 판정-적용 체인 정합 위반: $($chainViolations.Count)건"
$report += ""
$chainViolations | ForEach-Object { $report += "- $_" }
$report += ""
$report += "## 3. R6 카운터 (공격자 측 HP 차감 — Step 2 이후 0이어야 함)"
$report += ""
$report += "| 머신 | verdict.hp_apply.attackerSide |"
$report += "|---|---|"
$report += "| HOST | $r6Host |"
$report += "| CLIENT | $r6Client |"

$reportText = $report -join "`r`n"
Write-Output $reportText

$outPath = Join-Path $HostDir "verdict_diff_report.md"
$reportText | Out-File -FilePath $outPath -Encoding utf8
Write-Output ""
Write-Output "저장됨: $outPath"
