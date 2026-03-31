// BB_Test.cs — 테스트 후 삭제
using UnityEngine;
using TDA.PB4.Core;
using System.Collections.Generic;
public class BB_Test : MonoBehaviour {
    void Start() {
        var bb = GameBlackboard.Instance;
        if (bb == null) { Debug.LogError("BB NULL!"); return; }
        bb.SetTerrainTags(new List<string>{"NarrowPath","SpookyCave"});
        Debug.Log("Tags: " + string.Join(",", bb.ActiveTerrainTags));
        bb.SetGlobalIntensity(0.75f);
        Debug.Log("Intensity: " + bb.GlobalIntensityScore);
        bb.SetFactionState("goblin", new FactionWorldState{populationLevel=0.6f,isDominant=false});
        Debug.Log("Goblin pop: " + bb.ActiveFactionRegistry["goblin"].populationLevel);
        Debug.Log("=== BB TEST PASSED ===");
    }
}
