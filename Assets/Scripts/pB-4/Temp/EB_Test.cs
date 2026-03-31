// EB_Test.cs — 테스트 후 삭제
using UnityEngine;
using TDA.PB4.Core;
using System.Collections.Generic;
public class EB_Test : MonoBehaviour {
    void OnEnable() {
        EventBus.OnTerrainTagChanged += OnTagChanged;
        EventBus.OnFactionStateChanged += OnFactionChanged;
    }
    void OnDisable() {
        EventBus.OnTerrainTagChanged -= OnTagChanged;
        EventBus.OnFactionStateChanged -= OnFactionChanged;
    }
    void Start() {
        var bb = GameBlackboard.Instance;
        bb.SetTerrainTags(new List<string>{"DeathTrap"});
        bb.SetFactionState("orc", new FactionWorldState{populationLevel=0.8f,isDominant=true});
    }
    void OnTagChanged(IReadOnlyList<string> tags) => Debug.Log("EVENT: Tags=" + string.Join(",",tags));
    void OnFactionChanged(string id, FactionWorldState s) => Debug.Log($"EVENT: {id} pop={s.populationLevel}");
}
