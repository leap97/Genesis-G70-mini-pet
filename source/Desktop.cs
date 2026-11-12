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

// No text capture, network, auto-start or background service. A keyboard event
// contributes only a timestamp; closing the pet uninstalls its hook.
internal sealed class BoostCurve
{
    internal int StartRate = 150, MidRate = 400, HoldRate = 500, HighRate = 600;
    internal double MidBoost = 1.49, HoldBoost = 1.50, MaxBoost = 1.70;
    internal int IdleMotionPercent = 100, MotionBlurPercent = 100;
    internal int IdleSpeedPercent = 100, BlinkSpeedPercent = 100;
    internal int EngineWarningPercent = 0;
    internal int OpacityPercent = 100;
    internal string Validate()
    {
        if (StartRate < 1 || HighRate > 1199 || !(StartRate < MidRate && MidRate < HoldRate && HoldRate < HighRate))
            return "타자수는 1~1199 사이에서 시작 < 중간 < 유지 < 고속 순서로 입력하세요.";
        if (double.IsNaN(MidBoost) || double.IsNaN(HoldBoost) || double.IsNaN(MaxBoost)
            || !(0 <= MidBoost && MidBoost <= HoldBoost && HoldBoost <= MaxBoost && MaxBoost <= 2))
            return "압력은 0~2.00 bar 사이에서 중간 ≤ 유지 ≤ 고속 순서로 입력하세요.";
        if (IdleMotionPercent < 0 || IdleMotionPercent > 200 || MotionBlurPercent < 0 || MotionBlurPercent > 200)
            return "들썩임과 모션블러 강도는 0~200% 사이로 입력하세요.";
        if (IdleSpeedPercent < 25 || IdleSpeedPercent > 200 || BlinkSpeedPercent < 25 || BlinkSpeedPercent > 200)
            return "들썩임과 눈 깜박임 속도는 25~200% 사이로 입력하세요.";
        if (EngineWarningPercent < 0 || EngineWarningPercent > 100)
            return "엔진경고등 확률은 0~100% 사이로 입력하세요.";
        if (OpacityPercent < 1 || OpacityPercent > 100)
            return "미니펫 불투명도는 1~100% 사이로 입력하세요.";
        return null;
    }
    internal double Target(double cpm)
    {
        if (cpm < StartRate) return Motion.Idle * (1 - Motion.Clamp(cpm / StartRate, 0, 1));
        if (cpm <= MidRate) return (cpm - StartRate) / (MidRate - StartRate) * MidBoost;
        if (cpm < HoldRate) return MidBoost + (cpm - MidRate) / (HoldRate - MidRate) * (HoldBoost - MidBoost);
        return cpm <= HighRate ? HoldBoost : MaxBoost;
    }
    internal static BoostCurve Load(string path)
    {
        try
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] pair = line.Split(new[] { '=' }, 2);
                if (pair.Length == 2) values[pair[0].Trim()] = pair[1].Trim();
            }
            BoostCurve curve = new BoostCurve();
            curve.StartRate = int.Parse(values["start_cpm"], CultureInfo.InvariantCulture);
            curve.MidRate = int.Parse(values["mid_cpm"], CultureInfo.InvariantCulture);
            curve.HoldRate = int.Parse(values["hold_cpm"], CultureInfo.InvariantCulture);
            curve.HighRate = int.Parse(values["high_cpm"], CultureInfo.InvariantCulture);
            curve.MidBoost = double.Parse(values["mid_bar"], CultureInfo.InvariantCulture);
            curve.HoldBoost = double.Parse(values["hold_bar"], CultureInfo.InvariantCulture);
            curve.MaxBoost = double.Parse(values["max_bar"], CultureInfo.InvariantCulture);
            // Older settings retain their custom boost curve and gain the new defaults.
            curve.IdleMotionPercent = ReadPercent(values, "idle_motion_percent");
            curve.MotionBlurPercent = ReadPercent(values, "motion_blur_percent");
            curve.IdleSpeedPercent = ReadPercent(values, "idle_speed_percent", 25);
            curve.BlinkSpeedPercent = ReadPercent(values, "blink_speed_percent", 25);
            curve.EngineWarningPercent = ReadPercent(values, "engine_warning_percent", 0, 100, 0);
            curve.OpacityPercent = ReadPercent(values, "opacity_percent", 1, 100);
            if (curve.Validate() == null) return curve;
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        catch (FormatException) { } catch (OverflowException) { } catch (KeyNotFoundException) { }
        return new BoostCurve();
    }
    static int ReadPercent(Dictionary<string, string> values, string key, int minimum = 0, int maximum = 200, int fallback = 100)
    {
        string text; int value;
        return values.TryGetValue(key, out text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= minimum && value <= maximum ? value : fallback;
    }
    internal void Save(string path)
    {
        string error = Validate(); if (error != null) throw new ArgumentException(error);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temp, new[] {
                "start_cpm=" + StartRate.ToString(CultureInfo.InvariantCulture), "mid_cpm=" + MidRate.ToString(CultureInfo.InvariantCulture),
                "hold_cpm=" + HoldRate.ToString(CultureInfo.InvariantCulture), "high_cpm=" + HighRate.ToString(CultureInfo.InvariantCulture),
                "mid_bar=" + MidBoost.ToString("0.00", CultureInfo.InvariantCulture), "hold_bar=" + HoldBoost.ToString("0.00", CultureInfo.InvariantCulture),
                "max_bar=" + MaxBoost.ToString("0.00", CultureInfo.InvariantCulture),
                "idle_motion_percent=" + IdleMotionPercent.ToString(CultureInfo.InvariantCulture),
                "motion_blur_percent=" + MotionBlurPercent.ToString(CultureInfo.InvariantCulture),
                "idle_speed_percent=" + IdleSpeedPercent.ToString(CultureInfo.InvariantCulture),
                "blink_speed_percent=" + BlinkSpeedPercent.ToString(CultureInfo.InvariantCulture),
                "engine_warning_percent=" + EngineWarningPercent.ToString(CultureInfo.InvariantCulture),
                "opacity_percent=" + OpacityPercent.ToString(CultureInfo.InvariantCulture) });
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed class BoostSettingsWindow : Form
{
    readonly NumericUpDown[] rates = new NumericUpDown[4], boosts = new NumericUpDown[3];
    readonly NumericUpDown idleStrength = new NumericUpDown(), blurStrength = new NumericUpDown();
    readonly NumericUpDown idleSpeed = new NumericUpDown(), blinkSpeed = new NumericUpDown();
    readonly NumericUpDown warningChance = new NumericUpDown(), petOpacity = new NumericUpDown();
    readonly Label error = new Label();
    readonly Action<BoostCurve> apply;
    internal BoostSettingsWindow(BoostCurve curve, Action<BoostCurve> apply)
    {
        this.apply = apply;
        Text = "설정 · 꼬마 G70"; Font = new Font("맑은 고딕", 10F);
        ClientSize = new Size(550, 656); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Label title = new Label { Text = "부스트 · 움직임 · 표시를 설정하세요.", AutoSize = false, Bounds = new Rectangle(22, 18, 510, 27) };
        Controls.Add(title);
        TableLayoutPanel table = new TableLayoutPanel { Location = new Point(22, 54), Size = new Size(506, 176), ColumnCount = 3, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        for (int i = 0; i < 5; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 0 ? 28 : 37));
        foreach (string heading in new[] { "구간", "타/분", "부스트 (bar)" }) table.Controls.Add(CellLabel(heading));
        string[] labels = { "부스트 시작", "중간", "유지", "고속 (기준 초과)" };
        for (int i = 0; i < 4; i++)
        {
            table.Controls.Add(CellLabel(labels[i]), 0, i + 1);
            rates[i] = new NumericUpDown { Minimum = 1, Maximum = 1199, Increment = 10, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 15, 5), AccessibleName = labels[i] + " 타자수" };
            table.Controls.Add(rates[i], 1, i + 1);
            if (i == 0) table.Controls.Add(CellLabel("0.00"), 2, i + 1);
            else
            {
                boosts[i - 1] = new NumericUpDown { Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = .01M, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 5), AccessibleName = labels[i] + " 부스트압" };
                table.Controls.Add(boosts[i - 1], 2, i + 1);
            }
        }
        Controls.Add(table);
        Controls.Add(new Label { Bounds = new Rectangle(22, 242, 510, 60), Text = "시작 전: 음압 → 0 bar · 시작~중간~유지: 타수에 비례\n유지~고속 기준: 유지 압력 · 고속 기준 초과: 고속 압력\n입력 중단: −0.62 bar · 설정 가능한 최고 압력: 2.00 bar" });
        Controls.Add(new Label { Text = "움직임 · 경고 · 표시", Bounds = new Rectangle(22, 306, 240, 25) });
        AddPercent("대기 중 들썩임 강도", idleStrength, "IdleStrength", 334);
        AddPercent("들썩임 속도", idleSpeed, "IdleSpeed", 364, 25);
        AddPercent("눈 깜박임 속도", blinkSpeed, "BlinkSpeed", 394, 25);
        AddPercent("차량 모션블러", blurStrength, "BlurStrength", 424);
        AddPercent("엔진경고등 기본 확률", warningChance, "WarningChance", 454);
        warningChance.Maximum = 100; warningChance.Increment = 1;
        AddPercent("미니펫 불투명도", petOpacity, "PetOpacity", 484, 1);
        petOpacity.Maximum = 100; petOpacity.Increment = 1;
        Controls.Add(new Label { Text = "부스트 연동", Bounds = new Rectangle(387, 424, 140, 28), TextAlign = ContentAlignment.MiddleLeft });
        Controls.Add(new Label { Text = "간격·동작 함께", Bounds = new Rectangle(387, 394, 140, 28), TextAlign = ContentAlignment.MiddleLeft });
        Controls.Add(new Label { Text = "초기값 0%", Bounds = new Rectangle(387, 454, 140, 28), TextAlign = ContentAlignment.MiddleLeft });
        Controls.Add(new Label { Text = "100% 선명", Bounds = new Rectangle(387, 484, 140, 28), TextAlign = ContentAlignment.MiddleLeft });
        Controls.Add(new Label { Text = "강도: 0% 끄기 · 100% 기본 · 200% 강하게\n속도: 50% 느리게 · 100% 기본 · 200% 빠르게", Bounds = new Rectangle(22, 516, 510, 44) });
        error.SetBounds(22, 566, 510, 36); error.ForeColor = Color.Firebrick; Controls.Add(error);
        Button defaults = new Button { Text = "기본값 복원", Bounds = new Rectangle(22, 608, 126, 32) };
        Button save = new Button { Text = "저장", Bounds = new Rectangle(338, 608, 90, 32) };
        Button cancel = new Button { Text = "취소", Bounds = new Rectangle(438, 608, 90, 32), DialogResult = DialogResult.Cancel };
        defaults.Click += delegate { SetValues(new BoostCurve()); error.Text = "기본값을 불러왔어요. 저장하면 적용됩니다."; };
        save.Click += delegate { SaveValues(); };
        Controls.AddRange(new Control[] { defaults, save, cancel }); AcceptButton = save; CancelButton = cancel;
        SetValues(curve);
    }
    void AddPercent(string label, NumericUpDown input, string name, int y, int minimum = 0)
    {
        Controls.Add(new Label { Text = label, Bounds = new Rectangle(22, y, 210, 28), TextAlign = ContentAlignment.MiddleLeft });
        input.Name = name; input.AccessibleName = label; input.Minimum = minimum; input.Maximum = 200; input.Increment = minimum == 0 ? 10 : 25;
        input.SetBounds(236, y, 108, 28); Controls.Add(input);
        Controls.Add(new Label { Text = "%", Bounds = new Rectangle(352, y, 30, 28), TextAlign = ContentAlignment.MiddleLeft });
    }
    static Label CellLabel(string text) { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }; }
    internal void SetValues(BoostCurve curve)
    {
        int[] cpm = { curve.StartRate, curve.MidRate, curve.HoldRate, curve.HighRate };
        double[] bar = { curve.MidBoost, curve.HoldBoost, curve.MaxBoost };
        for (int i = 0; i < 4; i++) rates[i].Value = cpm[i];
        for (int i = 0; i < 3; i++) boosts[i].Value = (decimal)bar[i];
        idleStrength.Value = curve.IdleMotionPercent; blurStrength.Value = curve.MotionBlurPercent;
        idleSpeed.Value = curve.IdleSpeedPercent; blinkSpeed.Value = curve.BlinkSpeedPercent;
        warningChance.Value = curve.EngineWarningPercent;
        petOpacity.Value = curve.OpacityPercent;
    }
    internal BoostCurve ReadValues()
    {
        // Commit typed NumericUpDown text before reading, including an unblurred final field.
        ValidateChildren();
        return new BoostCurve { StartRate = (int)rates[0].Value, MidRate = (int)rates[1].Value, HoldRate = (int)rates[2].Value, HighRate = (int)rates[3].Value,
            MidBoost = (double)boosts[0].Value, HoldBoost = (double)boosts[1].Value, MaxBoost = (double)boosts[2].Value,
            IdleMotionPercent = (int)idleStrength.Value, MotionBlurPercent = (int)blurStrength.Value,
            IdleSpeedPercent = (int)idleSpeed.Value, BlinkSpeedPercent = (int)blinkSpeed.Value,
            EngineWarningPercent = (int)warningChance.Value, OpacityPercent = (int)petOpacity.Value };
    }
    internal bool SaveValues()
    {
        BoostCurve value = ReadValues(); error.Text = value.Validate(); if (!string.IsNullOrEmpty(error.Text)) return false;
        try { apply(value); DialogResult = DialogResult.OK; Close(); return true; }
        catch (IOException) { error.Text = "설정을 저장하지 못했어요. 폴더에 쓰기 권한이 있는지 확인하세요."; }
        catch (UnauthorizedAccessException) { error.Text = "설정을 저장하지 못했어요. 폴더에 쓰기 권한이 있는지 확인하세요."; }
        return false;
    }
}

internal sealed class Motion
{
    internal const double Idle = -.62, Tau = Math.PI * 2;
    static readonly Random releaseRandom = new Random();
    readonly Func<double> releaseRoll;
    internal readonly List<double> Strokes = new List<double>();
    internal double LastStroke = double.NegativeInfinity, Interval = 400;
    internal double Pressure = Idle, PressureAtLastStroke = Idle, Rate, Speed, Angle, Distance, Shift;
    internal double ReleaseAt = double.NegativeInfinity, FlutterUntil = double.NegativeInfinity;
    internal double LastTime = double.NaN;
    internal double RestAge, IdleLift;
    internal double ExtraWarningPercent;
    bool wasOverBoost;
    internal bool Typing, EngineWarning;
    internal BoostCurve Curve;
    internal Motion() : this(new BoostCurve()) { }
    internal Motion(BoostCurve curve) : this(curve, delegate { return releaseRandom.NextDouble() * 100; }) { }
    internal Motion(BoostCurve curve, Func<double> releaseRoll) { Curve = curve; this.releaseRoll = releaseRoll; }
    internal static double Clamp(double n, double low, double high) { return Math.Max(low, Math.Min(high, n)); }
    internal bool Led { get { return Math.Floor(Pressure * 100 + .5) >= 150; } }
    internal double WarningChance { get { return Math.Min(100, Curve.EngineWarningPercent + ExtraWarningPercent); } }
    internal int WarningFace(double now)
    {
        if (!EngineWarning || Typing) return -1;
        return (int)Clamp((now - ReleaseAt) / 125, 0, 7);
    }
    internal float WarningTwitch(double now)
    {
        if (!EngineWarning || Typing) return 0;
        double elapsed = double.IsNegativeInfinity(ReleaseAt) ? now : Math.Max(0, now - ReleaseAt);
        int phase = (int)(elapsed % 1200 / 300);
        return phase == 0 ? -9 : phase == 1 ? 9 : phase == 2 ? -6 : 6;
    }
    internal void Stroke(double now)
    {
        if (EngineWarning) ExtraWarningPercent = 0;
        if (now - LastStroke > Clamp(Interval * 2.25, 380, 1100)) Strokes.Clear();
        Strokes.Add(now);
        while (Strokes.Count > 9 || (Strokes.Count > 0 && now - Strokes[0] > 3200)) Strokes.RemoveAt(0);
        LastStroke = now; PressureAtLastStroke = Pressure;
        if (Strokes.Count >= 2) Interval = Math.Max(45, (now - Strokes[0]) / (Strokes.Count - 1));
        Typing = true; FlutterUntil = double.NegativeInfinity; EngineWarning = false;
    }
    internal void Update(double now)
    {
        double dt = double.IsNaN(LastTime) ? 0 : Clamp((now - LastTime) / 1000, 0, .05);
        LastTime = now;
        double elapsed = now - LastStroke;
        if (Typing && elapsed >= Clamp(Interval * 2.25, 380, 1100))
        {
            Typing = false; ReleaseAt = now;
            if (PressureAtLastStroke >= 0 || Pressure >= 0)
            {
                FlutterUntil = now + 1000;
                // One independent draw per eligible lift-off, never once per frame.
                EngineWarning = releaseRoll() < WarningChance;
            }
        }
        Rate = Typing && Strokes.Count >= 2
            ? Math.Floor(Math.Min(1200, 60000 / ((LastStroke - Strokes[0] + Math.Max(Interval, elapsed - 30)) / Strokes.Count)) + .5)
            : 0;
        double target = Typing ? Curve.Target(Rate) : Idle;
        double response = !Typing ? .14 : target < Pressure ? .25 : .72;
        Pressure += (target - Pressure) * (1 - Math.Exp(-dt / response));
        if (Typing && Rate < Curve.StartRate) Pressure = Math.Min(0, Pressure);
        Pressure = Clamp(Pressure, Idle, Curve.MaxBoost);
        if (Math.Abs(target - Pressure) < .0001) Pressure = target;
        // Count only upward threshold crossings, not every frame held over 1.5 bar.
        bool overBoost = Pressure > 1.5;
        if (overBoost && !wasOverBoost) ExtraWarningPercent = Math.Min(100, ExtraWarningPercent + 2.5);
        wasOverBoost = overBoost;
        double speedTarget = Typing && Rate > 0 ? (.65 + Math.Min(Rate, 800) / 800 * 5.7) * 2.15 : 0;
        Speed += (speedTarget - Speed) * (1 - Math.Exp(-dt / .25));
        if (Speed < .005 && !Typing) Speed = 0;
        Angle = (Angle + Speed * dt) % Tau;
        Distance = (Distance + Speed * dt * 55) % 100000;
        Shift += ((Typing ? Math.Min(Rate / 700, 1) * 14 : 0) - Shift) * (1 - Math.Exp(-dt / .30));
        if (!Typing && Shift < .0001) Shift = 0;
        // Gentle suspension motion belongs to the body, never to the tire geometry.
        bool resting = !Typing && Speed == 0 && FlutterUntil <= now && Curve.IdleMotionPercent > 0;
        // Accumulate animation time so a live speed change cannot jump the phase.
        double idleSpeed = Curve.IdleSpeedPercent / 100.0;
        RestAge = resting ? RestAge + dt * idleSpeed : 0;
        double restTarget = resting ? -16 * (Curve.IdleMotionPercent / 100.0) * (.5 - .5 * Math.Cos(RestAge * Tau / 3.8)) : 0;
        IdleLift += (restTarget - IdleLift) * (1 - Math.Exp(-dt * (resting ? idleSpeed : 1) / .18));
        if (!resting && Math.Abs(IdleLift) < .001) IdleLift = 0;
    }
}

internal static class IdleFace
{
    // The eyes wake immediately on a keystroke. This clock never drives boost or wheels.
    internal static int Frame(Motion m, double now)
    {
        if (m.Typing || m.Speed > .02 || m.FlutterUntil > now || m.EngineWarning) return -1;
        double elapsed = double.IsNegativeInfinity(m.LastStroke) ? now : now - m.LastStroke;
        double phase = Math.Max(0, elapsed) % 14000;
        double blinkPhase = Math.Max(0, elapsed) * m.Curve.BlinkSpeedPercent / 100.0 % 14000;
        double blink = blinkPhase >= 10800 ? blinkPhase - 10800 : blinkPhase - 3200;
        if (blink >= 0 && blink < 340) return blink < 90 ? 2 : blink < 200 ? 3 : 4;
        if (phase >= 7000 && phase < 9600) return 6 + Math.Min(5, (int)((phase - 7000) / (2600.0 / 6)));
        return -1;
    }
}

internal sealed class Renderer : IDisposable
{
    readonly Bitmap car, dial, hub, led, flutter;
    readonly Bitmap[][] wheels;
    readonly Bitmap[] faces, warningFaces;
    readonly Dictionary<Bitmap, Bitmap> cache = new Dictionary<Bitmap, Bitmap>();
    readonly GraphicsPath warningOutline = CreateWarningOutline(), warningCode = CreateWarningCode();
    Bitmap vehicleLayer;
    byte[] blurSource, blurResult;
    int cachedSize;
    internal const double FrontCamber = -1.5, RearCamber = -3.0;
    internal const float BodyLowering = 16f;
    // Convert the axle tilt into this illustration's perspective. Opposite-side
    // wheels lean toward the opposite screen direction. Contact patches stay put.
    internal static readonly float[] CamberShear = {
        (float)(-Math.Tan(RearCamber * Math.PI / 180) * (43.0 / 255) * (241.3 / 80.221)),
        (float)(-Math.Tan(FrontCamber * Math.PI / 180) * (55.0 / 255) * (241.3 / 91.547)),
        (float)(Math.Tan(FrontCamber * Math.PI / 180) * (55.0 / 255) * (241.3 / 91.547)) };
    internal static readonly float[] WheelGround = { 957, 1017, 1007 };
    static readonly PointF[] NeedlePoints = { new PointF(18, -6), new PointF(165, -1.4f), new PointF(165, 1.4f), new PointF(18, 6) };
    static readonly int[,] WindLines = { { 82, 708, 32 }, { 77, 739, 46 }, { 68, 811, 26 }, { 909, 821, 32 }, { 916, 861, 43 } };
    internal Renderer()
    {
        car = Load("car");
        // The source split left the last eight gauge rows in the vehicle layer.
        // Rejoin the original pixels once, before scaling or vehicle animation.
        Rectangle gaugeFooter = new Rectangle(840, 624, 144, 8);
        using (Bitmap face = Load("dial"))
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("car.png"))
        using (Bitmap source = new Bitmap(stream))
        {
            dial = new Bitmap(face.Width, gaugeFooter.Bottom - 170, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dial))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(face, 0, 0);
                g.DrawImage(source, new Rectangle(gaugeFooter.X - 675, face.Height, gaugeFooter.Width, gaugeFooter.Height),
                    gaugeFooter.X, gaugeFooter.Y, gaugeFooter.Width, gaugeFooter.Height, GraphicsUnit.Pixel);
            }
        }
        using (Graphics g = Graphics.FromImage(car))
        {
            // Include the extra filtering row produced by the vehicle PNG's DPI.
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillRectangle(Brushes.Transparent, gaugeFooter.X, gaugeFooter.Y, gaugeFooter.Width, gaugeFooter.Height + 2);
        }
        hub = Load("hub"); led = Load("led"); flutter = Load("flutter");
        warningFaces = Frames("warning-faces", 528, 64, 8, 8);
        wheels = new[] { Frames("rear", 132, 209), Frames("front", 154, 239), Frames("far", 160, 56) };
        faces = Frames("faces", 528, 64, 12, 12);
    }
    static Bitmap Load(string name)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".png"))
        using (Bitmap raw = new Bitmap(stream))
        {
            Bitmap converted = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(converted)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(raw, 0, 0); }
            return converted;
        }
    }
    static Bitmap[] Frames(string name, int width, int height, int count = 128, int columns = 16)
    {
        Bitmap[] frames = new Bitmap[count];
        using (Bitmap atlas = Load(name)) for (int i = 0; i < frames.Length; i++)
            frames[i] = atlas.Clone(new Rectangle(i % columns * width, i / columns * height, width, height), PixelFormat.Format32bppPArgb);
        return frames;
    }
    Bitmap Scaled(Bitmap b)
    {
        Bitmap small;
        if (!cache.TryGetValue(b, out small))
        {
            small = new Bitmap(Math.Max(1, (int)Math.Ceiling(b.Width * cachedSize / 1200.0)), Math.Max(1, (int)Math.Ceiling(b.Height * cachedSize / 1200.0)), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(small))
            using (ImageAttributes edges = new ImageAttributes())
            {
                edges.SetWrapMode(WrapMode.TileFlipXY);
                g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(b, new Rectangle(0, 0, small.Width, small.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, edges);
            }
            cache.Add(b, small);
        }
        return small;
    }
    void ImageAt(Graphics g, Bitmap b, float x, float y)
    {
        // Pixel rectangles avoid image DPI changing the approved geometry.
        Bitmap small = Scaled(b);
        g.DrawImage(small, new RectangleF(x, y, b.Width, b.Height), new RectangleF(0, 0, small.Width, small.Height), GraphicsUnit.Pixel);
    }
    void WheelAt(Graphics g, Bitmap wheel, int x, int y, int index)
    {
        GraphicsState saved = g.Save();
        float shear = CamberShear[index];
        using (Matrix camber = new Matrix(1, 0, -shear, 1, shear * WheelGround[index], 0))
        using (ImageAttributes edges = new ImageAttributes())
        {
            g.MultiplyTransform(camber);
            // Apply size, camber and vehicle pose in one sampling pass. A wheel
            // must not be downscaled into the cache and then filtered a second time.
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            edges.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(wheel, new Rectangle(x, y, wheel.Width, wheel.Height),
                0, 0, wheel.Width, wheel.Height, GraphicsUnit.Pixel, edges);
        }
        g.Restore(saved);
    }
    internal void Draw(Graphics g, Motion m, double now, int size)
    {
        if (cachedSize != size)
        {
            foreach (Bitmap b in cache.Values) b.Dispose(); cache.Clear(); cachedSize = size;
            if (vehicleLayer != null) { vehicleLayer.Dispose(); vehicleLayer = null; }
            blurSource = blurResult = null;
        }
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.Default;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        GraphicsState root = g.Save(); g.ScaleTransform(size / 1200f, size / 1200f);
        Flow(g, m, false);
        GraphicsState wind = g.Save(); VehiclePose(g, m); g.TranslateTransform(0, BodyLowering); Flow(g, m, true); g.Restore(wind);
        double setting = m.Curve.MotionBlurPercent / 100.0;
        double radius = 3 * Motion.Clamp(m.Pressure / 2, 0, 1) * setting * setting * Motion.Clamp(m.Speed / 10, 0, 1) * size / 320.0;
        if (radius > 0)
        {
            if (vehicleLayer == null) vehicleLayer = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics vehicle = Graphics.FromImage(vehicleLayer))
            {
                vehicle.Clear(Color.Transparent); vehicle.CompositingMode = CompositingMode.SourceOver;
                vehicle.CompositingQuality = CompositingQuality.Default; vehicle.SmoothingMode = SmoothingMode.AntiAlias;
                vehicle.InterpolationMode = InterpolationMode.Bilinear; vehicle.PixelOffsetMode = PixelOffsetMode.HighQuality;
                vehicle.ScaleTransform(size / 1200f, size / 1200f);
                // Rear body and roof supply the trail; the original car stays sharp.
                vehicle.SetClip(new Rectangle(0, 500, 450, 500));
                vehicle.SetClip(new Rectangle(0, 488, 690, 152), CombineMode.Union);
                VehiclePose(vehicle, m); vehicle.TranslateTransform(0, (float)m.IdleLift + BodyLowering);
                ImageAt(vehicle, car, 0, 0);
            }
            BlurRearOutline(radius);
            using (Graphics wheelMask = Graphics.FromImage(vehicleLayer))
            {
                wheelMask.CompositingMode = CompositingMode.SourceCopy;
                wheelMask.ScaleTransform(size / 1200f, size / 1200f); VehiclePose(wheelMask, m);
                // Protect the tires and the space immediately behind them so body
                // cutout edges cannot appear as detached wheel-shaped streaks.
                wheelMask.FillRectangle(Brushes.Transparent, -60, 740, 252, 226);
                wheelMask.FillRectangle(Brushes.Transparent, 185, 773, 211, 251);
            }
            // Draw the faint exterior trail behind the untouched, sharp vehicle.
            GraphicsState pixels = g.Save(); g.ResetTransform(); g.DrawImageUnscaled(vehicleLayer, 0, 0); g.Restore(pixels);
        }
        DrawVehicle(g, m, now);
        ImageAt(g, dial, 675, 170);
        GraphicsState needle = g.Save(); g.TranslateTransform(915, 409);
        g.RotateTransform((float)(181.8 + m.Pressure * 89.1));
        using (Brush red = new SolidBrush(Color.FromArgb(255, 33, 8)))
            g.FillPolygon(red, NeedlePoints);
        g.Restore(needle); ImageAt(g, hub, 886, 380);
        if (m.Led) ImageAt(g, led, 984, 398);
        double remaining = m.FlutterUntil - now;
        if (m.EngineWarning || remaining > 0)
        {
            double age = m.EngineWarning ? (now - m.ReleaseAt) / 1000 : (1000 - remaining) / 1000;
            float alpha = (float)Math.Max(0, Math.Min(1, m.EngineWarning ? age / .06 : Math.Min(remaining / 180, age / .06)));
            if (m.EngineWarning) DrawEngineWarning(g, alpha);
            else using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = alpha; attributes.SetColorMatrix(matrix);
                Bitmap small = Scaled(flutter);
                Rectangle destination = new Rectangle(50, 290, 600, 170);
                g.DrawImage(small, destination, 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, attributes);
            }
            if (m.EngineWarning)
            {
                using (Pen outline = new Pen(Color.FromArgb((int)(255 * alpha), 25, 20, 10), 6) { LineJoin = LineJoin.Round })
                using (Brush amber = new SolidBrush(Color.FromArgb((int)(255 * alpha), 255, 198, 38)))
                {
                    g.DrawPath(outline, warningCode); g.FillPath(amber, warningCode);
                }
            }
        }
        g.Restore(root);
    }
    void DrawEngineWarning(Graphics g, float alpha)
    {
        GraphicsState icon = g.Save(); g.TranslateTransform(245, 270);
        using (Pen shadow = new Pen(Color.FromArgb((int)(210 * alpha), 24, 20, 8), 12) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (Pen amber = new Pen(Color.FromArgb((int)(255 * alpha), 255, 190, 28), 7.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawPath(shadow, warningOutline); g.DrawPath(amber, warningOutline);
        }
        g.Restore(icon);
    }
    static GraphicsPath CreateWarningCode()
    {
        GraphicsPath code = new GraphicsPath();
        using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            code.AddString("P0234", FontFamily.GenericSansSerif, (int)FontStyle.Bold, 56, new Rectangle(220, 438, 275, 70), format);
        return code;
    }
    static GraphicsPath CreateWarningOutline()
    {
        GraphicsPath outline = new GraphicsPath();
        outline.AddPolygon(new[] { new PointF(50, 48), new PointF(72, 48), new PointF(88, 32), new PointF(137, 32),
                new PointF(155, 50), new PointF(155, 63), new PointF(179, 63), new PointF(179, 81), new PointF(205, 81),
                new PointF(205, 132), new PointF(178, 132), new PointF(159, 149), new PointF(68, 149), new PointF(48, 129),
                new PointF(48, 112), new PointF(31, 112), new PointF(31, 73), new PointF(50, 73) });
        outline.StartFigure(); outline.AddLine(82, 14, 141, 14);
        outline.StartFigure(); outline.AddLine(112, 14, 112, 32);
        outline.StartFigure(); outline.AddLine(13, 68, 13, 118);
        outline.StartFigure(); outline.AddLine(13, 92, 31, 92);
        return outline;
    }
    static void VehiclePose(Graphics g, Motion m)
    {
        g.TranslateTransform((float)(430 + m.Shift), 955);
        g.RotateTransform((float)(-m.Shift * .00055 * 180 / Math.PI));
        g.TranslateTransform(-430, -955);
    }
    void DrawVehicle(Graphics g, Motion m, double now)
    {
        GraphicsState body = g.Save(); VehiclePose(g, m);
        int frame = ((int)Math.Floor(m.Angle / Motion.Tau * 128 + .5)) % 128;
        WheelAt(g, wheels[0][frame], 53, 751, 0); WheelAt(g, wheels[1][frame], 233, 779, 1); WheelAt(g, wheels[2][frame], 710, 952, 2);
        // The lowered fenders cover the upper tires; tires never paint over bodywork.
        GraphicsState suspension = g.Save(); g.TranslateTransform(0, (float)m.IdleLift + BodyLowering);
        ImageAt(g, car, 0, 0);
        int warningFace = m.WarningFace(now), face = IdleFace.Frame(m, now);
        if (warningFace >= 0) { ImageAt(g, warningFaces[warningFace], 326, 738); DrawStartledEyes(g, warningFace, m.WarningTwitch(now)); }
        else if (face >= 0) ImageAt(g, faces[face], 326, 738);
        g.Restore(suspension);
        g.Restore(body);
    }
    static void DrawStartledEyes(Graphics g, int frame, float twitch)
    {
        // Preserve the approved lamp outlines; a wider white area and small pupils
        // communicate surprise without changing the vehicle's body or engine bay.
        for (int side = 0; side < 2; side++)
        {
            using (GraphicsPath lamp = new GraphicsPath())
            {
                if (side == 0)
                {
                    lamp.AddLine(329, 744, 449, 787);
                    lamp.AddBezier(449, 787, 432, 795, 382, 796, 348, 790);
                    lamp.AddBezier(348, 790, 337, 785, 332, 770, 329, 744);
                }
                else
                {
                    lamp.AddLine(788, 778, 843, 749);
                    lamp.AddBezier(843, 749, 848, 756, 849, 774, 837, 786);
                    lamp.AddLine(837, 786, 795, 791);
                }
                lamp.CloseFigure();
                GraphicsState clip = g.Save(); g.SetClip(lamp, CombineMode.Intersect);
                Rectangle bounds = side == 0 ? new Rectangle(328, 743, 123, 56) : new Rectangle(787, 748, 64, 47);
                using (Brush white = new LinearGradientBrush(bounds, Color.FromArgb(252, 252, 248), Color.FromArgb(223, 226, 224), LinearGradientMode.Vertical))
                    g.FillPath(white, lamp);
                float x = (side == 0 ? 390 : 817) + twitch * (side == 0 ? 1 : .65f), y = side == 0 ? 777 : 773;
                float radius = side == 0 ? 8.5f : 6.5f;
                using (Brush iris = new SolidBrush(Color.FromArgb(139, 91, 28)))
                using (Pen rim = new Pen(Color.FromArgb(62, 39, 14), 1.4f))
                using (Brush pupil = new SolidBrush(Color.FromArgb(19, 20, 21)))
                {
                    g.FillEllipse(iris, x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(rim, x - radius, y - radius, radius * 2, radius * 2);
                    g.FillEllipse(pupil, x - radius * .52f, y - radius * .65f, radius * 1.04f, radius * 1.3f);
                    g.FillEllipse(Brushes.White, x - radius * .45f, y - radius * .6f, radius * .48f, radius * .48f);
                }
                g.Restore(clip);
            }
        }
        // Enlarge the reaction-only bead while retaining its lower lamp-side anchor.
        GraphicsState sweat = g.Save();
        g.TranslateTransform(323, 696 + Math.Min(frame, 3) * 3); g.ScaleTransform(1.8f, 1.8f);
        float dropX = 0, dropY = 0;
        using (GraphicsPath drop = new GraphicsPath())
        using (Brush blue = new SolidBrush(Color.FromArgb(104, 207, 244)))
        using (Pen edge = new Pen(Color.FromArgb(42, 99, 131), 2.2f) { LineJoin = LineJoin.Round })
        {
            drop.AddBezier(dropX, dropY, dropX + 2, dropY + 6, dropX + 9, dropY + 13, dropX + 8, dropY + 19);
            drop.AddBezier(dropX + 8, dropY + 19, dropX + 6, dropY + 27, dropX - 8, dropY + 24, dropX - 8, dropY + 17);
            drop.AddBezier(dropX - 8, dropY + 17, dropX - 8, dropY + 11, dropX - 2, dropY + 5, dropX, dropY);
            drop.CloseFigure(); g.FillPath(blue, drop); g.DrawPath(edge, drop);
            g.FillEllipse(Brushes.White, dropX - 4, dropY + 13, 3.5f, 6);
        }
        g.Restore(sweat);
    }
    void BlurRearOutline(double radius)
    {
        // Keep only a soft trail outside the original rear-body alpha. The actual
        // vehicle is never filtered; vertical feathering avoids a rectangular cut.
        BitmapData pixels = vehicleLayer.LockBits(new Rectangle(0, 0, cachedSize, cachedSize), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int length = pixels.Stride * cachedSize;
            if (blurSource == null || blurSource.Length != length) { blurSource = new byte[length]; blurResult = new byte[length]; }
            Marshal.Copy(pixels.Scan0, blurSource, 0, length);
            int whole = (int)radius; double fraction = radius - whole, weight = whole + 1 + fraction;
            // The clipped source is empty outside this fixed region. Leave the
            // zero-initialized result outside it untouched instead of filtering it.
            int top = (int)Math.Floor(488 * cachedSize / 1200.0);
            int bottom = Math.Min(cachedSize, (int)Math.Ceiling(1000 * cachedSize / 1200.0));
            for (int y = top; y < bottom; y++)
            {
                int row = y * pixels.Stride;
                double b = 0, green = 0, r = 0, a = 0;
                double sourceY = y * 1200.0 / cachedSize;
                int right = Math.Min(cachedSize - 1, (int)Math.Ceiling((sourceY < 640 ? 690 : 450) * cachedSize / 1200.0));
                double fade = .55 * Motion.Clamp((sourceY - 488) / 24, 0, 1) * Motion.Clamp((1000 - sourceY) / 24, 0, 1);
                if (fade <= 0) { Array.Clear(blurResult, row, pixels.Stride); continue; }
                // A very small upward component lets the rearward trail follow
                // the whole roof/sunroof silhouette. Blend fractional rows to
                // avoid a stepping edge as boost or the blur setting changes.
                double rise = radius * .12 * Motion.Clamp((640 - sourceY) / 80, 0, 1);
                int riseWhole = (int)rise; double mix = rise - riseWhole, keep = 1 - mix;
                int row0 = Math.Min(cachedSize - 1, y + riseWhole) * pixels.Stride;
                int row1 = Math.Min(cachedSize - 1, y + riseWhole + 1) * pixels.Stride;
                for (int x = right; x >= 0; x--)
                {
                    int p = row + x * 4, p0 = row0 + x * 4, p1 = row1 + x * 4;
                    int tailX = x + whole + 1, tail0 = row0 + tailX * 4, tail1 = row1 + tailX * 4;
                    b += blurSource[p0] * keep + blurSource[p1] * mix;
                    green += blurSource[p0 + 1] * keep + blurSource[p1 + 1] * mix;
                    r += blurSource[p0 + 2] * keep + blurSource[p1 + 2] * mix;
                    a += blurSource[p0 + 3] * keep + blurSource[p1 + 3] * mix;
                    bool inside = tailX < cachedSize;
                    double tb = 0, tg = 0, tr = 0, ta = 0;
                    if (inside)
                    {
                        tb = blurSource[tail0] * keep + blurSource[tail1] * mix;
                        tg = blurSource[tail0 + 1] * keep + blurSource[tail1 + 1] * mix;
                        tr = blurSource[tail0 + 2] * keep + blurSource[tail1 + 2] * mix;
                        ta = blurSource[tail0 + 3] * keep + blurSource[tail1 + 3] * mix;
                        b -= tb; green -= tg; r -= tr; a -= ta;
                    }
                    double exterior = fade * (1 - blurSource[p + 3] / 255.0) / weight;
                    blurResult[p] = (byte)((b + tb * fraction) * exterior + .5);
                    blurResult[p + 1] = (byte)((green + tg * fraction) * exterior + .5);
                    blurResult[p + 2] = (byte)((r + tr * fraction) * exterior + .5);
                    blurResult[p + 3] = (byte)((a + ta * fraction) * exterior + .5);
                }
            }
            Marshal.Copy(blurResult, 0, pixels.Scan0, length);
        }
        finally { vehicleLayer.UnlockBits(pixels); }
    }
    static void Flow(Graphics g, Motion m, bool wind)
    {
        double strength = Motion.Clamp(m.Speed / 10, 0, 1);
        if (strength < .025) return;
        // Wind and ground remain crisp and independent of the vehicle blur setting.
        if (!wind)
        {
            for (int i = 0; i < 5; i++)
            {
                double phase = ((m.Distance * .9 + i * 151) % 790) / 790;
                float x = (float)(870 - phase * 790), y = 1034 + i % 3 * 7;
                using (Pen p = new Pen(Color.FromArgb((int)(255 * strength * Math.Sin(phase * Math.PI) * .40), 172, 193, 209), 1.6f))
                { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, x, y, x - (26 + i % 3 * 11), y + 2); }
            }
        }
        else
        {
            for (int i = 0; i < 5; i++)
            {
                double phase = ((m.Distance * .65 + i * 31) % 96) / 96;
                float x = (float)(WindLines[i, 0] - phase * 42), y = WindLines[i, 1], len = WindLines[i, 2];
                using (Pen p = new Pen(Color.FromArgb((int)(255 * strength * Math.Sin(phase * Math.PI) * .53), 204, 222, 234), 3.8f))
                { p.StartCap = p.EndCap = LineCap.Round; g.DrawBezier(p, x, y, x + len / 3, y - 2, x + len * 2 / 3, y - 2.33f, x + len, y - 1); }
            }
        }
    }
    public void Dispose()
    {
        foreach (Bitmap b in cache.Values) b.Dispose(); cache.Clear();
        foreach (Bitmap b in new[] { car, dial, hub, led, flutter }) b.Dispose();
        foreach (Bitmap[] row in wheels) foreach (Bitmap b in row) b.Dispose();
        foreach (Bitmap b in faces) b.Dispose();
        foreach (Bitmap b in warningFaces) b.Dispose();
        warningOutline.Dispose(); warningCode.Dispose();
        if (vehicleLayer != null) vehicleLayer.Dispose();
    }
}

internal static class Native
{
    internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int Width, Height; internal Size(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapHeader { internal int Size, Width, Height; internal short Planes, BitCount; internal int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dc, ref Point dest, ref Size size, IntPtr sourceDc, ref Point origin, int key, ref Blend blend, int flags);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

internal sealed class Layer : IDisposable
{
    readonly Rectangle viewport;
    readonly IntPtr dc, dib, previous;
    readonly Bitmap bitmap;
    internal readonly Graphics Graphics;
    internal static Rectangle Viewport(int size)
    {
        // Keep the original drawing scale; trim only its empty outer canvas.
        // These bounds include wheel rotation, wind, blur and both notifications.
        const int padding = 6;
        int left = Math.Max(0, (int)Math.Floor(17 * size / 1200.0) - 2);
        int top = Math.Max(0, (int)Math.Floor(178 * size / 1200.0) - padding);
        int right = Math.Min(size, (int)Math.Ceiling(1142 * size / 1200.0) + padding);
        int bottom = Math.Min(size, (int)Math.Ceiling(1054 * size / 1200.0) + 3);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
    internal Layer(int size)
    {
        viewport = Viewport(size); dc = Native.CreateCompatibleDC(IntPtr.Zero);
        Native.BitmapHeader info = new Native.BitmapHeader();
        info.Size = Marshal.SizeOf(info); info.Width = size; info.Height = -size; info.Planes = 1; info.BitCount = 32;
        IntPtr bits; dib = Native.CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        previous = Native.SelectObject(dc, dib);
        bitmap = new Bitmap(size, size, size * 4, PixelFormat.Format32bppPArgb, bits);
        Graphics = Graphics.FromImage(bitmap);
    }
    internal static byte OpacityAlpha(int percent)
    {
        return (byte)((Math.Max(1, Math.Min(100, percent)) * 255 + 50) / 100);
    }
    internal void Present(Form window, int opacityPercent)
    {
        Graphics.Flush(FlushIntention.Sync);
        Native.Point dest = new Native.Point(window.Left, window.Top), origin = new Native.Point(viewport.Left, viewport.Top);
        Native.Size dimensions = new Native.Size(viewport.Width, viewport.Height);
        Native.Blend blend = new Native.Blend(); blend.Alpha = OpacityAlpha(opacityPercent); blend.Format = 1;
        if (!Native.UpdateLayeredWindow(window.Handle, IntPtr.Zero, ref dest, ref dimensions, dc, ref origin, 0, ref blend, 2))
            throw new System.ComponentModel.Win32Exception();
    }
    public void Dispose()
    {
        Graphics.Dispose(); bitmap.Dispose(); Native.SelectObject(dc, previous); Native.DeleteObject(dib); Native.DeleteDC(dc);
    }
}

internal sealed class PetWindow : Form
{
    readonly Renderer renderer = new Renderer();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly Native.HookProc hookProc;
    Motion motion = new Motion();
    Layer layer;
    IntPtr hook;
    int petSize = 320;
    bool paused, dirty = true;
    int lastFace = -1;
    readonly string preferences = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "source", "window.txt");
    readonly string boostPreferences = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "source", "boost.txt");
    BoostSettingsWindow settingsWindow;
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80000 | 0x8000000 | 0x80; return p; }
    }
    internal static bool IsTypingKey(int key, bool control, bool alt, bool win)
    {
        if (control || alt || win) return false;
        return key == 8 || key == 46 || key == 32 || key == 13
            || key >= 0x30 && key <= 0x5A || key >= 0x60 && key <= 0x6F
            || key >= 0xBA && key <= 0xC0 || key >= 0xDB && key <= 0xE2;
    }
    internal PetWindow()
    {
        Text = "꼬마 G70"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        ClientSize = Layer.Viewport(petSize).Size;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 20);
        LoadPreferences(); motion.Curve = BoostCurve.Load(boostPreferences);
        ClientSize = Layer.Viewport(petSize).Size; KeepOnScreen();
        layer = new Layer(petSize); hookProc = Keyboard;
        ToolStripMenuItem sizes = new ToolStripMenuItem("크기");
        foreach (int value in new[] { 180, 240, 320, 400, 480, 600 })
        {
            int chosen = value;
            ToolStripMenuItem item = new ToolStripMenuItem(value + "px" + (value == 320 ? " (기본)" : ""));
            item.Tag = value; item.Click += delegate { ResizePet(chosen); }; sizes.DropDownItems.Add(item);
        }
        menu.Items.Add(sizes);
        menu.Items.Add("설정…", null, delegate { ShowBoostSettings(); });
        ToolStripMenuItem pause = new ToolStripMenuItem("타이핑 반응 일시정지");
        pause.Click += delegate { paused = !paused; pause.Checked = paused; motion = new Motion(motion.Curve) { EngineWarning = motion.EngineWarning, ReleaseAt = motion.ReleaseAt, ExtraWarningPercent = motion.ExtraWarningPercent }; dirty = true; };
        menu.Items.Add(pause);
        menu.Items.Add("종료", null, delegate { Close(); });
        menu.Opening += delegate { foreach (ToolStripMenuItem item in sizes.DropDownItems) item.Checked = (int)item.Tag == petSize; };
        tray.Icon = SystemIcons.Application; tray.Text = "꼬마 G70 · 우클릭으로 설정/크기/종료"; tray.ContextMenuStrip = menu; tray.Visible = true;
        tray.DoubleClick += delegate { KeepOnScreen(); dirty = true; };
        timer.Interval = 16; timer.Tick += Tick;
        Shown += delegate
        {
            hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "타이핑 반응을 연결하지 못했습니다.");
            timer.Start();
        };
        MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); KeepOnScreen(); SavePreferences(); }
            if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        };
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // Clicking never steals the typing focus.
        base.WndProc(ref m);
    }
    IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !paused && (message.ToInt64() == 0x100 || message.ToInt64() == 0x104))
        {
            // Read only the key category, never translate or retain characters.
            int key = Marshal.ReadInt32(data);
            int flags = Marshal.ReadInt32(data, 8);
            bool ctrl = Down(0x11), alt = Down(0x12), win = Down(0x5B) || Down(0x5C);
            if ((flags & 0x10) == 0 && IsTypingKey(key, ctrl, alt, win))
            { motion.Stroke(clock.Elapsed.TotalMilliseconds); timer.Interval = 16; }
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }
    void Tick(object sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalMilliseconds; motion.Update(now);
        bool moving = motion.Typing || motion.Speed > 0 || motion.Shift > 0 || motion.Pressure != Motion.Idle || motion.FlutterUntil > now
            || (motion.EngineWarning && now - motion.ReleaseAt < 1000);
        bool breathing = motion.RestAge > 0 || motion.IdleLift != 0;
        bool trembling = motion.EngineWarning;
        int face = IdleFace.Frame(motion, now);
        if (dirty || moving || breathing || trembling || face != lastFace) { renderer.Draw(layer.Graphics, motion, now, petSize); layer.Present(this, motion.Curve.OpacityPercent); dirty = false; lastFace = face; }
        // Render one settled frame before sleeping, so wind/lettering cannot linger.
        if (!moving && !breathing && !trembling && face < 0 && timer.Interval != 80)
        { renderer.Draw(layer.Graphics, motion, now, petSize); layer.Present(this, motion.Curve.OpacityPercent); timer.Interval = 80; }
        if (moving || breathing || trembling || face >= 0) timer.Interval = moving ? 16 : 33;

    }
    void ResizePet(int size)
    {
        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        Size extent = Layer.Viewport(size).Size;
        Point location = new Point(Left == area.Left ? area.Left : Right - extent.Width,
            Top == area.Top ? area.Top : Bottom - extent.Height);
        petSize = size; ClientSize = extent; Location = location; layer.Dispose(); layer = new Layer(size);
        KeepOnScreen(); dirty = true; SavePreferences();
    }
    void ShowBoostSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        using (BoostSettingsWindow dialog = new BoostSettingsWindow(motion.Curve, delegate(BoostCurve curve) {
            curve.Save(boostPreferences); motion.Curve = curve; dirty = true; timer.Interval = 16;
        }))
        {
            settingsWindow = dialog;
            try { dialog.ShowDialog(this); }
            finally { settingsWindow = null; }
        }
    }
    void KeepOnScreen()
    {
        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = FitOnScreen(Location, ClientSize, area);
    }
    internal static Point FitOnScreen(Point location, Size extent, Rectangle area)
    {
        return new Point(Math.Max(area.Left, Math.Min(location.X, area.Right - extent.Width)),
            Math.Max(area.Top, Math.Min(location.Y, area.Bottom - extent.Height)));
    }
    internal static Point RestorePosition(Point saved, int size, Rectangle area, int version)
    {
        Rectangle crop = Layer.Viewport(size);
        Point location = saved;
        if (version < 3)
        {
            // Floating pets keep their artwork position. Pets touching a screen
            // edge keep that edge, now with the smaller transparent margin.
            Rectangle previous = version == 1 ? new Rectangle(0, 0, size, size)
                : Rectangle.FromLTRB(0, crop.Top, crop.Right, crop.Bottom + 3);
            location.Offset(crop.Left - previous.Left, crop.Top - previous.Top);
            if (saved.X <= area.Left) location.X = area.Left;
            else if (saved.X + previous.Width >= area.Right) location.X = area.Right - crop.Width;
            if (saved.Y <= area.Top) location.Y = area.Top;
            else if (saved.Y + previous.Height >= area.Bottom) location.Y = area.Bottom - crop.Height;
        }
        return FitOnScreen(location, crop.Size, area);
    }
    void LoadPreferences()
    {
        try
        {
            string[] parts = File.ReadAllText(preferences).Split(',');
            int x, y, size;
            if ((parts.Length == 3 || parts.Length == 4 && (parts[3].Trim() == "2" || parts[3].Trim() == "3"))
                && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out size))
            {
                petSize = (int)Motion.Clamp(size, 180, 600);
                Rectangle area = Screen.FromRectangle(new Rectangle(x, y, petSize, petSize)).WorkingArea;
                Location = RestorePosition(new Point(x, y), petSize, area, parts.Length == 3 ? 1 : int.Parse(parts[3], CultureInfo.InvariantCulture));
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    void SavePreferences()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(preferences)); File.WriteAllText(preferences, Left + "," + Top + "," + petSize + ",3"); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        SavePreferences(); tray.Visible = false; tray.Dispose(); menu.Dispose(); timer.Dispose(); layer.Dispose(); renderer.Dispose();
        base.OnFormClosed(e);
    }
}

#if !TESTS
internal static class Program
{
    [STAThread] static int Main()
    {
        try
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (Mutex single = new Mutex(false, "Local\\G70MiniPetDesktop"))
            {
                // A forced stop must not prevent the next launch from acquiring the mutex.
                bool acquired;
                try { acquired = single.WaitOne(0, false); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return 0;
                try { Application.Run(new PetWindow()); }
                finally { single.ReleaseMutex(); }
            }
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show("꼬마 G70을 실행하지 못했어요.\n" + error.Message, "꼬마 G70", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
#endif
