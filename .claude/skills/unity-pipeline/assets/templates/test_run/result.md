# test_run · 테스트 결과 — <cycle-id>

> **이 문서는?** `/test-run <cycle-id>` 가 씬·에셋을 셋업하고 플레이로 검증한 **결과·증빙**입니다(무엇).
> "에셋이 잘 불러와지고 동작하는가"를 근거와 함께 남기려고(왜) 셋업 로그·플레이 판정·자동 수정 내역·
> 스크린샷을 `evidence/` 기반으로 기록하며(어떻게), 테스트 실행 직후 작성합니다(언제·누가).

## 검증 환경·시각
| 항목 | 값 |
|---|---|
| 실행 일시 | YYYY-MM-DD HH:MM ~ HH:MM |
| Unity / Connector | `unity-cli status` 출력 |
| 테스트 씬 | [`Assets/_TestRuns/<cycle-id>/<cycle-id>_TestScene.unity`](../../../../Assets/_TestRuns/<cycle-id>/<cycle-id>_TestScene.unity) |
| 에셋 구성 | 더미 N / 실제 M (asset_map 기준) |

## 셋업 결과
| 단계 | 결과 | 근거 |
|---|---|---|
| 폴더·에셋 생성 | ✅/⚠ | <생성·보존 개수> |
| 씬·Hierarchy 셋업 | ✅/⚠ | <GO·컴포넌트 배치 확인> |
| 에셋 슬롯 로딩 | ✅/⚠ | [components/load 덤프](evidence/…txt) |

## 플레이 테스트 판정
| 검증 항목 | 판정 | 근거 |
|---|---|---|
| 씬 로드 console error 0 | ✅/⛔ | [console](evidence/console_…txt) |
| 에셋 로딩 != null | ✅/⛔ |  |
| 참조(missing 0) | ✅/⛔ |  |
| play 진입 동작 | ✅/⛔/N-A | [스크린샷](evidence/…png) |

## 콘솔 발췌
```log
(핵심 출력 — 전문은 evidence/)
```

## 자동 수정 내역 (테스트 환경·매핑만 — §17-E)
> 게임 본 코드는 수정하지 않음. 본 코드 의심은 아래 "G6 보고"로.
| # | 증상 | 수정(환경/매핑) | 결과 |
|---|---|---|---|
| 1 |  |  |  |

## G6 보고 (게임 코드 의심 — 자동수정 안 함)
- <플레이 실패가 테스트 환경이 아니라 본 스크립트 버그로 의심되면 여기 선택지와 함께 보고. 없으면 "없음">

## 스크린샷·산출물
- ![설명](evidence/<파일>.png)
- 테스트 에셋 루트: [`Assets/_TestRuns/<cycle-id>/`](../../../../Assets/_TestRuns/<cycle-id>/)

## 잔여 / 다음 실행 안내
- 더미를 실제로 바꾸려면 [[<cycle-id>/test_run/asset_map|asset_map]] 편집 또는 폴더 드롭 후 `/test-run <cycle-id>` 재실행.

---
## 🔗 관련 문서 (Foam)
- 테스트: [[<cycle-id>/test_run/test_def|테스트 정의]] · [[<cycle-id>/test_run/asset_map|에셋 매핑]] · **result**(현재)
- 사이클: [[<cycle-id>/08_result|⑧ result]]
- 용어: [[_glossary|용어 사전]]
