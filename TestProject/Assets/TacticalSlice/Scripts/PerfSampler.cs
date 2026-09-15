using UnityEngine;

// Dogfood-only diagnostic -- not part of the shipped package. Task 4.5: standalone performance capture. Logs
// one line per real second: instantaneous fps (from smoothDeltaTime), GC.CollectionCount(0) (a real Gen0
// collection happening between samples is a directly observable "GC ran" signal, not just an allocation
// guess), and GC.GetTotalMemory (managed heap size -- a steady/flat trend during steady-state combat is the
// "no steady-state per-frame GC alloc" signal the agreed budget asks for; a sawtooth would show a real leak/
// churn instead).
public sealed class PerfSampler : MonoBehaviour
{
    private float timer;
    private int lastGen0Count;

    private void Start()
    {
        lastGen0Count = System.GC.CollectionCount(0);
        Debug.Log($"[Perf] START fps_target={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} resolution={Screen.currentResolution} fullscreen={Screen.fullScreen}");
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < 1f) return;
        timer = 0f;

        var gen0 = System.GC.CollectionCount(0);
        var gen0Delta = gen0 - lastGen0Count;
        lastGen0Count = gen0;

        Debug.Log($"[Perf] fps={1f / Time.smoothDeltaTime:F1} frame={Time.frameCount} totalManagedMemory={System.GC.GetTotalMemory(false)} gen0Collections={gen0} gen0Delta={gen0Delta}");
    }
}
