using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SchoolShield
{
    /// <summary>
    /// Local NSFW image classifier: Yahoo open_nsfw (ResNet-50, BSD-2-Clause) via ONNX Runtime on the CPU.
    /// Nothing leaves the PC. Output: probability (0..1) that the picture is not safe for work.
    /// </summary>
    internal sealed class NsfwEngine
    {
        private static NsfwEngine instance;
        private static readonly object Gate = new object();
        private static bool triedAndFailed;

        public static string LastError = "";

        private readonly InferenceSession session;
        private readonly string inputName;

        private NsfwEngine(string modelPath)
        {
            SessionOptions so = new SessionOptions();
            so.IntraOpNumThreads = 1;      // school PCs are modest: stay light
            so.InterOpNumThreads = 1;
            so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            session = new InferenceSession(modelPath, so);
            inputName = session.InputMetadata.Keys.First();
        }

        public static bool ModelPresent { get { return File.Exists(Release.ModelPath); } }

        /// <summary>The shared engine, or null when the model/runtime is unavailable (the reason is in LastError).</summary>
        public static NsfwEngine TryGet()
        {
            lock (Gate)
            {
                if (instance != null) return instance;
                if (triedAndFailed) return null;
                try
                {
                    if (!ModelPresent) throw new FileNotFoundException("Модель не установлена: " + Release.ModelPath);
                    string actual = Downloader.Sha256Hex(Release.ModelPath);
                    if (!string.Equals(actual, Release.ModelSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Файл модели повреждён (контрольная сумма не совпала).");
                    instance = new NsfwEngine(Release.ModelPath);
                    LastError = "";
                    Log.Write("ai", "ИИ-фильтр загружен");
                    return instance;
                }
                catch (Exception ex)
                {
                    string hint = Prerequisites.Hint(ex);
                    LastError = Prerequisites.Describe(ex) + (hint == null ? "" : "\n" + hint);
                    triedAndFailed = true;
                    Log.Write("ai", "ИИ-фильтр недоступен: " + LastError);
                    return null;
                }
            }
        }

        /// <summary>Forget a previous failure (after the model has been installed).</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                triedAndFailed = false;
                instance = null;
            }
        }

        /// <summary>NSFW probability of an image (any size). Reproduces open_nsfw preprocessing: 256x256 -> centre 224, BGR, mean-subtracted.</summary>
        public double Score(Bitmap src)
        {
            float[] data = Preprocess(src);
            DenseTensor<float> tensor = new DenseTensor<float>(data, new int[] { 1, 224, 224, 3 });
            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>();
            inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, tensor));
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs))
            {
                Tensor<float> output = results.First().AsTensor<float>();
                return output[0, 1];   // [sfw, nsfw]
            }
        }

        public double ScoreFile(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs, false, false))
            using (Bitmap bmp = new Bitmap(img))
                return Score(bmp);
        }

        private static float[] Preprocess(Bitmap src)
        {
            const int Resize = 256, Crop = 224, Offset = 16;
            using (Bitmap resized = new Bitmap(Resize, Resize, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(resized))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, Resize, Resize), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }

                BitmapData d = resized.LockBits(new Rectangle(0, 0, Resize, Resize), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = d.Stride;
                    byte[] buf = new byte[Math.Abs(stride) * Resize];
                    Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                    float[] data = new float[Crop * Crop * 3];
                    // Format24bppRgb is stored B,G,R - exactly the channel order the model expects.
                    for (int y = 0; y < Crop; y++)
                    {
                        for (int x = 0; x < Crop; x++)
                        {
                            int s = (y + Offset) * stride + (x + Offset) * 3;
                            int o = (y * Crop + x) * 3;
                            data[o] = buf[s] - 104f;
                            data[o + 1] = buf[s + 1] - 117f;
                            data[o + 2] = buf[s + 2] - 123f;
                        }
                    }
                    return data;
                }
                finally { resized.UnlockBits(d); }
            }
        }
    }

    /// <summary>
    /// Watches the screen of the student session. When a picture/video frame is classified as NSFW several
    /// times in a row, the window in front is closed. Frames are analysed in memory and never saved.
    /// </summary>
    internal sealed class AiGuard : IDisposable
    {
        private readonly Config cfg;
        private readonly Thread thread;
        private volatile bool stop;
        private int hits;

        public event Action<string> Alert;
        public event Action OwnWindowHit;

        public AiGuard(Config cfg)
        {
            this.cfg = cfg;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Name = "SchoolShield.AiGuard";
        }

        public void Start()
        {
            if (!cfg.Ai.Enabled) { Log.Write("ai", "Выключен в настройках"); return; }
            if (!NsfwEngine.ModelPresent) Log.Write("ai", "Модель не установлена - ИИ-фильтр экрана не работает, пока админ не нажмёт «Исправить» в панели");
            thread.Start();   // keeps retrying every minute, so a repair takes effect without a reboot
        }

        public void Dispose() { stop = true; }

        private void Loop()
        {
            NsfwEngine engine = NsfwEngine.TryGet();
            int interval = Math.Max(1000, cfg.Ai.IntervalMs);
            while (!stop)
            {
                if (engine == null)
                {
                    // The administrator may repair the missing files from the Admin panel: pick that up without a reboot.
                    for (int i = 0; i < 60 && !stop; i++) Thread.Sleep(1000);
                    if (stop) return;
                    if (!NsfwEngine.ModelPresent) continue;
                    NsfwEngine.Reset();
                    engine = NsfwEngine.TryGet();
                    continue;
                }
                try
                {
                    if (!State.AdminMode) Scan(engine);
                    else hits = 0;
                }
                catch (Exception ex) { Log.Write("ai", "Ошибка проверки экрана: " + ex.Message); }
                Thread.Sleep(interval);
            }
        }

        private void Scan(NsfwEngine engine)
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            double best;
            using (Bitmap shot = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(shot))
                    g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size, CopyPixelOperation.SourceCopy);

                best = engine.Score(shot);
                if (cfg.Ai.Tiles && best < cfg.Ai.Threshold)
                {
                    int hw = b.Width / 2, hh = b.Height / 2;
                    for (int ty = 0; ty < 2 && best < cfg.Ai.Threshold; ty++)
                    {
                        for (int tx = 0; tx < 2 && best < cfg.Ai.Threshold; tx++)
                        {
                            using (Bitmap tile = shot.Clone(new Rectangle(tx * hw, ty * hh, hw, hh), PixelFormat.Format24bppRgb))
                                best = Math.Max(best, engine.Score(tile));
                        }
                    }
                }
            }

            if (best < cfg.Ai.Threshold) { hits = 0; return; }

            hits++;
            if (hits < Math.Max(1, cfg.Ai.Confirmations)) return;
            hits = 0;
            Act(best);
        }

        private void Act(double score)
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint pid = Native.GetWindowPid(fg);
            string title = Native.GetWindowTitle(fg);
            Log.Write("ai", "Неприемлемое изображение на экране (" + score.ToString("0.00") + "), окно: \"" + title + "\" pid " + pid);
            Action<string> a = Alert;
            if (a != null) a("Неприемлемое изображение. Окно закрыто.");

            if (State.Preview) return;
            if (pid == (uint)Process.GetCurrentProcess().Id)
            {
                Action o = OwnWindowHit;
                if (o != null) o();
                return;
            }
            Native.PostMessage(fg, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(1500);
            if (Native.IsWindow(fg) && Native.IsWindowVisible(fg))
            {
                try
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        if (Array.IndexOf(Guard.Untouchable, p.ProcessName.ToLowerInvariant()) < 0) p.Kill();
                    }
                }
                catch { }
            }
        }
    }
}
