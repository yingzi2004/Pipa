using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PipaController : MonoBehaviour
{
    [System.Serializable]
    public struct NoteMarker {
        public string noteName;    // 音名，如 "工", "六"
        public GameObject markerObj; // 场景里对应位置的发光点对象
    }

    public List<NoteMarker> noteMarkers; // 在 Inspector 里配置这个列表
    
    private Dictionary<string, GameObject> markerDict;

    void Start() {
        markerDict = new Dictionary<string, GameObject>();
        foreach(var m in noteMarkers) {
            if(!markerDict.ContainsKey(m.noteName) && m.markerObj != null) {
                markerDict.Add(m.noteName, m.markerObj);
                // 确保初始是灭的
                m.markerObj.SetActive(false);
            }
        }
    }

    // Vue 调用的方法：点亮
    public void HighlightString(string noteName) {
        if (markerDict.ContainsKey(noteName)) {
            // 点亮对应的点
            markerDict[noteName].SetActive(true);
        } else {
            // 可选：如果没找到具体点，是否要 fallback 到点亮整根弦？
            // Debug.LogWarning("未找到音符点: " + noteName);
        }
    }

    // Vue 调用的方法：熄灭
    public void DimString(string noteName) {
        if (markerDict.ContainsKey(noteName)) {
            markerDict[noteName].SetActive(false);
        }
    }
}