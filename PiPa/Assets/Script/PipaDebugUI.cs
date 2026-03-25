using UnityEngine;
using System.Collections.Generic;

// 仅在编辑器模式下运行，打包时不包含
#if UNITY_EDITOR
public class PipaDebugUI : MonoBehaviour
{
    [Header("绑定控制器")]
    public PipaController pipaController;

    [Header("调试状态")]
    [Tooltip("当前选中的音名 (模拟前端发来的处理后字符)")]
    public string debugNote = "工";
    
    [Tooltip("当前选中的技法")]
    public string debugType = "";

    // 常用音名 (已包含前端转换后的特殊字符，并根据 soundMap.js 修正)
    // 𠆾 -> 𠆾 (注意：前端发送时会转为 亻六，但 soundMap 里依然用了 𠆾 的键，这里我们保留映射中存在的单音键前缀)
    private readonly string[] commonNotes = { 
        "一", "下", "㐅", "𫢅", "亻乂", "𠆾", "仜", "偲", "六", "士", "工", "思", "艹六", "艹工",
        "貝㐅", "彳㐅", // 五相特有
        "𢩩", "𫼚", "揌", // 指法特殊映射
        "甲线（一）", "甲线（下）", "甲线（亻乂）", "甲线（仜）", "甲线（六）", "甲线（工）", "甲线（思）"
    };

    // 常用技法 (对应 soundMap.js 中的 keys 或 fingerName)
    private readonly string[] commonTypes = { 
        "", // 默认(无类型)
        "点", 
        "挑", 
        "勾指√", 
        "落指）",
        "慢撚○", 
        "全撚○",
        "甲线十"
    };

    // 特殊时序效果 (TimedLightController)
    private readonly string[] timedEffects = {
        "top", "mid", "bottom"
    };

    private void Start()
    {
        if (pipaController == null)
            pipaController = FindObjectOfType<PipaController>();
    }

    private void OnGUI()
    {
        // 创建一个屏幕左侧的调试面板
        // 使用 ScrollView 防止按钮过多超出屏幕
        GUILayout.BeginArea(new Rect(10, 10, 300, Screen.height - 20));
        GUILayout.Box("琵琶仿真调试器 (Editor Only)", GUILayout.ExpandHeight(true));
        
        // --- 乐器切换 ---
        GUILayout.Space(10);
        GUILayout.Label("【1. 乐器切换】");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("四相 (FourAirPipes)")) 
        {
            if (pipaController) pipaController.SetInstrument("FourAirPipes");
            Debug.Log("[DebugUI] 切换为 四相琵琶");
        }
        if (GUILayout.Button("五相 (FiveAirPipes)")) 
        {
            if (pipaController) pipaController.SetInstrument("FiveAirPipes");
            Debug.Log("[DebugUI] 切换为 五相琵琶");
        }
        GUILayout.EndHorizontal();

        // --- 音名选择 ---
        GUILayout.Space(10);
        GUILayout.Label($"【2. 音名选择】当前: {debugNote}");
        int columns = 4;
        for (int i = 0; i < commonNotes.Length; i += columns)
        {
            GUILayout.BeginHorizontal();
            for (int j = 0; j < columns; j++)
            {
                if (i + j < commonNotes.Length)
                {
                    string n = commonNotes[i + j];
                    // 使用明确的 Style 对象
                    GUIStyle style = (n == debugNote) ? GUI.skin.box : GUI.skin.button;
                    if (GUILayout.Button(n, style))
                        debugNote = n;
                }
            }
            GUILayout.EndHorizontal();
        }

        // --- 技法选择 ---
        GUILayout.Space(10);
        GUILayout.Label($"【3. 技法/类型 (Type)】当前: {debugType}");
        
        // 基础技法
        for (int i = 0; i < commonTypes.Length; i += 3)
        {
            GUILayout.BeginHorizontal();
            for (int j = 0; j < 3; j++)
            {
                if (i + j < commonTypes.Length)
                {
                    string t = commonTypes[i + j];
                    string label = string.IsNullOrEmpty(t) ? "Empty(Default)" : t;
                    // 使用明确的 Style 对象
                    GUIStyle style = (t == debugType) ? GUI.skin.box : GUI.skin.button;
                    if (GUILayout.Button(label, style))
                        debugType = t;
                }
            }
            GUILayout.EndHorizontal();
        }

        // 时序特效 (针对 扌乂 等)
        GUILayout.Label("特殊时序后缀 (Timed):");
        GUILayout.BeginHorizontal();
        foreach (var eff in timedEffects)
        {
            // 使用明确的 Style 对象
            GUIStyle style = (eff == debugType) ? GUI.skin.box : GUI.skin.button;
            if (GUILayout.Button(eff, style))
                debugType = eff;
        }
        GUILayout.EndHorizontal();

        // --- 发送控制 ---
        GUILayout.Space(20);
        GUILayout.Label("【4. 发送指令】");
        
        // 构造消息预览
        string message = string.IsNullOrEmpty(debugType) ? debugNote : $"{debugNote}|{debugType}";
        GUILayout.TextField(message); // 只读显示

        if (GUILayout.Button("发送 HighlightString (按下)", GUILayout.Height(30)))
        {
            Debug.Log($"[DebugUI] 模拟前端发送: HighlightString('{message}')");
            if (pipaController) pipaController.HighlightString(message);
        }

        if (GUILayout.Button("发送 DimString (松开)"))
        {
            Debug.Log($"[DebugUI] 模拟前端发送: DimString('{message}')");
            if (pipaController) pipaController.DimString(message);
        }

        // --- 手动输入测试 ---
        GUILayout.Space(10);
        GUILayout.Label("手动输入测试:");
        GUILayout.BeginHorizontal();
        debugNote = GUILayout.TextField(debugNote, GUILayout.Width(80));
        GUILayout.Label("|");
        debugType = GUILayout.TextField(debugType, GUILayout.Width(80));
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
#endif
