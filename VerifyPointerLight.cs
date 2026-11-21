using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCL
{
    public class MyCard : Border { }
    public class MyButton : Border { }
}

internal sealed class ReachableButton : Button
{
    public void Fire() { OnClick(); }
}

internal static class VerifyPointerLight
{
    static int checks, failures;
    static readonly StringBuilder report = new StringBuilder();
    static void Log(string text) { report.AppendLine(text); Console.WriteLine(text); }
    static void Check(bool pass, string label)
    {
        checks++;
        if (!pass) failures++;
        Log((pass ? "PASS " : "FAIL ") + label);
    }
    static void Flush(int milliseconds)
    {
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        for (int i = 0; i < 2; i++)
        {
            DispatcherFrame drain = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { drain.Continue = false; }));
            Dispatcher.PushFrame(drain);
        }
    }
    static void Position(FrameworkElement element, double left, double top, double width, double height)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;
        element.Margin = new Thickness(left, top, 0, 0);
        element.Width = width;
        element.Height = height;
    }
    static byte[] Render(Grid root, string output)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap(600, 400, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        byte[] pixels = new byte[600 * 400 * 4];
        bitmap.CopyPixels(pixels, 600 * 4, 0);
        if (output != null)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(output)) encoder.Save(stream);
        }
        return pixels;
    }
    static int Brightness(byte[] pixels, int x, int y)
    {
        int i = (y * 600 + x) * 4;
        return (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
    }
    static Point Peak(byte[] pixels)
    {
        int best = -1, bestX = 0, bestY = 0;
        for (int y = 0; y < 400; y++)
            for (int x = 0; x < 600; x++)
            {
                int value = Brightness(pixels, x, y);
                if (value <= best) continue;
                best = value; bestX = x; bestY = y;
            }
        return new Point(bestX, bestY);
    }
    static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
    static string Hash(string path)
    {
        using (SHA256 hash = SHA256.Create())
        using (FileStream input = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
    }
    [STAThread]
    static int Main(string[] args)
    {
        string assemblyPath = Path.GetFullPath(args[0]);
        string outputDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outputDir);
        Window window = null;
        try
        {
            Log("Pointer-light component regression checks");
            Log("Assembly: " + assemblyPath);
            Log("SHA256: " + Hash(assemblyPath));
            Type pointer = Assembly.LoadFrom(assemblyPath).GetType("InfiniteCL.PointerLight", true);
            MethodInfo install = pointer.GetMethod("Install");
            MethodInfo preview = pointer.GetMethod("SetPreview");
            MethodInfo clear = pointer.GetMethod("ClearPreview");

            Grid root = new Grid { Width = 600, Height = 400, Background = Brushes.Black };
            PCL.MyCard left = new PCL.MyCard { Background = Brushes.Black, CornerRadius = new CornerRadius(14) };
            Position(left, 90, 90, 170, 100); root.Children.Add(left);
            Border right = new Border { Name = "InfiniteCLSurface", Background = Brushes.Black, CornerRadius = new CornerRadius(14) };
            Position(right, 340, 90, 170, 100); root.Children.Add(right);
            PCL.MyButton launch = new PCL.MyButton { Background = Brushes.Black, CornerRadius = new CornerRadius(10) };
            Position(launch, 70, 285, 180, 48); root.Children.Add(launch);
            ReachableButton button = new ReachableButton { Content = "", Background = Brushes.Black, Foreground = Brushes.Black };
            FrameworkElementFactory buttonBorder = new FrameworkElementFactory(typeof(Border));
            buttonBorder.SetValue(Border.BackgroundProperty, Brushes.Black);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = buttonBorder };
            Position(button, 430, 300, 120, 42); root.Children.Add(button);
            int clicks = 0;
            button.Click += delegate { clicks++; };

            int originalChildren = root.Children.Count;
            install.Invoke(null, new object[] { root });
            install.Invoke(null, new object[] { root });
            FrameworkElement layer = root.Children[root.Children.Count - 1] as FrameworkElement;
            Check(root.Children.Count == originalChildren + 1 && layer.Name == "InfiniteCLPointerLight", "Install is idempotent and adds one overlay");
            Check(!layer.IsHitTestVisible && !layer.Focusable && Panel.GetZIndex(layer) == 1000 && Grid.GetRowSpan(layer) == 2,
                "overlay has no hit testing/focus and covers both root rows");

            window = new Window { Content = root, Width = 600, Height = 400, Left = -16000, Top = -16000,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); Flush(100);
            Check(root.IsLoaded && (bool)Field(layer, "active"), "root Loaded activates component");
            Check(((ICollection)Field(layer, "surfaces")).Count == 3, "cache discovers PCL.MyCard, PCL.MyButton, and InfiniteCLSurface");

            byte[] unlit = Render(root, null);
            Check(Brightness(unlit, 140, 140) == 0 && Brightness(unlit, 460, 140) == 0, "light stays hidden while pointer is outside");
            preview.Invoke(null, new object[] { root, new Point(140, 140) }); Flush(35);
            byte[] a = Render(root, Path.Combine(outputDir, "pointer-A.png"));
            preview.Invoke(null, new object[] { root, new Point(460, 140) }); Flush(35);
            byte[] b = Render(root, Path.Combine(outputDir, "pointer-B.png"));
            Point peakA = Peak(a), peakB = Peak(b);
            Log("Brightest pixel A=" + peakA + "; B=" + peakB);
            Check(peakA.X < 280 && peakB.X > 320 && peakB.X - peakA.X > 200, "brightest highlight follows pointer from left to right");
            int centerA = Brightness(a, 140, 140), centerB = Brightness(b, 460, 140);
            Check(centerA >= 25 && centerA <= 40 && centerB >= 25 && centerB <= 40, "ambient maximum remains subtle (25-40 on a black surface)");
            Check(Brightness(a, 460, 140) == 0 && Brightness(b, 140, 140) == 0 &&
                Brightness(a, 300, 399) == 0 && Brightness(b, 300, 399) == 0, "distant pixels remain exactly black outside the 210 px light radius");
            Check(Brightness(a, 140, 90) > centerA && Brightness(b, 460, 90) > centerB, "nearby cached card edges receive a brighter shared light source");

            // InputHitTest follows UIElement.IsHitTestVisible, unlike the raw
            // visual-layer HitTest API which intentionally includes drawing visuals.
            DependencyObject target = root.InputHitTest(new Point(480, 320)) as DependencyObject;
            while (target != null && !Object.ReferenceEquals(target, button)) target = VisualTreeHelper.GetParent(target);
            Check(Object.ReferenceEquals(target, button), "original Button remains the hit-test target beneath the overlay");
            button.Fire();
            Check(clicks == 1, "original Button click handler still executes once");

            Flush(100);
            int layoutEvents = 0;
            EventHandler layoutCounter = delegate { layoutEvents++; };
            root.LayoutUpdated += layoutCounter;
            for (int i = 0; i < 24; i++)
            {
                preview.Invoke(null, new object[] { root, new Point(100 + i * 14, 170 + (i % 4) * 8) });
                Flush(8);
            }
            root.LayoutUpdated -= layoutCounter;
            Log("LayoutUpdated events during 24 rendered pointer updates: " + layoutEvents);
            Check(layoutEvents == 0, "repeated pointer movement schedules rendering without causing LayoutUpdated");

            PCL.MyCard late = new PCL.MyCard { Background = Brushes.Black, CornerRadius = new CornerRadius(14) };
            int lateLoads = 0; late.Loaded += delegate { lateLoads++; };
            Position(late, 280, 270, 100, 60); root.Children.Add(late); Flush(180);
            Log("Late card: IsLoaded=" + late.IsLoaded + "; Loaded events=" + lateLoads + "; cache=" + ((ICollection)Field(layer, "surfaces")).Count);
            Check(((ICollection)Field(layer, "surfaces")).Count == 4, "late-loaded cards are discovered after layout");
            root.Children.Remove(late); Flush(70);
            Check(((ICollection)Field(layer, "surfaces")).Count == 3, "removed page controls are released from the cache");

            clear.Invoke(null, new object[] { root }); Flush(30);
            Check(((DrawingVisual)Field(layer, "drawing")).Opacity == 0, "ClearPreview restores outside-pointer hidden state");
            window.Content = null; Flush(70);
            Check(!root.IsLoaded && !(bool)Field(layer, "active") && ((ICollection)Field(layer, "surfaces")).Count == 0 &&
                Field(layer, "pending") == null && ((DrawingVisual)Field(layer, "drawing")).Opacity == 0,
                "Unloaded cancels pending work, releases cached controls, and deactivates rendering");
            window.Content = root; Flush(70);
            install.Invoke(null, new object[] { root });
            preview.Invoke(null, new object[] { root, new Point(460, 140) }); Flush(30);
            byte[] reloaded = Render(root, null);
            Check(root.IsLoaded && (bool)Field(layer, "active") && root.Children.Count == originalChildren + 1 &&
                ((ICollection)Field(layer, "surfaces")).Count == 3 && Brightness(reloaded, 460, 140) >= 25,
                "reload reattaches once and restores working pointer rendering");
            window.Close(); window = null;
            Log("RESULT: " + checks + " checks, " + failures + " failures.");
        }
        catch (Exception exception)
        {
            failures++;
            Log("ERROR " + exception);
        }
        finally
        {
            if (window != null) window.Close();
            File.WriteAllText(Path.Combine(outputDir, "pointer-verification.txt"), report.ToString(), new UTF8Encoding(false));
        }
        return failures == 0 ? 0 : 1;
    }
}
