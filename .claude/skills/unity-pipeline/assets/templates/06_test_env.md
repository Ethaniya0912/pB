# 06 · test_env — 테스트 환경 정의

> as-is/to-be 를 unity-cli 재현 가능한 수준으로. 적용 전 G5 승인.
> `.prefab/.unity/.asset/.mat` 편집의 reserialize·compile·console 은 PostToolUse 훅이 자동 수행.

## test_env 매트릭스
| 대상 | as-is | to-be | 적용 방법 |
|---|---|---|---|
| Test.unity | 없음 |  | Write→훅 / exec |

## 적용 스니펫 (쿡북 §4 참조)
```bash
# 예: unity-cli exec "var go=new UnityEngine.GameObject(\"TestRig\"); ..."
```

## G5 확인
- 적용 대상 씬/프리팹: 
- 영향 범위:
