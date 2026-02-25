// Assets/Scripts/Utilities/Cave Genderator/CaveSaveLoadSystem.cs
// NRE 방어를 위한 Null 체크 로직 추가
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

namespace CaveSystem
{
    public class CaveSaveLoadSystem : MonoBehaviour
    {
        private Dictionary<Vector3Int, Dictionary<Vector3Int, sbyte>> deltaStorage;
        private string saveFilePath;
        private readonly object fileLock = new object();

        private const int MAX_CHUNKS_LIMIT = 500000;
        private const int MAX_VOXELS_PER_CHUNK = 4096;

        private void Awake()
        {
            deltaStorage = new Dictionary<Vector3Int, Dictionary<Vector3Int, sbyte>>();
            saveFilePath = Path.Combine(Application.persistentDataPath, "CaveWorldDelta.dat");
        }

        public IEnumerator InitializeAndLoadCoroutine()
        {
            bool loadComplete = false;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                LoadDeltaDataFromFile();
                loadComplete = true;
            });

            yield return new WaitUntil(() => loadComplete);
        }

        public void RecordDelta(Vector3Int chunkPos, Vector3Int localVoxelPos, sbyte newValue)
        {
            lock (fileLock)
            {
                if (deltaStorage == null) return;

                if (!deltaStorage.ContainsKey(chunkPos))
                {
                    deltaStorage[chunkPos] = new Dictionary<Vector3Int, sbyte>();
                }

                deltaStorage[chunkPos][localVoxelPos] = newValue;
            }
        }

        public bool TryGetChunkDelta(Vector3Int chunkPos, out Dictionary<Vector3Int, sbyte> chunkDelta)
        {
            lock (fileLock)
            {
                chunkDelta = null;
                if (deltaStorage == null) return false;
                return deltaStorage.TryGetValue(chunkPos, out chunkDelta);
            }
        }

        public void SaveDeltaDataToFile()
        {
            lock (fileLock)
            {
                try
                {
                    // [에러 수정] deltaStorage가 아예 초기화되지 않았거나 null일 경우 방어
                    if (deltaStorage == null) return;

                    using (FileStream fs = new FileStream(saveFilePath, FileMode.Create, FileAccess.Write))
                    using (BinaryWriter bw = new BinaryWriter(fs))
                    {
                        bw.Write(deltaStorage.Count);

                        foreach (var chunkKvp in deltaStorage)
                        {
                            // [에러 수정] 딕셔너리 내부 밸류가 비어있는 경우 방어
                            if (chunkKvp.Value == null) continue;

                            bw.Write(chunkKvp.Key.x);
                            bw.Write(chunkKvp.Key.y);
                            bw.Write(chunkKvp.Key.z);

                            bw.Write(chunkKvp.Value.Count);

                            foreach (var voxelKvp in chunkKvp.Value)
                            {
                                bw.Write(voxelKvp.Key.x);
                                bw.Write(voxelKvp.Key.y);
                                bw.Write(voxelKvp.Key.z);
                                bw.Write(voxelKvp.Value);
                            }
                        }
                    }
                    Debug.Log($"<color=cyan>[SaveLoadSystem]</color> 델타 데이터 저장 성공. (저장된 청크: {deltaStorage.Count}개)");
                }
                catch (Exception e)
                {
                    Debug.LogError($"<color=red>[SaveLoadSystem] 🚨 세이브 쓰기 중 치명적 오류 발생:</color> {e.Message}");
                }
            }
        }

        private void LoadDeltaDataFromFile()
        {
            lock (fileLock)
            {
                if (!File.Exists(saveFilePath))
                {
                    Debug.Log("<color=cyan>[SaveLoadSystem]</color> 기존 세이브 파일이 없습니다. 새로운 월드를 생성합니다.");
                    return;
                }

                var tempStorage = new Dictionary<Vector3Int, Dictionary<Vector3Int, sbyte>>();

                try
                {
                    using (FileStream fs = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read))
                    using (BinaryReader br = new BinaryReader(fs))
                    {
                        if (fs.Length == 0)
                        {
                            Debug.LogWarning("<color=yellow>[SaveLoadSystem]</color> ⚠️ 세이브 파일이 비어있습니다. 초기화합니다.");
                            return;
                        }

                        int chunkCount = br.ReadInt32();

                        if (chunkCount < 0 || chunkCount > MAX_CHUNKS_LIMIT)
                        {
                            throw new Exception($"비정상적인 청크 개수 감지됨 ({chunkCount}개). 파일 헤더가 손상되었습니다.");
                        }

                        int totalVoxelsLoaded = 0;

                        for (int i = 0; i < chunkCount; i++)
                        {
                            Vector3Int chunkPos = new Vector3Int(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                            int voxelCount = br.ReadInt32();

                            if (voxelCount < 0 || voxelCount > MAX_VOXELS_PER_CHUNK)
                            {
                                throw new Exception($"청크 {chunkPos}의 비정상적인 복셀 개수 감지됨 ({voxelCount}개). 데이터 블록이 손상되었습니다.");
                            }

                            Dictionary<Vector3Int, sbyte> voxelDict = new Dictionary<Vector3Int, sbyte>();
                            for (int j = 0; j < voxelCount; j++)
                            {
                                Vector3Int voxelPos = new Vector3Int(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                                sbyte val = br.ReadSByte();
                                voxelDict[voxelPos] = val;
                                totalVoxelsLoaded++;
                            }

                            tempStorage[chunkPos] = voxelDict;
                        }

                        deltaStorage = tempStorage;
                        Debug.Log($"<color=green>[SaveLoadSystem] ✔️ 델타 세이브 데이터 복구 완료!</color> (청크: {chunkCount}개, 변경된 복셀: {totalVoxelsLoaded}개)");
                    }
                }
                catch (EndOfStreamException)
                {
                    Debug.LogError("<color=red>[SaveLoadSystem] 🚨 세이브 파일 로드 실패 (파일 잘림/깨짐 감지!)</color>");
                }
                catch (Exception e)
                {
                    Debug.LogError($"<color=red>[SaveLoadSystem] 🚨 세이브 데이터 파싱 중 치명적 오류:</color> {e.Message}");
                }
            }
        }

        private void OnApplicationQuit()
        {
            SaveDeltaDataToFile();
        }
    }
}