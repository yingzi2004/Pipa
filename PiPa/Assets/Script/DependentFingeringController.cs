using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 依赖单音指法效果控制器 (如: x|甲线十，x|轮指等)
/// 专门用于处理“必须配合单音才能发声”的指法操作所带来的特殊光影表现。
/// </summary>
public class DependentFingeringController : MonoBehaviour
{
    [System.Serializable]
    public class FingeringEffect
    {
        [Tooltip("触发的指法关键词，如：甲线、轮指、勾指等")]
        public string keyword;

        [Tooltip("该指法对应的专属激活颜色")]
        [ColorUsage(true, true)] public Color effectColor = new Color(0.8f, 0.2f, 1f, 1f);

        [Tooltip("是否附带额外的发光材质表现")]
        public bool applyEmissionColor = true;

        [Tooltip("发光强度乘数")]
        public float emissionMultiplier = 1.5f;
    }

    [Header("--- 依赖单音的指法视觉特效配置 ---")]
    [Tooltip("凡是操作后缀中包含以下关键词的，接管默认着色逻辑，使用下面配置的专属颜色")]
    public List<FingeringEffect> specialEffects = new List<FingeringEffect>();

    [Tooltip("全局控制：是否开启指法接管")]
    public bool enableHandling = true;

    /// <summary>
    /// 处理对于特定依赖型指法的激活与视觉表现
    /// </summary>
    /// <param name="targetObj">目标游戏物体（如“工”单音对应的模型）</param>
    /// <param name="typeStr">操作类型（如“甲线十”）</param>
    /// <param name="wasActive">物体在接管前是否已经是激活状态</param>
    /// <returns>返回 true 表示成功匹配并接管处理；false 表示交回主脚本默认走</returns>
    public bool HandleAction(GameObject targetObj, string typeStr, bool wasActive)
    {
        if (!enableHandling || string.IsNullOrEmpty(typeStr) || targetObj == null)
        {
            return false;
        }

        // 查找是否有匹配的指法特效配置
        FingeringEffect matchedEffect = null;
        foreach (var effect in specialEffects)
        {
            if (!string.IsNullOrEmpty(effect.keyword) && typeStr.Contains(effect.keyword))
            {
                matchedEffect = effect;
                break;
            }
        }

        if (matchedEffect == null) return false;

        Debug.Log($"[DependentFingeringController] ✨ 捕捉到关联指法: {typeStr}, 正应用专属视觉配置于 {targetObj.name}");
        
        // 设为激活
        targetObj.SetActive(true);

        // 设置材质专属颜色
        var renderers = targetObj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.material.color = matchedEffect.effectColor;
            if (matchedEffect.applyEmissionColor)
            {
                r.material.SetColor("_EmissionColor", matchedEffect.effectColor * matchedEffect.emissionMultiplier);
            }
            else
            {
                r.material.SetColor("_EmissionColor", Color.black);
            }
        }

        // 设置 UI 图片颜色
        var images = targetObj.GetComponentsInChildren<Image>(true);
        foreach (var i in images) i.color = matchedEffect.effectColor;

        // 获取并触发泛光波纹效果组件
        var pulse = targetObj.GetComponent<GlowPulseEffect>();
        if (pulse == null) pulse = targetObj.AddComponent<GlowPulseEffect>();

        // 仅当物体本身已经是激活状态时，才需要手动触发（防止和初始化OnEnable冲突）
        if (wasActive)
        {
            StartCoroutine(DelayedPulseCall(pulse));
        }

        return true; 
    }

    private System.Collections.IEnumerator DelayedPulseCall(GlowPulseEffect pulse)
    {
        yield return new WaitForEndOfFrame();
        if (pulse != null) pulse.Pulse();
    }
}
