#ifndef CAVE_GEOMETRY_PRIMITIVES_INCLUDED
#define CAVE_GEOMETRY_PRIMITIVES_INCLUDED

// ═══════════════════════════════════════════════════════════════════════════
// [Gate 5 Phase E-α.1] Cave Geometry Primitives Library
//
// 목적:
//   기존 sdCapsule (원통 통로) + sdSphere (구형 방) 단일 primitive 한계 해소.
//   지질 Case별로 다른 기하 특성을 표현할 수 있는 primitive 라이브러리 제공.
//
// 설계 원칙:
//   1. 모든 함수는 pure SDF (no side effects)
//   2. byte-identical: 라우터에서 기본값 Capsule/Sphere 선택 시 기존 sdCapsule/sdSphere 결과와 동일
//   3. Nyquist 안전: 모든 primitive의 최소 feature 크기가 voxelSize(0.15m) × 2 이상
//   4. 플레이어빌리티: minPassableWidth 2m 유지 가능한 파라미터 범위
//
// Primitive 카탈로그:
//   === 통로 (Tunnel) ===
//   sdBoxCapsule       — 사각 단면 통로 (퇴적암 수평 통로)
//   sdHexPrismCapsule  — 육각 단면 통로 (주상절리 기둥 사이)
//   sdSlotCapsule      — 좁고 높은 슬롯 통로 (협곡 slot)
//   sdCapsuleWithBlocks — 거친 블록 암반 통로 (Rocky 변주)
//
//   === 방 (Room) ===
//   sdPear             — 서양배 모양 (Karst 용식방)
//   sdDiscPlate        — 납작한 원판형 (Sedimentary 층 사이 방)
//   sdHexColumnSpace   — 육각 기둥 내부 공간 (Columnar 방)
//   sdCathedralVault   — 대성당 궁륭 (Colonnade / Boss)
//
// 호환성:
//   기존 sdCapsule (CaveBiomeMath.hlsl L51), sdSphere (compute shader 내부) 재선언 안 함.
//   신규 primitive들만 여기에 정의.
// ═══════════════════════════════════════════════════════════════════════════

// ───────────────────────────────────────────────────────────────────────────
// 공통 유틸
// ───────────────────────────────────────────────────────────────────────────

// Box SDF (axis-aligned, 중심 기준)
//   halfExtent는 반쪽 크기 벡터 (전체 크기의 절반)
float sdBox(float3 p, float3 halfExtent)
{
    float3 q = abs(p) - halfExtent;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// ═══════════════════════════════════════════════════════════════════════════
// [TUNNEL PRIMITIVES] — 통로 프리미티브
// ═══════════════════════════════════════════════════════════════════════════

// ───────────────────────────────────────────────────────────────────────────
// sdBoxCapsule — 사각 단면 통로
//
// 용도: Case 2 Sedimentary — 수평 퇴적층 사이 통로
// 특성:
//   - 사각 단면 (폭 wideR, 높이 lowR)
//   - capsule처럼 양 끝 반구 마감
//   - wideR > lowR 일 때 wide-low 통로 (퇴적암 갈라진 수평 틈)
//
// 파라미터:
//   p: 샘플 위치, a/b: 통로 시작/끝, wideR: 가로 반경, lowR: 세로 반경
//
// 플레이어빌리티:
//   wideR ≥ 1.0m, lowR ≥ 1.2m (플레이어 높이 2m 통과 보장)
// ───────────────────────────────────────────────────────────────────────────
float sdBoxCapsule(float3 p, float3 a, float3 b, float wideR, float lowR)
{
    float3 ab = b - a;
    float3 ap = p - a;
    float abLen2 = dot(ab, ab);
    if (abLen2 < 1e-6)
    {
        // 퇴화: sdBox fallback
        return sdBox(p - a, float3(wideR, lowR, wideR));
    }
    float t = saturate(dot(ap, ab) / abLen2);
    float3 closestOnAxis = a + ab * t;
    float3 localP = p - closestOnAxis;

    // 축 방향 기준 local coord로 변환 — XZ 평면을 단면으로 사용
    //   (완전 local frame 변환은 비용 큼. 근사: axis-aligned box 사용)
    float3 halfExt = float3(wideR, lowR, wideR);
    return sdBox(localP, halfExt);
}

// ───────────────────────────────────────────────────────────────────────────
// sdHexPrismCapsule — 육각 단면 통로
//
// 용도: Case 1 Columnar — 주상절리 기둥 사이 육각 통로
// 특성:
//   - 육각 단면 (6면체)
//   - capsule의 원형 단면을 hex로 대체
//
// 플레이어빌리티:
//   r ≥ 1.15m (육각 내접원 기준 플레이어 통과)
// ───────────────────────────────────────────────────────────────────────────
float sdHexPrismCapsule(float3 p, float3 a, float3 b, float r)
{
    float3 ab = b - a;
    float3 ap = p - a;
    float abLen2 = dot(ab, ab);
    if (abLen2 < 1e-6) return length(ap) - r;  // 퇴화

    float t = saturate(dot(ap, ab) / abLen2);
    float3 closestOnAxis = a + ab * t;
    float3 localP = p - closestOnAxis;

    // Hex prism SDF (Y축 기준, 간이 근사)
    //   정육각형 거리 공식: max of 3 symmetric half-plane distances
    const float2 k = float2(-0.8660254, 0.5);  // cos(30°)=-√3/2, sin(30°)=1/2
    float2 pxz = abs(localP.xz);
    pxz -= 2.0 * min(dot(k, pxz), 0.0) * k;
    float2 q = float2(length(pxz - float2(clamp(pxz.x, -k.y * r, k.y * r), r)),
                       abs(localP.y) - r * 0.5);  // Y 두께는 reduce
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
}

// ───────────────────────────────────────────────────────────────────────────
// sdSlotCapsule — 좁고 높은 슬롯 통로
//
// 용도: Case 5 Canyon — 협곡 slot 통로
// 특성:
//   - 가로 좁고 (width) 세로 길게 (height) 편평
//   - 협곡 모사
//
// 플레이어빌리티:
//   width ≥ 0.9m, height ≥ 3m
// ───────────────────────────────────────────────────────────────────────────
float sdSlotCapsule(float3 p, float3 a, float3 b, float width, float height)
{
    float3 ab = b - a;
    float3 ap = p - a;
    float abLen2 = dot(ab, ab);
    if (abLen2 < 1e-6) return sdBox(p - a, float3(width, height, width));

    float t = saturate(dot(ap, ab) / abLen2);
    float3 closestOnAxis = a + ab * t;
    float3 localP = p - closestOnAxis;

    // 가로 좁은 slot: width는 XZ 단면 축소, height는 Y 확장
    float3 halfExt = float3(width, height, width * 1.5);
    return sdBox(localP, halfExt);
}

// ───────────────────────────────────────────────────────────────────────────
// sdCapsuleWithBlocks — 거친 블록 암반 통로
//
// 용도: Case 4 Rocky — 거친 블록 변주 capsule
// 특성:
//   - 기본 capsule + 블록 형태 noise로 덩어리 변주
//   - noise 파라미터는 외부 전달 없이 pos만으로 결정 (deterministic)
//
// 파라미터:
//   p, a, b, r (원형 capsule), blockScale (블록 크기 m), blockAmp (돌출 amp m)
//
// 플레이어빌리티:
//   blockAmp ≤ r × 0.3 (통로 차단 방지)
// ───────────────────────────────────────────────────────────────────────────
float sdCapsuleWithBlocks(float3 p, float3 a, float3 b, float r, float blockScale, float blockAmp)
{
    float3 ab = b - a;
    float3 ap = p - a;
    float abLen2 = dot(ab, ab);
    if (abLen2 < 1e-6) return length(ap) - r;

    float t = saturate(dot(ap, ab) / abLen2);
    float3 closestOnAxis = a + ab * t;
    float dist = length(p - closestOnAxis);

    // Block pattern: floor(p / blockScale) 기반 hash
    float3 cell = floor(p / max(blockScale, 0.5));
    float h = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
    float blockOffset = (h - 0.5) * blockAmp;
    float safeAmp = min(blockAmp, r * 0.3);  // 플레이어빌리티 가드
    blockOffset = clamp(blockOffset, -safeAmp, safeAmp);

    return dist - r - blockOffset;
}

// ═══════════════════════════════════════════════════════════════════════════
// [ROOM PRIMITIVES] — 방 프리미티브
// ═══════════════════════════════════════════════════════════════════════════

// ───────────────────────────────────────────────────────────────────────────
// sdPear — 서양배 모양
//
// 용도: Case 0 Karst — 용식 확장된 비대칭 방
// 특성:
//   - 하부 넓음 (용식 확장), 상부 좁음 (천장 수렴)
//   - Y 비선형 스케일링으로 배 모양 생성
//
// 파라미터:
//   p, center (방 중심), r (기본 반경)
//
// 플레이어빌리티:
//   r ≥ 3m 권장 (하부 공간 확보)
// ───────────────────────────────────────────────────────────────────────────
float sdPear(float3 p, float3 center, float r)
{
    float3 local = p - center;
    // Y 기반 비선형 스케일: y<0 넓게, y>0 좁게
    float yNorm = local.y / max(r, 0.5);
    float scaleXZ = lerp(1.3, 0.6, smoothstep(-1.0, 1.0, yNorm));
    float2 xz = local.xz / max(scaleXZ, 0.3);
    return length(float3(xz.x, local.y, xz.y)) - r;
}

// ───────────────────────────────────────────────────────────────────────────
// sdDiscPlate — 납작한 원판형 방
//
// 용도: Case 2 Sedimentary — 퇴적층 사이 납작한 방
// 특성:
//   - 가로 넓고 (r) 세로 낮음 (thickness)
//   - 플레이어 시야 넓게 열린 공간
//
// 파라미터:
//   p, center, r (반경), thickness (높이 반경)
//
// 플레이어빌리티:
//   thickness ≥ 1.5m (플레이어 높이 보장)
// ───────────────────────────────────────────────────────────────────────────
float sdDiscPlate(float3 p, float3 center, float r, float thickness)
{
    float3 local = p - center;
    float2 d2 = float2(length(local.xz) - r, abs(local.y) - thickness);
    return min(max(d2.x, d2.y), 0.0) + length(max(d2, 0.0));
}

// ───────────────────────────────────────────────────────────────────────────
// sdHexColumnSpace — 육각 기둥 내부 공간
//
// 용도: Case 1 Columnar — 주상절리 방 (큰 육각 공간)
// 특성:
//   - 큰 육각 단면
//   - 높이 방향 자유
//
// 파라미터:
//   p, center, r (육각 내접원 반경), height (Y 반경)
// ───────────────────────────────────────────────────────────────────────────
float sdHexColumnSpace(float3 p, float3 center, float r, float height)
{
    float3 local = p - center;
    const float2 k = float2(-0.8660254, 0.5);
    float2 pxz = abs(local.xz);
    pxz -= 2.0 * min(dot(k, pxz), 0.0) * k;
    float2 q = float2(length(pxz - float2(clamp(pxz.x, -k.y * r, k.y * r), r)),
                       abs(local.y) - height);
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
}

// ───────────────────────────────────────────────────────────────────────────
// sdCathedralVault — 대성당 궁륭
//
// 용도: Case 3 Colonnade / Boss room — 높은 천장 대칭 공간
// 특성:
//   - 타원형 단면 (XZ 넓음)
//   - 천장 dome 높음 (Y 상부 확장)
//
// 파라미터:
//   p, center, r (XZ 반경), height (상부 dome 높이)
// ───────────────────────────────────────────────────────────────────────────
float sdCathedralVault(float3 p, float3 center, float r, float height)
{
    float3 local = p - center;
    // 하부: 원기둥
    float2 cyl = float2(length(local.xz) - r, local.y);
    float cylSDF = min(max(cyl.x, -cyl.y - height * 0.3), 0.0) + length(max(cyl, 0.0));

    // 상부: dome (타원체 상반부)
    float3 domeP = float3(local.x, max(local.y - height * 0.3, 0.0) * (r / max(height, 1.0)), local.z);
    float domeSDF = length(domeP) - r;

    return min(cylSDF, domeSDF);
}

#endif // CAVE_GEOMETRY_PRIMITIVES_INCLUDED
