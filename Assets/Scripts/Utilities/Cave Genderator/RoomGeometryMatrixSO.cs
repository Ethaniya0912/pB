using UnityEngine;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase E-α.7] RoomGeometryMatrixSO
    //
    // 목적:
    //   roomType (Spawn/Boss/Treasure/Sinkhole/Normal) × biomeType (Case 0~6) 
    //   2D 매트릭스로 실제 Room Geometry를 세밀하게 결정.
    //
    //   예시 매트릭스 (35칸):
    //   ┌─────────────┬───────┬──────────┬──────────┬──────────┬─────────┐
    //   │ roomType ╲ biome │ Karst │ Columnar │ Sediment │ Colonnade │ ... │
    //   ├─────────────┼───────┼──────────┼──────────┼──────────┼─────────┤
    //   │ Spawn (1)    │ Pear  │ HexCol   │ DiscPlate │ Cathedral │ ...    │
    //   │ Boss (2)     │ Cathedral │ Cathedral │ DiscPlate × 2 │ Cathedral × 3 │ ... │
    //   │ Treasure (3) │ Pear S│ Pear S   │ DiscPlate S │ HexCol S │ ... │
    //   │ Sinkhole (4) │ Slot  │ Slot     │ Slot      │ Slot      │ ... │
    //   │ Normal (0)   │ Sphere│ HexCol   │ DiscPlate │ Sphere    │ ... │
    //   └─────────────┴───────┴──────────┴──────────┴──────────┴─────────┘
    //
    //   + radiusMultiplier (S=0.7, default 1.0, Boss=1.5)
    //   + sculptFlagsOverride (A.5 sculpt 편향)
    //
    // 현재 상태:
    //   ✓ SO 정의 + Inspector 매트릭스 편집
    //   ○ Phase 4-3 또는 Phase 5에서 실제 shader 소비 (primitive routing 확장 + sculptFlags 활용)
    //   ✓ null slot 기본값 fallback (biomeData.roomPrimitive 사용)
    //
    // 규칙 #6:
    //   Matrix SO 미할당 → 기존 경로 (CaveBiomeData.roomPrimitive 전역 적용).
    // ══════════════════════════════════════════════════════════════════════

    [CreateAssetMenu(fileName = "NewRoomGeometryMatrix", menuName = "Cave System/Room Geometry Matrix", order = 14)]
    public class RoomGeometryMatrixSO : ScriptableObject
    {
        [Header("Identity")]
        public string matrixName = "Default Room Geometry Matrix";

        // ═══════════════════════════════════════
        // Matrix Cells
        //   5 roomTypes × 7 biomes = 35 cells
        //   단, Inspector에서 [System.Serializable] struct 리스트로 편집
        // ═══════════════════════════════════════
        [Header("Matrix Entries (편집 가능한 조합)")]
        [Tooltip("특정 (roomType, biomeType) 조합에 대한 geometry 덮어쓰기. " +
                 "여기 없는 조합은 CaveBiomeData.roomPrimitive 기본값 사용.")]
        public MatrixEntry[] entries = new MatrixEntry[0];

        // ═══════════════════════════════════════
        // Editor Event
        // ═══════════════════════════════════════
        public static event System.Action OnMatrixModified;

        private void OnValidate()
        {
            if (Application.isPlaying) OnMatrixModified?.Invoke();
        }

        // ═══════════════════════════════════════
        // Lookup
        // ═══════════════════════════════════════

        /// <summary>
        /// (roomType, biomeType) 조합에 매칭하는 entry 찾기.
        /// 매칭 없으면 null.
        /// </summary>
        public MatrixEntry? Lookup(int roomType, int biomeType)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].roomType == roomType && entries[i].biomeType == biomeType)
                    return entries[i];
            }
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // MatrixEntry — 한 (roomType, biomeType) 조합의 설정
    // ══════════════════════════════════════════════════════════════════════
    [System.Serializable]
    public struct MatrixEntry
    {
        [Tooltip("roomType: 0=Normal, 1=Spawn, 2=Boss, 3=Treasure, 4=Sinkhole")]
        [Range(0, 4)]
        public int roomType;

        [Tooltip("biomeType (noiseType): 0=Karst, 1=Columnar, 2=Sedimentary, 3=Colonnade, 4=Rocky, 5=Canyon, 6=Marine")]
        [Range(0, 6)]
        public int biomeType;

        [Header("Geometry Override")]
        [Tooltip("이 조합에서 사용할 room primitive (기본 Sphere=0).")]
        public RoomPrimitiveType primitiveOverride;

        [Tooltip("반경 배수. 1.0=기본, Boss용 1.5, Treasure용 0.7 권장.")]
        [Range(0.3f, 3f)]
        public float radiusMultiplier;

        [Header("Sculpt Override (A.5 sculptFlags)")]
        [Tooltip("sculptFlags 덮어쓰기. (0,0,0)=기본 roomType 자동 설정 유지.")]
        public Vector3 sculptFlagsOverride;

        [Tooltip("sculptFlagsOverride 적용 여부. false면 위 벡터 무시.")]
        public bool useSculptOverride;
    }
}
