#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-D 제안 7] PreviewPresets — 큐레이션된 preset 라이브러리
    //
    // 목적: Preview Editor에서 자주 쓰는 파라미터 조합을 원클릭 로드.
    //   - Small Dungeon: 빠른 테스트용 (10 rooms, 200×200)
    //   - Large Boss: Boss 멀티에 적합 (30 rooms, 400×400)
    //   - Thin Corridor: 좁은 통로 스타일 (Width 3m)
    //   - Wide Cavern: 넓은 공동 스타일 (Width 12m, radius 20m)
    //
    // 사용: GraphPreviewWindow에서 Preset dropdown → 선택 시 _inputs 덮어쓰기.
    // ══════════════════════════════════════════════════════════════════════

    public static class PreviewPresets
    {
        public class Preset
        {
            public string name;
            public string description;
            public Vector3 dungeonBounds;
            public int targetRoomCount;
            public float mainPathWidth;
            public float sidePathWidth;
            public float minRoomRadius;
            public float maxRoomRadius;
            public float bossRoomRadiusMultiplier;
            public float loopProbability;
            public float macroBiomeScale;
            public float heatmapGridSize;
        }

        public static List<Preset> All => _all;

        private static readonly List<Preset> _all = new List<Preset>
        {
            // 1. Small Dungeon — 빠른 테스트용
            new Preset {
                name = "🏠 Small Dungeon (빠른 테스트)",
                description = "10 rooms, 200×200m — 전체 생성 ~0.5s",
                dungeonBounds = new Vector3(200f, 50f, 200f),
                targetRoomCount = 10,
                mainPathWidth = 6f,
                sidePathWidth = 3f,
                minRoomRadius = 6f,
                maxRoomRadius = 12f,
                bossRoomRadiusMultiplier = 1.8f,
                loopProbability = 0.1f,
                macroBiomeScale = 300f,
                heatmapGridSize = 8f,
            },

            // 2. Default (기본값)
            new Preset {
                name = "⚖ Default (현재 설정 기본값)",
                description = "20 rooms, 300×300m — pB-4 표준",
                dungeonBounds = new Vector3(300f, 50f, 300f),
                targetRoomCount = 20,
                mainPathWidth = 8f,
                sidePathWidth = 4f,
                minRoomRadius = 8f,
                maxRoomRadius = 15f,
                bossRoomRadiusMultiplier = 2.0f,
                loopProbability = 0.15f,
                macroBiomeScale = 500f,
                heatmapGridSize = 10f,
            },

            // 3. Large Boss — Boss 멀티
            new Preset {
                name = "⚔ Large Boss (Boss 멀티)",
                description = "30 rooms, 400×400m — Boss 2배 크기",
                dungeonBounds = new Vector3(400f, 60f, 400f),
                targetRoomCount = 30,
                mainPathWidth = 10f,
                sidePathWidth = 5f,
                minRoomRadius = 10f,
                maxRoomRadius = 18f,
                bossRoomRadiusMultiplier = 2.5f,
                loopProbability = 0.2f,
                macroBiomeScale = 600f,
                heatmapGridSize = 12f,
            },

            // 4. Thin Corridor — 좁은 통로 스타일
            new Preset {
                name = "🕳 Thin Corridor (좁은 통로)",
                description = "통로 3m, 방 작음 — 긴장감 유발",
                dungeonBounds = new Vector3(250f, 40f, 250f),
                targetRoomCount = 18,
                mainPathWidth = 4f,
                sidePathWidth = 2.5f,
                minRoomRadius = 5f,
                maxRoomRadius = 9f,
                bossRoomRadiusMultiplier = 2.2f,
                loopProbability = 0.1f,
                macroBiomeScale = 400f,
                heatmapGridSize = 8f,
            },

            // 5. Wide Cavern — 넓은 공동 스타일
            new Preset {
                name = "🌋 Wide Cavern (넓은 공동)",
                description = "통로 12m, 방 20m — 개방감 중심",
                dungeonBounds = new Vector3(350f, 60f, 350f),
                targetRoomCount = 15,
                mainPathWidth = 12f,
                sidePathWidth = 8f,
                minRoomRadius = 12f,
                maxRoomRadius = 20f,
                bossRoomRadiusMultiplier = 2.0f,
                loopProbability = 0.2f,
                macroBiomeScale = 700f,
                heatmapGridSize = 12f,
            },

            // 6. Loop Heavy — 루프 많음 (막다른 길 방지)
            new Preset {
                name = "🔁 Loop Heavy (순환 연결)",
                description = "loopProbability 40% — 탐색 가치 높음",
                dungeonBounds = new Vector3(300f, 50f, 300f),
                targetRoomCount = 22,
                mainPathWidth = 8f,
                sidePathWidth = 5f,
                minRoomRadius = 8f,
                maxRoomRadius = 14f,
                bossRoomRadiusMultiplier = 2.0f,
                loopProbability = 0.4f,
                macroBiomeScale = 500f,
                heatmapGridSize = 10f,
            },
        };

        /// <summary>선택된 preset을 PreviewInputs에 적용.</summary>
        public static void ApplyPreset(PreviewInputs inputs, Preset p)
        {
            if (inputs == null || p == null) return;
            inputs.dungeonBounds = p.dungeonBounds;
            inputs.targetRoomCount = p.targetRoomCount;
            inputs.mainPathWidth = p.mainPathWidth;
            inputs.sidePathWidth = p.sidePathWidth;
            inputs.minRoomRadius = p.minRoomRadius;
            inputs.maxRoomRadius = p.maxRoomRadius;
            inputs.bossRoomRadiusMultiplier = p.bossRoomRadiusMultiplier;
            inputs.loopProbability = p.loopProbability;
            inputs.macroBiomeScale = p.macroBiomeScale;
            inputs.heatmapGridSize = p.heatmapGridSize;
        }
    }
}
#endif
