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
        [Tooltip("键位名称 (如: 扌乂|top, 扌乂|mid, 摁)")]
        public string keyName;

        [Tooltip("是否为该按键开启专属发光颜色")]
        public bool useCustomColor = true;

        [Tooltip("专属发光颜色")]
        [ColorUsage(true, true)] public Color keyColor = new Color(0f, 0.5f, 1f, 3f);

        [Tooltip("勾选时，动画和发光效果会持续维持在最后，直到玩家松开该按键")]
        public bool holdUntilRelease = false;

        public List<LightSequence> lightSequences = new List<LightSequence>();
    }

[Header("独立发声指法配置")]
    [Tooltip("配置不需要配合单音就直接发声的独立指法序列")]
    public List<KeyLightConfig> independentKeys = new List<KeyLightConfig>();

    [Header("--- 材质高亮配置 ---")]
    [Tooltip("是否在激活动画时同时赋予材质发光效果（与 PipaController 保持一致）")]
    public bool applyEmissionColor = false;
    [ColorUsage(true, true)] public Color glowColor = new Color(0f, 0.5f, 1f, 3f);

    [Header("WebGL特效资源修正 (注入给动态生成的组件)")]
    public Shader rippleShader;
    public Texture2D rippleTexture;

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
        
        // 【新增】在初始化时，确保所有配置的光效对象设为隐藏（与单音保持一致）
        TurnOffAllLightsAtStart();
    }

    void TurnOffAllLightsAtStart()
    {
        if (independentKeys != null) {
            foreach (var config in independentKeys) {
                if (config != null && config.lightSequences != null) {
                    foreach (var seq in config.lightSequences) {
                        if (seq.lightObject) seq.lightObject.SetActive(false);
                    }
                }
            }
        }
    }

    void RebuildKeyConfigMap()
    {
        _keyConfigMap.Clear();

        if (independentKeys != null) {
            foreach (var config in independentKeys) {
                if (config != null && !string.IsNullOrEmpty(config.keyName)) {
                    _keyConfigMap[config.keyName.Replace(" ", "").Trim()] = config;
                }
            }
        }

        string loadedKeys = string.Join(", ", _keyConfigMap.Keys);
        Debug.Log($"[TimedLightController] 已加载 {_keyConfigMap.Count} 个独立指法按键配置: [{loadedKeys}]。警告：我正挂载在名为【{gameObject.name}】的物体上！");
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
            if (!_currentKeyConfig.holdUntilRelease) {
                StopSequence();
                return;
            }
            // 若需要持续到松开发声停止，就不再增加时间，直接保持最后的状态
            _sequenceTimer = maxEndTime; 
        }

        // 记录每个物理对象的最终状态，防止同一个对象被后续的 false 强行覆盖
        Dictionary<GameObject, bool> objectStates = new Dictionary<GameObject, bool>();
        List<GameObject> objectsToPulse = new List<GameObject>();

        for (int i = 0; i < _currentKeyConfig.lightSequences.Count; i++)
        {
            var seq = _currentKeyConfig.lightSequences[i];
            if (seq.lightObject == null) continue;

            bool shouldBeActive;
            if (_currentKeyConfig.holdUntilRelease) {
                // 如果开启长按保持，则只要过完 startTime 就一直保持激活（忽略endTime限制）
                shouldBeActive = _sequenceTimer >= seq.startTime;
            } else {
                shouldBeActive = _sequenceTimer >= seq.startTime && _sequenceTimer <= seq.endTime; // 保证最后一帧依然亮起
            }

            if (objectStates.ContainsKey(seq.lightObject)) {
                objectStates[seq.lightObject] = objectStates[seq.lightObject] || shouldBeActive;
            } else {
                objectStates[seq.lightObject] = shouldBeActive;
            }

            if (shouldBeActive && !_lightPrevState[i])
            {
                if (seq.lightObject.activeInHierarchy && !objectsToPulse.Contains(seq.lightObject)) {
                    objectsToPulse.Add(seq.lightObject);
                }
            }

            _lightPrevState[i] = shouldBeActive;
        }

                foreach (var kvp in objectStates)
        {
            GameObject obj = kvp.Key;
            bool targetState = kvp.Value;

            if (obj.activeSelf != targetState)
            {
                if (targetState && obj.transform.parent != null && !obj.transform.parent.gameObject.activeInHierarchy) { Debug.LogWarning($"[TimedLightController] 警告: 试图显示 {obj.name}，但其父物体 {obj.transform.parent.name} 已被隐藏！光效将不可见！"); } obj.SetActive(targetState);
            }
            
            // 无论刚才是否切换了状态，只要这帧需要亮，就确保加上发光效果
            if (targetState)
            {
                if (applyEmissionColor || (_currentKeyConfig != null && _currentKeyConfig.useCustomColor)) {
                    Color c = (_currentKeyConfig != null && _currentKeyConfig.useCustomColor) ? _currentKeyConfig.keyColor : glowColor;
                    ApplyGlow(obj, c);
                }
                var pulse = obj.GetComponent<GlowPulseEffect>();
                if (pulse == null) pulse = obj.AddComponent<GlowPulseEffect>();
                
                // WebGL修复: 使用 SetResources 方法安全注入资源并触发材质刷新
                if (pulse != null) {
                   pulse.SetResources(rippleShader, rippleTexture);
                }
            }
        }

        // 统一触发我们需要激发的对象，不论它是否是刚刚被 SetActive(true)
                foreach (var obj in objectsToPulse)
        {
            if (obj != null)
            {
                Debug.Log($"[TimedLightController] attempting to pulse {obj.name}, activeInHierarchy={obj.activeInHierarchy}, time={_sequenceTimer}");
                if (obj.activeInHierarchy)
                {
                    if (applyEmissionColor || (_currentKeyConfig != null && _currentKeyConfig.useCustomColor)) {
                        Color c = (_currentKeyConfig != null && _currentKeyConfig.useCustomColor) ? _currentKeyConfig.keyColor : glowColor;
                        ApplyGlow(obj, c);
                    }
                    var pulse = obj.GetComponent<GlowPulseEffect>();
                    if (pulse != null) 
                    {
                        Debug.Log($"[TimedLightController] Calling pulse.Pulse() on {obj.name}");
                        StartCoroutine(DelayedPulseCall(pulse));
                    }
                }
            }
        }
    }


    /// <summary>
    /// 对物体应用高亮发光材质（同步 PipaController 的视觉效果）
    /// </summary>
    private System.Collections.IEnumerator DelayedPulseCall(GlowPulseEffect pulse) {
        yield return new WaitForEndOfFrame();
        if (pulse != null) pulse.Pulse();
    }

private void ApplyGlow(GameObject obj, Color colorToApply)
    {
        if (obj == null) return;

        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.material.color = colorToApply;
            r.material.SetColor("_EmissionColor", colorToApply * 1.5f);
        }
        var images = obj.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        foreach (var img in images)
        {
            img.color = colorToApply;
        }
    }

    // ===== 公开接口 (模仿 PipaController.HighlightString) =====

    /// <summary>
    /// 根据键位名称来启动对应的亮光序列
    /// （和 PipaController.HighlightString 接口保持一致）
    /// </summary>
    /// <param name="keyName">键位名称 (如: 扌乂|top, 扌乂|mid, 扌乂|bottom)</param>
    /// <returns>返回是否成功匹配接管了该独立指法</returns>
    public bool OnKeyPressed(string keyName)
    {
        if (string.IsNullOrEmpty(keyName)) return false;

        Debug.Log($"[TimedLightController] 收到按键指令: '{keyName}'");

        // 去除前端可能带进来的多余空格或不可见字符
        string cleanKey = keyName.Replace(" ", "").Trim();

        // 提取基础按键（去掉可能携带的 '|点' 等后缀）例如 "摁|点" -> "摁"
        string baseKey = cleanKey;
        if (cleanKey.Contains("|")) {
            baseKey = cleanKey.Split('|')[0].Trim();
        }

        // 强容错匹配：专门用来对付前端特殊指法字符串可能带来的编码乱码
        KeyLightConfig config = null;
        if (_keyConfigMap.TryGetValue(cleanKey, out var exactConfig)) {
            config = exactConfig;
        } else if (_keyConfigMap.TryGetValue(baseKey, out var baseConfig)) {
            config = baseConfig;
        } // 若都没匹配上，再尝试原始名字
        else if (_keyConfigMap.TryGetValue(keyName, out var rawConfig)) {
            config = rawConfig;
        }

        if (config == null || config.lightSequences.Count == 0 || string.IsNullOrEmpty(config.keyName))
        {
            // 如果是在独立按键列表里找不到该指令是正常的，因为有可能是单音或者依赖类指法（由 PipaController/DependentFingeringController 接管）
            Debug.LogWarning($"[TimedLightController] 按键没匹配到独立配置! received={keyName}, cleanKey={cleanKey}, baseKey={baseKey}");
            return false;
        }

        // 停止当前序列（如果有的话）
        if (_isSequenceActive)
        {
            StopSequence();
        }

        Debug.Log($"[TimedLightController] 独立发声被成功捕获！即将播放: {config.keyName} 的 {config.lightSequences.Count} 段序列");

        // 设置新的配置并启动
        Debug.Log("[TimedLightController] Match Found! Starting sequence for: " + config.keyName); 
        _currentKeyConfig = config;
        StartSequence();

        // 可选：发送给后端
        if (sendToBackend)
        {
            SendToBackend(keyName);
        }

        return true;
    }

    /// <summary>
    /// 前端松开按键时调用（用于打断需要 holdUntilRelease 的亮光序列）
    /// </summary>
    public void OnKeyReleased(string keyName)
    {
        if (string.IsNullOrEmpty(keyName) || !_isSequenceActive || _currentKeyConfig == null) return;

        string cleanKey = keyName.Replace(" ", "").Trim();
        string baseKey = cleanKey;
        if (cleanKey.Contains("|")) {
            baseKey = cleanKey.Split('|')[0].Trim();
        }

        KeyLightConfig config = null;
        if (_keyConfigMap.TryGetValue(cleanKey, out var exactConfig)) {
            config = exactConfig;
        } else if (_keyConfigMap.TryGetValue(baseKey, out var baseConfig)) {
            config = baseConfig;
        } else if (_keyConfigMap.TryGetValue(keyName, out var rawConfig)) {
            config = rawConfig;
        }

        // 如果前端正在熄灭的值 就是我们正在播放的配置，则立刻停止亮光！
        if (config != null && config == _currentKeyConfig)
        {
            Debug.Log($"[TimedLightController] 前端松开按键: {keyName}，长按保持结束，立刻中止并熄灭该序列！");
            StopSequence();
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















