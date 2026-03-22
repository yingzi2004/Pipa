using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 时间序列亮光控制器（特殊键位版）
/// - 接收来自前端的特殊键位名称（如: 扌乂|top, 扌乂|mid, 扌乂|bottom）
/// - 根据键位名称激活对应的亮光时间序列
/// - 每个键位可配置不同的亮光出现/消失时间
/// </summary>
public class TimedLightController : MonoBehaviour
{
    [System.Serializable]
    public class LightSequence
    {
        [Tooltip("亮光物体")]
        public GameObject lightObject;
        
        [Tooltip("该亮光何时亮起 (秒)")]
        public float startTime = 0f;
        
        [Tooltip("该亮光何时熄灭 (秒)")]
        public float endTime = 0.5f;
    }

    [System.Serializable]
    public class KeyLightConfig
    {
        [Tooltip("键位名称 (如: 扌乂|top, 扌乂|mid, 扌乂|bottom)")]
        public string keyName;
        
        [Tooltip("该键位对应的亮光序列配置")]
        public List<LightSequence> lightSequences = new List<LightSequence>();
    }

    [Header("--- 键位配置（仅三个特殊键位）---")]
    [Tooltip("扌乂|top 的亮光配置")]
    public KeyLightConfig topKey;
    
    [Tooltip("扌乂|mid 的亮光配置")]
    public KeyLightConfig midKey;
    
    [Tooltip("扌乂|bottom 的亮光配置")]
    public KeyLightConfig bottomKey;

    [Header("--- 材质高亮配置 ---")]
    [Tooltip("是否在激活动画时同时赋予材质发光效果（与 PipaController 保持一致）")]
    public bool applyEmissionColor = true;
    [ColorUsage(true, true)] public Color glowColor = new Color(0f, 0.5f, 1f, 3f);

    [Header("--- 后端发送配置 (可选) ---")]
    [Tooltip("勾选后在触发时发送消息给后端")]
    public bool sendToBackend = false;
    [Tooltip("后端脚本名称 (如: PipaController)")]
    public string backendScriptName = "PipaController";
    [Tooltip("后端方法名称 (如: HighlightString)")]
    public string backendMethodName = "HighlightString";

    // 内部状态
    private float _sequenceTimer = 0f;
    private bool _isSequenceActive = false;
    private KeyLightConfig _currentKeyConfig = null;
    private List<bool> _lightPrevState = new List<bool>();
    private Dictionary<string, KeyLightConfig> _keyConfigMap = new Dictionary<string, KeyLightConfig>();

    void Start()
    {
        // 构建键位名称到配置的映射
        RebuildKeyConfigMap();
    }

    void RebuildKeyConfigMap()
    {
        _keyConfigMap.Clear();

        // 只加载三个特殊键位的配置
        if (topKey != null && !string.IsNullOrEmpty(topKey.keyName))
        {
            _keyConfigMap.Add(topKey.keyName, topKey);
        }
        if (midKey != null && !string.IsNullOrEmpty(midKey.keyName))
        {
            _keyConfigMap.Add(midKey.keyName, midKey);
        }
        if (bottomKey != null && !string.IsNullOrEmpty(bottomKey.keyName))
        {
            _keyConfigMap.Add(bottomKey.keyName, bottomKey);
        }

        Debug.Log($"[TimedLightController] 已加载 {_keyConfigMap.Count} 个特殊键位配置");
    }

    void Update()
    {
        if (!_isSequenceActive || _currentKeyConfig == null) return;

        // 更新计时器
        _sequenceTimer += Time.deltaTime;

        // 找到最大的结束时间
        float maxEndTime = GetMaxEndTime(_currentKeyConfig.lightSequences);

        // 检查是否序列已完成
        if (_sequenceTimer > maxEndTime)
        {
            StopSequence();
            return;
        }

        // 记录每个物理对象的最终状态，防止同一个对象被后续的 false 强行覆盖
        Dictionary<GameObject, bool> objectStates = new Dictionary<GameObject, bool>();

        for (int i = 0; i < _currentKeyConfig.lightSequences.Count; i++)
        {
            var seq = _currentKeyConfig.lightSequences[i];
            if (seq.lightObject == null) continue;

            bool shouldBeActive = _sequenceTimer >= seq.startTime && _sequenceTimer < seq.endTime;

            if (objectStates.ContainsKey(seq.lightObject)) {
                objectStates[seq.lightObject] = objectStates[seq.lightObject] || shouldBeActive;
            } else {
                objectStates[seq.lightObject] = shouldBeActive;
            }
            
            // 同步旧的数组状态（不再绝对依赖，保留以防万一）
            _lightPrevState[i] = shouldBeActive;
        }

        // 统一应用每个对象的最终状态
        foreach (var kvp in objectStates)
        {
            GameObject obj = kvp.Key;
            bool targetState = kvp.Value;

            if (obj.activeSelf != targetState)
            {
                if (targetState && obj.transform.parent != null && !obj.transform.parent.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning($"[TimedLightController] 警告: 您想显示的物体 {obj.name} 所在的父物体被隐藏了！必须先激活其父节点！");
                }

                obj.SetActive(targetState);

                if (targetState && applyEmissionColor)
                {
                    ApplyGlow(obj);
                }
            }
        }
    }

    /// <summary>
    /// 对物体应用高亮发光材质（同步 PipaController 的视觉效果）
    /// </summary>
    private void ApplyGlow(GameObject obj)
    {
        if (obj == null) return;
        
        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.material.color = glowColor;
            r.material.SetColor("_EmissionColor", glowColor * 1.5f);
        }
        var images = obj.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        foreach (var img in images)
        {
            img.color = glowColor;
        }
    }

    // ===== 公开接口 (模仿 PipaController.HighlightString) =====

    /// <summary>
    /// 根据键位名称来启动对应的亮光序列
    /// （和 PipaController.HighlightString 接口保持一致）
    /// </summary>
    /// <param name="keyName">键位名称 (如: 扌乂|top, 扌乂|mid, 扌乂|bottom)</param>
    public void OnKeyPressed(string keyName)
    {
        if (string.IsNullOrEmpty(keyName)) return;

        Debug.Log($"[TimedLightController] 收到按键指令: '{keyName}'");

        // 去除前端可能带进来的多余空格或不可见字符
        string cleanKey = keyName.Replace(" ", "").Trim();

        // 强容错匹配：专门用来对付前端特殊指法字符串可能带来的编码乱码
        KeyLightConfig config = null;
        if (cleanKey.EndsWith("|top")) {
            config = topKey;
        } else if (cleanKey.EndsWith("|mid")) {
            config = midKey;
        } else if (cleanKey.EndsWith("|bottom")) {
            config = bottomKey;
        } else if (_keyConfigMap.TryGetValue(cleanKey, out var exactConfig)) {
            config = exactConfig;
        } // 若都没匹配上，再尝试原始名字
        else if (_keyConfigMap.TryGetValue(keyName, out var rawConfig)) {
            config = rawConfig;
        }

        if (config == null || config.lightSequences.Count == 0 || string.IsNullOrEmpty(config.keyName))
        {
            Debug.LogWarning($"[TimedLightController] 无法执行序列，找不到配置或配置列表为空。当前匹配键: {cleanKey}");
            return;
        }

        // 停止当前序列（如果有的话）
        if (_isSequenceActive)
        {
            StopSequence();
        }

        Debug.Log($"[TimedLightController] 成功匹配并启动: {config.keyName} 的 {config.lightSequences.Count} 段光效序列");

        // 设置新的配置并启动
        _currentKeyConfig = config;
        StartSequence();

        // 可选：发送给后端
        if (sendToBackend)
        {
            SendToBackend(keyName);
        }
    }

    /// <summary>
    /// 启动亮光时间序列
    /// </summary>
    private void StartSequence()
    {
        if (_currentKeyConfig == null || _currentKeyConfig.lightSequences.Count == 0)
        {
            Debug.LogWarning("[TimedLightController] 无法启动序列: 配置为空");
            return;
        }

        // 重新初始化状态列表
        _lightPrevState.Clear();
        for (int i = 0; i < _currentKeyConfig.lightSequences.Count; i++)
        {
            _lightPrevState.Add(false);
        }

        _sequenceTimer = 0f;
        _isSequenceActive = true;
    }

    /// <summary>
    /// 停止亮光序列
    /// </summary>
    private void StopSequence()
    {
        _isSequenceActive = false;
        _sequenceTimer = 0f;

        // 关闭所有亮光
        if (_currentKeyConfig != null)
        {
            for (int i = 0; i < _currentKeyConfig.lightSequences.Count; i++)
            {
                if (_currentKeyConfig.lightSequences[i].lightObject != null)
                    _currentKeyConfig.lightSequences[i].lightObject.SetActive(false);
                if (i < _lightPrevState.Count)
                    _lightPrevState[i] = false;
            }
        }

        _currentKeyConfig = null;
        Debug.Log("[TimedLightController] Sequence ended.");
    }

    /// <summary>
    /// 手动停止序列
    /// </summary>
    public void ManualStop()
    {
        StopSequence();
    }

    /// <summary>
    /// 获取序列的最大结束时间
    /// </summary>
    private float GetMaxEndTime(List<LightSequence> sequences)
    {
        float max = 0f;
        foreach (var seq in sequences)
        {
            if (seq.endTime > max)
                max = seq.endTime;
        }
        return max;
    }

    /// <summary>
    /// 发送消息给后端脚本
    /// </summary>
    private void SendToBackend(string data)
    {
        GameObject targetObj = GameObject.Find(backendScriptName);
        if (targetObj == null)
        {
            Debug.LogWarning($"[TimedLightController] Backend object '{backendScriptName}' not found.");
            return;
        }

        var component = targetObj.GetComponent(backendScriptName);
        if (component == null)
        {
            Debug.LogWarning($"[TimedLightController] Component '{backendScriptName}' not found on target object.");
            return;
        }

        // 反射调用后端方法
        System.Reflection.MethodInfo method = component.GetType().GetMethod(backendMethodName);
        if (method != null)
        {
            method.Invoke(component, new object[] { data });
            Debug.Log($"[TimedLightController] Sent to backend: {data}");
        }
        else
        {
            Debug.LogWarning($"[TimedLightController] Method '{backendMethodName}' not found on component.");
        }
    }

    /// <summary>
    /// 获取当前是否在播放序列
    /// </summary>
    public bool IsSequenceActive() => _isSequenceActive;

    /// <summary>
    /// 获取当前序列进度 (0 ~ 1)
    /// </summary>
    public float GetSequenceProgress()
    {
        if (!_isSequenceActive || _currentKeyConfig == null) return 0f;
        float max = GetMaxEndTime(_currentKeyConfig.lightSequences);
        return max > 0 ? _sequenceTimer / max : 0f;
    }
}
