using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace LastWar
{
    /// <summary>
    /// Mobile benchmark (Settings > Benchmark, or ?bench=1 on WebGL): a match on the ground with 30 bots, measured with
    /// 10, then 20, then 30 of them active. Per phase: frames per second, frame time (mean and 95th percentile), the
    /// device's own CPU / GPU frame times when it reports them (FrameTimingManager - shown as "not reported" otherwise,
    /// never guessed), memory, and the game's own costs timed in code: the hidden skeletons' animation (the playable
    /// graphs evaluated by hand in this mode), the pose transfer onto the outfits, and the weapon IK.
    /// </summary>
    public class Bench : MonoBehaviour
    {
        public static bool Active;
        public const float Warmup = 6f, PhaseSeconds = 20f;
        static readonly int[] Phases = { 10, 20, 30 };

        // timings collected by SoldierBody while the bench runs (ticks of System.Diagnostics.Stopwatch)
        public static long AnimTicks, TransferTicks, IKTicks, BodyTicks;
        public static readonly List<SoldierBody> Bodies = new List<SoldierBody>();
        static readonly Stopwatch sw = new Stopwatch();

        Text label; readonly List<string> results = new List<string>();

        void OnDestroy() { Active = false; Bodies.Clear(); } // (leaving the match: the lobby's soldier animates normally again)

        public static void Begin(List<Fighter> bots)
        {
            Active = true;
            var go = new GameObject("Bench"); var b = go.AddComponent<Bench>();
            b.StartCoroutine(b.Run(bots));
        }

        /// <summary>evaluates every soldier's animation graph by hand (timed), instead of the Animator doing it unseen</summary>
        void Update()
        {
            float dt = Time.deltaTime;
            sw.Restart();
            for (int i = Bodies.Count - 1; i >= 0; i--)
            {
                SoldierBody b = Bodies[i];
                if (b == null) { Bodies.RemoveAt(i); continue; }
                if (b.isActiveAndEnabled) b.EvaluateGraph(dt);
            }
            AnimTicks += sw.ElapsedTicks;
        }

        IEnumerator Run(List<Fighter> bots)
        {
            BuildUi();
            FrameTimingManager.CaptureFrameTimings();
            foreach (int count in Phases)
            {
                for (int i = 0; i < bots.Count; i++) if (bots[i] != null) bots[i].gameObject.SetActive(i < count);
                Show($"Benchmark: {count} bots - settling…\n" + string.Join("\n", results));
                yield return new WaitForSeconds(Warmup);
                AnimTicks = TransferTicks = IKTicks = BodyTicks = 0;
                var frames = new List<float>(); double cpu = 0, gpu = 0; int timed = 0, gpuFrames = 0;
                var timings = new FrameTiming[1];
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < PhaseSeconds)
                {
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        if (timings[0].cpuFrameTime > 0) { cpu += timings[0].cpuFrameTime; timed++; }
                        if (timings[0].gpuFrameTime > 0) { gpu += timings[0].gpuFrameTime; gpuFrames++; }
                    }
                    if (frames.Count % 30 == 0) Show($"Benchmark: {count} bots - measuring {Time.realtimeSinceStartup - t0:0}/{PhaseSeconds:0} s\n" + string.Join("\n", results));
                }
                int n = Mathf.Max(1, frames.Count);
                float sum = 0; foreach (float f in frames) sum += f;
                frames.Sort(); float p95 = frames[Mathf.Clamp((int)(frames.Count * 0.95f), 0, frames.Count - 1)];
                double ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency / n;
                long mem = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
                long mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024 * 1024);
                string line = $"{count} bots: {n / sum:0.0} fps | frame {sum / n * 1000f:0.0} ms (p95 {p95 * 1000f:0.0}) | " +
                    $"CPU {(timed > 0 ? (cpu / timed).ToString("0.0") + " ms" : "not reported")} GPU {(gpuFrames > 0 ? (gpu / gpuFrames).ToString("0.0") + " ms" : "not reported by this device")} | " +
                    $"anim {ms(AnimTicks):0.00} ms transfer {ms(TransferTicks):0.00} ms IK {ms(IKTicks):0.00} ms rest of body code {ms(BodyTicks - TransferTicks - IKTicks):0.00} ms | mem {mem} MB (scripts {mono} MB)";
                results.Add(line);
                Debug.Log("[LastWar] bench " + line);
            }
            string device = $"{SystemInfo.deviceModel} | {SystemInfo.processorType} x{SystemInfo.processorCount} | {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | RAM {SystemInfo.systemMemorySize} MB | {Screen.width}x{Screen.height} | quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}";
            Debug.Log("[LastWar] bench device " + device);
            Show("BENCHMARK DONE - take a screenshot\n" + device + "\n" + string.Join("\n", results));
            for (int i = 0; i < bots.Count; i++) if (bots[i] != null) bots[i].gameObject.SetActive(true);
        }

        void BuildUi()
        {
            var cv = new GameObject("BenchUI", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 999;
            var sc = cv.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1300, 600);
            var bg = UiKit.Img(cv.transform, "bg", null, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(1250, 230), new Color(0, 0, 0, 0.72f));
            label = UiKit.Txt(bg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1230, 220), 13, TextAnchor.UpperLeft, Color.white);
        }
        void Show(string s) { if (label != null) label.text = s; }

        // ---- timing helpers for SoldierBody (only while the bench runs)
        public static long Now() => Active ? Stopwatch.GetTimestamp() : 0;
        public static void Add(ref long acc, long start) { if (Active && start != 0) acc += Stopwatch.GetTimestamp() - start; }
    }
}
