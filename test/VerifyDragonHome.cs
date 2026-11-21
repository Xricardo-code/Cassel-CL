// Runs the shipped theme DLL against a real WPF home-page host. No product
// source is linked into this executable.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCL { public sealed class PageLaunchRight : UserControl { } }

static class VerifyDragonHome
{
    static int checks, failures;
    static void Check(bool ok, string label)
    { checks++; if (!ok) failures++; Console.WriteLine((ok ? "PASS " : "FAIL ") + label); }
    static void Flush()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; })); Dispatcher.PushFrame(frame);
        }
    }
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var item = root as FrameworkElement; if (item != null) yield return item;
        if (!(root is Visual)) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    { return Elements(root).OfType<T>().Single(item => item.Name == name); }
    static Rect Bounds(FrameworkElement item, Visual ancestor)
    { return item.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(), item.RenderSize)); }
    static bool Contains(Rect parent, Rect child)
    { parent.Inflate(.2, .2); return parent.Contains(child); }
    static bool Separate(Rect first, Rect second)
    { first.Inflate(-.1, -.1); second.Inflate(-.1, -.1); return !first.IntersectsWith(second); }
    static BitmapSource ResourceImage(Assembly assembly, string name)
    {
        using (var stream = assembly.GetManifestResourceStream(name))
        {
            if (stream == null) throw new InvalidDataException("Missing resource: " + name);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
        }
    }
    static string Hash(string path)
    { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
    static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected theme DLL and QA output directory.");
            string dll = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            Console.WriteLine("THEME_SHA256 " + Hash(dll));
            var assembly = Assembly.LoadFrom(dll);
            var apply = assembly.GetType("InfiniteCL.DragonHome", true).GetMethod("Apply");
            var crest = ResourceImage(assembly, "InfiniteCL.png");
            var background = ResourceImage(assembly, "InfiniteCL.City.png");
            foreach (int width in new[] { 690, 525 })
            {
                var panel = new StackPanel { Name = "PanMain" };
                var originalFirst = new Button { Name = "NativeFirst", Content = "Native retained action", Height = 38 };
                var originalSecond = new TextBlock { Name = "NativeSecond", Text = "Native retained content" };
                panel.Children.Add(originalFirst); panel.Children.Add(originalSecond);
                var scroller = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                var page = new PCL.PageLaunchRight { Content = scroller };
                var host = new Grid { Background = new ImageBrush(background) { Stretch = Stretch.UniformToFill } };
                host.Children.Add(page);
                var window = new Window { Content = host, Width = width, Height = 660, WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize, Left = -16000, Top = -16000, ShowActivated = false, ShowInTaskbar = false };
                window.Show(); Flush();
                var requests = new List<int>(); Action<int> navigate = delegate(int id) { requests.Add(id); };
                object[] arguments = { page, navigate, crest };
                apply.Invoke(null, arguments); Flush();
                var home = Find<StackPanel>(page, "InfiniteCLDragonHome");
                apply.Invoke(null, arguments); apply.Invoke(null, arguments); Flush();
                Check(panel.Children.Count == 3 && Object.ReferenceEquals(panel.Children[0], home) &&
                    Object.ReferenceEquals(panel.Children[1], originalFirst) && Object.ReferenceEquals(panel.Children[2], originalSecond),
                    width + ": repeated Apply inserts one home and preserves native child identity/order");
                Check(requests.Count == 0 && originalFirst.Content.Equals("Native retained action") && originalSecond.Text == "Native retained content",
                    width + ": construction and reapplication neither navigate nor mutate native content");
                var entries = new[] { Find<Border>(home, "InfiniteCLDragonSurfaceCommand01"),
                    Find<Border>(home, "InfiniteCLDragonSurfaceCommand02"), Find<Border>(home, "InfiniteCLDragonSurfaceCommand03") };
                int[] destinations = { 1, 4, 3 }; string[] names = { "版本下载", "帮助与工具", "启动器设置" };
                Check(entries.Select((entry, i) => entry.Focusable && KeyboardNavigation.GetIsTabStop(entry) &&
                    AutomationProperties.GetName(entry) == names[i] && !String.IsNullOrEmpty(AutomationProperties.GetHelpText(entry))).All(ok => ok),
                    width + ": all navigation entries are keyboard focus targets with descriptive accessible names");
                for (int i = 0; i < entries.Length; i++)
                {
                    int before = requests.Count;
                    var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                        { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
                    entries[i].RaiseEvent(mouse); Flush();
                    bool clicked = requests.Count == before + 1 && requests[before] == destinations[i] && mouse.Handled;
                    bool keys = true;
                    foreach (Key key in new[] { Key.Enter, Key.Space })
                    {
                        before = requests.Count;
                        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
                            { RoutedEvent = Keyboard.KeyDownEvent };
                        entries[i].RaiseEvent(input); Flush();
                        keys &= requests.Count == before + 1 && requests[before] == destinations[i] && input.Handled;
                    }
                    Check(clicked && keys, width + ": " + names[i] + " mouse, Enter and Space each dispatch exactly once to " + destinations[i]);
                }
                int enabledRequests = requests.Count; entries[0].IsEnabled = false;
                entries[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                entries[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter)
                    { RoutedEvent = Keyboard.KeyDownEvent }); Flush();
                Check(requests.Count == enabledRequests, width + ": disabled entry suppresses mouse and keyboard navigation");
                entries[0].IsEnabled = true; Flush();

                var title = Find<TextBlock>(home, "InfiniteCLDragonHeading");
                var folio = Find<Border>(home, "InfiniteCLDragonSurfaceFolio");
                var commands = Find<Grid>(home, "InfiniteCLArchiveCommands");
                var authors = Find<Border>(home, "InfiniteCLHomeCredits");
                Rect view = new Rect(0, 0, width, 660), homeRect = Bounds(home, host), titleRect = Bounds(title, host),
                    folioRect = Bounds(folio, host), commandsRect = Bounds(commands, host), authorsRect = Bounds(authors, host);
                Check(title.Text == "雨落狂流之暗" && title.ActualHeight > 0 && Contains(folioRect, titleRect) && Contains(view, titleRect),
                    width + ": requested main title remains fully inside its folio and visible viewport");
                Check(entries.All(entry => entry.ActualWidth > 0 && entry.ActualHeight > 0 && Contains(commandsRect, Bounds(entry, host))) &&
                    Separate(Bounds(entries[0], host), Bounds(entries[1], host)) && Separate(Bounds(entries[0], host), Bounds(entries[2], host)) &&
                    Separate(Bounds(entries[1], host), Bounds(entries[2], host)), width + ": navigation entries have positive size and never overlap or overflow");
                Check(Separate(folioRect, commandsRect) && Separate(commandsRect, authorsRect) && Contains(view, authorsRect) && Contains(view, homeRect),
                    width + ": folio, commands and author card do not overlap and the full home fits the first viewport");
                var originalName = Find<TextBlock>(home, "InfiniteCLOriginalName"); var ricardoName = Find<TextBlock>(home, "InfiniteCLRicardoName");
                var originalRole = Find<TextBlock>(home, "InfiniteCLOriginalRole"); var ricardoRole = Find<TextBlock>(home, "InfiniteCLRicardoRole");
                Check(originalName.Text == "龙腾猫跃" && ricardoName.Text == "Ricardo" &&
                    new[] { originalName, ricardoName, originalRole, ricardoRole }.All(item => item.ActualWidth > 0 && item.ActualHeight > 0 && Contains(authorsRect, Bounds(item, host))) &&
                    Separate(Bounds(originalName, host), Bounds(ricardoName, host)),
                    width + ": both author names/roles are retained, visible and inside the author card");
                Check(width == 690 ? entries.All(entry => Grid.GetRow(entry) == 0) :
                    entries.Select((entry, i) => Grid.GetRow(entry) == i && Grid.GetColumn(entry) == 0).All(ok => ok),
                    width + ": entry layout adapts between wide columns and narrow rows");
                Console.WriteLine("LAYOUT " + width + " HOME " + homeRect + " CREDITS " + authorsRect);
                Save(window, Path.Combine(output, "DragonHome-" + width + ".png"));
                window.Close(); Flush();
            }
            Console.WriteLine("DRAGON_HOME_VERIFICATION " + (failures == 0 ? "PASSED " : "FAILED ") + (checks - failures) + "/" + checks);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.WriteLine("HARNESS_ERROR " + error); return 2; }
    }
}
