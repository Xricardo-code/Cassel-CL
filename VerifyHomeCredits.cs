// Verifies the home author card with the supplied avatar and a real WPF layout.
// Optional second argument identifies the original supplied Ricardo JPEG fixture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

sealed class CreditsCommand : ICommand
{
    public int Calls; public object Parameter;
    public bool CanExecute(object parameter) { return true; }
    public void Execute(object parameter) { Calls++; Parameter = parameter; }
    public event EventHandler CanExecuteChanged { add { } remove { } }
}
sealed class CreditsButton : Button { public void Fire() { OnClick(); } }

static class VerifyHomeCredits
{
    static int checks, failures;
    static void Check(bool success, string label)
    {
        checks++; if (!success) failures++;
        Console.WriteLine((success ? "PASS " : "FAIL ") + label);
    }
    static void Flush()
    {
        var settle = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += delegate { timer.Stop(); settle.Continue = false; }; timer.Start(); Dispatcher.PushFrame(settle);
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var element = root as FrameworkElement; if (element != null) yield return element;
        if (!(root is Visual)) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    { return Elements(root).OfType<T>().Single(e => e.Name == name); }
    static byte[] Resource(Assembly assembly, string name)
    {
        using (var stream = assembly.GetManifestResourceStream(name))
        {
            if (stream == null) return null;
            using (var copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
        }
    }
    static bool Circle(Image image)
    {
        var circle = image.Clip as EllipseGeometry;
        return circle != null && circle.Center == new Point(22, 22) && circle.RadiusX == 22 && circle.RadiusY == 22 && image.Width == 44 && image.Height == 44 && image.Stretch == Stretch.UniformToFill;
    }
    // Inspect real call instructions from creation helpers. Merely storing the
    // RequestNavigate delegate is not a call and is intentionally not followed.
    static bool CreationCanLaunchProcess(Type cardType)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(o => unchecked((ushort)o.Value));
        var queue = new Queue<MethodBase>(); var visited = new HashSet<MethodBase>();
        queue.Enqueue(cardType.GetMethod("Create")); if (cardType.TypeInitializer != null) queue.Enqueue(cardType.TypeInitializer);
        while (queue.Count > 0)
        {
            var method = queue.Dequeue(); if (!visited.Add(method)) continue;
            var body = method.GetMethodBody(); if (body == null) continue;
            var bytes = body.GetILAsByteArray();
            for (int offset = 0; offset < bytes.Length; )
            {
                ushort key = bytes[offset++]; if (key == 0xfe) key = (ushort)(0xfe00 | bytes[offset++]);
                var code = opcodes[key]; int size;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(bytes, offset); break;
                    default: size = 4; break;
                }
                if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset));
                    if (called.DeclaringType == typeof(System.Diagnostics.Process) && called.Name == "Start") return true;
                    if (called.DeclaringType == cardType) queue.Enqueue(called);
                }
                offset += size;
            }
        }
        return false;
    }
    static bool AncestorIs(DependencyObject item, DependencyObject ancestor)
    {
        while (item != null)
        {
            if (Object.ReferenceEquals(item, ancestor)) return true;
            var content = item as FrameworkContentElement;
            item = content != null ? content.Parent : item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
        }
        return false;
    }
    static bool Near(double first, double second) { return Math.Abs(first - second) < .1; }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string path = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InfiniteCL.Theme.dll") : args[0]);
            var assembly = Assembly.LoadFrom(path);
            var credits = assembly.GetType("InfiniteCL.HomeCredits", true);
            var card = (Border)credits.GetMethod("Create").Invoke(null, null);
            string project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), ".."));
            string fixture = args.Length > 1 ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "mmexport1792773117315..jpg");
            if (!File.Exists(fixture)) fixture = Path.Combine(project, "assets", "Ricardo.jpg");
            var supplied = File.ReadAllBytes(fixture);
            var embedded = Resource(assembly, "InfiniteCL.Ricardo.jpg");
            Check(card.Name == "InfiniteCLHomeCredits" && card.Child != null, "home attribution card is available as a dedicated named element");
            Check(embedded != null && embedded.SequenceEqual(supplied), "embedded Ricardo JPEG is byte-for-byte identical to supplied avatar fixture");
            var originalBytes = Resource(assembly, "InfiniteCL.OriginalAuthor.jpg");
            Check(originalBytes != null && originalBytes.Length > 0, "original author avatar is embedded in the theme assembly");
            var originalAvatar = Find<Image>(card, "InfiniteCLOriginalAvatar");
            var ricardoAvatar = Find<Image>(card, "InfiniteCLRicardoAvatar");
            var originalBitmap = originalAvatar.Source as BitmapSource;
            var ricardoBitmap = ricardoAvatar.Source as BitmapSource;
            Check(originalBitmap != null && originalBitmap.IsFrozen && originalBitmap.PixelWidth > 0 && originalBitmap.PixelHeight > 0, "original author image is decoded, nonempty and frozen");
            Check(ricardoBitmap != null && ricardoBitmap.IsFrozen && ricardoBitmap.PixelWidth == 500 && ricardoBitmap.PixelHeight == 500, "Ricardo avatar preserves the supplied 500x500 pixels and frozen decoded source");
            Check(Circle(originalAvatar) && Circle(ricardoAvatar), "both avatar images use 44px circular crops without modifying the source images");
            Check(System.Windows.Automation.AutomationProperties.GetName(originalAvatar) == "龙腾猫跃头像" && System.Windows.Automation.AutomationProperties.GetName(ricardoAvatar) == "Ricardo头像", "avatars have descriptive original-author and Ricardo accessibility names");
            Check(Find<TextBlock>(card, "InfiniteCLOriginNotice").Text == "此启动器基于 PCL 二次创作", "home clearly identifies this launcher as a PCL derivative");
            var originalName = Find<TextBlock>(card, "InfiniteCLOriginalName");
            var originalRole = Find<TextBlock>(card, "InfiniteCLOriginalRole");
            var ricardoName = Find<TextBlock>(card, "InfiniteCLRicardoName");
            var ricardoRole = Find<TextBlock>(card, "InfiniteCLRicardoRole");
            Check(originalName.Text == "龙腾猫跃" && originalRole.Text == "原作作者 / PCL", "original author name and role are correctly attributed");
            Check(ricardoName.Text == "Ricardo" && ricardoRole.Text == "二改作者 / Cassell CL", "Ricardo name and derivative-author role are correctly attributed");
            var sourceText = Find<TextBlock>(card, "InfiniteCLOriginalLink");
            var sourceLink = sourceText.Inlines.OfType<Hyperlink>().Single();
            Check(sourceLink.NavigateUri.AbsoluteUri == "https://meloong.com/afd/a/LTCat" && new TextRange(sourceLink.ContentStart, sourceLink.ContentEnd).Text == "原作主页与支持作者 ↗", "source link targets the intended original-author support page with descriptive text");
            Check(!CreationCanLaunchProcess(credits), "card creation and static resource loading have no call path that launches an external process");
            Check(!Elements(card).OfType<ButtonBase>().Any() && !card.Focusable, "attribution adds no buttons and does not take keyboard focus from launcher controls");

            int navigationRequests = 0;
            sourceLink.RequestNavigate += delegate { navigationRequests++; };
            var originalCommand = new CreditsCommand();
            var originalButton = new CreditsButton { Name = "OriginalAction", Content = "Original launcher action", Height = 42, Command = originalCommand, CommandParameter = "1.21.4", Margin = new Thickness(0, 16, 0, 0) };
            int clicks = 0; originalButton.Click += delegate { clicks++; };
            var root = new StackPanel { Margin = new Thickness(16) }; root.Children.Add(card); root.Children.Add(originalButton);
            var cardBackground = card.Background; var originalForeground = originalName.Foreground;
            assembly.GetType("InfiniteCL.Theme", true).GetMethod("Install").Invoke(null, null);
            var window = new Window { Content = root, Width = 680, Height = 330, Left = -16000, Top = -16000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
            window.Show(); Flush();
            Check(navigationRequests == 0, "creating and rendering home credits never raises automatic link navigation");
            Check(Object.ReferenceEquals(card.Background, cardBackground) && Object.ReferenceEquals(originalName.Foreground, originalForeground) && Object.ReferenceEquals(originalAvatar.Source, originalBitmap) && Object.ReferenceEquals(ricardoAvatar.Source, ricardoBitmap), "Theme.Install preserves named credit styling and both original avatar source instances");
            Check(originalName.ActualWidth > 0 && originalName.ActualHeight > 0 && ricardoName.ActualWidth > 0 && ricardoName.ActualHeight > 0 && originalRole.ActualHeight > 0 && ricardoRole.ActualHeight > 0, "both author names and roles participate in real rendered WPF layout");
            var originalRow = Find<Grid>(card, "InfiniteCLOriginalAuthor"); var ricardoRow = Find<Grid>(card, "InfiniteCLRicardoAuthor");
            var originalPoint = originalRow.TranslatePoint(new Point(), card); var ricardoPoint = ricardoRow.TranslatePoint(new Point(), card);
            Check(Grid.GetColumn(originalRow) == 0 && Grid.GetColumn(ricardoRow) == 2 && ricardoPoint.X > originalPoint.X + originalRow.ActualWidth && Math.Abs(originalPoint.Y - ricardoPoint.Y) < .1, "original and Ricardo credits render side by side in separate author columns");
            // UniformToFill may give the non-square source a larger RenderSize;
            // its explicit ellipse clip defines the visible 44px avatar bounds.
            Check(Near(originalAvatar.Clip.Bounds.Width,44) && Near(originalAvatar.Clip.Bounds.Height,44) && Near(ricardoAvatar.Clip.Bounds.Width,44) && Near(ricardoAvatar.Clip.Bounds.Height,44) && originalAvatar.ActualWidth >= 44 && originalAvatar.ActualHeight >= 44 && ricardoAvatar.ActualWidth >= 44 && ricardoAvatar.ActualHeight >= 44, "both avatars render enough image content to fill their intended 44px visible circular bounds");
            var buttonCenter = originalButton.TranslatePoint(new Point(originalButton.ActualWidth / 2, originalButton.ActualHeight / 2), root);
            var hit = root.InputHitTest(buttonCenter) as DependencyObject;
            Check(hit != null && AncestorIs(hit, originalButton), "author card stays inside its layout bounds and does not intercept original launcher button hit testing");
            originalButton.Fire(); Flush();
            Check(clicks == 1 && originalCommand.Calls == 1 && (string)originalCommand.Parameter == "1.21.4" && navigationRequests == 0, "existing launcher click and command execute once with original parameter and no source-link navigation");
            var image = new RenderTargetBitmap(680, 330, 96, 96, PixelFormats.Pbgra32); image.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HomeCredits.png"))) png.Save(output);
            Check(image.PixelWidth == 680 && image.PixelHeight == 330 && image.Format == PixelFormats.Pbgra32, "real author-card host renders successfully to a reviewable image");
            window.Close(); Flush();
            Check(navigationRequests == 0 && originalName.Text == "龙腾猫跃" && ricardoName.Text == "Ricardo" && Object.ReferenceEquals(ricardoAvatar.Source, ricardoBitmap), "window lifecycle preserves author text and avatar without automatic navigation");
            Console.WriteLine("HOME_CREDITS_VERIFICATION " + (failures == 0 ? "PASSED" : "FAILED") + " " + (checks - failures) + "/" + checks);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine("HARNESS_ERROR " + ex); return 2; }
    }
}
