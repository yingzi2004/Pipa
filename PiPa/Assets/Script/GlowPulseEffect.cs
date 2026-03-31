using UnityEngine;
using System.Collections;

/// <summary>
/// 挂载在需要泛光的小球或其父物体上
/// 当物体被激活(OnEnable)或者被调用 Pulse() 时，进行一次缩放泛光动画
/// </summary>
public class GlowPulseEffect : MonoBehaviour
{
    [Header("动画配置")]
    [Tooltip("动画持续时间")]
    public float duration = 0.3f;
    
    [Tooltip("初始缩放倍数")]
    public float startScale = 0.8f;
    
    [Tooltip("最大缩放倍数")]
    public float maxScale = 1.08f;

    [Tooltip("泛光强度曲线")]
    public AnimationCurve intensityCurve = new AnimationCurve(
        new Keyframe(0f, 1f), 
        new Keyframe(0.5f, 3f), 
        new Keyframe(1f, 1f)
    );

    private Vector3 originalScale;
    private Renderer[] renderers;
    private MaterialPropertyBlock propBlock;
    private Coroutine pulseRoutine;

    // 状态控制
    private bool isStopping = false;

    [Header("WebGL兼容设置 (防丢失)")]
    [Tooltip("WebGL打包会剥离未引用的Shader。请在此手动拖入 'Particles/Standard Unlit' 或 'Mobile/Particles/Additive'")]
    public Shader rippleShader;

    [Tooltip("WebGL无法使用内置Editor资源。请在此手动拖入一个圆形光圈贴图(如 Default-Particle)，否则会显示为方块")]
    public Texture2D rippleTexture;

    // 缓存原始颜色，避免颜色被改写后丢失
    private Color baseColor = Color.white;
    private ParticleSystem rippleSystem;

    // 添加公共初始化方法，供外部脚本（如 TimedLightController）在运行时注入资源后调用
    public void SetResources(Shader shader, Texture2D texture)
    {
        bool needRefresh = false;
        if (shader != null && rippleShader != shader) {
            rippleShader = shader;
            needRefresh = true;
        }
        if (texture != null && rippleTexture != texture) {
            rippleTexture = texture;
            needRefresh = true;
        }

        if (needRefresh || rippleSystem == null) {
            RefreshRippleSystem();
        }
    }

    void RefreshRippleSystem()
    {
        if (rippleSystem == null) {
            // 尝试查找已存在的子物体，避免重复创建
            var existing = transform.Find("RippleVFX");
            if (existing != null) rippleSystem = existing.GetComponent<ParticleSystem>();
        }

        if (rippleSystem == null) {
            CreateRippleSystem();
        } else {
            // 如果粒子系统已存在，仅刷新材质
            var psr = rippleSystem.GetComponent<ParticleSystemRenderer>();
            if (psr != null) SetupRippleMaterial(psr);
        }
    }

    void Awake()
    {
        originalScale = transform.localScale;
        renderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();

        // 尝试初始化
        RefreshRippleSystem();
    }
    
    void CreateRippleSystem()
    {
        if (rippleSystem != null) return;

        // 创建一个简单的粒子系统来模拟波纹
        GameObject go = new GameObject("RippleVFX");
        go.transform.SetParent(this.transform, false);
        go.transform.localPosition = Vector3.zero;
        
        rippleSystem = go.AddComponent<ParticleSystem>();
        
        // --- 核心修复: 创建时立刻停止，防止 playOnAwake 导致无法设置 duration ---
        rippleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // --- 粒子系统核心配置 ---
        var main = rippleSystem.main;
        var emission = rippleSystem.emission;
        var shape = rippleSystem.shape;
        var col = rippleSystem.colorOverLifetime;
        var sz = rippleSystem.sizeOverLifetime;
        var psr = go.GetComponent<ParticleSystemRenderer>();

        main.playOnAwake = false; 
        
        main.duration = 1f;
        main.startLifetime = 0.5f;   // [修改] 缩短生命周期 (原 0.8f)
        main.startSpeed = 0f;
        main.startSize = 7.0f;       // [修改] 增大基础尺寸 (原 5.0f)
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = new Color(1f, 1f, 1f, 0.8f); // [修改] 增加不透明度 (原 0.7f)
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        
        // 确保它不会被裁剪
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        
        // 甚至可以强制设置 Layer = Default
        go.layer = 0; 

        emission.enabled = false;

        shape.enabled = false;

        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.8f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        col.color = grad;

        sz.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0.0f, 1.0f);
        curve.AddKey(1.0f, 3.0f); // [修改] 增大扩散倍数 (原 2.5f)
        sz.size = new ParticleSystem.MinMaxCurve(1.0f, curve);

        // Renderer Configuration (Merged)
        psr.sortMode = ParticleSystemSortMode.YoungestInFront; // 确保新生成的粒子在前
        SetupRippleMaterial(psr);
    }

    void SetupRippleMaterial(ParticleSystemRenderer psr)
    {
        // WebGL 修复: 
        // 1. 优先使用 Inspector 中引用的 Shader
        // 2. 如果没有，则直接创建一个基于 "Custom/CircleRipple" 或者简单 Unlit 的材质
        // 3. 为了避开 Shader.Find() 在构建后可能失效的问题，这里我们尽量使用最通用的 Legacy Shader
        
        Shader shader = rippleShader;
        
        // 兼容性查找序列 (注意: WebGL中 Shader.Find 仅对 PlayerSettings -> Always Included Shaders 中的Shader有效)
        // 建议使用 "Mobile/Particles/Additive" 或 "Legacy Shaders/Particles/Additive"
        if (shader == null) shader = Shader.Find("Mobile/Particles/Additive");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent"); // 最后的保底
        
        if (shader != null) 
        {
            // 确保使用新材质实例，避免污染共享材质
            if (psr.material == null || psr.material.shader != shader) {
                psr.material = new Material(shader);
            }
            
            // 重要: Mobile/Particles/Additive 通常只需要 _MainTex，不需要复杂的 Blend 设置
            // 如果是 Standard Unlit，则可能需要设置 BlendMode
            
            if (shader.name.Contains("Standard")) {
                 if (psr.material.HasProperty("_Mode")) psr.material.SetFloat("_Mode", 2); 
                 if (psr.material.HasProperty("_SrcBlend")) psr.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                 if (psr.material.HasProperty("_DstBlend")) psr.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                 if (psr.material.HasProperty("_ZWrite")) psr.material.SetInt("_ZWrite", 0);
                 
                 psr.material.DisableKeyword("_ALPHATEST_ON");
                 psr.material.EnableKeyword("_ALPHABLEND_ON");
                 psr.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            // 设置贴图 - 如果没有提供贴图，则动态生成一个圆形渐变贴图
            if (rippleTexture != null) {
                if (psr.material.HasProperty("_MainTex")) psr.material.mainTexture = rippleTexture;
            } else {
                // 如果没有外部贴图，动态生成一个
                if (psr.material.HasProperty("_MainTex")) psr.material.mainTexture = GenerateCircleTexture();
            }
        }
        else
        {
            // 极端情况: 所有的 Shader.Find 都失败了 (紫色本质原因)
            Debug.LogError("GlowPulseEffect: WebGL Shader 丢失! 请前往 Project Settings -> Graphics -> Always Included Shaders 添加 'Mobile/Particles/Additive'");
        }
    }

    // 动态生成圆形渐变贴图 (WebGL保底方案) - 缓存以避免重复创建
    private static Texture2D cachedCircleTexture;

    Texture2D GenerateCircleTexture()
    {
        if (cachedCircleTexture != null) return cachedCircleTexture;

        int size = 64;
        // WebGL 修复: 使用 RGBA32 替代 ARGB32 以兼容更多设备
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(1f - (dist / radius));
                // 使用平滑的径向衰减: alpha^2
                alpha = alpha * alpha; 
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(colors);
        texture.Apply();
        
        cachedCircleTexture = texture;
        return texture;
    }

    void OnEnable()
    {
        // 确保原始Scale已被记录 (防止 Awake 未调用的边缘情况)
        if (originalScale == Vector3.zero && transform.localScale != Vector3.zero) 
            originalScale = transform.localScale;

        // 延迟到当前帧末尾再执行，等待 PipaController 把颜色设置好
        StartCoroutine(DelayedPulse());
    }

    IEnumerator DelayedPulse()
    {
        yield return new WaitForEndOfFrame();
        // 再次检查此时是否依然激活，防止一帧内开启又关闭导致的逻辑错位
        if (this.enabled && gameObject.activeInHierarchy) {
            Pulse();
        }
    }

    void OnDisable()
    {
        // 停止动画时复位
        transform.localScale = originalScale;
        if (rippleSystem != null) rippleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 重置 Emission，防止下次激活时在第一帧显示上一次的高亮残留
        if (renderers != null && propBlock != null)
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(propBlock);
                propBlock.SetColor("_EmissionColor", Color.black);
                r.SetPropertyBlock(propBlock);
            }
        }
    }

    public void Pulse()
    {
        // 每次开始播放时都重置停止标志，确保动画能正常启动
        isStopping = false;

        // 重新获取材质和颜色，因为 PipaController 可能会在 Activate 时修改材质颜色
        renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            if (renderers[0].material.HasProperty("_Color")) {
                baseColor = renderers[0].material.color;
            } else if (renderers[0].material.HasProperty("_BaseColor")) {
                baseColor = renderers[0].material.GetColor("_BaseColor");
            }
        }

        if (rippleSystem != null) { 
            // 每次Pulse都强制重置粒子状态，防止 WebGL 下只播放一次
            rippleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rippleSystem.Play();
        }
        if (pulseRoutine != null) StopCoroutine(pulseRoutine);
        pulseRoutine = StartCoroutine(PulseRoutine());
    }

    /// <summary>
    ///  请求优雅停止：如果还在播放第一圈，会坚持播完；如果已经播完一圈，则在当前圈结束后停止并隐藏物体
    /// </summary>
    public void RequestGracefulStop()
    {
        isStopping = true;
    }

    IEnumerator PulseRoutine()
    {
        int loopCount = 0;

        // 循环播放直到被Disable停止
        while (true)
        {
            // 如果收到了停止请求，并且已经至少完成了一次完整的激活动画，则退出循环并隐藏物体
            if (isStopping && loopCount > 0)
            {
                gameObject.SetActive(false);
                yield break;
            }

// 1. 发射波纹粒子 - 仅第一圈发射
            if (rippleSystem != null && loopCount == 0) {
                var rMain = rippleSystem.main; 
                // 确保保留波纹的半透明属性，只借用琴弦对应的 RGB。比如只给 0.6f 的透明度
                rMain.startColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.6f); 
                rippleSystem.Emit(1); 
            }

            // 2. 执行一轮缩放动画
            float timer = 0f;
            while (timer < duration)
            {
                // [优化] 如果是立即切换（如切换到另一音），这里不管；但如果是松手后的优雅退出，我们必须保证动画完整性
                float progress = timer / duration;
                
                // 缩放效果 (类似心脏跳动) - 已禁用
                // 0 -> 0.5 (放大), 0.5 -> 1 (回弹)
                /* 
                float scaleMulti;
                if (progress < 0.5f)
                    scaleMulti = Mathf.Lerp(startScale, maxScale, progress * 2f);
                else
                    scaleMulti = Mathf.Lerp(maxScale, 1f, (progress - 0.5f) * 2f);
                
                // 如果对象已被外部禁用，则立即退出
                if (!gameObject.activeInHierarchy) yield break;

                transform.localScale = originalScale * scaleMulti; 
                */

                // 如果对象已被外部禁用，则立即退出
                if (!gameObject.activeInHierarchy) yield break;
                // transform.localScale = originalScale; // 保持原大小

                // 泛光强度效果 - 已禁用闪烁 (固定亮度)
                // float intensity = intensityCurve.Evaluate(progress);
                Color finalEmission = baseColor; // 保持原色亮度，不再波动

                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(propBlock);
                    propBlock.SetColor("_EmissionColor", finalEmission);
                    // 确保开启了 Emission 关键字（针对标准材质）
                    r.material.EnableKeyword("_EMISSION"); 
                    r.SetPropertyBlock(propBlock);
                }

                timer += Time.deltaTime;
                yield return null;
            }
            
            loopCount++; // 标记完成一圈
            yield return null;
        }
    }
}
