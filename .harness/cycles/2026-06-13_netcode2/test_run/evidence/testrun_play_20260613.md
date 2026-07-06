# Netcode2 Instrumentation Test Run — 2026-06-13

**Scene:** Assets/_TestRuns/2026-06-13_netcode2/2026-06-13_netcode2_TestScene.unity  
**Start:** 2026-06-15 16:02  
**End:** 2026-06-15 16:13  
**Verdict:** FAIL — Editor offline during play mode entry

---

## Step Results

### Step 1 — Open Scene
- Command: `unity-cli exec "UnityEditor.SceneManagement.EditorSceneManager.OpenScene(...); return \"opened\";"`
- Result: `opened`
- Status: PASS

### Step 2 — Pre-play Compile + Console Check
- `unity-cli editor refresh --compile`: Compilation complete (no errors)
- `unity-cli console --type error`: 2 errors found, both PRE-EXISTING/IRRELEVANT:
  1. CaveBiomeSettings DepthLayerGPUData memory size (CaveBiomeSettings.cs:339) — irrelevant
  2. SSGI URP material not using expected shader (ScreenSpaceGlobalIlluminationURP.cs:255) — irrelevant
- Instrumentation/scene-related compile errors: 0
- Transport link check: not reached (pre-play console clean of relevant errors)
- Status: PASS (no relevant errors)

### Step 3 — Enter Play Mode
- Command: `unity-cli editor play --wait`
- Result: FAILED — connector returned "cannot connect to Unity" immediately
- Subsequent `unity-cli status` showed "not responding" with heartbeat frozen at the same timestamp
- Heartbeat elapsed at time of final check: 10m+ (monotonically increasing, never recovered)
- Status: BLOCKED — Editor offline

### Step 4 — Bootstrap + StartHost + M1 Verification
- Status: NOT EXECUTED (blocked by Step 3 failure)
- NetDiagnostics GameObject: not verified
- Component count: N/A
- SessionDir: N/A
- StartHost result: N/A (Steam availability unknown)
- IsHost: N/A
- M1 RTT: N/A
- RNSM config/visible: N/A

### Step 5 — NetSim Toggle
- Status: NOT EXECUTED
- Before/after Active.Name: N/A
- Enabled: N/A

### Step 6 — M3 Disconnect
- Status: NOT EXECUTED
- events.csv: not readable (no session created)
- OnClientDisconnectCallback present: N/A
- Spurious Connect after Shutdown: N/A

### Step 7 — M8 Rehost
- Status: NOT EXECUTED
- SteamClient.IsValid: N/A
- StartHost #1, #2: N/A
- Rehost errors: N/A

### Step 8 — Stop Play
- Status: NOT EXECUTED (editor already offline)

---

## events.csv Excerpt
NOT AVAILABLE — no play session was created.

---

## Key Numbers Summary

| Metric | Value |
|--------|-------|
| Compile errors (relevant) | 0 |
| Pre-existing irrelevant errors | 2 (CaveBiomeSettings, SSGI) |
| Play mode entry | BLOCKED (Editor offline) |
| Component count | N/A |
| StartHost | N/A |
| M1 RTT | N/A |
| NetSim toggle | N/A |
| M3 disconnect-clean | N/A |
| M8 rehost errors | N/A |
| Steam available | Unknown (play never entered) |

---

## Root Cause Assessment

The Unity Editor (PID 15344, connector 0.3.22) lost its connector WebSocket connection immediately 
when `editor play --wait` was issued. The connector itself survived (heartbeat counter kept 
incrementing), but Unity's main thread stopped responding to the connector socket.

Probable causes (in order of likelihood):
1. Domain reload during play mode entry caused the connector's in-process server to restart, 
   temporarily breaking the WebSocket — the `--wait` command fired before reconnection.
2. Steam SDK (SteamP2PRelayTransport) attempted to initialize during RuntimeInitializeOnLoadMethod 
   and blocked the main thread long enough to trigger a connector timeout.
3. The connector needs a moment after domain reload before accepting commands — `play --wait` 
   with no reconnect retry hit the window.

The editor may still be running in play mode (or may have crashed). The user must check the 
Unity Editor window directly.

---

## Recommendation (for 08_result)

**FAIL** — Play verification could not be completed. The editor went offline at Step 3.

Actions required:
- Check Unity Editor window: is it in play mode, edit mode, or crashed?
- If in play mode: manually stop play, then re-run the test with `editor play` (without `--wait`) 
  followed by a brief pause before issuing exec commands.
- If crashed: investigate the console for Steam SDK initialization errors.
- Consider wrapping SteamP2PRelayTransport initialization in a try/catch to prevent hang on 
  domain reload when Steam is not running.

---

## Evidence Files

- `evidence/status.txt` — unity-cli status before and after play attempt
- `evidence/console_preplay_1602.txt` — pre-play console error dump (step 2)
- `evidence/console_step2_preplay_1602.txt` — annotated pre-play console
- `evidence/console_playmode_BLOCKED_1602.txt` — play mode failure detail
- `evidence/testrun_play_20260613.md` — this file
