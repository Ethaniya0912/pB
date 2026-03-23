/// <summary>
/// [Dreamcore Stencil Registry — Phase 0]
///
/// 설계 근거:
///   Unity URP에서 스텐실 버퍼는 렌더러 전체가 공유하는 단 하나의 8비트(0~255) 버퍼입니다.
///   여러 Renderer Feature가 동일한 스텐실 값을 기록하면 서로 마스킹 조건이 충돌하여
///   특정 Feature의 효과가 다른 Feature의 오브젝트에 잘못 적용되거나 누락됩니다.
///
///   이 파일은 프로젝트 전체에서 스텐실 값을 한 곳에서 관리하는 레지스트리 역할을 합니다.
///   모든 Feature는 숫자를 하드코딩하는 대신 반드시 이 클래스의 상수를 참조해야 합니다.
///
/// 현재 할당된 값:
///   0        — 기본 (어떤 Feature도 기록하지 않은 픽셀)
///   1        — MultiLayerStencilFeature PlayerEffectGroup (기존)
///   2        — MultiLayerStencilFeature EnemyGroup (기존, Inspector에서 확인 필요)
///   10       — ObjectMotionBlur 대상 오브젝트 마킹 (Phase 3 신규)
///   11       — ScreenSpaceMotionBlur 제외 영역 (Phase 2 예약)
///   12~255   — 미사용 예약
///
/// [주의] MultiLayerStencilFeature.cs는 Inspector에서 stencilReference를 설정합니다.
///        현재 기본값이 1이며, 두 번째 레이어가 2를 사용 중인지 씬에서 직접 확인하세요.
///        만약 기존 레이어 중 하나가 10 이상의 값을 사용 중이라면 이 파일의 값을 조정하세요.
/// </summary>
public static class DreamcoreStencilRegistry
{
    // ── 기존 시스템 (MultiLayerStencilFeature) ──────────────────
    /// <summary>플레이어 그룹. MultiLayerStencilFeature 기본값.</summary>
    public const int PlayerEffectGroup = 1;

    /// <summary>적군 그룹. MultiLayerStencilFeature 두 번째 레이어 (Inspector 확인 필요).</summary>
    public const int EnemyGroup = 2;

    // ── 신규 시스템 (Phase 3) ────────────────────────────────────
    /// <summary>
    /// ObjectMotionBlur 적용 대상 오브젝트에 기록하는 값.
    /// ObjectMotionBlur.shader의 Pass에서 Stencil { Ref 10; Pass Replace; } 로 기록됩니다.
    /// ScreenSpaceMotionBlurFeature는 이 값이 기록된 픽셀을 블러에서 제외합니다.
    /// </summary>
    public const int ObjectMotionBlur = 10;

    /// <summary>ScreenSpaceMotionBlur가 제외하는 영역 예약값 (Phase 2 선행 예약).</summary>
    public const int ScreenSpaceExclude = 11;

    // ── 디버그 유틸리티 ──────────────────────────────────────────
    /// <summary>
    /// 현재 레지스트리 내용을 콘솔에 출력합니다.
    /// 씬 시작 시 DreamcoreBootstrap 등에서 호출하여 값 충돌 여부를 확인할 수 있습니다.
    /// </summary>
    public static void PrintRegistry()
    {
        UnityEngine.Debug.Log(
            "[DreamcoreStencilRegistry]\n" +
            $"  PlayerEffectGroup  = {PlayerEffectGroup}\n" +
            $"  EnemyGroup         = {EnemyGroup}\n" +
            $"  ObjectMotionBlur   = {ObjectMotionBlur}\n" +
            $"  ScreenSpaceExclude = {ScreenSpaceExclude}\n" +
            "  12~255 = 미사용 예약"
        );
    }
}
