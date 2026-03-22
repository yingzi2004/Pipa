using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PipaController : MonoBehaviour
{
    // === 数据结构 ===

    [System.Serializable]
    public struct NoteTarget {
        public string noteName;      // 音名 (如: 工)
        public GameObject targetObj; // 物体
    }

    [System.Serializable]
    public class FingerGroup {
        [Tooltip("特殊指法名称")]
        public string fingerName;    
        
        [Tooltip("不勾选 = 严格模式(没有则不亮)。勾选 = 没配置的使用单音位置。")]
        public bool inheritSingleNotes = false; 

        [Tooltip("特殊位置配置")]
        public List<NoteTarget> noteOverrides;

        [Header("--- 指法偏移 (可选) ---")]
        [Tooltip("生成物体时相对单音位置的全局偏移量，适用于整组指法统一偏移的情况")]
        public Vector3 groupOffset = Vector3.zero;

        [Header("--- 指法颜色 (可选) ---")]
        [Tooltip("勾选后使用自定义颜色，否则用全局点/挑颜色")]
        public bool useCustomColor = false;
        [ColorUsage(true, true)] public Color customColor = new Color(0f, 1f, 0.5f, 3f);
    }

    [System.Serializable]
    public class InstrumentSet {
        [Header("--- 共享位置配置 (Base) ---")]
        public List<NoteTarget> singleNotes;

        [Header("--- 特殊位置配置 (Special) ---")]
        public List<FingerGroup> fingerGroups;
    }

    // === Inspector ===
    
    [Header("全局颜色")]
    [ColorUsage(true, true)] public Color tiaoColor = new Color(0f, 0.5f, 1f, 3f);   
    [ColorUsage(true, true)] public Color dianColor = new Color(1f, 0.8f, 0f, 3f);   

    [Header("四空管配置")]
    public InstrumentSet fourPipes; 

    [Header("五空管配置")]
    public InstrumentSet fivePipes; 

    // === 内部状态 ===
    private InstrumentSet currentSet;
    private Dictionary<string, GameObject> singleNoteMap = new Dictionary<string, GameObject>();
    private Dictionary<string, FingerGroup> fingerGroupMap = new Dictionary<string, FingerGroup>();
    private GameObject lastActiveObj = null;

    // 基础音色类型（这些不是特殊指法，用 singleNote 位置）
    private static readonly HashSet<string> basicToneTypes = new HashSet<string> { "点", "挑", "落指）", "勾指√" };

    void Start() {
        SetInstrument("FourAirPipes"); // 默认载入
        Debug.Log("PipaController Initialized (Unified-Message Mode).");
    }

    public void SetInstrument(string mode) {
        TurnOffAll();
        
        Debug.Log($"Switching Instrument to: {mode}");

        if (mode == "FiveAirPipes") {
            currentSet = fivePipes;
        } else {
            currentSet = fourPipes;
        }

        RebuildCache();
    }

    void RebuildCache() {
        singleNoteMap.Clear();
        fingerGroupMap.Clear();
        
        if(currentSet == null) return;

        foreach(var n in currentSet.singleNotes) {
            if(!string.IsNullOrEmpty(n.noteName) && n.targetObj != null && !singleNoteMap.ContainsKey(n.noteName))
                singleNoteMap.Add(n.noteName, n.targetObj);
        }

        foreach(var g in currentSet.fingerGroups) {
            if(!string.IsNullOrEmpty(g.fingerName) && !fingerGroupMap.ContainsKey(g.fingerName))
                fingerGroupMap.Add(g.fingerName, g);
        }
    }

    // 标记正在派发以防循环调用
    private bool _isHighlightDispatching = false;

    // ============================
    // Vue 调用入口 (统一消息格式)
    // 接收格式: "工" / "工|点" / "工|挑" / "工|甲线十" / "工|慢撚○" ...
    // ============================
    public void HighlightString(string data) {
        if(string.IsNullOrEmpty(data) || _isHighlightDispatching) return;

        _isHighlightDispatching = true;
        try {
            string note = "";
            string typeStr = "";

            if (data.Contains("|")) {
                var parts = data.Split('|');
                note = parts[0];
                if (parts.Length > 1) typeStr = parts[1];
            } else {
                note = data; 
            }

            // 把前端可能传来的生僻字 𢩩 转换为 Unity Inspector 易于配置的 扌乂
            if (note.Contains("𢩩") || note.Contains("扌")) note = "扌乂";

            string processedKey = string.IsNullOrEmpty(typeStr) ? note : $"{note}|{typeStr}";
            
            // 转发给时间序列亮光控制器（用于处理类似 扌乂|top 的动态时序亮光）
            var tlc = GetComponent<TimedLightController>();
            if (tlc == null) tlc = FindObjectOfType<TimedLightController>();
            if (tlc != null) {
                Debug.Log($"[PipaController] 已找到时序控制器，正在派发: {processedKey}");
                tlc.OnKeyPressed(processedKey);
            } else {
                Debug.LogWarning($"[PipaController] 未找到 TimedLightController，无法执行特殊灯光效果。");
            }

            Debug.Log($"[Highlight] note={note}, type={typeStr}");
            UpdateDisplay(note, typeStr);
        } finally {
            _isHighlightDispatching = false;
        }
    }

    void UpdateDisplay(string note, string typeStr) {
        if(string.IsNullOrEmpty(note)) return;

        GameObject target = null;
        bool isBasicTone = basicToneTypes.Contains(typeStr);
        
        // === 位置查找 ===
        if(!string.IsNullOrEmpty(typeStr) && !isBasicTone) {
            // 特殊指法（甲线十、勾指√、慢撚○、全撚○、落指）等）→ 查 fingerGroupMap
            if(fingerGroupMap.TryGetValue(typeStr, out var group)) {
                var overrideNote = group.noteOverrides.FirstOrDefault(n => n.noteName == note);
                
                if(overrideNote.targetObj != null) {
                    target = overrideNote.targetObj;
                } 
                else if(group.inheritSingleNotes) {
                    singleNoteMap.TryGetValue(note, out target);
                }
                // else: inherit=false -> target=null (没配就不亮)
            }
            // fingerGroupMap 里找不到这个指法 → target 保持 null（没配就不亮）
        } 
        else {
            // 基础音色（点/挑）或无类型 → 使用共享位置
            singleNoteMap.TryGetValue(note, out target);
        }

        // === 切换显示物体 ===
        if(target != lastActiveObj) {
            if(lastActiveObj != null) lastActiveObj.SetActive(false);
            if(target != null) {
                Activate(target, typeStr);
                lastActiveObj = target;
            } else {
                lastActiveObj = null;
            }
        } else if (target != null) {
            // 即使是同一个物体，也需要重新刷新颜色（比如从"挑"立刻变成"点"）
            Activate(target, typeStr);
        }
    }

    public void DimString(string data) {
        if (string.IsNullOrEmpty(data)) return;

        // 【可选功能】如果前端音频结束后希望立刻打断 TimedLightController 的剩余发光序列，请释放以下代码：
        /*
        string note = "";
        string typeStr = "";
        if (data.Contains("|")) {
            var parts = data.Split('|');
            note = parts[0];
            if (parts.Length > 1) typeStr = parts[1];
        } else {
            note = data; 
        }
        if (note == "𢩩") note = "扌乂";
        string processedKey = string.IsNullOrEmpty(typeStr) ? note : $"{note}|{typeStr}";
        
        var tlc = GetComponent<TimedLightController>();
        if (tlc == null) tlc = FindObjectOfType<TimedLightController>();
        // 当熄灭指令和正在播放的序列匹配时停止
        // if (tlc != null && tlc.IsSequenceActive()) tlc.ManualStop();
        */

        if(lastActiveObj != null) {
            lastActiveObj.SetActive(false);
            lastActiveObj = null;
        }
    }

    // 兼容旧接口（保留但不再使用）
    public void HighlightFingering(string finger) { }
    public void DimFingering(string finger) { }

    // ============================
    // 颜色决定逻辑
    // ============================
    Color ResolveColor(string typeStr) {
        // 1. 如果是特殊指法且配置了自定义颜色 → 使用自定义颜色
        if(!string.IsNullOrEmpty(typeStr) && !basicToneTypes.Contains(typeStr)) {
            if(fingerGroupMap.TryGetValue(typeStr, out var group) && group.useCustomColor) {
                return group.customColor;
            }
        }

        // 2. 基础规则: "点" → dianColor, 其他 → tiaoColor
        //    特殊指法中含"点"或"甲线"也走 dianColor（兼容旧逻辑）
        if(!string.IsNullOrEmpty(typeStr) && (typeStr == "点" || typeStr.Contains("甲线"))) {
            return dianColor;
        }

        return tiaoColor;
    }

    void Activate(GameObject obj, string typeStr) {
        if(!obj) return;
        obj.SetActive(true);
        
        Color c = ResolveColor(typeStr);
        
        // 设置所有材质颜色
        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach(var r in renderers) {
            r.material.color = c;
            r.material.SetColor("_EmissionColor", c * 1.5f);
        }
        var images = obj.GetComponentsInChildren<Image>(true);
        foreach(var i in images) i.color = c;
    }

    void TurnOffAll() {
        DisableSet(fourPipes);
        DisableSet(fivePipes);
        lastActiveObj = null;
    }

    void DisableSet(InstrumentSet set) {
        if(set != null) {
            foreach(var n in set.singleNotes) if(n.targetObj) n.targetObj.SetActive(false);
            foreach(var g in set.fingerGroups)
                foreach(var n in g.noteOverrides) if(n.targetObj) n.targetObj.SetActive(false);
        }
    }

#if UNITY_EDITOR
    // ============================
    // Editor 工具
    // ============================

    [ContextMenu("自动列出指法 (Reset & Populate)")]
    private void Reset() {
        fourPipes = InitInstrumentSet();
        fivePipes = InitInstrumentSet();
    }

    private InstrumentSet InitInstrumentSet() {
        var set = new InstrumentSet();
        set.singleNotes = new List<NoteTarget>();
        set.fingerGroups = new List<FingerGroup>();

        // 这里的指法才关乎位置变化
        string[] specialFingers = new string[] {
            "慢撚○",
            "全撚○",
            "甲线十"
        };

        foreach(var f in specialFingers) {
            var group = new FingerGroup();
            group.fingerName = f;
            group.noteOverrides = new List<NoteTarget>();
            group.inheritSingleNotes = false; // 默认严格模式
            group.useCustomColor = false;
            set.fingerGroups.Add(group);
        }
        return set;
    }

    /// <summary>
    /// 一键从单音物体克隆生成指法物体。
    /// 对每个 FingerGroup，以 singleNotes 为模板克隆物体，
    /// 加上 groupOffset 偏移，放在以指法命名的父容器下。
    /// 已有 noteOverrides 的音不会重复生成。
    /// </summary>
    [ContextMenu("★ 四空管: 从单音克隆生成指法物体")]
    private void GenerateFingerObjectsFour() {
        GenerateFingerObjects(fourPipes, "四空管");
    }

    [ContextMenu("★ 五空管: 从单音克隆生成指法物体")]
    private void GenerateFingerObjectsFive() {
        GenerateFingerObjects(fivePipes, "五空管");
    }

    [ContextMenu("✖ 四空管: 清除指法物体并重新生成")]
    private void ClearAndRegenerateFour() {
        ClearFingerObjects(fourPipes, "四空管");
        GenerateFingerObjects(fourPipes, "四空管");
    }

    [ContextMenu("✖ 五空管: 清除指法物体并重新生成")]
    private void ClearAndRegenerateFive() {
        ClearFingerObjects(fivePipes, "五空管");
        GenerateFingerObjects(fivePipes, "五空管");
    }

    private void ClearFingerObjects(InstrumentSet set, string setLabel) {
        if(set == null) return;

        Undo.RecordObject(this, $"清除指法物体 ({setLabel})");

        foreach(var group in set.fingerGroups) {
            if(string.IsNullOrEmpty(group.fingerName)) continue;

            // 删除场景中的容器物体
            string containerName = $"指法_{group.fingerName}_{setLabel}";
            Transform container = transform.Find(containerName);
            if(container != null) {
                Undo.DestroyObjectImmediate(container.gameObject);
                Debug.Log($"  删除容器: {containerName}");
            }

            // 如果noteOverrides里有残留物体也删除（不在容器下的散落物体）
            if(group.noteOverrides != null) {
                foreach(var n in group.noteOverrides) {
                    if(n.targetObj != null) {
                        Undo.DestroyObjectImmediate(n.targetObj);
                    }
                }
            }

            // 清空列表
            group.noteOverrides = new List<NoteTarget>();
        }

        EditorUtility.SetDirty(this);
        Debug.Log($"[{setLabel}] 已清除所有指法物体和引用。");
    }

    private void GenerateFingerObjects(InstrumentSet set, string setLabel) {
        if(set == null) {
            Debug.LogWarning($"[{setLabel}] InstrumentSet 为 null！");
            return;
        }
        if(set.singleNotes == null || set.singleNotes.Count == 0) {
            Debug.LogWarning($"[{setLabel}] 没有单音配置，无法生成。请先配置 singleNotes。");
            return;
        }
        if(set.fingerGroups == null || set.fingerGroups.Count == 0) {
            Debug.LogWarning($"[{setLabel}] 没有指法分组配置！");
            return;
        }

        // 检查单音有效性
        int validSingleNotes = set.singleNotes.Count(n => !string.IsNullOrEmpty(n.noteName) && n.targetObj != null);
        Debug.Log($"[{setLabel}] 开始生成... 有效单音数: {validSingleNotes}, 指法组数: {set.fingerGroups.Count}");

        Undo.RecordObject(this, $"生成指法物体 ({setLabel})");

        int totalCreated = 0;

        foreach(var group in set.fingerGroups) {
            if(string.IsNullOrEmpty(group.fingerName)) {
                Debug.LogWarning($"  跳过: 指法名称为空");
                continue;
            }

            // 已有的音名集合，避免重复
            HashSet<string> existingNotes = new HashSet<string>();
            if(group.noteOverrides != null) {
                foreach(var n in group.noteOverrides) {
                    if(!string.IsNullOrEmpty(n.noteName) && n.targetObj != null)
                        existingNotes.Add(n.noteName);
                }
            } else {
                group.noteOverrides = new List<NoteTarget>();
            }

            Debug.Log($"  指法[{group.fingerName}]: 已有 {existingNotes.Count} 个有效覆盖, noteOverrides列表长度={group.noteOverrides.Count}");

            // 查找或创建父容器: "指法_甲线十_四空管"
            string containerName = $"指法_{group.fingerName}_{setLabel}";
            Transform container = transform.Find(containerName);
            if(container == null) {
                var go = new GameObject(containerName);
                go.transform.SetParent(transform);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(go, $"创建容器 {containerName}");
                container = go.transform;
                Debug.Log($"  创建容器: {containerName}");
            }

            int groupCreated = 0;
            foreach(var singleNote in set.singleNotes) {
                if(string.IsNullOrEmpty(singleNote.noteName) || singleNote.targetObj == null) continue;
                if(existingNotes.Contains(singleNote.noteName)) continue;

                // 克隆单音物体
                GameObject clone = Instantiate(singleNote.targetObj, container);
                clone.name = $"{singleNote.noteName}_{group.fingerName}";
                
                // 应用位置 = 原始位置 + 组偏移
                clone.transform.position = singleNote.targetObj.transform.position + group.groupOffset;
                clone.transform.rotation = singleNote.targetObj.transform.rotation;
                clone.transform.localScale = singleNote.targetObj.transform.localScale;
                
                clone.SetActive(true); // 编辑模式下保持可见，方便调整位置
                Undo.RegisterCreatedObjectUndo(clone, $"克隆 {clone.name}");

                // 添加到 noteOverrides
                group.noteOverrides.Add(new NoteTarget {
                    noteName = singleNote.noteName,
                    targetObj = clone
                });

                groupCreated++;
                totalCreated++;
            }
            Debug.Log($"  指法[{group.fingerName}]: 新建 {groupCreated} 个物体");
        }

        EditorUtility.SetDirty(this);
        // 如果是 Prefab 实例，确保修改被记录
        if(PrefabUtility.IsPartOfPrefabInstance(this)) {
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        Debug.Log($"[{setLabel}] 指法物体生成完毕！共创建 {totalCreated} 个物体。" +
                  $"\n→ 现在可以在 Scene 中选中各指法容器(指法_xxx_{setLabel})，拖动子物体到正确位置。");
    }

    /// <summary>
    /// 对已生成的指法物体重新应用 groupOffset（不重新克隆，只移动位置）。
    /// 用法：调整 groupOffset 后右键执行此操作。
    /// </summary>
    [ContextMenu("★ 四空管: 重新应用指法偏移")]
    private void ReapplyOffsetFour() {
        ReapplyOffset(fourPipes, "四空管");
    }

    [ContextMenu("★ 五空管: 重新应用指法偏移")]
    private void ReapplyOffsetFive() {
        ReapplyOffset(fivePipes, "五空管");
    }

    private void ReapplyOffset(InstrumentSet set, string setLabel) {
        if(set == null) return;

        Undo.RecordObject(this, $"重新应用偏移 ({setLabel})");

        foreach(var group in set.fingerGroups) {
            if(group.noteOverrides == null) continue;

            foreach(var noteOverride in group.noteOverrides) {
                if(noteOverride.targetObj == null || string.IsNullOrEmpty(noteOverride.noteName)) continue;

                // 找到对应的单音位置
                var singleNote = set.singleNotes.FirstOrDefault(n => n.noteName == noteOverride.noteName);
                if(singleNote.targetObj == null) continue;

                Undo.RecordObject(noteOverride.targetObj.transform, $"移动 {noteOverride.noteName}");
                noteOverride.targetObj.transform.position = singleNote.targetObj.transform.position + group.groupOffset;
            }
        }

        Debug.Log($"[{setLabel}] 偏移已重新应用。如需微调个别音，请在 Scene 中手动拖动对应物体。");
    }

    [ContextMenu("👁 四空管: 显示所有指法物体 (编辑用)")]
    private void ShowFingerObjectsFour() { ToggleFingerObjects(fourPipes, "四空管", true); }

    [ContextMenu("👁 四空管: 隐藏所有指法物体")]
    private void HideFingerObjectsFour() { ToggleFingerObjects(fourPipes, "四空管", false); }

    [ContextMenu("👁 五空管: 显示所有指法物体 (编辑用)")]
    private void ShowFingerObjectsFive() { ToggleFingerObjects(fivePipes, "五空管", true); }

    [ContextMenu("👁 五空管: 隐藏所有指法物体")]
    private void HideFingerObjectsFive() { ToggleFingerObjects(fivePipes, "五空管", false); }

    private void ToggleFingerObjects(InstrumentSet set, string setLabel, bool show) {
        if(set == null) return;
        int count = 0;
        foreach(var group in set.fingerGroups) {
            if(group.noteOverrides == null) continue;
            foreach(var n in group.noteOverrides) {
                if(n.targetObj != null) {
                    Undo.RecordObject(n.targetObj, $"Toggle {n.noteName}");
                    n.targetObj.SetActive(show);
                    count++;
                }
            }
        }
        Debug.Log($"[{setLabel}] {(show ? "显示" : "隐藏")}了 {count} 个指法物体。");
    }
#endif
}
