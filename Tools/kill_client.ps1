# =============================================================================
# [Step 0 · 계측] 강제 끊김 매크로 — SCN-02(끊김·재호스팅) 반복용
# 실행계획 v1.1 §0.A.3-3.
#
# 클라이언트 게임 프로세스를 강제 종료(crash 시뮬레이션 = Alt+F4보다 더 거친 단절)한다.
# P0-2 베이스라인: 이 직후 호스트의 events.csv 에
#   TRANSPORT-RAW "Server.OnDisconnected" → TRANSPORT-EVT "Connect"  ← 오발화 짝
# 이 기록되는 것이 증거다. (Step 1 수정 후에는 TRANSPORT-EVT "Disconnect"가 떠야 정상)
#
# 사용법:
#   powershell -File Tools\kill_client.ps1                  # 기본 프로세스명 pB
#   powershell -File Tools\kill_client.ps1 -ProcessName pB -Repeat 5 -DelaySeconds 20
#     → 20초 간격으로 5회 강제 종료 (각 회차 사이에 수동으로 클라를 다시 띄울 것)
# =============================================================================
param(
    [string]$ProcessName = "pB",
    [int]$Repeat = 1,
    [int]$DelaySeconds = 20
)

for ($i = 1; $i -le $Repeat; $i++) {
    $procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Output "[$i/$Repeat] '$ProcessName' 프로세스 없음 — 클라이언트가 떠 있는지 확인"
    }
    else {
        foreach ($p in $procs) {
            Write-Output "[$i/$Repeat] $(Get-Date -Format HH:mm:ss.fff) 강제 종료: $($p.ProcessName) (PID $($p.Id))"
            Stop-Process -Id $p.Id -Force -Confirm:$false
        }
    }
    if ($i -lt $Repeat) {
        Write-Output "  ... ${DelaySeconds}s 대기 (이 사이에 클라 재기동)"
        Start-Sleep -Seconds $DelaySeconds
    }
}
Write-Output "완료 — 호스트 세션폴더의 events.csv 타임라인을 확인하세요."
