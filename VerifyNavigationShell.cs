// Behavioral navigation checks using an ordinary WPF host and native-style radio controls.
// The native radios retain responsibility for selection and all original page events.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

public delegate void NativeCheckHandler(object sender, bool raisedByMouse);

public sealed class NativeRadio : Border
{
    NativeCheckHandler check;
    public bool Checked { get; private set; }
    public int SetCheckedCalls { get; private set; }
    public bool LastUser, LastAnimation;
    public int SubscriberCount { get { return check == null ? 0 : check.GetInvocationList().Length; } }
    public event NativeCheckHandler Check { add { check += value; } remove { check -= value; } }
    public void SetChecked(bool value, bool user, bool animation)
    {
        SetCheckedCalls++; LastUser = user; LastAnimation = animation;
        if (value)
        {
            var parent = Parent as Panel;
            if (parent != null)
                foreach (var sibling in parent.Children.OfType<NativeRadio>())
                    if (!Object.ReferenceEquals(this, sibling)) sibling.SetCore(false, user);
        }
        SetCore(value, user);
    }
    void SetCore(bool value, bool user)
    {
        if (Checked == value) return;
        Checked = value;
        var handler = check; if (handler != null) handler(this, user);
    }
}

static class VerifyNavigationShell
{
    static int checks, failures;
    static Type shell;
    static void Check(bool success, string label)
    {
        checks++; if (!success) failures++;
        Console.WriteLine((success ? "PASS " : "FAIL ") + label);
    }
    static void Flush()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
    static FrameworkElement Find(DependencyObject root, string name)
    {
        var element = root as FrameworkElement;
        if (element != null && element.Name == name) return element;
        if (!(root is Visual)) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Find(VisualTreeHelper.GetChild(root, i), name);
            if (child != null) return child;
        }
        return null;
    }
    static object Controller(Window window)
    {
        var controllers = shell.GetField("Controllers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        object[] args = { window, null };
        return (bool)controllers.GetType().GetMethod("TryGetValue").Invoke(controllers, args) ? args[1] : null;
    }
    static int SubscriptionCount(object controller)
    {
        return controller == null ? -1 : ((ICollection)controller.GetType().GetField("remove", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Count;
    }
    static Border Indicator(Button button) { return (Border)Find(button, "InfiniteCLRailIndicator"); }
    static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); Flush(); }
    static bool Near(double a, double b) { return Math.Abs(a - b) < .1; }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string path = args.Length == 0 ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InfiniteCL.Theme.dll") : args[0];
            shell = Assembly.LoadFrom(System.IO.Path.GetFullPath(path)).GetType("InfiniteCL.NavigationShell", true);
            var install = shell.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
            install.Invoke(null, new object[] { null, null });

            var form = new Grid { Name = "PanForm" };
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition());
            var title = new Grid { Name = "PanTitle", Height = 48 };
            var header = new Grid { Name = "PanTitleMain" };
            var nativeNav = new StackPanel { Name = "PanTitleSelect", Orientation = Orientation.Horizontal };
            var nativeInner = new Grid { Name = "PanTitleInner" };
            header.Children.Add(nativeNav); title.Children.Add(header); title.Children.Add(nativeInner);
            form.Children.Add(title);
            var main = new Grid { Name = "PanMain", Width = 1000, Height = 652, Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetRow(main, 1); form.Children.Add(main);
            var left = new Border { Name = "PanLeft", Width = 235 };
            Grid.SetRow(left, 1); form.Children.Insert(1, left);
            var modal = new Border { Name = "PanMsg", Visibility = Visibility.Collapsed, Background = Brushes.Black };
            Grid.SetRowSpan(modal, 2); form.Children.Add(modal);
            var pageContent = new Border { Name = "OriginalPageHost", Child = new TextBlock { Text = "Original version and account content" } };
            main.Children.Add(pageContent);
            const string originalCredit = "Plain Craft Launcher 2 · 作者：龙腾猫跃 · 原始版权与许可";
            var creditRun = new Run(originalCredit);
            var creditLink = new Hyperlink(creditRun) { NavigateUri = new Uri("https://github.com/Hex-Dragon/PCL2") };
            int creditClicks = 0;
            creditLink.Click += delegate { creditClicks++; };
            var about = new TextBlock { Name = "OriginalAboutCredits" }; about.Inlines.Add(creditLink); main.Children.Add(about);
            var originals = new NativeRadio[5];
            int selectionEvents = 0, selectedPage = -1;
            for (int i = 0; i < originals.Length; i++)
            {
                int page = i;
                var radio = new NativeRadio { Name = "BtnTitleSelect" + i, Child = new TextBlock { Text = "Original page " + i }, Width = 80, Height = 30, Focusable = true };
                radio.Check += delegate(object sender, bool user) {
                    if (((NativeRadio)sender).Checked) { selectionEvents++; selectedPage = page; }
                };
                originals[i] = radio; nativeNav.Children.Add(radio);
            }
            originals[0].SetChecked(true, false, false);
            var nativeChildren = nativeNav.Children.Cast<UIElement>().ToArray();
            var mainChildren = main.Children.Cast<UIElement>().ToArray();
            int baselineEvents = selectionEvents;
            var window = new Window {
                Content = form, Width = 1000, Height = 700, Left = -16000, Top = -16000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
            };
            window.Show(); Flush();
            install.Invoke(null, new object[] { window, form }); Flush();
            var rail = Find(form, "InfiniteCLNavigationRail");
            var proxies = Enumerable.Range(0, 5).Select(i => (Button)Find(form, "InfiniteCLNav" + i)).ToArray();
            Check(rail != null && proxies.All(p => p != null), "installs five visual navigation proxies and one rail");
            Check(Near(title.Height, 64) && Near(rail.Width, 76) && Grid.GetRow(rail) == 1, "new navigation uses 64px header and 76px left rail");
            Check(Object.ReferenceEquals(window.Content, form) && Object.ReferenceEquals(main.Parent, form) && Object.ReferenceEquals(pageContent.Parent, main), "original window, main content host and page content instances remain in place");
            Check(nativeNav.Children.Cast<UIElement>().SequenceEqual(nativeChildren) && originals.All(r => Object.ReferenceEquals(r.Parent, nativeNav)), "native navigation parent, child order and original radio instances are preserved");
            Check(main.Children.Cast<UIElement>().SequenceEqual(mainChildren), "navigation inserts no controls into the original page host");
            Check(nativeNav.Opacity == 0 && !nativeNav.IsHitTestVisible && originals.All(r => !r.Focusable), "native navigation is visually concealed and excluded from pointer and keyboard focus");
            Check(selectionEvents == baselineEvents && originals[0].SetCheckedCalls == 1 && originals.Skip(1).All(r => r.SetCheckedCalls == 0), "install does not invoke selection or original page events");
            Check(Indicator(proxies[0]).Visibility == Visibility.Visible && proxies.Skip(1).All(p => Indicator(p).Visibility == Visibility.Hidden), "initial proxy selection matches original checked radio");
            Check(proxies.Select(p => System.Windows.Automation.AutomationProperties.GetName(p)).SequenceEqual(new[] { "启动游戏", "下载与安装资源", "联机", "启动器设置", "帮助、工具与关于" }), "every navigation proxy has an explicit accessible name");
            Check(Near(main.Margin.Left, 76) && Near(left.Margin.Left, 76) && Near(main.Width, form.ActualWidth - 76) && Near(main.Height, form.ActualHeight - 64), "content width, height and left background compensate for header and rail");

            object controller = Controller(window); int subscriptions = SubscriptionCount(controller);
            install.Invoke(null, new object[] { window, form }); Flush();
            Check(Object.ReferenceEquals(controller, Controller(window)) && subscriptions == SubscriptionCount(Controller(window)) && form.Children.Cast<FrameworkElement>().Count(e => e.Name == "InfiniteCLNavigationRail") == 1, "repeated install keeps exactly one controller, rail and subscription set");
            Check(originals.All(r => r.SubscriberCount == 2), "each native radio has exactly one original handler and one shell handler");

            int before = selectionEvents; Click(proxies[1]);
            Check(originals[1].SetCheckedCalls == 1 && originals[1].LastUser && originals[1].LastAnimation && selectionEvents == before + 1 && selectedPage == 1, "proxy click calls original SetChecked(true,true,true) and original selection handler exactly once");
            Check(originals[1].Checked && !originals[0].Checked && Indicator(proxies[1]).Visibility == Visibility.Visible && Indicator(proxies[0]).Visibility == Visibility.Hidden, "native radio group remains selection authority and proxies synchronize");
            before = selectionEvents; Click(proxies[1]);
            Check(originals[1].SetCheckedCalls == 2 && selectionEvents == before, "clicking an already selected proxy preserves native duplicate-event behavior");
            before = selectionEvents; originals[4].SetChecked(true, false, true); Flush();
            Check(selectionEvents == before + 1 && selectedPage == 4 && Indicator(proxies[4]).Visibility == Visibility.Visible && Indicator(proxies[1]).Visibility == Visibility.Hidden, "original programmatic page change synchronizes proxy selection without another SetChecked call");

            originals[2].Visibility = Visibility.Collapsed; Flush();
            Check(proxies[2].Visibility == Visibility.Collapsed, "original collapsed navigation remains collapsed in the rail");
            originals[2].Visibility = Visibility.Hidden; Flush();
            Check(proxies[2].Visibility == Visibility.Hidden, "original hidden navigation retains reserved space in the rail");
            originals[2].Visibility = Visibility.Visible; originals[2].IsEnabled = false; Flush();
            int disabledCalls = originals[2].SetCheckedCalls; before = selectionEvents; Click(proxies[2]);
            Check(!proxies[2].IsEnabled && proxies[2].Opacity == .45 && originals[2].SetCheckedCalls == disabledCalls && selectionEvents == before, "disabled original produces visibly disabled proxy and cannot invoke selection");
            originals[2].IsEnabled = true; header.IsHitTestVisible = false; Flush();
            Check(proxies.All(p => !p.IsEnabled), "native modal/navigation lock disables every proxy");
            before = selectionEvents; Click(proxies[0]);
            Check(selectionEvents == before, "locked original header prevents proxy page changes");
            header.IsHitTestVisible = true; header.Visibility = Visibility.Collapsed; Flush();
            Check(proxies.All(p => !p.IsEnabled), "hidden native main header disables every proxy");
            header.Visibility = Visibility.Visible; Flush();
            Check(proxies.All(p => p.IsEnabled && p.Opacity == 1), "native navigation recovery reenables proxies");
            Check(Panel.GetZIndex(rail) == 0 && form.Children.IndexOf(rail) < form.Children.IndexOf(modal), "rail remains below the original PanMsg modal in native child stacking order");
            modal.Visibility = Visibility.Visible; Flush();
            before = selectionEvents; int modalCalls = originals[0].SetCheckedCalls; Click(proxies[0]);
            Check(proxies.All(p => !p.IsEnabled) && selectionEvents == before && originals[0].SetCheckedCalls == modalCalls, "visible native dialog disables every proxy and prevents original page selection");
            var modalHit = form.InputHitTest(new Point(38, 150));
            Check(Object.ReferenceEquals(modalHit, modal), "native modal physically covers the rail and owns its pointer hit testing");
            modal.Visibility = Visibility.Collapsed; Flush();
            Check(proxies.All(p => p.IsEnabled), "collapsing native modal restores normal proxy navigation without accepting the dialog");

            window.Width = 1280; window.Height = 820; Flush();
            Check(Near(main.Width, form.ActualWidth - 76) && Near(main.Height, form.ActualHeight - 64) && Near(nativeInner.MaxWidth, form.ActualWidth - 660), "real window resize preserves content and header space allocation");
            main.Width = form.ActualWidth; main.Height = form.ActualHeight - 48; Flush();
            Check(Near(main.Width, form.ActualWidth - 76) && Near(main.Height, form.ActualHeight - 64), "original runtime size assignments are corrected without accumulating rail offsets");
            window.Width = 940; window.Height = 630; Flush();
            Check(Near(main.Width, form.ActualWidth - 76) && Near(main.Height, form.ActualHeight - 64) && main.Width > 0 && main.Height > 0, "smaller supported window preserves positive content bounds");
            creditLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, creditLink));
            Check(creditRun.Text == originalCredit && creditLink.NavigateUri.AbsoluteUri == "https://github.com/Hex-Dragon/PCL2" && creditClicks == 1 && Object.ReferenceEquals(about.Parent, main), "original About attribution text, source URI, click handler and host remain unmodified");

            originals[3].IsEnabled = false; // Queue a synchronization, then unload before it can run.
            form.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, form)); Flush();
            Check(Controller(window) == null && Find(form, "InfiniteCLNavigationRail") == null && originals.All(r => r.SubscriberCount == 1), "unload removes controller and rail and detaches every native Check handler");
            Check(SubscriptionCount(controller) == 0 && (bool)controller.GetType().GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller), "unload empties property/event subscription cleanup list and marks controller disposed");
            int oldCalls = originals[0].SetCheckedCalls;
            originals[3].IsEnabled = true; originals[0].SetChecked(true, false, false); Flush();
            Check(Controller(window) == null && Find(form, "InfiniteCLNavigationRail") == null && originals[0].SetCheckedCalls == oldCalls + 1, "queued or later native updates do not reinstall a disposed shell or duplicate selection");
            install.Invoke(null, new object[] { window, form }); Flush();
            Check(Controller(window) != null && !Object.ReferenceEquals(controller, Controller(window)) && originals.All(r => r.SubscriberCount == 2), "manual reinstall after unload creates one fresh controller and native handler set");
            int closing = 0; window.Closing += delegate { closing++; }; window.Close(); Flush();
            Check(closing == 1 && Controller(window) == null && Find(form, "InfiniteCLNavigationRail") == null && originals.All(r => r.SubscriberCount == 1), "real window close preserves original Closing handler and cleans navigation subscriptions");
            Check(creditRun.Text == originalCredit && creditLink.NavigateUri.AbsoluteUri == "https://github.com/Hex-Dragon/PCL2", "original authorship and license credits survive the complete navigation lifecycle");

            var unknownForm = new Grid(); var unknownWindow = new Window { Content = unknownForm };
            install.Invoke(null, new object[] { unknownWindow, unknownForm });
            Check(Controller(unknownWindow) == null && unknownForm.Children.Count == 0, "unrecognized window with no native navigation is left untouched");
            Console.WriteLine("NAVIGATION_VERIFICATION " + (failures == 0 ? "PASSED" : "FAILED") + " " + (checks - failures) + "/" + checks);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine("HARNESS_ERROR " + ex); return 2; }
    }
}
