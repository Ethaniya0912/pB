using UnityEngine;
using TDA.Cameras;

namespace TDA.World.EditorTools
{
    /// <summary>
    /// [프리뷰어] 씬 뷰(Scene View)에서 CameraStancePresetSO 데이터를 시각적으로 편집하기 위한 프록시(Proxy) 컴포넌트입니다.
    /// 인게임에는 아무런 영향을 주지 않으며, 에디터 상에서만 작동합니다.
    /// 플레이어(Player) 프리팹이나 씬에 있는 더미 모델에 부착하여 사용하세요.
    /// </summary>
    [ExecuteAlways]
    public class CameraDirectingPreviewer : MonoBehaviour
    {
        [Header("Target Stance (편집할 SO 에셋)")]
        [Tooltip("씬 뷰에서 위치와 FOV를 조절할 1계층 카메라 스탠스 SO를 연결하세요.")]
        public CameraStancePresetSO targetStance;

        [Header("Preview Settings (미리보기 설정)")]
        [Tooltip("캐릭터의 질량 중심(가슴) 높이입니다. PlayerCamera의 연산 기준점과 일치시켜야 합니다.")]
        public float massCenterHeight = 1.4f;

        [Tooltip("씬 뷰에 그려질 가상의 카메라 기즈모 색상입니다.")]
        public Color gizmoColor = new Color(0.2f, 0.8f, 1.0f, 0.8f);

        private void OnDrawGizmos()
        {
            if (targetStance == null) return;

            // 1. 질량 중심(Pivot) 계산
            Vector3 massCenter = transform.position + (Vector3.up * massCenterHeight);

            // 2. SO 데이터 기반 카메라 위치 계산
            Vector3 cameraPosition = massCenter
                                   + (transform.right * targetStance.baseOffset.x)
                                   + (transform.forward * targetStance.baseOffset.z)
                                   + (Vector3.up * (targetStance.baseOffset.y - massCenterHeight));

            // 3. 카메라 방향 계산 (캐릭터 앞쪽 10m 지점을 바라보도록 대략적 정렬)
            Vector3 lookTarget = massCenter + (transform.forward * 10f);
            Quaternion cameraRotation = Quaternion.LookRotation(lookTarget - cameraPosition);

            // Z-Tilt (Dutch Angle) 적용
            cameraRotation *= Quaternion.Euler(0, 0, targetStance.zTilt);

            // 4. 기즈모 렌더링 (카메라 본체와 시야각 Frustum)
            Gizmos.color = gizmoColor;

            // 카메라와 중심점 사이의 연결선
            Gizmos.DrawLine(massCenter, cameraPosition);

            // 질량 중심점 표시
            Gizmos.DrawSphere(massCenter, 0.1f);

            // 카메라 위치와 방향을 기준으로 매트릭스 변환
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(cameraPosition, cameraRotation, Vector3.one);

            // 가상의 렌즈 뷰(Frustum) 그리기 (FOV 반영)
            Gizmos.DrawFrustum(Vector3.zero, targetStance.fov, 2.0f, 0.1f, 1.77f); // 1.77 = 16:9 비율

            Gizmos.matrix = oldMatrix;
        }
    }
}