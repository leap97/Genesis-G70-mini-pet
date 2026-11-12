using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Checks
{
    static Bitmap Snapshot(Renderer renderer, Motion m, double now, int size)
    {
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap)) renderer.Draw(g, m, now, size);
        return bitmap;
    }
    static string[] ReferenceRows()
    {
        using (StreamReader reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("reference.csv")))
            return reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    internal static void Gauge(string folder)
    {
        Directory.CreateDirectory(folder);
        int cases = 0;
        using (Renderer renderer = new Renderer())
        foreach (int size in new[] { 1200, 180, 240, 320, 400, 480, 600 })
        using (Bitmap idle = Snapshot(renderer, new Motion(), 0, size))
        {
            idle.Save(Path.Combine(folder, "gauge-idle-" + size + ".png"));
            if (size == 1200)
            {
                // The six-o'clock rim must join the face instead of ending at
                // a straight cut, with a loose crescent floating below it.
                for (int y = 618; y <= 630; y++)
                    Require(idle.GetPixel(915, y).A >= 16, "Gap in lower gauge rim at 915," + y);
                for (int y = 634; y < 674; y++) for (int x = 810; x < 1040; x++)
                    Require(idle.GetPixel(x, y).A == 0, "Detached gauge pixels below rim at " + x + "," + y);
            }
            Rectangle rim = Rectangle.FromLTRB((int)Math.Floor(810 * size / 1200.0),
                (int)Math.Floor(610 * size / 1200.0), (int)Math.Ceiling(1040 * size / 1200.0),
                (int)Math.Ceiling(674 * size / 1200.0));
            for (int state = 0; state < 6; state++)
            {
                Motion m = new Motion(new BoostCurve { MotionBlurPercent = 200 });
                if (state < 3) m.IdleLift = -16 * state;
                else
                {
                    m.Pressure = 1.7; m.Typing = true; m.Speed = 14; m.Shift = 14;
                    m.Angle = .75; m.Distance = 211;
                    if (state == 4) { m.EngineWarning = true; m.ReleaseAt = 0; }
                    if (state == 5) m.FlutterUntil = 1000;
                }
                using (Bitmap frame = Snapshot(renderer, m, 375, size))
                    SameRegion(idle, frame, rim, "Gauge rim moved with vehicle at size/state " + size + "/" + state);
                cases++;
            }
        }
        File.WriteAllText(Path.Combine(folder, "gauge-check.txt"), "PASS: continuous six-o'clock rim; no detached lower pixels; unchanged gauge footer in " + cases + " idle/drive/notification cases across seven sizes.\r\n");
    }
    internal static void Bounds(string folder)
    {
        Directory.CreateDirectory(folder);
        int cases = 0;
        using (Renderer renderer = new Renderer())
        foreach (int size in new[] { 180, 181, 240, 319, 320, 400, 480, 599, 600 })
        {
            Rectangle crop = Layer.Viewport(size);
            int firstX = size, firstY = size, lastX = 0, lastY = 0;
            for (int frame = 0; frame < 160; frame++)
            {
                Motion m = new Motion(new BoostCurve { MotionBlurPercent = 200 });
                double now = frame * 125;
                if (frame < 128)
                {
                    m.Typing = true; m.Speed = 14; m.Pressure = 2;
                    m.Angle = frame * Motion.Tau / 128; m.Distance = frame * 73.27;
                    m.Shift = frame % 2 == 0 ? 14 : 0;
                }
                else if (frame < 144)
                { m.EngineWarning = true; m.ReleaseAt = now - (frame - 128) * 125; m.IdleLift = -32; }
                else if (frame < 152) m.FlutterUntil = now + 500;
                using (Bitmap full = Snapshot(renderer, m, now, size))
                {
                    BitmapData pixels = full.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    byte[] bytes = new byte[pixels.Stride * size];
                    Marshal.Copy(pixels.Scan0, bytes, 0, bytes.Length); full.UnlockBits(pixels);
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    {
                        if (bytes[y * pixels.Stride + x * 4 + 3] == 0) continue;
                        if (!crop.Contains(x, y)) Require(false, "Viewport cut visible pixels at size/state " + size + "/" + frame + ": " + x + "," + y);
                        firstX = Math.Min(firstX, x); firstY = Math.Min(firstY, y); lastX = Math.Max(lastX, x + 1); lastY = Math.Max(lastY, y + 1);
                    }
                    if (size == 320 && (frame == 0 || frame == 136 || frame == 144 || frame == 159))
                    using (Bitmap visible = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics g = Graphics.FromImage(visible))
                        { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(full, -crop.Left, -crop.Top); }
                        for (int y = 0; y < crop.Height; y++) for (int x = 0; x < crop.Width; x++)
                            Require(visible.GetPixel(x, y) == full.GetPixel(x + crop.Left, y + crop.Top), "Crop rescaled or altered artwork");
                        visible.Save(Path.Combine(folder, "trimmed-" + frame + ".png"));
                    }
                }
                cases++;
            }
            Require(firstY - crop.Top >= 4 && firstY - crop.Top <= 9, "Top padding not small and visible");
            Require(crop.Right - lastX >= 4 && crop.Right - lastX <= 9, "Right padding not small and visible");
            // Rear silhouette edits shift the faint trail edge inside the fixed
            // viewport. Larger render sizes retain at most six pixels of padding;
            // the per-pixel check above still rejects any clipped visible pixel.
            Require(firstX - crop.Left >= 1 && firstX - crop.Left <= 6, "Left movement padding incorrect at size " + size + ": " + (firstX - crop.Left));
            Require(crop.Bottom - lastY >= 2 && crop.Bottom - lastY <= 7, "Bottom movement padding incorrect");
            foreach (Rectangle area in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -200, 1920, 1080) })
            {
                Point topRight = new Point(area.Right - crop.Width, area.Top);
                Point bottomLeft = new Point(area.Left, area.Bottom - crop.Height);
                Require(PetWindow.RestorePosition(new Point(area.Right - size, area.Top), size, area, 1) == topRight, "Legacy top-right docking lost");
                Require(PetWindow.RestorePosition(new Point(area.Left, area.Bottom - size), size, area, 1) == bottomLeft, "Legacy bottom-left docking lost");
                Require(PetWindow.RestorePosition(topRight, size, area, 3) == topRight, "Restart moved cropped position");
                Require(PetWindow.RestorePosition(new Point(area.Right - crop.Right, area.Top), size, area, 2) == topRight, "Previous crop lost top-right docking");
                Require(PetWindow.RestorePosition(new Point(area.Left, area.Bottom - crop.Height - 3), size, area, 2) == bottomLeft, "Previous crop lost bottom-left docking");
                Point floating = new Point(area.Left + 100, area.Top + 100);
                Point restored = PetWindow.RestorePosition(floating, size, area, 1);
                Require(restored.X - crop.Left == floating.X && restored.Y - crop.Top == floating.Y, "Migration moved floating artwork");
                Require(PetWindow.RestorePosition(floating, size, area, 2) == new Point(floating.X + crop.Left, floating.Y), "Previous crop migration moved floating artwork");
                Require(PetWindow.FitOnScreen(new Point(area.Right + 100, area.Top - 100), crop.Size, area) == topRight, "Right/top edge clamp incorrect");
            }
        }
        File.WriteAllText(Path.Combine(folder, "bounds-check.txt"), "PASS: " + cases + " render cases at nine sizes; no artwork/effect/notification clipped; exact pixel crop with no scaling; small padding; legacy edge/floating positions and negative-coordinate monitors preserved.\r\n");
    }
    internal static void Smoke(string folder)
    {
        IntPtr foreground = GetForegroundWindow();
        Exception failure = null;
        FieldInfo hook = typeof(PetWindow).GetField("hook", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo liveMotion = typeof(PetWindow).GetField("motion", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo dirty = typeof(PetWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo resize = typeof(PetWindow).GetMethod("ResizePet", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo tick = typeof(PetWindow).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
        using (PetWindow window = new PetWindow())
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 800 })
        {
            window.Shown += delegate { timer.Start(); };
            timer.Tick += delegate
            {
                timer.Stop();
                try
                {
                    int style = GetWindowLong(window.Handle, -20);
                    Require((style & 0x8080088) == 0x8080088 && (IntPtr)hook.GetValue(window) != IntPtr.Zero && IsWindowVisible(window.Handle), "Window/hook smoke check failed");
                    Require(GetForegroundWindow() == foreground, "Pet stole the typing focus");
                    Rectangle area = Screen.FromRectangle(window.Bounds).WorkingArea;
                    window.Location = new Point(area.Right - window.Width, area.Top);
                    Motion current = (Motion)liveMotion.GetValue(window);
                    current.Curve.OpacityPercent = 37;
                    foreach (int size in new[] { 180, 240, 320, 400, 480, 600, 320 })
                    {
                        resize.Invoke(window, new object[] { size });
                        tick.Invoke(window, new object[] { null, EventArgs.Empty });
                        Require(window.ClientSize == Layer.Viewport(size).Size, "Actual cropped window size mismatch");
                        Require(window.Right == area.Right && window.Top == area.Top, "Resize lost top-right edge placement");
                        Require(current.Curve.OpacityPercent == 37, "Resizing reset opacity");
                    }
                    for (int percent = 1; percent <= 100; percent++)
                    {
                        current.Curve.OpacityPercent = percent; dirty.SetValue(window, true);
                        tick.Invoke(window, new object[] { null, EventArgs.Empty });
                        Require(IsWindowVisible(window.Handle) && (IntPtr)hook.GetValue(window) != IntPtr.Zero, "Opacity hid the window or lost typing input");
                    }
                    Require(window.Opacity == 1, "Opacity was incorrectly applied through WinForms instead of the layered bitmap");
                }
                catch (Exception error) { failure = error; }
                finally { window.Close(); }
            };
            Application.Run(window);
            if (failure != null) throw failure;
            Require((IntPtr)hook.GetValue(window) == IntPtr.Zero, "Keyboard hook was not released on close");
        }
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "window-check.txt"), "PASS: cropped transparent/topmost window at all six scales; opacity retained on resize and all 100 levels presented; focus preserved; keyboard hook installed and released on close.\r\n");
    }
    static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    static void SameRegion(Bitmap first, Bitmap second, Rectangle region, string reason)
    {
        for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
            Require(first.GetPixel(x, y) == second.GetPixel(x, y), reason + " at " + x + "," + y + ": " + first.GetPixel(x, y) + " -> " + second.GetPixel(x, y));
    }
    static long PixelDifference(Bitmap first, Bitmap second, Rectangle region)
    {
        long sum = 0;
        for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
        {
            Color a = first.GetPixel(x, y), b = second.GetPixel(x, y);
            sum += Math.Abs(a.A - b.A) + Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }
        return sum;
    }
    internal static void Overboost(string folder)
    {
        Directory.CreateDirectory(folder);
        Motion m = new Motion(new BoostCurve { MaxBoost = 1.5 }, delegate { return 99.99; });
        double next = 0;
        for (int now = 0; now <= 8000; now += 10) { while (next <= now) { m.Stroke(next); next += 75; } m.Update(now); }
        Require(m.Pressure == 1.5 && m.WarningChance == 0 && m.ExtraWarningPercent == 0, "Exactly 1.50 bar counted as overboost");
        m.Curve = new BoostCurve();
        for (int now = 8010; now <= 20000; now += 10) { while (next <= now) { m.Stroke(next); next += 75; } m.Update(now); }
        Require(m.Pressure == 1.7 && m.ExtraWarningPercent == 2.5 && m.WarningChance == 2.5, "Sustained overboost must add exactly 2.5 points once");
        m.Curve = new BoostCurve { MaxBoost = 1.5 };
        for (int now = 20010; now <= 24000; now += 10) { while (next <= now) { m.Stroke(next); next += 75; } m.Update(now); }
        Require(m.Pressure == 1.5 && m.ExtraWarningPercent == 2.5, "Falling to threshold reset accrued chance");
        m.Curve = new BoostCurve();
        for (int now = 24010; now <= 28000; now += 10) { while (next <= now) { m.Stroke(next); next += 75; } m.Update(now); }
        Require(m.Pressure > 1.5 && m.ExtraWarningPercent == 5 && m.WarningChance == 5, "Second crossing did not accumulate another 2.5 points");
        for (int now = 28010; now <= 30000; now += 10) m.Update(now);
        Require(!m.EngineWarning && m.Pressure == Motion.Idle && m.ExtraWarningPercent == 5, "Non-warning release cleared accumulated chance");
        m.Stroke(30010); Require(m.ExtraWarningPercent == 5, "Ordinary retyping cleared accumulated chance");
        m.EngineWarning = true; m.Curve.EngineWarningPercent = 12; m.Update(40000);
        Require(m.ExtraWarningPercent == 5, "Waiting warning cleared accumulated chance");
        m.Stroke(40010); Require(m.ExtraWarningPercent == 0 && m.WarningChance == 12 && !m.EngineWarning, "Acknowledging warning did not reset only the bonus");
        Require(new Motion(new BoostCurve { EngineWarningPercent = 99 }) { ExtraWarningPercent = 2.5 }.WarningChance == 100, "Warning chance exceeded 100%");
        foreach (double bonus in new[] { 0.0, 2.5, 7.5, 100.0 })
        {
            int hits = 0;
            for (int draw = 0; draw < 200; draw++)
            {
                Motion exact = new Motion(new BoostCurve(), delegate { return draw / 2.0; }) { ExtraWarningPercent = bonus };
                exact.Pressure = .8; exact.Stroke(0); exact.Update(0); exact.Update(900);
                if (exact.EngineWarning) hits++;
            }
            Require(hits == bonus * 2, "Fractional warning chance rounded incorrectly: " + bonus);
        }
        File.WriteAllText(Path.Combine(folder, "overboost-check.txt"), "PASS: default 0%; exact 1.50 excluded; one +2.5 point increment per upward crossing; sustained pressure counted once; bonus persists through ordinary input/release; warning acknowledged by typing resets bonus only; fractional 2.5/7.5% sampling and 100% cap.\r\n");
    }
    internal static void Warning(string folder)
    {
        Directory.CreateDirectory(folder);
        int warnings = 0;
        for (int draw = 0; draw < 100; draw++)
        {
            int calls = 0;
            Motion m = new Motion(new BoostCurve { EngineWarningPercent = 5 }, delegate { calls++; return draw; });
            m.Pressure = .8; m.Stroke(0); m.Update(0);
            for (int now = 10; now <= 900; now += 10) m.Update(now);
            Require(calls == 1 && m.FlutterUntil == 1900 && m.EngineWarning == (draw < 5), "5% release selection/boundary failed");
            if (m.EngineWarning) warnings++;
            for (int now = 910; now < 1900; now += 10)
            {
                m.Update(now);
                Require(calls == 1 && m.EngineWarning == (draw < 5), "Release rerolled during playback");
            }
            Require((m.WarningFace(1899) >= 0) == (draw < 5), "Warning face/timer mismatch");
            m.Update(1900);
            Require(m.EngineWarning == (draw < 5) && (m.WarningFace(1900) == 7) == (draw < 5) && m.FlutterUntil <= 1900, "Warning disappeared after animation completed");
            m.Update(60000);
            Require(calls == 1 && m.EngineWarning == (draw < 5), "Stopped car rerolled or cleared warning");
            if (draw < 5) Require(m.WarningFace(60000) == 7 && IdleFace.Frame(m, 60000) == -1, "Waiting face replaced persistent warning expression");
            m.Stroke(60010);
            Require(!m.EngineWarning && m.WarningFace(60010) == -1 && m.WarningTwitch(60010) == 0 && m.FlutterUntil < 60010, "Retyping did not clear held warning/tremor");
        }
        Require(warnings == 5, "Exactly five of 100 equally likely outcomes must show warning");
        foreach (int chance in new[] { 0, 1, 37, 99, 100 })
        {
            int hits = 0;
            for (int draw = 0; draw < 100; draw++)
            {
                Motion configured = new Motion(new BoostCurve { EngineWarningPercent = chance }, delegate { return draw; });
                configured.Pressure = 1; configured.Stroke(0); configured.Update(0); configured.Update(900);
                if (configured.EngineWarning) hits++;
                Require(configured.FlutterUntil == 1900, "Chance setting changed ordinary release eligibility");
            }
            Require(hits == chance, "Configured warning chance boundary failed at " + chance);
        }
        Motion liveChance = new Motion(new BoostCurve { EngineWarningPercent = 0 }, delegate { return 99; });
        liveChance.Pressure = 1; liveChance.Stroke(0); liveChance.Update(0);
        liveChance.Curve = new BoostCurve { EngineWarningPercent = 100 }; liveChance.Update(900);
        Require(liveChance.EngineWarning, "Live chance setting ignored");
        liveChance.Curve.EngineWarningPercent = 0; liveChance.Update(60000);
        Require(liveChance.EngineWarning, "Chance edit dismissed the warning before typing");
        liveChance.Stroke(60010); Require(!liveChance.EngineWarning, "Typing did not clear warning after chance edit");
        foreach (double pressure in new[] { -.001, 0.0, 1.7 })
        {
            int calls = 0;
            Motion m = new Motion(new BoostCurve { EngineWarningPercent = 5 }, delegate { calls++; return 0; });
            m.Pressure = pressure; m.Stroke(0); m.Update(0); m.Update(900);
            Require(calls == (pressure >= 0 ? 1 : 0) && m.EngineWarning == (pressure >= 0), "Warning eligibility changed boost boundary");
            m.Stroke(920);
            Require(!m.EngineWarning && m.WarningFace(920) == -1 && m.FlutterUntil < 920, "Retyping did not clear warning, code and expression");
        }
        int eventCount = 0;
        Motion repeated = new Motion(new BoostCurve { EngineWarningPercent = 5 }, delegate { return eventCount++ == 0 ? 4 : 5; });
        repeated.Pressure = 1; repeated.Stroke(0); repeated.Update(0); repeated.Update(900);
        Require(repeated.EngineWarning, "First release did not show warning");
        repeated.Pressure = 1; repeated.Stroke(1000); repeated.Update(1000); repeated.Update(1900);
        Require(eventCount == 2 && !repeated.EngineWarning && repeated.FlutterUntil == 2900, "Next release inherited previous warning instead of drawing again");
        using (Renderer renderer = new Renderer())
        {
            Motion normal = new Motion { LastStroke = 0 }, rare = new Motion { LastStroke = 0, ReleaseAt = 0, EngineWarning = true, FlutterUntil = 1000 };
            using (Bitmap original = Snapshot(renderer, normal, 375, 600))
            {
                int bodyOffset = (int)(Renderer.BodyLowering / 2);
                Rectangle near = new Rectangle(151, 342 + bodyOffset, 81, 59), far = new Rectangle(389, 369 + bodyOffset, 40, 30);
                Rectangle notification = new Rectangle(105, 130, 155, 130);
                for (int frame = 0; frame < 8; frame++)
                {
                    int now = frame * 125 + 62;
                    Require(rare.WarningFace(now) == frame && IdleFace.Frame(rare, now) == -1, "Warning expression priority/frame failed");
                    using (Bitmap visible = Snapshot(renderer, rare, now, 600))
                    {
                        int eyePixels = 0, codePixels = 0;
                        Rectangle code = new Rectangle(110, 219, 138, 35);
                        for (int y = 0; y < 600; y++) for (int x = 0; x < 600; x++) if (original.GetPixel(x, y) != visible.GetPixel(x, y))
                        {
                            Require(notification.Contains(x, y) || near.Contains(x, y) || far.Contains(x, y), "Warning changed vehicle/gauge outside eyes");
                            if (near.Contains(x, y) || far.Contains(x, y)) eyePixels++;
                            if (code.Contains(x, y)) codePixels++;
                        }
                        Require(eyePixels > 10 && codePixels > 100, "Warning expression or P0234 code missing");
                        visible.Save(Path.Combine(folder, "warning-" + frame + ".png"));
                    }
                }
                using (Bitmap held = Snapshot(renderer, rare, 1200, 600))
                using (Bitmap later = Snapshot(renderer, rare, 60000, 600))
                {
                    SameRegion(held, later, new Rectangle(0, 0, 600, 600), "Warning/code/face faded while waiting for typing");
                    Require(PixelDifference(original, later, notification) > 1000, "Persistent warning/code invisible");
                }
                // Only the pupils keep moving after the initial reaction; even with
                // body bob disabled, late frames must retain the full tremor cycle.
                rare.Curve.IdleMotionPercent = 0;
                foreach (int time in new[] { 1200, 60000, 3600000 })
                using (Bitmap left = Snapshot(renderer, rare, time, 600))
                using (Bitmap held = Snapshot(renderer, rare, time + 250, 600))
                using (Bitmap right = Snapshot(renderer, rare, time + 300, 600))
                using (Bitmap loop = Snapshot(renderer, rare, time + 1200, 600))
                {
                    Rectangle nearEye = new Rectangle(161, 367, 70, 36), farEye = new Rectangle(389, 371, 40, 30);
                    SameRegion(left, held, new Rectangle(0, 0, 600, 600), "Pupils advanced before the slower 300ms interval");
                    Require(PixelDifference(left, right, nearEye) > 100 && PixelDifference(left, right, farEye) > 100, "Warning pupil tremor stopped during hold");
                    for (int y = 0; y < 600; y++) for (int x = 0; x < 600; x++)
                        if (!nearEye.Contains(x, y) && !farEye.Contains(x, y)) Require(left.GetPixel(x, y) == right.GetPixel(x, y), "Tremor moved sweat/body/warning/gauge");
                    SameRegion(left, loop, new Rectangle(0, 0, 600, 600), "Warning pupil tremor did not loop");
                }
                for (int phase = 0; phase < 4; phase++)
                    using (Bitmap small = Snapshot(renderer, rare, 60000 + phase * 300, 320)) small.Save(Path.Combine(folder, "warning-loop-" + phase + ".png"));
                using (Bitmap small = Snapshot(renderer, rare, 375, 320)) small.Save(Path.Combine(folder, "warning-320.png"));
                using (Bitmap small = Snapshot(renderer, rare, 60000, 320)) small.Save(Path.Combine(folder, "warning-held-320.png"));
                normal.FlutterUntil = 1000;
                using (Bitmap regular = Snapshot(renderer, normal, 375, 320)) regular.Save(Path.Combine(folder, "regular-320.png"));
                using (Bitmap expired = Snapshot(renderer, normal, 1000, 600)) SameRegion(original, expired, new Rectangle(0, 0, 600, 600), "Ordinary Stututututu did not expire after one second");
                normal.Stroke(400); rare.Stroke(400);
                using (Bitmap resumed = Snapshot(renderer, rare, 400, 600))
                using (Bitmap expected = Snapshot(renderer, normal, 400, 600)) SameRegion(resumed, expected, new Rectangle(0, 0, 600, 600), "Retyping left warning residue");
            }
        }
        File.WriteAllText(Path.Combine(folder, "warning-check.txt"), "PASS: configurable warning chance; slower 300ms pupil steps / 1200ms loop still active after 1 hour with idle bob disabled; sweat/body/gauge/code remain fixed; typing immediately clears warning and tremor; ordinary text lasts one second.\r\n");
    }
    internal static void Animations(string folder)
    {
        Directory.CreateDirectory(folder);
        Motion idle = new Motion();
        Require(IdleFace.Frame(idle, 0) == -1 && IdleFace.Frame(idle, 3200) == 2 && IdleFace.Frame(idle, 3300) == 3 && IdleFace.Frame(idle, 3450) == 4, "Idle blink phases");
        Require(IdleFace.Frame(idle, 3600) == -1 && IdleFace.Frame(idle, 7000) == 6 && IdleFace.Frame(idle, 9500) == 11 && IdleFace.Frame(idle, 9700) == -1, "Waiting and neutral return");
        foreach (int percent in new[] { 25, 50, 100, 200 })
        {
            Motion paced = new Motion(new BoostCurve { IdleSpeedPercent = percent, BlinkSpeedPercent = percent });
            double factor = percent / 100.0;
            Require(IdleFace.Frame(paced, 3200 / factor) == 2 && IdleFace.Frame(paced, 3300 / factor) == 3
                && IdleFace.Frame(paced, 3450 / factor) == 4, "Blink speed did not change interval and closing/opening duration");
            paced.Curve.IdleSpeedPercent = 200;
            Require(IdleFace.Frame(paced, 3300 / factor) == 3, "Idle speed affected blink speed");
            paced.Curve.IdleSpeedPercent = percent;
            paced.Update(0); double before = 0, previousLift = 0, firstPeak = -1;
            int peakCount = 0;
            for (int now = 10; now < 3 * 3800 / factor; now += 10)
            {
                paced.Update(now);
                if (previousLift < before && paced.IdleLift >= previousLift)
                {
                    Require(previousLift < -14 && previousLift >= -16, "Speed control changed bob strength");
                    if (firstPeak >= 0) Require(Math.Abs((now - firstPeak) - 3800 / factor) <= 20, "Idle speed did not change measured bob period");
                    firstPeak = now; peakCount++;
                }
                before = previousLift; previousLift = paced.IdleLift;
            }
            Require(peakCount >= 2 && paced.Pressure == Motion.Idle && paced.Angle == 0, "Idle speed altered driving state");
            double oldLift = paced.IdleLift, age = paced.RestAge;
            paced.Curve.IdleSpeedPercent = percent == 200 ? 25 : 200;
            paced.Update(paced.LastTime + 10);
            Require(paced.RestAge > age && paced.RestAge - age <= .020001 && Math.Abs(paced.IdleLift - oldLift) < .8, "Live speed change jumped the body phase");
            paced.Stroke(paced.LastTime); Require(IdleFace.Frame(paced, paced.LastTime) == -1, "Adjusted blinking ignored typing");
        }
        Require(IdleFace.Frame(new Motion(new BoostCurve { BlinkSpeedPercent = 200 }), 7800) == 7, "Blink speed changed waiting cadence");
        idle.Stroke(7500); Require(IdleFace.Frame(idle, 7500) == -1, "Typing did not immediately cancel waiting");
        idle.Typing = false; idle.Speed = .5; Require(IdleFace.Frame(idle, 15000) == -1, "Coasting car showed idle face");
        idle.Speed = 0; idle.FlutterUntil = 16000; Require(IdleFace.Frame(idle, 15000) == -1, "Idle face interrupted lift-off reaction");
        using (Renderer renderer = new Renderer())
        {
            idle = new Motion();
            using (Bitmap original = Snapshot(renderer, idle, 0, 600))
            {
                foreach (int time in new[] { 3300, 7800 }) using (Bitmap changed = Snapshot(renderer, idle, time, 600))
                {
                    int differences = 0;
                    int bodyOffset = (int)(Renderer.BodyLowering / 2);
                    Rectangle near = new Rectangle(161, 365 + bodyOffset, 70, 36), far = new Rectangle(389, 369 + bodyOffset, 40, 30);
                    for (int y = 0; y < 600; y++) for (int x = 0; x < 600; x++) if (original.GetPixel(x, y) != changed.GetPixel(x, y))
                    { differences++; Require(near.Contains(x, y) || far.Contains(x, y), "Eye animation shifted body, wheel or gauge"); }
                    Require(differences > 10, "Eye animation invisible");
                    changed.Save(Path.Combine(folder, time == 3300 ? "blink.png" : "waiting.png"));
                }
                original.Save(Path.Combine(folder, "neutral.png"));
            }
            string idleFolder = Path.Combine(folder, "idle-frames"); Directory.CreateDirectory(idleFolder);
            for (int frame = 0; frame < 280; frame++)
            {
                idle.Update(frame * 50);
                using (Bitmap b = Snapshot(renderer, idle, frame * 50, 320)) b.Save(Path.Combine(idleFolder, frame.ToString("D3") + ".png"));
            }
            Motion rest = new Motion(); rest.Update(0);
            using (Bitmap baseline = Snapshot(renderer, rest, 0, 600))
            {
                for (int now = 50; now <= 2050; now += 50) rest.Update(now);
                Require(rest.IdleLift < -14 && rest.IdleLift >= -16 && rest.Angle == 0 && rest.Pressure == Motion.Idle, "Idle suspension/boost isolation");
                using (Bitmap lifted = Snapshot(renderer, rest, 2050, 600))
                {
                    lifted.Save(Path.Combine(folder, "body-lifted.png"));
                    baseline.Save(Path.Combine(folder, "body-rest.png"));
                    SameRegion(baseline, lifted, new Rectangle(355, 102, 200, 195), "Idle suspension moved gauge");
                    SameRegion(baseline, lifted, new Rectangle(128, 420, 25, 70), "Idle suspension moved front tire/rim");
                    // Check opaque wheel interiors; translucent fender-edge pixels can reveal the moving body behind them.
                    SameRegion(baseline, lifted, new Rectangle(44, 405, 10, 42), "Idle suspension moved rear tire/rim");
                    // The lowered front bumper now covers the upper far tire;
                    // compare its still-exposed lower tread instead.
                    SameRegion(baseline, lifted, new Rectangle(382, 499, 30, 3), "Idle suspension moved far tire");
                }
                baseline.Save(Path.Combine(folder, "body-rest.png"));
                for (int now = 2100; now <= 4000; now += 50) rest.Update(now);
                Require(Math.Abs(rest.IdleLift) < .8, "Idle suspension did not return near baseline");
                rest.IdleLift = -7.9; double lift = rest.IdleLift;
                rest.Stroke(4010); rest.Update(4010);
                Require(rest.RestAge == 0 && Math.Abs(rest.IdleLift - lift) < .5, "Idle-to-typing transition snapped");
                for (int now = 4020; now <= 5800; now += 10) { rest.Stroke(now); rest.Update(now); }
                Require(rest.IdleLift == 0, "Suspension continued during sustained typing");
            }
            Motion sample = new Motion { Typing = true, Speed = 10, Distance = 211, Angle = .5 };
            Rectangle vehicleArea = new Rectangle(3, 136, 233, 138), ground = new Rectangle(16, 274, 260, 10), windArea = new Rectangle(238, 205, 40, 32);
            sample.Pressure = 0;
            using (Bitmap unboosted = Snapshot(renderer, sample, 0, 320))
            {
                long previousDifference = -1;
                foreach (double pressure in new[] { 0.0, 1.0, 2.0 })
                {
                    sample.Pressure = pressure;
                    using (Bitmap image = Snapshot(renderer, sample, 0, 320))
                    {
                        long difference = PixelDifference(unboosted, image, vehicleArea);
                        Require(difference > previousDifference, "Boost did not increase vehicle blur"); previousDifference = difference;
                        SameRegion(unboosted, image, ground, "Boost blurred the ground");
                        SameRegion(unboosted, image, windArea, "Boost blurred the wind");
                        image.Save(Path.Combine(folder, "boost-blur-" + ((int)pressure).ToString() + ".png"));
                    }
                }
            }
            double defaultLift = 0;
            foreach (int strength in new[] { 0, 100, 200 })
            {
                Motion adjusted = new Motion(new BoostCurve { IdleMotionPercent = strength, MotionBlurPercent = strength });
                adjusted.Update(0); for (int now = 50; now <= 2050; now += 50) adjusted.Update(now);
                if (strength == 0) Require(adjusted.IdleLift == 0 && adjusted.RestAge == 0, "Disabled idle motion kept running");
                if (strength == 100) defaultLift = adjusted.IdleLift;
                if (strength == 200) Require(Math.Abs(adjusted.IdleLift - 2 * defaultLift) < 1e-9, "Idle strength scaling ignored");
                using (Bitmap image = Snapshot(renderer, adjusted, 2050, 600)) image.Save(Path.Combine(folder, "idle-strength-" + strength + ".png"));
                adjusted.Curve.IdleMotionPercent = 0;
                for (int now = 2100; now <= 4500; now += 50) adjusted.Update(now);
                Require(adjusted.IdleLift == 0 && adjusted.RestAge == 0, "Disabling idle motion did not settle");
            }
            sample.Curve.MotionBlurPercent = 0; sample.Pressure = 1.7;
            using (Bitmap crisp = Snapshot(renderer, sample, 0, 320))
            {
                long defaultDifference = 0;
                foreach (int strength in new[] { 0, 100, 200 })
                {
                    sample.Curve.MotionBlurPercent = strength;
                    using (Bitmap image = Snapshot(renderer, sample, 0, 320))
                    {
                        long difference = PixelDifference(crisp, image, vehicleArea);
                        if (strength == 0) Require(difference == 0, "0% vehicle blur is not off");
                        if (strength == 100) { Require(difference > 1000, "Vehicle blur invisible"); defaultDifference = difference; }
                        if (strength == 200) Require(difference > defaultDifference * 1.3, "200% blur indistinct on vehicle at 320px");
                        SameRegion(crisp, image, ground, "Blur slider changed ground"); SameRegion(crisp, image, windArea, "Blur slider changed wind");
                        SameRegion(crisp, image, new Rectangle(190, 55, 100, 95), "Blur affected gauge");
                        image.Save(Path.Combine(folder, "blur-320-" + strength + ".png"));
                    }
                }
                sample.Curve.MotionBlurPercent = 0; sample.Pressure = 0;
                using (Bitmap zero = Snapshot(renderer, sample, 0, 320)) SameRegion(crisp, zero, vehicleArea, "Zero-pressure rendering changed unblurred vehicle");
                sample.Curve.MotionBlurPercent = 200;
                using (Bitmap zero = Snapshot(renderer, sample, 0, 320)) SameRegion(crisp, zero, vehicleArea, "Vehicle blur survived zero boost");
            }
            sample.Pressure = 1.7; sample.Curve.MotionBlurPercent = 0;
            using (Bitmap crisp = Snapshot(renderer, sample, 0, 600))
            {
                sample.Curve.MotionBlurPercent = 200;
                using (Bitmap blurred = Snapshot(renderer, sample, 0, 600))
                {
                    SameRegion(crisp, blurred, new Rectangle(355, 102, 200, 195), "Blur affected gauge");
                    Rectangle allowedTrail = new Rectangle(0, 249, 226, 252);
                    Rectangle roofTrail = new Rectangle(0, 243, 346, 78);
                    Require(PixelDifference(crisp, blurred, allowedTrail) > 1000, "Rear contour trail invisible");
                    Require(PixelDifference(crisp, blurred, new Rectangle(225, 244, 116, 38)) > 1000, "Trail did not reach the roof/sunroof silhouette");
                    SameRegion(crisp, blurred, new Rectangle(24, 376, 66, 104), "Expanded trail touched rear tire/rim");
                    SameRegion(crisp, blurred, new Rectangle(117, 390, 69, 119), "Expanded trail touched front tire/rim");
                    int bodyOffset = (int)(Renderer.BodyLowering / 2);
                    SameRegion(crisp, blurred, new Rectangle(161, 365 + bodyOffset, 70, 36), "Expanded trail touched headlamp eyes");
                    SameRegion(crisp, blurred, new Rectangle(175, 338 + bodyOffset, 160, 25), "Expanded trail blurred engine bay");
                    SameRegion(crisp, blurred, new Rectangle(170, 283 + bodyOffset, 110, 35), "Expanded trail blurred windshield");
                    SameRegion(crisp, blurred, new Rectangle(180, 266 + bodyOffset, 90, 8), "Roof trail blurred the sunroof glass");
                    for (int y = 0; y < 600; y++) for (int x = 0; x < 600; x++)
                        if (!allowedTrail.Contains(x, y) && !roofTrail.Contains(x, y)) Require(crisp.GetPixel(x, y) == blurred.GetPixel(x, y), "Rear/roof blur changed front, interior, wheels or UI at " + x + "," + y);
                    blurred.Save(Path.Combine(folder, "vehicle-blur-600.png"));
                }
                sample.EngineWarning = true; sample.Typing = false; sample.FlutterUntil = 1000;
                using (Bitmap warningBlur = Snapshot(renderer, sample, 375, 600))
                {
                    sample.Curve.MotionBlurPercent = 0;
                    using (Bitmap warningCrisp = Snapshot(renderer, sample, 375, 600))
                        SameRegion(warningBlur, warningCrisp, new Rectangle(105, 130, 155, 125), "Vehicle blur affected warning/P0234");
                }
                sample.EngineWarning = false; sample.FlutterUntil = double.NegativeInfinity;
                sample.Curve.MotionBlurPercent = 200;
                sample.Speed = 0;
                using (Bitmap stopped = Snapshot(renderer, sample, 0, 320))
                {
                    sample.Curve.MotionBlurPercent = 0;
                    using (Bitmap cleanStop = Snapshot(renderer, sample, 0, 320)) SameRegion(stopped, cleanStop, new Rectangle(0, 0, 320, 320), "Blur survived stopping");
                }
            }
            Motion drive = new Motion(new BoostCurve(), delegate { return 100; }); double next = 0;
            for (double now = 0; now <= 6000; now += 10) { while (next <= now) { drive.Stroke(next); next += 75; } drive.Update(now); }
            string driveFolder = Path.Combine(folder, "drive-frames"); Directory.CreateDirectory(driveFolder);
            for (int frame = 0; frame < 60; frame++)
            {
                double now = 6040 + frame * 40;
                while (next <= now) { drive.Stroke(next); next += 75; } drive.Update(now);
                using (Bitmap b = Snapshot(renderer, drive, now, 320)) b.Save(Path.Combine(driveFolder, frame.ToString("D3") + ".png"));
            }
            using (Bitmap b = Snapshot(renderer, drive, 8400, 1200)) b.Save(Path.Combine(folder, "subtle-motion-blur.png"));
            for (double now = 8410; now <= 12000; now += 10) drive.Update(now);
            using (Bitmap b = Snapshot(renderer, drive, 12000, 320)) b.Save(Path.Combine(folder, "settled.png"));
            Require(drive.Speed == 0 && drive.Pressure == Motion.Idle, "Motion effects did not settle");
        }
        File.WriteAllText(Path.Combine(folder, "animation-check.txt"), "PASS: idle/blink speeds and stable suspension; only rear exterior trail varies with boost and 0/100/200%; all pixels outside rear contour remain identical, including face/engine/body/wheels/gauge; crisp wind and warning/code; no trail at zero boost or rest.\r\n");
    }
    internal static void Settings(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "boost-test.txt");
        BoostCurve custom = new BoostCurve { StartRate = 200, MidRate = 350, HoldRate = 550, HighRate = 750, MidBoost = .80, HoldBoost = 1.20, MaxBoost = 1.90, IdleMotionPercent = 150, MotionBlurPercent = 50, IdleSpeedPercent = 50, BlinkSpeedPercent = 200, EngineWarningPercent = 37 };
        custom.OpacityPercent = 37;
        Require(custom.Validate() == null, "Custom curve rejected");
        double[] rates = { 0, 100, 199, 200, 275, 350, 450, 550, 750, 751, 1200 };
        double[] pressure = { -.62, -.31, -.0031, 0, .40, .80, 1.00, 1.20, 1.20, 1.90, 1.90 };
        for (int i = 0; i < rates.Length; i++) Require(Math.Abs(custom.Target(rates[i]) - pressure[i]) < 1e-9, "Custom interpolation/boundary " + rates[i]);
        CultureInfo previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            custom.Save(path); BoostCurve loaded = BoostCurve.Load(path);
            Require(loaded.StartRate == 200 && loaded.MidBoost == .8 && loaded.HighRate == 750 && loaded.MaxBoost == 1.9, "Settings culture/reload failed");
            Require(loaded.IdleMotionPercent == 150 && loaded.MotionBlurPercent == 50, "Effect settings did not persist");
            Require(loaded.IdleSpeedPercent == 50 && loaded.BlinkSpeedPercent == 200, "Animation speed settings did not persist");
            Require(loaded.EngineWarningPercent == 37, "Warning chance did not persist");
            Require(loaded.OpacityPercent == 37, "Opacity did not persist");
            foreach (int percent in new[] { 1, 37, 100 })
            {
                custom.OpacityPercent = percent; custom.Save(path);
                Require(BoostCurve.Load(path).OpacityPercent == percent, "Opacity endpoint did not persist");
            }
            custom.OpacityPercent = 37; custom.Save(path);
            List<string> withoutOpacity = new List<string>();
            foreach (string line in File.ReadAllLines(path)) if (!line.StartsWith("opacity_percent=")) withoutOpacity.Add(line);
            File.WriteAllLines(path, withoutOpacity.ToArray()); loaded = BoostCurve.Load(path);
            Require(loaded.OpacityPercent == 100 && loaded.StartRate == 200 && loaded.MidBoost == .8 && loaded.EngineWarningPercent == 37, "Legacy opacity default reset existing preferences");
            foreach (string invalid in new[] { "0", "101", "-1", "oops", "5.5" })
            {
                File.WriteAllLines(path, withoutOpacity.ToArray()); File.AppendAllText(path, "opacity_percent=" + invalid + "\n");
                loaded = BoostCurve.Load(path);
                Require(loaded.OpacityPercent == 100 && loaded.StartRate == 200 && loaded.EngineWarningPercent == 37, "Invalid opacity reset other preferences");
            }
            custom.Save(path);
            foreach (int chance in new[] { 0, 100 })
            {
                custom.EngineWarningPercent = chance; custom.Save(path);
                Require(BoostCurve.Load(path).EngineWarningPercent == chance, "Warning chance endpoint did not persist");
            }
            custom.EngineWarningPercent = 37; custom.Save(path);
            List<string> beforeChance = new List<string>();
            foreach (string line in File.ReadAllLines(path)) if (!line.StartsWith("engine_warning_percent=")) beforeChance.Add(line);
            File.WriteAllLines(path, beforeChance.ToArray()); loaded = BoostCurve.Load(path);
            Require(loaded.EngineWarningPercent == 0 && loaded.StartRate == 200 && loaded.IdleSpeedPercent == 50 && loaded.BlinkSpeedPercent == 200 && loaded.MotionBlurPercent == 50, "Warning chance upgrade reset existing preferences");
            foreach (string invalid in new[] { "-1", "101", "oops", "5.5" })
            {
                File.AppendAllText(path, "engine_warning_percent=" + invalid + "\n"); loaded = BoostCurve.Load(path);
                Require(loaded.EngineWarningPercent == 0 && loaded.StartRate == 200 && loaded.IdleSpeedPercent == 50, "Invalid chance reset other preferences");
            }
            List<string> previousVersion = new List<string>();
            foreach (string line in File.ReadAllLines(path)) if (!line.StartsWith("idle_speed_percent=") && !line.StartsWith("blink_speed_percent=")) previousVersion.Add(line);
            File.WriteAllLines(path, previousVersion.ToArray()); loaded = BoostCurve.Load(path);
            Require(loaded.IdleSpeedPercent == 100 && loaded.BlinkSpeedPercent == 100 && loaded.IdleMotionPercent == 150 && loaded.MotionBlurPercent == 50 && loaded.StartRate == 200, "Adding speed settings lost existing preferences");
            List<string> legacy = new List<string>();
            foreach (string line in File.ReadAllLines(path)) if (!line.StartsWith("idle_motion_percent=") && !line.StartsWith("motion_blur_percent=")) legacy.Add(line);
            File.WriteAllLines(path, legacy.ToArray()); loaded = BoostCurve.Load(path);
            Require(loaded.StartRate == 200 && loaded.HoldRate == 550 && loaded.MidBoost == .8 && loaded.IdleMotionPercent == 100 && loaded.MotionBlurPercent == 100, "Upgrade lost legacy boost settings");
            File.AppendAllText(path, "idle_motion_percent=oops\nmotion_blur_percent=999\nidle_speed_percent=0\nblink_speed_percent=oops\n"); loaded = BoostCurve.Load(path);
            Require(loaded.MidBoost == .8 && loaded.IdleMotionPercent == 100 && loaded.MotionBlurPercent == 100 && loaded.IdleSpeedPercent == 100 && loaded.BlinkSpeedPercent == 100, "Invalid effect settings reset the boost curve");
            new BoostCurve().Save(path); Require(BoostCurve.Load(path).MidBoost == 1.49, "Atomic overwrite/default restoration failed");
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
        Require(new BoostCurve { MidRate = 150 }.Validate() != null, "Equal thresholds accepted");
        Require(new BoostCurve { HighRate = 1200 }.Validate() != null, "Unreachable high threshold accepted");
        Require(new BoostCurve { HoldBoost = 1.0 }.Validate() != null, "Descending pressure accepted");
        Require(new BoostCurve { MaxBoost = 2.01 }.Validate() != null, "Off-scale pressure accepted");
        Require(new BoostCurve { MidBoost = double.NaN }.Validate() != null, "NaN accepted");
        Require(new BoostCurve { IdleMotionPercent = -1 }.Validate() != null && new BoostCurve { MotionBlurPercent = 201 }.Validate() != null, "Invalid effect strength accepted");
        Require(new BoostCurve { EngineWarningPercent = -1 }.Validate() != null && new BoostCurve { EngineWarningPercent = 101 }.Validate() != null, "Invalid warning chance accepted");
        Require(new BoostCurve { OpacityPercent = 0 }.Validate() != null && new BoostCurve { OpacityPercent = 101 }.Validate() != null, "Invalid opacity accepted");
        int[] levels = { 1, 25, 37, 50, 75, 99, 100 }, alphas = { 3, 64, 94, 128, 191, 252, 255 };
        for (int i = 0; i < levels.Length; i++) Require(Layer.OpacityAlpha(levels[i]) == alphas[i], "Incorrect native opacity alpha");
        for (int percent = 2; percent <= 100; percent++) Require(Layer.OpacityAlpha(percent) > Layer.OpacityAlpha(percent - 1), "Opacity levels are not distinct");
        Require(new BoostCurve { IdleSpeedPercent = 24 }.Validate() != null && new BoostCurve { IdleSpeedPercent = 201 }.Validate() != null
            && new BoostCurve { BlinkSpeedPercent = 0 }.Validate() != null && new BoostCurve { BlinkSpeedPercent = 201 }.Validate() != null, "Invalid speed accepted");
        string valid = File.ReadAllText(path);
        try { new BoostCurve { MidRate = 1 }.Save(path); throw new Exception("Invalid settings saved"); } catch (ArgumentException) { }
        Require(File.ReadAllText(path) == valid, "Invalid save changed previous settings");
        File.WriteAllText(path, "start_cpm=oops\nmid_bar=NaN"); Require(BoostCurve.Load(path).StartRate == 150, "Corrupt settings did not fall back");
        Require(BoostCurve.Load(Path.Combine(folder, "absent.txt")).MaxBoost == 1.7, "Missing settings fallback");
        Motion m = new Motion(custom); double next = 0;
        for (double now = 0; now <= 8000; now += 10) { while (next <= now) { m.Stroke(next); next += 75; } m.Update(now); }
        Require(m.Pressure > 1.89 && m.Led, "Live model ignored custom maximum");
        m.Curve = new BoostCurve { MidBoost = .4, HoldBoost = .6, MaxBoost = .9 }; m.Stroke(8010); m.Update(8010);
        Require(m.Pressure <= .9, "Live maximum reduction ignored");
        for (double now = 8020; now <= 8700; now += 10) m.Update(now);
        Require(m.Pressure < 0 && m.FlutterUntil > 8700, "Configured curve broke lift-off");
        m.Stroke(8710); Require(m.FlutterUntil < 8710, "Configured curve broke retyping");
        bool saved = false;
        using (BoostSettingsWindow dialog = new BoostSettingsWindow(custom, delegate(BoostCurve c) { saved = true; c.Save(path); }))
        {
            // Materialize the real WinForms layout before rendering the UI test artifact.
            dialog.Show(); Application.DoEvents(); dialog.PerformLayout();
            Require(dialog.ReadValues().MaxBoost == 1.9, "Settings UI did not load custom values");
            Require(dialog.ReadValues().IdleMotionPercent == 150 && dialog.ReadValues().MotionBlurPercent == 50, "Settings UI did not load effect strengths");
            Require(dialog.ReadValues().IdleSpeedPercent == 50 && dialog.ReadValues().BlinkSpeedPercent == 200, "Settings UI did not load speeds");
            Require(dialog.ReadValues().EngineWarningPercent == 37, "Settings UI did not load warning chance");
            Require(dialog.ReadValues().OpacityPercent == 37 && dialog.Opacity == 1, "Opacity setting did not load or faded the settings dialog");
            using (Bitmap image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(folder, "settings-window.png")); }
            dialog.SetValues(new BoostCurve { MidRate = 150 }); Require(!dialog.SaveValues() && !saved, "UI accepted invalid thresholds");
            dialog.SetValues(custom);
            TableLayoutPanel inputs = null;
            foreach (Control c in dialog.Controls) if (c is TableLayoutPanel) inputs = (TableLayoutPanel)c;
            NumericUpDown last = (NumericUpDown)inputs.GetControlFromPosition(2, 4);
            last.Focus(); last.Text = "1.85";
            NumericUpDown blur = (NumericUpDown)dialog.Controls["BlurStrength"];
            blur.Focus(); blur.Text = "160";
            NumericUpDown idleRate = (NumericUpDown)dialog.Controls["IdleSpeed"], blinkRate = (NumericUpDown)dialog.Controls["BlinkSpeed"];
            idleRate.Focus(); idleRate.Text = "75"; blinkRate.Focus(); blinkRate.Text = "150";
            NumericUpDown chanceInput = (NumericUpDown)dialog.Controls["WarningChance"];
            Require(chanceInput.Minimum == 0 && chanceInput.Maximum == 100 && chanceInput.Increment == 1, "Warning chance UI range incorrect");
            chanceInput.Focus(); chanceInput.Text = "17";
            NumericUpDown opacityInput = (NumericUpDown)dialog.Controls["PetOpacity"];
            Require(opacityInput.Minimum == 1 && opacityInput.Maximum == 100 && opacityInput.Increment == 1 && opacityInput.DecimalPlaces == 0, "Opacity UI is not 1..100 in integer steps");
            opacityInput.Focus(); opacityInput.Text = "36";
            Require(dialog.SaveValues() && saved && BoostCurve.Load(path).StartRate == 200 && BoostCurve.Load(path).MaxBoost == 1.85
                && BoostCurve.Load(path).MotionBlurPercent == 160 && BoostCurve.Load(path).IdleMotionPercent == 150
                && BoostCurve.Load(path).IdleSpeedPercent == 75 && BoostCurve.Load(path).BlinkSpeedPercent == 150
                && BoostCurve.Load(path).EngineWarningPercent == 17 && BoostCurve.Load(path).OpacityPercent == 36, "UI save/unblurred field did not persist");
        }
        using (BoostSettingsWindow dialog = new BoostSettingsWindow(new BoostCurve(), delegate { }))
        {
            dialog.Show(); Application.DoEvents();
            Require(dialog.ReadValues().IdleMotionPercent == 100 && dialog.ReadValues().MotionBlurPercent == 100, "Default effect controls incorrect");
            Require(dialog.ReadValues().IdleSpeedPercent == 100 && dialog.ReadValues().BlinkSpeedPercent == 100, "Default speed controls incorrect");
            Require(dialog.ReadValues().EngineWarningPercent == 0, "Default warning chance is not 0%");
            Require(dialog.ReadValues().OpacityPercent == 100, "Default opacity is not 100%");
            using (Bitmap image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(folder, "settings-defaults.png")); }
            dialog.Close();
        }
        saved = false;
        using (BoostSettingsWindow dialog = new BoostSettingsWindow(custom, delegate { saved = true; })) { dialog.SetValues(new BoostCurve()); dialog.Close(); }
        Require(!saved, "Cancel applied changes");
        using (BoostSettingsWindow dialog = new BoostSettingsWindow(custom, delegate { throw new IOException("simulated write failure"); }))
            Require(!dialog.SaveValues(), "Save failure reported success");
        File.WriteAllText(Path.Combine(folder, "settings-check.txt"), "PASS: custom boundaries/interpolation; opacity 1..100 in integer steps, alpha mapping, persistence, legacy/invalid fallback and opaque settings UI; existing preferences, defaults, live model and save/cancel/write failure.\r\n");
    }
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        double[] rates = { 0, 149, 150, 275, 400, 450, 500, 600, 601, 1000 };
        double[] values = { -.62, -.62 / 150, 0, .745, 1.49, 1.495, 1.5, 1.5, 1.7, 1.7 };
        for (int i = 0; i < rates.Length; i++) Require(Math.Abs(new BoostCurve().Target(rates[i]) - values[i]) < 1e-9, "Boost range");
        Motion m = new Motion(new BoostCurve(), delegate { return 100; });
        foreach (string row in ReferenceRows())
        {
            string[] fields = row.Split(',');
            if (fields[0] == "reset") { m = new Motion(new BoostCurve(), delegate { return 100; }); continue; }
            double time = double.Parse(fields[1], CultureInfo.InvariantCulture);
            if (fields[0] == "stroke") { m.Stroke(time); continue; }
            m.Update(time);
            double[] actual = { m.Rate, m.Pressure, m.Speed, m.Angle, m.Distance, m.Shift };
            for (int i = 0; i < actual.Length; i++) Require(Math.Abs(actual[i] - double.Parse(fields[i + 2], CultureInfo.InvariantCulture)) < 1e-7, "Approved model mismatch at " + time + ", field " + i);
            Require(m.Typing == (fields[8] == "1") && (m.FlutterUntil > time) == (fields[9] == "1"), "Typing/flutter state mismatch");
        }
        foreach (int key in new[] { 0x57, 0x41, 0x20, 0xBA, 0x60, 8, 46, 13 }) Require(PetWindow.IsTypingKey(key, false, false, false), "Typing key missing");
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0x15, 0x1B, 0x70, 0x25, 0x5B }) Require(!PetWindow.IsTypingKey(key, false, false, false), "Non-typing key counted");
        Require(!PetWindow.IsTypingKey(0x56, true, false, false), "Paste shortcut counted");
        using (Renderer renderer = new Renderer())
        {
            m = new Motion(new BoostCurve(), delegate { return 100; });
            using (Bitmap idle = Snapshot(renderer, m, 0, 1200)) idle.Save(Path.Combine(folder, "desktop-idle.png"));
            double next = 0;
            for (double now = 0; now <= 8000; now += 10) { while (next <= now) { m.Stroke(next); next += 60000.0 / 700; } m.Update(now); }
            Require(m.Pressure > 1.69 && m.Led && m.Speed > 0, "Fast typing reaction");
            using (Bitmap active = Snapshot(renderer, m, 8000, 1200)) active.Save(Path.Combine(folder, "desktop-driving.png"));
            using (Bitmap small = Snapshot(renderer, m, 8000, 320)) small.Save(Path.Combine(folder, "desktop-320.png"));
            for (double now = 8010; now <= 8580; now += 10) m.Update(now);
            Require(m.Pressure < 0 && m.FlutterUntil > 8580, "Lift-off reaction");
            using (Bitmap release = Snapshot(renderer, m, 8580, 1200)) release.Save(Path.Combine(folder, "desktop-flutter.png"));
            m.Stroke(8590); Require(m.FlutterUntil < 8590, "Retyping hides flutter");
            foreach (double pressure in new[] { 1.49, 1.494, 1.495, 1.50, 1.7 }) { m.Pressure = pressure; Require(m.Led == (pressure >= 1.495), "LED display threshold"); }
            m = new Motion(new BoostCurve(), delegate { return 100; });
            Stopwatch speed = Stopwatch.StartNew();
            using (Bitmap frame = new Bitmap(320, 320, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(frame)) for (int i = 0; i < 120; i++) { m.Stroke(i * 80); m.Update(i * 80); renderer.Draw(g, m, i * 80, 320); }
            File.WriteAllText(Path.Combine(folder, "native-check.txt"), "PASS: native boost/typing model matches approved reference trace; keyboard categories, pressure/LED, release and retyping verified.\r\nMean render ms: " + (speed.Elapsed.TotalMilliseconds / 120).ToString("F2", CultureInfo.InvariantCulture));
        }
    }
}

internal static class TestProgram
{
    [STAThread] static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: G70.Tests.exe --self-test|--animation-test|--warning-test|--overboost-test|--settings-test|--bounds-test|--gauge-test|--smoke-test output-folder"); return 2; }
        try
        {
            switch (args[0])
            {
                case "--self-test": Checks.Run(args[1]); break;
                case "--animation-test": Checks.Animations(args[1]); break;
                case "--warning-test": Checks.Warning(args[1]); break;
                case "--overboost-test": Checks.Overboost(args[1]); break;
                case "--bounds-test": Checks.Bounds(args[1]); break;
                case "--gauge-test": Checks.Gauge(args[1]); break;
                case "--settings-test":
                case "--smoke-test":
                    Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    if (args[0] == "--settings-test") Checks.Settings(args[1]); else Checks.Smoke(args[1]);
                    break;
                default: throw new ArgumentException("Unknown test: " + args[0]);
            }
            return 0;
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "error.txt"), error.ToString());
            Console.Error.WriteLine(error); return 1;
        }
    }
}
