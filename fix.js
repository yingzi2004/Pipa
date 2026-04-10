const fs = require('fs');
let pc = fs.readFileSync('f:/unityGithub/github/pipa/PiPa/Assets/Script/PipaController.cs', 'utf-8');
pc = pc.replace('            Debug.Log($"[Highlight] REAL RAW DATA FROM UI',
                var _tlcs = FindObjectsOfType<TimedLightController>();
            foreach(var tlc in _tlcs) {
                if (tlc != null) tlc.ManualStop();
            }

            Debug.Log($"[Highlight] REAL RAW DATA FROM UI);
pc = pc.replace(            // [修改] 实现优雅退出：如果是点击（按住时间短），让它播完一次动画再隐藏；否则在当前循环结束后隐藏
            var pulse = lastActiveObj.GetComponent<GlowPulseEffect>();
            if (pulse != null) {
                pulse.RequestGracefulStop();
            } else {
                lastActiveObj.SetActive(false);
            },             lastActiveObj.SetActive(false););
fs.writeFileSync('f:/unityGithub/github/pipa/PiPa/Assets/Script/PipaController.cs', pc);

let gc = fs.readFileSync('f:/unityGithub/github/pipa/PiPa/Assets/Script/GlowPulseEffect.cs', 'utf-8');
gc = gc.replace(    public void RequestGracefulStop()
    {
        isStopping = true;
    },     public void RequestGracefulStop()
    {
        isStopping = true;
        gameObject.SetActive(false);
    });
fs.writeFileSync('f:/unityGithub/github/pipa/PiPa/Assets/Script/GlowPulseEffect.cs', gc);
console.log('done from ps script');