using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-G Stage 3-E + 3-F] PassageMetric
    //
    // 두 가지 layer 제공:
    //   1) RiskScore — passage 자체의 4원칙 위험 수치화 (Stage 3-E)
    //      L/W/B/M/C/Y 6 metric × biome pair multiplier → Safe/Caution/Critical
    //
    //   2) PassageProfile — 10-segment sampling으로 유동적 속성 표시 (Stage 3-F)
    //      구간별 width/biome/risk 변화 + Junction Type 자동 분류
    //
    // 데이터는 CPU-only 저장 (GPU 전송 불필요, EdgeData stride 유지).
    // ══════════════════════════════════════════════════════════════════════

    public static class PassageMetric
    {
        public enum RiskLevel { Safe, Caution, Critical }

        // ═══════════════════════════════════════════════════════════════════
        // [Phase 15] ★ 5m minSafeWidth 정책 반영 — Crawl 제거 + 4단계 width
        //
        //   ★ 사용자 지적: minSafeWidth = 5.0f 정책상 Crawl(<1.5m)/Corridor(1.5~3m)
        //                   는 빌드 안 됨. δ Mid-node 분할 또는 ε per-segment 처리.
        //
        //   ★ Junction Type은 모두 5m+ 보장된 통로에 부여:
        //     Narrow   5~7m   — sidePathWidth + G2 expansion
        //     Standard 7~10m  — 일반 통로 (★ fallback)
        //     Wide     10~15m — mainPathWidth + G2 (Gallery 격상)
        //     Grand    >15m   — Boss 진입 grand path (Matrix override)
        //
        //   ★ 5m 미달 통로는 ★ Sub5mEmergency 마킹 (Visualizer 경고 + δ/ε 후보)
        //
        //   기존 Crawl/Corridor/Gallery enum 값은 ★ 호환성 유지를 위해 alias:
        //     Crawl  → Sub5mEmergency (★ 5m 미달 — 빌드 위반)
        //     Corridor → Standard (대부분 7~10m로 진화)
        //     Gallery → Wide (격상)
        // ═══════════════════════════════════════════════════════════════════
        public enum PassageJunctionType
        {
            // ★ Phase 15 — 5m 정책 반영 (★ enum 값 0~7은 호환 유지)
            Sub5mEmergency = 0, // ★ 5m 미달 (★ 정책 위반 — δ/ε 처리 필요), 기존 Crawl alias
            Standard       = 1, // ★ 7~10m 일반 통로 (★ fallback), 기존 Corridor alias
            Wide           = 2, // ★ 10~15m 넓은 통로 (★ Gallery 격상)
            Chimney        = 3, // |Yslope| > 0.7
            Ramp           = 4, // 0.3 ~ 0.7 slope
            Bridge         = 5, // waypoint + Yoffset > 4m
            Collapse       = 6, // Rocky biome 관통
            Spiral         = 7, // curvAmp > 2.5 + length > 15m
            // ★ Phase 15 신규 (8~)
            Narrow         = 8, // ★ 5~7m 좁은 통로 (긴장감)
            Grand          = 9, // ★ >15m Boss 진입 grand
            // ─── 호환 alias (★ deprecated, 기존 코드 호환만) ───
            Crawl    = Sub5mEmergency, // ★ 호환 alias
            Corridor = Standard,        // ★ 호환 alias
            Gallery  = Wide             // ★ 호환 alias
        }

        // ─────────────────────────────────────────────────────────────────
        // RiskScore — Stage 3-E
        // ─────────────────────────────────────────────────────────────────
        public struct RiskScore
        {
            public float lengthRisk;     // R_L  (length / 30m)
            public float narrowRisk;     // R_W  (1 - (width-1)/5)
            public float blendRisk;      // R_B  (blend coverage along edge)
            public float biomeMixRisk;   // R_M  (1 if biomeA != biomeB)
            public float curvatureRisk;  // R_C  (curvAmp / width)
            public float waypointRisk;   // R_Y  (numWaypoints / 2)
            public float pairMultiplier; // biome 쌍 특수 가중
            public float total;          // [0, 1]
            public RiskLevel level;

            public int biomeA;
            public int biomeB;

            // [Phase 4.5-G P2.E] γ Single-Source 위험도 — Plain Passage Risk
            //   양 끝 모두 blend zone (centrality 높음)일 때 amp ~0 (plain wall) 위험.
            //   계산 조건 (afterf3 PassageProfile 데이터 활용):
            //     - cross-biome (biomeA != biomeB) 또는 양 끝 segments centrality 높음
            //     - blendRisk 높음 (≥ 0.4)
            //     - length ≥ 8m (짧으면 영향 작음)
            //   범위 [0, 1]:
            //     0.0 ~ 0.3: Safe
            //     0.3 ~ 0.6: Moderate (Centrality-Aware Spawn 고려)
            //     0.6 ~ 1.0: Critical (★ 노드 재배치 또는 edge 우회 권장)
            public float plainPassageRisk;
            public bool isPlainRisk;     // plainPassageRisk >= 0.6 인지 (Critical flag)

            public Color GetLevelColor()
            {
                switch (level)
                {
                    case RiskLevel.Safe:     return new Color(0.25f, 0.73f, 0.31f);
                    case RiskLevel.Caution:  return new Color(0.82f, 0.60f, 0.13f);
                    case RiskLevel.Critical: return new Color(0.97f, 0.32f, 0.29f);
                }
                return Color.gray;
            }

            public string GetLevelStr()
            {
                switch (level)
                {
                    case RiskLevel.Safe:     return "Safe";
                    case RiskLevel.Caution:  return "Caution";
                    case RiskLevel.Critical: return "Critical";
                }
                return "?";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // PassageProfile — Stage 3-F (유동적 정보)
        // ─────────────────────────────────────────────────────────────────
        public struct PassageSegment
        {
            public float t;              // [0, 1] along passage
            public Vector3 worldPos;
            public int biomeIdx;         // ★ DEPRECATED — bIdxA만 (legacy 호환). biomeIdxA 사용 권장.
            public float blendWeight;    // raw frac (시각화용 그라디언트)
            public float blendCentrality;// boundary proximity (실제 transition zone)
            public float localWidth;     // edge.width × (1 + boost × proximity)
            public bool inWaypointSegment;
            public uint activeEffects;   // 활성 noise/effect 비트마스크

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Fix] biome A/B/effective 명확화
            //
            //   문제: 기존 biomeIdx는 bIdxA (= floor(scaledMap))만 저장하여
            //         GPU 실제 평가 biome ((bw<0.5)?typeA:typeB)과 불일치.
            //         사용자 보고: panel '#0(Karst)' 표기인데 mesh는 Columnar 형태.
            //
            //   해결: biomeIdxA = bIdxA (그대로), biomeIdxB = bIdxA+1 (신규)
            //         effective biome = (bw<0.5) ? biomeIdxA : biomeIdxB
            //         downstream 코드 (Predictive amp, panel info, SS cube)
            //         는 effective 사용으로 GPU와 일치.
            // ═══════════════════════════════════════════════════════════════
            public int biomeIdxA;        // = floor(scaledMap), GPU의 bIdxA
            public int biomeIdxB;        // = bIdxA + 1, GPU의 bIdxB

            /// <summary>
            /// GPU가 실제로 평가하는 biome.
            /// Single-Source: bw<0.5 → biomeIdxA, bw>0.5 → biomeIdxB.
            /// dual blend (Legacy)에서도 이 값으로 visualize하면 dominant biome 표시.
            /// </summary>
            public int EffectiveBiomeIdx
                => (blendWeight < 0.5f) ? biomeIdxA : biomeIdxB;

            // [신규] Per-segment risk contributions (mini-graph용)
            //   각 metric의 "이 segment 기여도" — 전체 평균과 다를 수 있음
            //   범위 [0, 1] — UI bar 그래프에 직접 사용
            public float segLengthRisk;     // 이 segment가 전체 length 위험에 기여 (균일)
            public float segNarrowRisk;     // localWidth 기반 (segment 별 다름)
            public float segBlendRisk;      // boundary proximity (segment 별 다름)
            public float segMixRisk;        // 이 segment가 다른 biome인지 (binary)
            public float segCurvatureRisk;  // 균일 (edge 단위)
            public float segWaypointRisk;   // 균일 (edge 단위)

            // ═══════════════════════════════════════════════════════════════
            // [Phase 4.5-G η DebugAware Phase 2] Predictive Measurement fields
            //
            //   목적: STAGE 1 (GraphBuilder)에서 STAGE 3 (GPU Compute) 결과 예측.
            //         GPU mesh가 좁아질 때까지 기다리지 않고 사전에 측정.
            //
            //   채워지는 조건: η DebugAware OR ε FullMerge State에서만
            //                 (δ 이하에서는 default 값 0/false 유지)
            //
            //   default (η OFF 시): 모두 0/false → 기존 코드 영향 없음 (byte-identical)
            // ═══════════════════════════════════════════════════════════════

            // [Single-Source 추적] 결함 #1 — γ Single-Source가 어느 case 선택했는지
            //   blendWeight < 0.5: typeA 선택 (singleSourceTypeA = biomeIdx)
            //   blendWeight > 0.5: typeB 선택 (singleSourceTypeB = biomeIdx)
            //   == 0.5: blank (둘 다 -1)
            //   γ 비활성 (α/β): dual blend → state=3
            public int singleSourceTypeA;       // typeA 선택 시 biome idx, 아니면 -1
            public int singleSourceTypeB;       // typeB 선택 시 biome idx, 아니면 -1
            public int singleSourceState;       // 0=A, 1=B, 2=blank, 3=dual (γ OFF)
            public float singleSourceAmpScale;  // pow(1-centrality, FadePower)

            // [Predictive Measurement] STAGE 3 결과 예측
            //   PREDICTIVE_AMP_LOOKUP + 인접 edges fan 정보로 추정
            public float predNoiseAmp;          // 4원칙 lookup × ampScale (실효 amp)
            public float predSminPenetration;   // 인접 edges fan 깊이 (m)
            public int predNearbyEdgeCount;     // 5m 이내 인접 edge 수
            public float predEffectiveWidth;    // = localWidth - sminPen - noiseAmp×2 - curveImpact
            public bool predIsBelowSafeMargin;  // effectiveWidth < MIN_SAFE_WIDTH (★ 5.0m, 이전 2.0m)
        }

        // ════════════════════════════════════════════════════════════════
        // [Stage 3-F+] Active Effects 비트마스크 — 디버그용
        //   각 passage segment에서 어떤 noise/effect가 활성인지 추적.
        //   디자이너가 "이 좁은 구간에 어떤 효과가 작동 중인가" 확인용.
        // ════════════════════════════════════════════════════════════════
        public const uint EFX_BIOME_KARST       = 1u << 0;   // Perlin fbm
        public const uint EFX_BIOME_COLUMNAR    = 1u << 1;   // Hex column space
        public const uint EFX_BIOME_SEDIMENTARY = 1u << 2;   // BoxWideLow + Disc
        public const uint EFX_BIOME_COLONNADE   = 1u << 3;   // Cathedral vault
        public const uint EFX_BIOME_ROCKY       = 1u << 4;   // Capsule blocks
        public const uint EFX_BIOME_CANYON      = 1u << 5;   // Slot canyon
        public const uint EFX_BIOME_MARINE      = 1u << 6;   // Notch + Scallop
        public const uint EFX_TRANSITION_ZONE   = 1u << 7;   // Boundary proximity > 0.1
        public const uint EFX_BLEND_DAMPING     = 1u << 8;   // ampDampingFactor < 0.85
        public const uint EFX_CURVATURE         = 1u << 9;   // curvatureAmp 활성
        public const uint EFX_WAYPOINT          = 1u << 10;  // numWaypoints > 0
        public const uint EFX_WIDTH_BOOSTED     = 1u << 11;  // [D4] width 확장됨
        public const uint EFX_WIDTH_NARROWED    = 1u << 12;  // [D4] width 축소됨
        public const uint EFX_SEDIMENT          = 1u << 13;  // Sediment shader 효과
        public const uint EFX_FLOOR_DETAIL      = 1u << 14;  // FloorDetail 효과
        public const uint EFX_CANYON_VARIATION  = 1u << 15;  // canyonWeight 활성

        public static string EffectsToString(uint efx)
        {
            if (efx == 0) return "(none)";
            var sb = new System.Text.StringBuilder();
            // Biome
            if ((efx & EFX_BIOME_KARST) != 0)       sb.Append("Karst ");
            if ((efx & EFX_BIOME_COLUMNAR) != 0)    sb.Append("Columnar ");
            if ((efx & EFX_BIOME_SEDIMENTARY) != 0) sb.Append("Sedimentary ");
            if ((efx & EFX_BIOME_COLONNADE) != 0)   sb.Append("Colonnade ");
            if ((efx & EFX_BIOME_ROCKY) != 0)       sb.Append("Rocky ");
            if ((efx & EFX_BIOME_CANYON) != 0)      sb.Append("Canyon ");
            if ((efx & EFX_BIOME_MARINE) != 0)      sb.Append("Marine ");
            // Modifiers
            if ((efx & EFX_TRANSITION_ZONE) != 0)   sb.Append("[Transition] ");
            if ((efx & EFX_BLEND_DAMPING) != 0)     sb.Append("[Damped] ");
            if ((efx & EFX_CURVATURE) != 0)         sb.Append("[Curved] ");
            if ((efx & EFX_WAYPOINT) != 0)          sb.Append("[WP] ");
            if ((efx & EFX_WIDTH_BOOSTED) != 0)     sb.Append("[+W] ");
            if ((efx & EFX_WIDTH_NARROWED) != 0)    sb.Append("[-W] ");
            if ((efx & EFX_SEDIMENT) != 0)          sb.Append("[Sediment] ");
            if ((efx & EFX_FLOOR_DETAIL) != 0)      sb.Append("[Floor] ");
            if ((efx & EFX_CANYON_VARIATION) != 0)  sb.Append("[CanyonVar] ");
            return sb.ToString().TrimEnd();
        }

        /// <summary>biome idx → biome effect bit.</summary>
        public static uint BiomeIdxToEffectBit(int biomeIdx)
        {
            switch (biomeIdx)
            {
                case 0: return EFX_BIOME_KARST;
                case 1: return EFX_BIOME_COLUMNAR;
                case 2: return EFX_BIOME_SEDIMENTARY;
                case 3: return EFX_BIOME_COLONNADE;
                case 4: return EFX_BIOME_ROCKY;
                case 5: return EFX_BIOME_CANYON;
                case 6: return EFX_BIOME_MARINE;
            }
            return 0;
        }

        public struct PassageProfile
        {
            public int edgeIndex;
            public Vector3 startPos;
            public Vector3 endPos;
            public float length;
            public int waypointCount;
            public PassageSegment[] segments;     // 10 samples
            public RiskScore risk;
            public PassageJunctionType inferredType;
            public int dominantBiomeA;
            public int dominantBiomeB;
            public float maxLocalWidth;
            public float minLocalWidth;
            public float ySlope;                  // |dy/length|
            public uint tags;

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Phase 6] 결함 #3 — visitedBiomes 표시
            //
            //   기존 dominantBiomeA/B는 양 끝점만 봄 — 통과 중인 biome 무시.
            //   visitedBiomes는 모든 segment 통과 biome 추적.
            //
            //   사용 예: "Karst(15%) → Columnar(40%) → Colonnade(45%)"
            //
            //   default: null (η OFF 시 기존 동작 유지)
            //   채워지는 조건: enablePredictive=true (η/ε)
            // ═══════════════════════════════════════════════════════════════
            public int[] visitedBiomes;             // 통과 순서대로 biome idx (중복 제거)
            public float[] biomeOccupancy;          // 각 visitedBiome의 점유율 (0~1, 합=1)
            public int trueDominantBiome;           // 가장 많이 점유한 biome (-1 = 미계산)
        }

        // ════════════════════════════════════════════════════════════════
        // Compute Profile (entry)
        //
        // [Phase 4.5-G η DebugAware Phase 2] Predictive Measurement 추가
        //   allEdges + enablePredictive 인자 default null/false → 후방 호환
        //   η/ε에서만 allEdges 전달, 다른 state는 기존 동작
        // ════════════════════════════════════════════════════════════════
        public static PassageProfile ComputeProfile(
            EdgeData edge, int edgeIndex,
            int biomeA_atStart, int biomeB_atEnd,
            CaveBiomeSettings settings,
            int numSegments = 10,
            float blendWidthBoost = 0.3f,
            // [η DebugAware Phase 2] optional Predictive params
            System.Collections.Generic.List<EdgeData> allEdges = null,
            bool enablePredictive = false,
            float fadePower = 2.0f,
            bool singleSourceActive = false,
            float minSafeWidth = 5.0f)  // ★ η Fix #3: 2.0 → 5.0 (사용자 요청)
        {
            var profile = new PassageProfile
            {
                edgeIndex = edgeIndex,
                startPos = edge.startPos,
                endPos = edge.endPos,
                length = Vector3.Distance(edge.startPos, edge.endPos),
                waypointCount = (int)(edge.flags & 0x3u),
                segments = new PassageSegment[numSegments],
                dominantBiomeA = biomeA_atStart,
                dominantBiomeB = biomeB_atEnd
            };

            // 1) 10-Segment Sampling
            float boundaryProximitySum = 0f;     // [Bug fix] 진짜 transition zone
            float blendWeightSum = 0f;           // 디버그/시각화용 (raw)
            float maxW = 0f, minW = float.MaxValue;
            int distinctBiomeCount = 0;          // [Bug fix] segment에서 본 biome 수
            HashSet<int> seenBiomes = new HashSet<int>();
            bool widthBoosted = false;
            bool widthNarrowed = false;

            // Per-segment risk 계산용 균일값 (edge 단위)
            float length = Vector3.Distance(edge.startPos, edge.endPos);
            float lengthRiskUniform = Mathf.Clamp01(length / 30f);
            float curvRiskUniform = Mathf.Clamp01(edge.curvatureAmp / Mathf.Max(edge.width, 0.1f));
            float wpRiskUniform = profile.waypointCount / 2f;

            for (int i = 0; i < numSegments; i++)
            {
                float t = (i + 0.5f) / numSegments;
                Vector3 sampleWorld = SampleAlongEdge(edge, t);

                // ═══════════════════════════════════════════════════════════
                // [η DebugAware Fix] biome A/B/effective 명확화
                //
                //   GPU L591: bIdxA = floor(scaledMap), bIdxB = bIdxA + 1
                //   GPU 실제 평가: bw<0.5 → typeA(=bIdxA), bw>0.5 → typeB(=bIdxB)
                //
                //   기존 bi = SampleBiomeIdx() = bIdxA만 → bw>0.5인 경우
                //   downstream (Predictive amp, segMixRisk, ssTypeB)이 잘못된 biome 사용.
                //   해결: biB 명시적 계산 + effective 사용.
                // ═══════════════════════════════════════════════════════════
                int bi = SampleBiomeIdx(sampleWorld, settings);     // ★ bIdxA (GPU와 일치)
                int biB = (settings != null && settings.globalBiomes != null
                           && settings.globalBiomes.Count > 0)
                    ? Mathf.Clamp(bi + 1, 0, settings.globalBiomes.Count - 1)
                    : bi;                                            // ★ bIdxB
                float bw = SampleBlendWeightAt(sampleWorld, settings);    // raw frac, 시각화용
                int effectiveBi = (bw < 0.5f) ? bi : biB;            // ★ GPU 실제 평가 biome

                seenBiomes.Add(effectiveBi);  // ★ effective 기준 (GPU 일치)
                float bp = SampleBoundaryProximity(sampleWorld, settings); // [신규] 진짜 transition zone
                float bc = bp;  // R_B 계산용 — 경계 근접도가 진짜 의미
                float lw = edge.width * (1f + blendWidthBoost * bp);

                // [신규] activeEffects 비트마스크 — ★ effective biome 기반
                uint efx = BiomeIdxToEffectBit(effectiveBi);
                if (bp > 0.1f) efx |= EFX_TRANSITION_ZONE;

                if (bp > 0.05f && settings != null && settings.globalBiomes != null
                    && settings.globalBiomes.Count > effectiveBi)
                {
                    var biome = settings.globalBiomes[effectiveBi];
                    if (biome != null && biome.blendDamping < 0.95f)
                        efx |= EFX_BLEND_DAMPING;
                }

                if (edge.curvatureAmp > 0.01f) efx |= EFX_CURVATURE;
                if (profile.waypointCount > 0) efx |= EFX_WAYPOINT;
                // [D4] flags bit 3/4 → 전체 edge level (모든 segment에 동일 적용)
                if ((edge.flags & 0x8u) != 0)  efx |= EFX_WIDTH_BOOSTED;
                if ((edge.flags & 0x10u) != 0) efx |= EFX_WIDTH_NARROWED;

                // [신규] Per-segment risk contributions
                float segNarrowRisk = Mathf.Clamp01(1f - (lw - 1f) / 5f);  // localWidth 기반
                float segBlendRisk = Mathf.Clamp01(bp);
                float segMixRisk = (effectiveBi != biomeA_atStart) ? 1f : 0f;       // ★ effective 기준

                // ═══════════════════════════════════════════════════════════════
                // [Phase 4.5-G η DebugAware Phase 2] Predictive Measurement
                //   enablePredictive=true (η/ε)일 때만 채움. 그 외 default 0/false → byte-identical
                // ═══════════════════════════════════════════════════════════════
                int ssTypeA = -1, ssTypeB = -1, ssState = 3;  // default: dual blend (γ OFF)
                float ssAmpScale = 1.0f;
                float predNoiseAmp = 0f, predSminPen = 0f, predEffectiveWidth = 0f;
                int predNearbyCount = 0;
                bool predBelowMargin = false;

                if (enablePredictive)
                {
                    // ── Single-Source 추적 (결함 #1 해결) ─────────────────
                    //   ★ η DebugAware Fix: ssTypeB는 biB여야 (이전 bi=bIdxA 잘못)
                    //   typeA = pA.noiseType (bIdxA의 biome)
                    //   typeB = pB.noiseType (bIdxB의 biome) ★
                    if (singleSourceActive)
                    {
                        if (bw < 0.5f - 0.001f)      { ssState = 0; ssTypeA = bi; }
                        else if (bw > 0.5f + 0.001f) { ssState = 1; ssTypeB = biB; }  // ★ bi → biB
                        else                          { ssState = 2; }  // blank (정확히 0.5)
                        // ampScale = pow(1 - centrality, FadePower)
                        ssAmpScale = Mathf.Pow(Mathf.Max(1f - bp, 0f),
                                               Mathf.Max(fadePower, 0.001f));
                    }
                    // (singleSourceActive=false면 ssState=3 유지: dual blend)

                    // ── Predictive amp (PREDICTIVE_AMP_LOOKUP 활용) ─────────
                    //   ★ η DebugAware Fix: effective biome 사용 (이전 bi=bIdxA 잘못)
                    //   bw>0.5인 경우 GPU는 typeB=biB 평가하므로 amp도 biB 기반
                    predNoiseAmp = ComputeEffectiveBiomeAmp(
                        effectiveBi, bp, fadePower, singleSourceActive);

                    // ── 인접 edges fan smin penetration 추정 ──────────────
                    if (allEdges != null && allEdges.Count > 0)
                    {
                        const float SEARCH_RADIUS = 5.0f;
                        const float SMIN_K = 2.5f;
                        for (int j = 0; j < allEdges.Count; j++)
                        {
                            if (j == edgeIndex) continue;
                            float distToOther = ClosestPointDistanceOnSegment(
                                allEdges[j].startPos, allEdges[j].endPos, sampleWorld);
                            if (distToOther < SEARCH_RADIUS) predNearbyCount++;
                        }
                        // smin 누적: (N-1) × k/4 (인접 edge 수 - 1)
                        if (predNearbyCount > 1)
                            predSminPen = (predNearbyCount - 1) * SMIN_K * 0.25f;
                    }

                    // ── 실효 폭 = localWidth - smin침투 - noise×2 - curve×0.5 ──
                    float curveImpact = edge.curvatureAmp * 0.5f;
                    BiomeAmpInfo ampInfo = LookupBiomeAmpInfo(effectiveBi);  // ★ effective
                    float widthImpact = ampInfo.symmetric ? predNoiseAmp * 2f : predNoiseAmp;
                    predEffectiveWidth = lw - predSminPen - widthImpact - curveImpact;

                    // ── 안전 마진 검사 ─────────────────────────────────────
                    predBelowMargin = predEffectiveWidth < minSafeWidth;
                }

                profile.segments[i] = new PassageSegment
                {
                    t = t,
                    worldPos = sampleWorld,
                    biomeIdx = bi,           // legacy 호환 — bIdxA 그대로 (DEPRECATED)
                    biomeIdxA = bi,          // ★ η DebugAware Fix — bIdxA 명시
                    biomeIdxB = biB,         // ★ η DebugAware Fix — bIdxB 신규
                    blendWeight = bw,
                    blendCentrality = bc,
                    localWidth = lw,
                    inWaypointSegment = (profile.waypointCount > 0),
                    activeEffects = efx,
                    segLengthRisk = lengthRiskUniform,
                    segNarrowRisk = segNarrowRisk,
                    segBlendRisk = segBlendRisk,
                    segMixRisk = segMixRisk,
                    segCurvatureRisk = curvRiskUniform,
                    segWaypointRisk = wpRiskUniform,
                    // [η DebugAware Phase 2] Predictive fields
                    singleSourceTypeA = ssTypeA,
                    singleSourceTypeB = ssTypeB,
                    singleSourceState = ssState,
                    singleSourceAmpScale = ssAmpScale,
                    predNoiseAmp = predNoiseAmp,
                    predSminPenetration = predSminPen,
                    predNearbyEdgeCount = predNearbyCount,
                    predEffectiveWidth = predEffectiveWidth,
                    predIsBelowSafeMargin = predBelowMargin
                };

                boundaryProximitySum += bp;
                blendWeightSum += bw;
                if (lw > maxW) maxW = lw;
                if (lw < minW) minW = lw;
            }

            float avgBoundaryProximity = boundaryProximitySum / numSegments;  // [Bug fix] R_B 핵심
            distinctBiomeCount = seenBiomes.Count;  // [Bug fix] R_M 개선용
            profile.maxLocalWidth = maxW;
            profile.minLocalWidth = minW;

            // 2) Y slope
            float dy = Mathf.Abs(edge.endPos.y - edge.startPos.y);
            profile.ySlope = profile.length > 0.01f ? (dy / profile.length) : 0f;

            // 3) Risk 계산
            //   [Bug fix] R_M은 segment 따라 본 distinct biome 수 기반 (양 끝점만 비교 X)
            //   [Bug fix] R_B는 boundary proximity 평균 (실제 transition zone)
            bool actualMix = (distinctBiomeCount >= 2);
            profile.risk = ComputeRisk(edge, profile, avgBoundaryProximity,
                                        biomeA_atStart, biomeB_atEnd, actualMix);

            // 4) Junction Type 자동 분류
            profile.inferredType = ClassifyJunctionType(edge, profile);

            // 5) Tags 부여
            profile.tags = ComputeTags(edge, profile);

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Phase 6] 결함 #3 — visitedBiomes 추적
            //
            //   모든 segment 순회하여 통과한 biome 순서 + 점유율 계산.
            //   사용자 표시 예: "Karst(15%) → Columnar(40%) → Colonnade(45%)"
            //
            //   default: null (η OFF) — 기존 코드 영향 없음
            //   채워지는 조건: enablePredictive=true (η/ε)
            // ═══════════════════════════════════════════════════════════════
            profile.visitedBiomes = null;
            profile.biomeOccupancy = null;
            profile.trueDominantBiome = -1;
            if (enablePredictive)
            {
                // segment 순서대로 biome 추적, 연속 동일 biome은 합쳐짐
                var visited = new System.Collections.Generic.List<int>();
                var occupancyCount = new System.Collections.Generic.Dictionary<int, int>();
                int prevBiome = -1;
                for (int s = 0; s < numSegments; s++)
                {
                    int bi = profile.segments[s].biomeIdx;
                    if (bi != prevBiome)
                    {
                        visited.Add(bi);
                        prevBiome = bi;
                    }
                    if (!occupancyCount.ContainsKey(bi)) occupancyCount[bi] = 0;
                    occupancyCount[bi]++;
                }

                // 중복 제거된 visited 리스트 + occupancy %
                profile.visitedBiomes = visited.ToArray();
                profile.biomeOccupancy = new float[profile.visitedBiomes.Length];
                int maxCount = 0;
                for (int v = 0; v < profile.visitedBiomes.Length; v++)
                {
                    int bi = profile.visitedBiomes[v];
                    int cnt = occupancyCount.GetValueOrDefault(bi, 0);
                    profile.biomeOccupancy[v] = cnt / (float)numSegments;
                    if (cnt > maxCount)
                    {
                        maxCount = cnt;
                        profile.trueDominantBiome = bi;
                    }
                }
            }

            // 6) [Phase 4.5-G P2.E] Plain Passage Risk 계산 — γ Single-Source 위험
            //   양 끝 모두 blend zone (centrality 높음)일 때 amp ~0 (plain wall) 위험
            //   afterf3 PassageProfile 데이터 활용 — segments가 이미 채워진 상태
            {
                // (a) 양 끝 segment의 blendCentrality 평균
                float startCentrality = 0f, endCentrality = 0f;
                if (profile.segments != null && profile.segments.Length >= 2)
                {
                    startCentrality = profile.segments[0].blendCentrality;
                    endCentrality = profile.segments[profile.segments.Length - 1].blendCentrality;
                }
                float avgEndsCentrality = (startCentrality + endCentrality) * 0.5f;

                // (b) 길이 정규화 (8m 미만은 영향 작음)
                float lenFactor = Mathf.Clamp01((profile.length - 8f) / 22f);  // 8m=0, 30m=1

                // (c) cross-biome 또는 blend coverage 기반
                bool crossBiome = (biomeA_atStart != biomeB_atEnd);
                float blendFactor = profile.risk.blendRisk;  // 이미 [0,1]

                // 합산: 양 끝 centrality + blend coverage + length 가중
                //   양 끝 모두 blend zone에 깊이 있으면 plainPassageRisk 증가
                float plainBase = avgEndsCentrality;
                float plainAdj = crossBiome ? 1.2f : 1.0f;  // cross-biome일 때 가중 증폭
                profile.risk.plainPassageRisk = Mathf.Clamp01(
                    plainBase * plainAdj * (0.5f + 0.5f * blendFactor) * (0.3f + 0.7f * lenFactor)
                );

                // (d) Critical flag (≥ 0.6 = "심각한 plain risk")
                profile.risk.isPlainRisk = profile.risk.plainPassageRisk >= 0.6f;
            }

            // ═══════════════════════════════════════════════════════════════════
            // [Phase 12-C] ★ Pass 1.5 — Per-segment Amp Smoothing (3-tap)
            //
            //   사용자 의도: "segmentation별 들쭉날쭉하지 않고 유기적으로 연결"
            //
            //   기존: predNoiseAmp는 segment별 독립 계산 → biome 경계 segment에서
            //         raw amp 점프 (예: Karst 1.125m → Sediment 3.0m, 1.875m 점프)
            //   수정: 3-tap moving average — smoothed[i] = (raw[i-1] + 2×raw[i] + raw[i+1]) / 4
            //         경계 점프 50% 완화 (0.937m로 감소)
            //
            //   적용 대상: predNoiseAmp만 (predEffectiveWidth는 재계산 안 함 — Pass 2에서 처리)
            //   D2 보장: enableSegmentAmpSmoothing = false면 skip
            //   atomic preset η/ε에서 자동 ON
            // ═══════════════════════════════════════════════════════════════════
            if (enableSegmentAmpSmoothing && enablePredictive
                && profile.segments != null && profile.segments.Length >= 3)
            {
                int n = profile.segments.Length;
                // 임시 raw amp 배열 (★ in-place 수정 시 누적 오염 방지)
                float[] rawAmps = new float[n];
                for (int i = 0; i < n; i++)
                    rawAmps[i] = profile.segments[i].predNoiseAmp;

                // 3-tap moving average 적용
                for (int i = 0; i < n; i++)
                {
                    float prevAmp = (i > 0) ? rawAmps[i - 1] : rawAmps[i];
                    float currAmp = rawAmps[i];
                    float nextAmp = (i < n - 1) ? rawAmps[i + 1] : rawAmps[i];
                    float smoothed = (prevAmp + 2f * currAmp + nextAmp) * 0.25f;
                    profile.segments[i].predNoiseAmp = smoothed;
                }
                // 주: predEffectiveWidth는 재계산 안 함 — 이는 Pass 2 RESOLVE에서 활용
                //    (Pass 2가 smoothed amp 기반으로 위험 검출하면 더 정확)
            }

            return profile;
        }

        // ════════════════════════════════════════════════════════════════
        // Risk Score 계산
        //   [Bug fix] actualMix는 segment를 따라 본 distinct biome 수 기반.
        //             양 끝점만 비교가 아닌 "passage가 실제로 다른 biome 통과 했는가".
        // ════════════════════════════════════════════════════════════════
        private static RiskScore ComputeRisk(
            EdgeData edge, PassageProfile profile,
            float blendCoverage, int biomeA, int biomeB,
            bool actualMix)
        {
            var r = new RiskScore { biomeA = biomeA, biomeB = biomeB };

            r.lengthRisk = Mathf.Clamp01(profile.length / 30f);
            r.narrowRisk = Mathf.Clamp01(1f - (edge.width - 1f) / 5f);
            r.blendRisk = Mathf.Clamp01(blendCoverage);    // [Bug fix] 진짜 transition zone proximity 평균
            r.biomeMixRisk = actualMix ? 1f : 0f;          // [Bug fix] segments 기반 (양 끝점 X)
            r.curvatureRisk = Mathf.Clamp01(edge.curvatureAmp / Mathf.Max(edge.width, 0.1f));
            r.waypointRisk = profile.waypointCount / 2f;

            float baseTotal = 0.20f * r.lengthRisk
                            + 0.25f * r.narrowRisk
                            + 0.20f * r.blendRisk
                            + 0.15f * r.biomeMixRisk
                            + 0.10f * r.curvatureRisk
                            + 0.10f * r.waypointRisk;

            r.pairMultiplier = GetBiomePairMultiplier(biomeA, biomeB);
            r.total = Mathf.Clamp01(baseTotal * r.pairMultiplier);

            r.level = (r.total < 0.3f) ? RiskLevel.Safe
                    : (r.total < 0.6f) ? RiskLevel.Caution
                    : RiskLevel.Critical;

            return r;
        }

        // 0=Karst, 1=Columnar, 2=Sedimentary, 3=Colonnade, 4=Rocky, 5=Canyon, 6=Marine
        private static float GetBiomePairMultiplier(int a, int b)
        {
            if (a == b) return 1.0f;
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            if (lo == 1 && hi == 5) return 1.5f;   // Columnar ↔ Canyon
            if (a == 3 || b == 3)   return 1.3f;   // Colonnade ↔ Any
            if (lo == 4 && hi == 6) return 1.2f;   // Rocky ↔ Marine
            return 1.0f;
        }

        // ════════════════════════════════════════════════════════════════
        // [Phase 4.5-G Stage 3-D D4] Natural Width Multiplier (per pair)
        //
        // 자연 동굴학 의미 기반 width 변경 방향:
        //   양수 = 확장 (해식, 용식, 대홀 진입)
        //   0    = 변경 없음 (sharp 절리, 동일 biome)
        //   음수 = 축소 (낙반, 붕괴대)
        //
        // 사용: edge.width *= 1 + boostBase × blendCoverage × multiplier
        //   multiplier가 음수면 width 감소 (Rocky 통로 등)
        // ════════════════════════════════════════════════════════════════
        public static float GetNaturalWidthMultiplier(int a, int b)
        {
            if (a == b) return 0f;  // 동일 biome — 변경 없음

            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);

            // ── 확장형 (해식, 용식) ──
            if ((lo == 0 && hi == 6)) return 1.0f;   // Karst ↔ Marine — 해식동 입구
            if ((lo == 2 && hi == 6)) return 0.7f;   // Sedimentary ↔ Marine — 침식 chamber
            if ((lo == 0 && hi == 2)) return 0.5f;   // Karst ↔ Sedimentary — 호환 형태
            if ((lo == 0 && hi == 3)) return 0.4f;   // Karst ↔ Colonnade — 대홀 진입로
            if ((lo == 2 && hi == 3)) return 0.4f;   // Sedimentary ↔ Colonnade

            // ── Sharp 보존 (변경 없음) ──
            if (lo == 1) return 0f;                   // Columnar pair — 주상절리 끝 자연 좁음
            if ((lo == 3 && hi == 5)) return 0f;     // Colonnade ↔ Canyon — sharp 충돌

            // ── 축소형 (낙반, 붕괴) ──
            if (lo == 4 || hi == 4) return -0.3f;    // Rocky ↔ Any — 낙반대 자연 좁아짐

            // ── 약한 확장 (Canyon, 일반 transition) ──
            if (lo == 5 || hi == 5) return 0.2f;     // Canyon ↔ Any — 협곡 끝 약간 확장
            if ((lo == 3 && hi == 6)) return 0.5f;   // Colonnade ↔ Marine

            // Default — 일반 transition
            return 0.4f;
        }

        public static string DescribeNaturalPair(int a, int b)
        {
            float m = GetNaturalWidthMultiplier(a, b);
            if (m > 0.5f) return "Confluence (확장)";
            if (m > 0.0f) return "Soft Transition";
            if (m > -0.01f && m < 0.01f) return "Sharp Junction";
            return "Constricted (축소)";
        }

        // ════════════════════════════════════════════════════════════════
        // Junction Type 자동 분류
        // ════════════════════════════════════════════════════════════════
        private static PassageJunctionType ClassifyJunctionType(
            EdgeData edge, PassageProfile profile)
        {
            float avgW = edge.width;
            float ySlope = profile.ySlope;
            int wpCount = profile.waypointCount;

            // 1. Bridge (waypoint Y offset 큼)
            if (wpCount >= 1)
            {
                float maxYOffset = 0f;
                for (int i = 0; i < profile.segments.Length; i++)
                {
                    float baseY = Mathf.Lerp(edge.startPos.y, edge.endPos.y, profile.segments[i].t);
                    float diff = Mathf.Abs(profile.segments[i].worldPos.y - baseY);
                    if (diff > maxYOffset) maxYOffset = diff;
                }
                if (maxYOffset > 4f) return PassageJunctionType.Bridge;
            }

            // 2. Spiral
            if (edge.curvatureAmp > 2.5f && profile.length > 15f)
                return PassageJunctionType.Spiral;

            // 3. Chimney (수직)
            if (ySlope > 0.7f) return PassageJunctionType.Chimney;

            // 4. Ramp (경사)
            if (ySlope > 0.3f) return PassageJunctionType.Ramp;

            // 5. Collapse (Rocky biome 관통)
            bool rockyDominant = false;
            for (int i = 0; i < profile.segments.Length; i++)
            {
                if (profile.segments[i].biomeIdx == 4) { rockyDominant = true; break; }
            }
            if (rockyDominant) return PassageJunctionType.Collapse;

            // ═══════════════════════════════════════════════════════════════
            // [Phase 15] ★ Width 4단계 분류 — 5m 정책 반영
            //
            //   ★ 사용자 지적: minSafeWidth = 5.0f. 5m 미달은 빌드 위반.
            //
            //   < 5m       → Sub5mEmergency (★ 정책 위반 경고)
            //   5~7m       → Narrow         (긴장감 통로)
            //   7~10m      → Standard       (일반 fallback)
            //   10~15m     → Wide           (Gallery 격상)
            //   > 15m      → Grand          (Boss 진입)
            // ═══════════════════════════════════════════════════════════════
            if (avgW < 5.0f)
            {
                // ★ 5m 미달 — δ Mid-node 분할 또는 ε per-segment 처리 필요
                // Visualizer에서 경고 표시될 영역
                return PassageJunctionType.Sub5mEmergency;
            }

            if (avgW < 7.0f) return PassageJunctionType.Narrow;     // ★ 5~7m
            if (avgW < 10.0f) return PassageJunctionType.Standard;  // ★ 7~10m (★ fallback)
            if (avgW <= 15.0f) return PassageJunctionType.Wide;     // ★ 10~15m

            return PassageJunctionType.Grand;                       // ★ > 15m
        }

        // ════════════════════════════════════════════════════════════════
        // Tags
        // ════════════════════════════════════════════════════════════════
        public const uint TAG_NARROW     = 1u << 0;
        public const uint TAG_WIDE       = 1u << 1;
        public const uint TAG_VERTICAL   = 1u << 2;
        public const uint TAG_HORIZONTAL = 1u << 3;
        public const uint TAG_SACRED     = 1u << 4;
        public const uint TAG_DANGER     = 1u << 5;

        private static uint ComputeTags(EdgeData edge, PassageProfile profile)
        {
            uint t = 0u;
            if (edge.width < 1.5f) t |= TAG_NARROW;
            if (edge.width >= 3.5f) t |= TAG_WIDE;
            if (profile.ySlope > 0.5f) t |= TAG_VERTICAL;
            if (profile.ySlope < 0.2f) t |= TAG_HORIZONTAL;
            if (profile.inferredType == PassageJunctionType.Bridge) t |= TAG_SACRED;
            if (profile.inferredType == PassageJunctionType.Collapse) t |= TAG_DANGER;
            return t;
        }

        public static string TagsToString(uint tags)
        {
            if (tags == 0) return "(none)";
            var sb = new System.Text.StringBuilder();
            if ((tags & TAG_NARROW) != 0)     sb.Append("Narrow ");
            if ((tags & TAG_WIDE) != 0)       sb.Append("Wide ");
            if ((tags & TAG_VERTICAL) != 0)   sb.Append("Vertical ");
            if ((tags & TAG_HORIZONTAL) != 0) sb.Append("Horizontal ");
            if ((tags & TAG_SACRED) != 0)     sb.Append("Sacred ");
            if ((tags & TAG_DANGER) != 0)     sb.Append("Danger ");
            return sb.ToString().TrimEnd();
        }

        // ════════════════════════════════════════════════════════════════
        // Sampling 헬퍼
        // ════════════════════════════════════════════════════════════════

        /// <summary>Edge 위 t (0~1) 위치의 world 좌표. Waypoint 분기 처리.</summary>
        public static Vector3 SampleAlongEdge(EdgeData edge, float t)
        {
            int wp = (int)(edge.flags & 0x3u);
            if (wp == 0)
            {
                return Vector3.Lerp(edge.startPos, edge.endPos, t);
            }
            else if (wp == 1)
            {
                Vector3 mid = (edge.startPos + edge.endPos) * 0.5f;
                Vector3 w1 = mid + RouteGeometry.UnpackHalf3(edge.w1_packed);
                if (t < 0.5f)
                    return Vector3.Lerp(edge.startPos, w1, t / 0.5f);
                return Vector3.Lerp(w1, edge.endPos, (t - 0.5f) / 0.5f);
            }
            else // wp == 2
            {
                Vector3 mid = (edge.startPos + edge.endPos) * 0.5f;
                Vector3 w1 = mid + RouteGeometry.UnpackHalf3(edge.w1_packed);
                Vector3 w2 = mid + RouteGeometry.UnpackHalf3(edge.w2_packed);
                const float third = 1f / 3f;
                if (t < third)
                    return Vector3.Lerp(edge.startPos, w1, t / third);
                else if (t < 2f * third)
                    return Vector3.Lerp(w1, w2, (t - third) / third);
                return Vector3.Lerp(w2, edge.endPos, (t - 2f * third) / third);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // [Phase 4.5-G η DebugAware Phase 2] Geometry helper
        //   point에서 line segment (a→b) 까지 최소 거리 계산.
        //   EstimateSminPenetration에서 인접 edges fan 검출에 사용.
        // ─────────────────────────────────────────────────────────────────
        public static float ClosestPointDistanceOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector3.Distance(point, a);
            float t = Vector3.Dot(point - a, ab) / lenSq;
            t = Mathf.Clamp01(t);
            Vector3 closest = a + ab * t;
            return Vector3.Distance(point, closest);
        }

        // ─────────────────────────────────────────────────────────────────
        // [Phase 4.5-G F3] CPU/GPU Biome Sampling Sync 토글
        //   기본 false → 기존 Mathf.PerlinNoise 경로 (byte-identical, D2 원칙)
        //   true → BiomeShaderSync 경로 (GPU shader와 정확 일치)
        //
        //   외부에서 CaveNodeGraphBuilder.useShaderCompatBiomeSampling이 true면
        //   GenerateGraphInternal 시작 시 이 flag도 true로 설정.
        //   PassageMetric은 static 클래스라 flag도 static.
        // ─────────────────────────────────────────────────────────────────
        public static bool useShaderCompatSampling = false;

        // ═══════════════════════════════════════════════════════════════════
        // [Phase 12-C] ★ Per-segment Amp Smoothing (3-tap moving average)
        //
        //   사용자 의도: "segmentation별 들쭉날쭉하지 않고 유기적으로 연결"
        //   default false (D2 보장) → atomic preset η/ε에서 자동 ON
        //   경계 segment amp 점프 50% 완화 (예: 1.875m → 0.937m)
        // ═══════════════════════════════════════════════════════════════════
        public static bool enableSegmentAmpSmoothing = false;

        /// <summary>
        /// worldPos에서 우세 biome idx.
        /// useShaderCompatSampling 토글:
        ///   false (기본): legacy Unity Perlin + offset 1000m (CPU/GPU mismatch 가능)
        ///   true: BiomeShaderSync — GPU snoise2D와 정확 일치
        /// </summary>
        public static int SampleBiomeIdx(Vector3 pos, CaveBiomeSettings bs)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count == 0) return 0;

            // [F3 토글 가드] GPU sync 사용 시 BiomeShaderSync 호출
            if (useShaderCompatSampling)
            {
                return BiomeShaderSync.SampleBiomeIdx(pos, bs);
            }

            // [기존 legacy 경로 — D2 byte-identical 보장]
            float macroScale = Mathf.Max(1f, bs.macroBiomeScale);
            int biomeCount = bs.globalBiomes.Count;
            float n = Mathf.PerlinNoise(pos.x / macroScale + 1000f, pos.z / macroScale + 1000f);
            float scaled = n * Mathf.Max(biomeCount - 1, 0f);
            return Mathf.Clamp(Mathf.FloorToInt(scaled), 0, biomeCount - 1);
        }

        /// <summary>
        /// [Stage 3-D D4 Bug Fix] worldPos에서 raw blendWeight (frac of scaledMap).
        /// Shader의 frac(scaledMap)과 정확 일치.
        ///
        /// 주의: 이 값은 [0, 1) 단순 위상값. biome 경계와는 무관하게 전 영역을 커버.
        /// 즉 biome 내부 어디서나 0~1 값을 가짐 — "blend 중심 위치"가 아닌 단순 위상.
        /// 진짜 "transition 영역" 검출은 IsInTransitionZone() 또는 SampleBlendCentrality() 사용.
        ///
        /// useShaderCompatSampling 토글 적용:
        ///   false (기본): legacy Unity Perlin frac
        ///   true: BiomeShaderSync — GPU smoothstep(0,1,frac) 정확 일치
        /// </summary>
        public static float SampleBlendWeightAt(Vector3 pos, CaveBiomeSettings bs)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count <= 1) return 0f;

            // [F3 토글 가드]
            if (useShaderCompatSampling)
            {
                return BiomeShaderSync.SampleBlendWeight(pos, bs);
            }

            // [기존 legacy 경로]
            float macroScale = Mathf.Max(1f, bs.macroBiomeScale);
            int biomeCount = bs.globalBiomes.Count;
            float n = Mathf.PerlinNoise(pos.x / macroScale + 1000f, pos.z / macroScale + 1000f);
            float scaled = n * Mathf.Max(biomeCount - 1, 0f);
            return scaled - Mathf.Floor(scaled);  // [0, 1)
        }

        // ════════════════════════════════════════════════════════════════
        // [Bug Fix] 진짜 "Transition Zone" 검출 — 두 가지 메트릭
        //
        // 문제: 기존 R_B (blendCentrality 평균) 는 biome 내부에서도 0.5 근처 값 반환
        //       → 동일 biome 안에서도 R_B 높게 나오는 버그
        //
        // 해결: "biome boundary 까지의 실제 거리" 기반 메트릭 신규
        //   - GetBiomeAt 으로 인접 sample 비교 → 실제 경계 검출
        //   - 거리 임계 (boundary_threshold) 이내 voxel만 "transition" 으로 판단
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// 해당 위치가 biome boundary 근처인지 (실제 transition zone).
        /// 주변 4방향 sample이 모두 같은 biome이면 안정 (false), 다르면 transition (true).
        /// </summary>
        public static bool IsInTransitionZone(Vector3 pos, CaveBiomeSettings bs, float radius = 5f)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count <= 1) return false;
            int center = SampleBiomeIdx(pos, bs);
            if (SampleBiomeIdx(pos + Vector3.right * radius, bs) != center) return true;
            if (SampleBiomeIdx(pos - Vector3.right * radius, bs) != center) return true;
            if (SampleBiomeIdx(pos + Vector3.forward * radius, bs) != center) return true;
            if (SampleBiomeIdx(pos - Vector3.forward * radius, bs) != center) return true;
            return false;
        }

        /// <summary>
        /// [신규] 진짜 transition zone에서만 0이 아닌 값을 반환.
        ///   - 두 biome 사이 경계까지 거리 측정 (radial sample)
        ///   - boundaryDist &lt; threshold 이면 부드럽게 [0, 1] 변화
        ///   - threshold 이상이면 0
        /// 결과: 동일 biome 내부 = 0, 경계 근처 = 양수 (smoothstep)
        /// </summary>
        public static float SampleBoundaryProximity(Vector3 pos, CaveBiomeSettings bs,
                                                      float maxDist = 12f, int rays = 8)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count <= 1) return 0f;
            int center = SampleBiomeIdx(pos, bs);

            float minBoundaryDist = maxDist;
            // 8방향으로 radial scan — 처음 다른 biome 만나는 거리
            for (int r = 0; r < rays; r++)
            {
                float angle = (Mathf.PI * 2f * r) / rays;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

                // 1m 단위 binary search (간단)
                for (float d = 1f; d <= maxDist; d += 1f)
                {
                    int sampled = SampleBiomeIdx(pos + dir * d, bs);
                    if (sampled != center)
                    {
                        if (d < minBoundaryDist) minBoundaryDist = d;
                        break;
                    }
                }
            }

            if (minBoundaryDist >= maxDist) return 0f;
            // 가까울수록 큰 값 — smoothstep
            float t = 1f - (minBoundaryDist / maxDist);
            return t * t * (3f - 2f * t);  // smoothstep
        }

        // ════════════════════════════════════════════════════════════════
        // Aggregate Stats (디버그용)
        // ════════════════════════════════════════════════════════════════

        public struct RiskAggregate
        {
            public int totalEdges;
            public int safeCount;
            public int cautionCount;
            public int criticalCount;

            public Dictionary<PassageJunctionType, int> typeCounts;
            public override string ToString()
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"Risk: Safe={safeCount}, Caution={cautionCount}, Critical={criticalCount} | Types: ");
                if (typeCounts != null)
                {
                    foreach (var kv in typeCounts) sb.Append($"{kv.Key}={kv.Value}, ");
                }
                return sb.ToString().TrimEnd(',', ' ');
            }
        }

        public static RiskAggregate ComputeAggregate(List<PassageProfile> profiles)
        {
            var agg = new RiskAggregate
            {
                totalEdges = profiles.Count,
                typeCounts = new Dictionary<PassageJunctionType, int>()
            };

            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                switch (p.risk.level)
                {
                    case RiskLevel.Safe:     agg.safeCount++; break;
                    case RiskLevel.Caution:  agg.cautionCount++; break;
                    case RiskLevel.Critical: agg.criticalCount++; break;
                }

                if (!agg.typeCounts.ContainsKey(p.inferredType)) agg.typeCounts[p.inferredType] = 0;
                agg.typeCounts[p.inferredType]++;
            }

            return agg;
        }

        // ════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G η DebugAware Phase 1] Predictive Lookup Table
        //
        //   목적: STAGE 1 (GraphBuilder)에서 STAGE 3 (GPU Compute) 결과를 예측.
        //         CPU 측에서 동일 식으로 noise amp / smin penetration 추정 가능.
        //
        //   4원칙 audit (ecotone_and_4principle_cases_audit.html) 결과 직접 활용:
        //     - Pure Analytical (Case 3): closed-form → 100% 정확
        //     - Hybrid (Case 1γ Voronoi, Case 2): cell 격자 명확 → 95% 정확
        //     - Pure Noise (Case 0/1Legacy/4/5/6): 통계적 → 80% 정확
        //
        //   사용처: η DebugAware Phase 4 (3-Pass Optimization)에서 활용 예정.
        //           Phase 1에서는 인프라만 정의, 실제 사용은 Phase 2~5에서.
        //
        //   D2 보장: 이 lookup table은 static 데이터일 뿐, GPU mesh에 영향 없음.
        //           η DebugAware OFF (γ/δ) 시 호출 자체가 안 됨 → byte-identical.
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>4원칙 분류 — Predictive amp 정확도 결정.</summary>
        public enum NoiseClass
        {
            /// <summary>★★★ closed-form (sin/cos/fmod/length/floor/abs/smoothstep) — 100% 정확.</summary>
            PureAnalytical,
            /// <summary>★★ hash + analytical (Voronoi/hash2D) — 95% 정확, cell 격자 명확.</summary>
            Hybrid,
            /// <summary>★ snoise/fBm — 80% 정확 (통계적), worst case 통제 필요.</summary>
            PureNoise
        }

        /// <summary>각 biome (Case 0~6)의 Predictive amp 정보.</summary>
        public struct BiomeAmpInfo
        {
            /// <summary>4원칙 분류.</summary>
            public NoiseClass principleClass;
            /// <summary>예측 정확도 [0, 1] — Adaptive safety factor 결정.</summary>
            public float accuracyGrade;
            /// <summary>Base amp (코드 상수, m 단위).</summary>
            public float baseAmp;
            /// <summary>true = ± 양방향 (additive noise), false = 한 방향 (subtractive 또는 Replacement).</summary>
            public bool symmetric;
            /// <summary>★ 4원칙 위반 여부 (audit 결과). Adaptive 시 추가 보수적 마진 적용.</summary>
            public bool principleViolation;
            /// <summary>코드 참조 (디버깅용).</summary>
            public string codeRef;

            /// <summary>실효 amp (양/음 합산) — Adaptive 계산용.
            /// symmetric=true: ± baseAmp → 통로 양쪽 영향 → 2×baseAmp 부담.
            /// symmetric=false: -baseAmp 또는 +baseAmp → 한쪽 영향 → baseAmp 부담.</summary>
            public float EffectiveAmpForWidth =>
                symmetric ? baseAmp * 2f : baseAmp;

            /// <summary>Safety factor — Adaptive 마진 결정.
            /// Pure Analytical: 1.0 (정확)
            /// Hybrid: 1.05 (5% 마진)
            /// Pure Noise: 1.2 (20% 마진)
            /// 4원칙 위반: 추가 1.5× → 위반 Pure Noise는 1.2 × 1.25 = 1.5</summary>
            public float SafetyFactor
            {
                get
                {
                    float baseFactor = principleClass switch
                    {
                        NoiseClass.PureAnalytical => 1.00f,
                        NoiseClass.Hybrid => 1.05f,
                        NoiseClass.PureNoise => 1.20f,
                        _ => 1.20f
                    };
                    return principleViolation ? baseFactor * 1.25f : baseFactor;
                }
            }
        }

        /// <summary>
        /// 4원칙 audit 결과 기반 Predictive amp lookup table.
        /// 각 biome (Case 0~6)의 noise/analytical 분류 + amp 값.
        /// CaveBiomeMath.hlsl L391~670 코드 분석 결과 hard-coded.
        /// 향후 CaveBiomeSettings에서 자동 추출하도록 확장 가능 (Phase 5+).
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<int, BiomeAmpInfo> PREDICTIVE_AMP_LOOKUP
            = new System.Collections.Generic.Dictionary<int, BiomeAmpInfo>
        {
            // ── Case 0 — Karst (Pure Noise: fBm 2 octaves) ★ Phase 10 후 4원칙 준수 ──
            //   detailSDF += karstNoise * 0.75 * wallMask  (Phase 10: oct 4→2, amp 0.6→0.75)
            //   fBm(oct=2, gain=0.5) max amp = 1+0.5 = 1.5
            //   실효 amp = 0.75 × 1.5 = ±1.125m (변경 전과 동일 — 호환 유지)
            //   ★ Phase 10: oct 2 → 4원칙 borderline 해결 (사용자 권장)
            [0] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.PureNoise,
                accuracyGrade = 0.85f,                      // ★ oct 2로 정확도 ↑ (통계적 안정)
                baseAmp = 1.125f,
                symmetric = true,
                principleViolation = false,                 // ★ Phase 10 후 정상화
                codeRef = "CaveBiomeMath.hlsl L432 (Phase 10: 2 oct, amp 0.75)"
            },

            // ── Case 1 (γ Voronoi) — Hybrid ★ 사용자 모범 ────────────────────
            //   detailSDF -= ridge * (1.0 + yJoint) * 0.4 * wallMask
            //   max: 1.0 × (1+0.3) × 0.4 = 0.52m (subtractive, 한 방향)
            //   ★ 4원칙 모범 (Voronoi cell 격자 명확)
            //   Note: γ Voronoi는 _EnableColumnarVoronoiNoise=1일 때만. 기본 가정.
            [1] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.Hybrid,
                accuracyGrade = 0.95f,
                baseAmp = 0.52f,
                symmetric = false,           // subtractive
                principleViolation = false,  // ★ 모범
                codeRef = "CaveBiomeMath.hlsl L485 (γ Voronoi 옵션)"
            },

            // ── Case 2 — Sediment (Hybrid: Voronoi mass + Replacement) ───────
            //   columnSDF = 1 - f1
            //   heightOffset = hash2D × 5.0 ★ 5m
            //   detailSDF = max(detailSDF, -(columnSDF × 2 + heightOffset))
            //   max amp ≈ heightOffset 5m + columnSDF 2m = 7m (Replacement, 한 방향)
            //   ★ 4원칙 OK (analytical), but P4 amp 위반은 별개
            [2] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.Hybrid,
                accuracyGrade = 0.95f,
                baseAmp = 7.0f,              // ★ P4 위반 그대로 반영
                symmetric = false,           // Replacement
                principleViolation = false,  // 4원칙 OK
                codeRef = "CaveBiomeMath.hlsl L536"
            },

            // ── Case 3 — Colonnade ★★ Pure Analytical (사용자 명시 모범) ─────
            //   cylinderSDF = length(repeatXZ) - 2.0
            //   brickPattern = abs(sin(pos.y × 10)) × 0.1
            //   detailSDF = max(detailSDF, -(cylinderSDF - brickPattern))
            //   max amp = cylinder radius 2m + brick 0.1m = 2.1m (Replacement)
            //   ★★ 4원칙 모범 (noise 0%, fmod + length + sin만)
            [3] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.PureAnalytical,
                accuracyGrade = 1.00f,       // ★★★ 100% 정확
                baseAmp = 2.1f,
                symmetric = false,           // Replacement
                principleViolation = false,  // ★★ 모범
                codeRef = "CaveBiomeMath.hlsl L545"
            },

            // ── Case 4 — Rocky ★★★ 가장 심각 위반 ────────────────────────────
            //   ridgedBlock = (ridgedNoise1 + ridgedNoise2) × max(bumpAmplitude, 3.0)
            //   blockySDF = baseSDF - ridgedBlock × wallMask
            //   max amp ≈ 6m (★ P4 500% 초과)
            //   ★★★ snoise 5번 (구조적 요소)
            [4] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.PureNoise,
                accuracyGrade = 0.70f,       // 다중 noise → 통계적
                baseAmp = 6.0f,              // ★★★ P4 500% 위반
                symmetric = true,
                principleViolation = true,   // ★★★ 가장 심각
                codeRef = "CaveBiomeMath.hlsl L560-562"
            },

            // ── Case 5 — Canyon (Pure Noise + analytical strata) ─────────────
            //   blockCut + deepErosion + 표면 noise
            //   max amp ≈ 4m (P4 333% 초과)
            //   ★ snoise 3번 (구조적 요소)
            [5] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.PureNoise,
                accuracyGrade = 0.75f,
                baseAmp = 4.0f,
                symmetric = false,           // Replacement
                principleViolation = true,
                codeRef = "CaveBiomeMath.hlsl L596-613"
            },

            // ── Case 6 — Marine (Pure Noise mixed 4-Layer) ───────────────────
            //   notchDepth 0.8m + stratSDF 0.5m + scallopSDF 0.3m
            //   ★ snoise 4번 (3개가 구조적), borderline 88%
            [6] = new BiomeAmpInfo
            {
                principleClass = NoiseClass.PureNoise,
                accuracyGrade = 0.80f,
                baseAmp = 1.6f,              // 누적 합 (notch + strat + scallop)
                symmetric = true,
                principleViolation = true,   // borderline
                codeRef = "CaveBiomeMath.hlsl L640-667"
            },
        };

        /// <summary>
        /// biomeIdx로 Predictive amp 정보 조회.
        /// 미정의 biome은 안전한 default (Pure Noise + 보수적 마진) 반환.
        /// </summary>
        public static BiomeAmpInfo LookupBiomeAmpInfo(int biomeIdx)
        {
            if (PREDICTIVE_AMP_LOOKUP.TryGetValue(biomeIdx, out BiomeAmpInfo info))
                return info;
            // Default — 미지의 biome은 보수적으로 처리
            return new BiomeAmpInfo
            {
                principleClass = NoiseClass.PureNoise,
                accuracyGrade = 0.70f,
                baseAmp = 1.5f,
                symmetric = true,
                principleViolation = true,   // 미지 → 보수적
                codeRef = "default (unknown biome)"
            };
        }

        /// <summary>
        /// Single-Source γ/δ/η/ε에서 ampScale = pow(1-centrality, FadePower) 적용 후
        /// 실효 noise amp 추정.
        /// </summary>
        /// <param name="biomeIdx">biome 인덱스</param>
        /// <param name="blendCentrality">[0,1] boundary proximity</param>
        /// <param name="fadePower">Single-Source FadePower (default 2.0)</param>
        /// <param name="singleSourceActive">γ/δ/η/ε 활성 여부</param>
        /// <returns>실효 amp (m) — Adaptive width 계산용</returns>
        public static float ComputeEffectiveBiomeAmp(
            int biomeIdx, float blendCentrality, float fadePower, bool singleSourceActive)
        {
            BiomeAmpInfo info = LookupBiomeAmpInfo(biomeIdx);
            float baseAmp = info.EffectiveAmpForWidth;  // ± 합산 또는 단방향

            if (singleSourceActive)
            {
                // γ/δ/η/ε: ampScale로 자동 감쇠
                float ampScale = Mathf.Pow(Mathf.Max(1f - blendCentrality, 0f),
                                           Mathf.Max(fadePower, 0.001f));
                baseAmp *= ampScale;
            }

            // Safety factor 적용 (4원칙 분류 기반)
            return baseAmp * info.SafetyFactor;
        }
    }
}
