using UnityEngine;

/// <summary>
/// VHS 및 드림코어 스타일의 불안정한 핸드헬드 카메라 움직임을 구현하는 스크립트입니다.
/// </summary>
public class VHSHandheldCamera : MonoBehaviour
{
    [Header("Position Sway (위치 흔들림)")]
    [Range(0f, 1f)] public float posAmount = 0.05f;      // 흔들림 범위
    [Range(0f, 5f)] public float posSpeed = 0.5f;       // 흔들림 속도

    [Header("Rotation Sway (회전 흔들림)")]
    [Range(0f, 5f)] public float rotAmount = 1.0f;      // 회전 각도 범위
    [Range(0f, 5f)] public float rotSpeed = 0.5f;       // 회전 속도

    [Header("VHS Micro Jitter (미세 떨림)")]
    [Tooltip("VHS 특유의 지직거리는 미세한 진동을 추가합니다.")]
    [Range(0f, 0.2f)] public float jitterAmount = 0.01f;
    [Range(0f, 60f)] public float jitterSpeed = 15f;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;

    // Perlin Noise를 위한 시드값
    private float _seedX;
    private float _seedY;
    private float _seedZ;

    void Start()
    {
        // 시작 시 위치와 회전값 저장
        _initialPosition = transform.localPosition;
        _initialRotation = transform.localRotation;

        // 매번 다른 패턴을 위해 랜덤 시드 설정
        _seedX = Random.value * 100f;
        _seedY = Random.value * 100f;
        _seedZ = Random.value * 100f;
    }

    void LateUpdate()
    {
        ApplySway();
    }

    private void ApplySway()
    {
        float time = Time.time;

        // 1. 위치 흔들림 (Perlin Noise 사용으로 부드럽고 불규칙하게)
        float pX = (Mathf.PerlinNoise(_seedX, time * posSpeed) - 0.5f) * 2f;
        float pY = (Mathf.PerlinNoise(_seedY, time * posSpeed) - 0.5f) * 2f;
        float pZ = (Mathf.PerlinNoise(_seedZ, time * posSpeed) - 0.5f) * 2f;

        Vector3 posOffset = new Vector3(pX, pY, pZ) * posAmount;

        // 2. 미세 떨림 (고주파 노이즈)
        if (jitterAmount > 0)
        {
            float jX = (Mathf.PerlinNoise(time * jitterSpeed, _seedX) - 0.5f) * 2f;
            float jY = (Mathf.PerlinNoise(time * jitterSpeed, _seedY) - 0.5f) * 2f;
            posOffset += new Vector3(jX, jY, 0) * jitterAmount;
        }

        // 3. 회전 흔들림
        float rX = (Mathf.PerlinNoise(_seedZ, time * rotSpeed) - 0.5f) * 2f;
        float rY = (Mathf.PerlinNoise(_seedX, time * rotSpeed) - 0.5f) * 2f;
        float rZ = (Mathf.PerlinNoise(_seedY, time * rotSpeed) - 0.5f) * 2f;

        Quaternion rotOffset = Quaternion.Euler(new Vector3(rX, rY, rZ) * rotAmount);

        // 결과 적용
        transform.localPosition = _initialPosition + posOffset;
        transform.localRotation = _initialRotation * rotOffset;
    }
}