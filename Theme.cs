using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace InfiniteCL
{
    // Presentation extension: original launcher pages, data bindings and event handlers are retained.
    public static class Theme
    {
        public const string Brand = "Cassell CL";
        static readonly Brush Canvas = B("#060607"), Sidebar = B("#EB0B0B0C"), Card = B("#EE111113"),
            Input = B("#0C0C0E"), Hover = B("#28231C"), Selected = B("#31291D"), Line = B("#463D2E"),
            Text = B("#EEE7DA"), Muted = B("#ADA28F"), Accent = B("#C8AA72"), Ink = B("#211A10"),
            Danger = B("#F1A2A0"), Warning = B("#EBC38B"), DangerBack = B("#382326");
        static readonly ConditionalWeakTable<FrameworkElement, State> States = new ConditionalWeakTable<FrameworkElement, State>();
        static bool installed;
        static Window mainWindow;
        static BitmapSource brandImage;
        static BitmapSource cityImage;
        class State
        {
            public bool Alive = true;
            public readonly List<Action> Remove = new List<Action>();
        }
        static Brush B(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b;
        }
        static bool Same(object a, object b) { return Object.ReferenceEquals(a, b); }
        static bool Palette(Brush b)
        {
            return Same(b, Canvas) || Same(b, Sidebar) || Same(b, Card) || Same(b, Input) || Same(b, Hover) ||
                Same(b, Selected) || Same(b, Line) || Same(b, Text) || Same(b, Muted) || Same(b, Accent) ||
                Same(b, Ink) || Same(b, Danger) || Same(b, Warning) || Same(b, DangerBack);
        }
        static bool Transparent(object value)
        {
            var b = value as Brush; var s = b as SolidColorBrush;
            return b == null || b.Opacity == 0 || (s != null && s.Color.A < 3);
        }
        static bool Red(Color c) { return c.R > 160 && c.G < c.R * .60 && c.R > c.B * 1.2; }
        static FrameworkElement Parent(FrameworkElement e)
        {
            return e.Parent as FrameworkElement ?? (e is Visual ? VisualTreeHelper.GetParent(e) as FrameworkElement : null);
        }
        static FrameworkElement Ancestor(FrameworkElement e, string type)
        {
            for (int i = 0; e != null && i < 16; i++, e = Parent(e)) if (e.GetType().FullName == type) return e;
            return null;
        }
        static object Get(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p == null ? null : p.GetValue(o, null);
        }
        static bool Checked(FrameworkElement e) { return Object.Equals(Get(e, "Checked"), true); }
        static FrameworkElement Find(DependencyObject root, string name)
        {
            var f = root as FrameworkElement; if (f != null && f.Name == name) return f;
            if (!(root is Visual)) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = Find(VisualTreeHelper.GetChild(root, i), name); if (found != null) return found; }
            return null;
        }
        public static void Install()
        {
            if (installed) return; installed = true;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(Loaded), true);
        }
        static void Loaded(object sender, RoutedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, e.OriginalSource)) return;
            var f = sender as FrameworkElement; if (f == null) return;
            Walk(f);
        }
        static void Walk(DependencyObject root)
        {
            var f = root as FrameworkElement;
            if (f != null) try { Attach(f); } catch { /* A style must never prevent a functional control from loading. */ }
            if (!(root is Visual)) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Walk(VisualTreeHelper.GetChild(root, i));
            var popup = root as Popup; if (popup != null && popup.IsOpen && popup.Child != null) Walk(popup.Child);
        }
        static void Attach(FrameworkElement e)
        {
            LauncherUpdateUi.Apply(e);
            State existing; if (States.TryGetValue(e, out existing)) return;
            var state = new State(); States.Add(e, state);
            RoutedEventHandler unload = null;
            unload = delegate { state.Alive = false; foreach (var remove in state.Remove) remove(); e.Unloaded -= unload; States.Remove(e); };
            e.Unloaded += unload;
            if (e.Name.StartsWith("InfiniteCL", StringComparison.Ordinal)) return;
            InterfaceCopy.Apply(e);
            var panel = e as Panel;
            if (panel != null) Watch(e, Panel.BackgroundProperty, v => Background(e, v), state);
            var border = e as Border;
            if (border != null)
            {
                Watch(e, Border.BackgroundProperty, v => Background(e, v), state);
                Watch(e, Border.BorderBrushProperty, v => Edge(e, v), state);
                var parent = Parent(e);
                if (parent != null && parent.GetType().FullName == "PCL.MyCard")
                {
                    border.CornerRadius = new CornerRadius(7); border.BorderThickness = new Thickness(1);
                    border.SetCurrentValue(Border.BackgroundProperty, Card); border.SetCurrentValue(Border.BorderBrushProperty, Line);
                }
                if (e.Name == "PanFore" && Ancestor(e, "PCL.MyButton") != null) border.CornerRadius = new CornerRadius(6);
                if (e.Name == "RectBack") border.CornerRadius = new CornerRadius(8);
                if (e.GetType().FullName == "PCL.MyHint") border.CornerRadius = new CornerRadius(8);
            }
            var control = e as Control;
            if (control != null)
            {
                Watch(e, Control.BackgroundProperty, v => Background(e, v), state);
                Watch(e, Control.ForegroundProperty, v => Foreground(e, v), state);
                Watch(e, Control.BorderBrushProperty, v => Edge(e, v), state);
                if (control is TextBox) ((TextBox)control).CaretBrush = Accent;
            }
            var text = e as TextBlock;
            if (text != null)
            {
                Watch(e, TextBlock.ForegroundProperty, v => Foreground(e, v), state);
                Watch(e, TextBlock.BackgroundProperty, v => Background(e, v), state);
                foreach (Inline inline in text.Inlines) InlineColor(inline, e, state);
                if (e.Name == "LabText" && Ancestor(e, "PCL.MyButton") != null) text.FontWeight = FontWeights.Medium;
            }
            var shape = e as Shape;
            if (shape != null && e.Name.IndexOf("Resizer", StringComparison.OrdinalIgnoreCase) < 0 && e.Name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Watch(e, Shape.FillProperty, v => e.Name == "RectLeftBackground" ? Sidebar : (e.Name == "ShapeBorder" ? Input : Foreground(e, v)), state);
                Watch(e, Shape.StrokeProperty, v => Foreground(e, v), state);
            }
            if (e.GetType().FullName == "PCL.MyRadioButton")
            {
                border.CornerRadius = new CornerRadius(9);
                if (e.Name.StartsWith("BtnTitleSelect")) { e.MaxHeight = 36; e.MinHeight = 36; e.Height = 36; e.Margin = new Thickness(4, 0, 4, 0); }
            }
            if (e.GetType().FullName == "PCL.PageLaunchLeft")
            {
                e.Width = 296;
                var login=Find(e,"PanLogin"); if(login!=null) { login.VerticalAlignment=VerticalAlignment.Top; login.Margin=new Thickness(20,28,20,0); }
                var launch = Find(e, "BtnLaunch"); if (launch != null) launch.Height = 66;
                var label = Find(e, "LabVersion") as TextBlock; if (label != null) { label.Margin = new Thickness(35, 0, 35, 12); label.FontSize = 11; }
                var version = Find(e, "BtnVersion"); if (version != null) version.Height = 38;
                var more = Find(e, "BtnMore"); if (more != null) more.Height = 38;
            }
            if (e.GetType().FullName == "PCL.PageLaunchRight") MakeHome(e);
            if (e.GetType().FullName == "PCL.PageOtherAbout") AboutCredits.Apply(e);
            var popup = e as Popup;
            if (popup != null)
            {
                EventHandler opened = delegate { popup.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { if (popup.Child != null) Walk(popup.Child); })); };
                popup.Opened += opened; state.Remove.Add(delegate { popup.Opened -= opened; });
            }
            var window = e as Window;
            if (window != null) TrackLayout(window, state);
            if (window != null && e.GetType().FullName == "PCL.FormMain") SetupWindow(window, state);
        }
        static void TrackLayout(Window w, State state)
        {
            // Template-generated labels can appear after Loaded without their own Loaded event.
            // Coalesce actual layout changes; stop scanning while the window is idle.
            var pending = new DispatcherTimer(DispatcherPriority.ContextIdle, w.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
            pending.Tick += delegate { pending.Stop(); if (state.Alive && w.IsLoaded) Walk(w); };
            EventHandler changed = delegate { if (state.Alive && !pending.IsEnabled) pending.Start(); };
            w.LayoutUpdated += changed;
            state.Remove.Add(delegate { pending.Stop(); w.LayoutUpdated -= changed; });
            w.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { if (state.Alive) Walk(w); }));
        }
        static void InlineColor(Inline inline, FrameworkElement owner, State state)
        {
            WatchObject(inline, TextElement.ForegroundProperty, v => Foreground(owner, v), state);
            var span = inline as Span; if (span != null) foreach (Inline child in span.Inlines) InlineColor(child, owner, state);
        }
        static object Background(FrameworkElement e, object value)
        {
            if (e.Name == "PanMsg") return value;
            if (e.Name == "PanTitle") return Sidebar;
            if (e.Name == "PanForm") return Canvas;
            if (e.Name == "RectLeftBackground") return Sidebar;
            var button = Ancestor(e, "PCL.MyButton");
            if (button != null && e.Name == "PanFore")
            {
                if (button.Name == "BtnLaunch") return button.IsEnabled ? (button.IsMouseOver ? BHoverAccent : Accent) : Hover;
                var solid = value as SolidColorBrush;
                if (solid != null && Red(solid.Color)) return DangerBack;
                return button.IsMouseOver ? Hover : Input;
            }
            if (e.GetType().FullName == "PCL.MyRadioButton") return Checked(e) ? Selected : (e.IsMouseOver ? Hover : Brushes.Transparent);
            if (Transparent(value)) return value;
            var brush = value as Brush;
            // Original notification animations mutate their gradient stops directly.
            if (brush is GradientBrush) return value;
            if (!(brush is SolidColorBrush) && !(brush is GradientBrush)) return value;
            if (Palette(brush)) return value;
            var color = (brush as SolidColorBrush); if (color != null && Red(color.Color)) return DangerBack;
            if (Ancestor(e, "PCL.MyCard") != null && e is Border && Parent(e) != null && Parent(e).GetType().FullName == "PCL.MyCard") return Card;
            if (e is TextBox || e is ComboBox || e.Name.IndexOf("PART_", StringComparison.Ordinal) == 0) return Input;
            if (e.Name == "RectBack") return e.IsMouseOver ? Hover : Selected;
            return Card;
        }
        static readonly Brush BHoverAccent = B("#E6CE94");
        static object Foreground(FrameworkElement e, object value)
        {
            if (Transparent(value)) return value;
            var button = Ancestor(e, "PCL.MyButton");
            if (button != null && button.Name == "BtnLaunch" && button.IsEnabled) return Ink;
            if (button != null) return button.IsEnabled ? Text : Muted;
            if (e.Name == "LabVersion") return Ink;
            var radio = Ancestor(e, "PCL.MyRadioButton"); if (radio != null) return Checked(radio) ? Accent : Muted;
            var b = value as Brush; if (Palette(b)) return Same(b, Line) || Same(b, Input) || Same(b, Card) || Same(b, Canvas) || Same(b, Sidebar) ? Text : value;
            var s = value as SolidColorBrush; if (s == null) return value;
            var c = s.Color; if (Red(c)) return Danger;
            if (c.R > 180 && c.G > 100 && c.G < 210 && c.B < 140) return Warning;
            if (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) > 45) return Accent;
            if (e.Name.IndexOf("Info", StringComparison.Ordinal) >= 0 || (c.R >= 100 && c.R < 210 && Math.Abs(c.R-c.G)<20)) return Muted;
            return Text;
        }
        static object Edge(FrameworkElement e, object value)
        {
            var button = Ancestor(e, "PCL.MyButton");
            if (button != null && e.Name == "PanFore") return button.Name == "BtnLaunch" ? Accent : (button.IsMouseOver ? Accent : Line);
            if (Transparent(value)) return value;
            var s = value as SolidColorBrush; if (s != null && Red(s.Color)) return Danger;
            if (e is TextBox && e.IsKeyboardFocusWithin) return Accent;
            return Line;
        }
        static void Watch(FrameworkElement e, DependencyProperty p, Func<object, object> transform, State s) { WatchObject(e, p, transform, s); }
        static void WatchObject(DependencyObject target, DependencyProperty p, Func<object, object> transform, State state)
        {
            bool changing = false, queued = false;
            var d = DependencyPropertyDescriptor.FromProperty(p, target.GetType()); if (d == null) return;
            EventHandler update = delegate
            {
                if (changing || queued || !state.Alive) return; queued = true;
                target.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    queued = false; if (!state.Alive) return;
                    var old = target.GetValue(p); var next = transform(old); if (Object.Equals(old, next)) return;
                    changing = true;
                    try
                    {
                        var ui = target as UIElement;
                        if (ui != null && p.PropertyType == typeof(Brush) && DependencyPropertyHelper.GetValueSource(target, p).IsAnimated) ui.BeginAnimation(p, null);
                        target.SetCurrentValue(p, next);
                    }
                    finally { changing = false; }
                }));
            };
            d.AddValueChanged(target, update); state.Remove.Add(delegate { d.RemoveValueChanged(target, update); }); update(target, EventArgs.Empty);
        }
        static BitmapSource Image()
        {
            if (brandImage != null) return brandImage;
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("InfiniteCL.png"))
            {
                if (stream == null) return null;
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); brandImage = bitmap;
            }
            return brandImage;
        }
        static BitmapSource CityImage()
        {
            if (cityImage != null) return cityImage;
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("InfiniteCL.City.png"))
            {
                if (stream == null) return null;
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); cityImage = bitmap;
            }
            return cityImage;
        }
        static void SetupWindow(Window w, State state)
        {
            mainWindow = w; w.Title = Brand + " · 龙族完整动效 · 雨落狂流之暗"; w.Icon = Image();
            w.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); w.Foreground = Text;
            var title = Find(w, "PanTitle") as Grid;
            if (title == null || Find(w, "InfiniteCLBrand") != null) return;
            title.Height = 104; title.Background = Sidebar;
            var stockLogo = Find(w, "ShapeTitleLogo");
            if (stockLogo != null) Watch(stockLogo, UIElement.VisibilityProperty, v => Visibility.Collapsed, state);
            // Keep the launcher's user-supplied title text/image available beside the brand.
            foreach (string n in new[] {"LabTitleLogo", "ImageTitleLogo"})
            {
                var custom = Find(w, n); if (custom != null) { custom.VerticalAlignment = VerticalAlignment.Top; custom.Margin = new Thickness(345,20,0,0); custom.MaxHeight = 30; custom.MaxWidth = 250; }
            }
            var header = new StackPanel { Name = "InfiniteCLBrand", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(23, 15, 0, 0), IsHitTestVisible = false };
            header.Children.Add(new System.Windows.Controls.Image { Name = "InfiniteCLMark", Source = Image(), Width = 32, Height = 32, Margin = new Thickness(0, 0, 11, 0) });
            var labels = new StackPanel(); labels.Children.Add(T(Brand, 16, Text, FontWeights.SemiBold));
            labels.Children.Add(T("CASSELL ARCHIVE  /  龙族 · 雨落狂流之暗", 9, Accent, FontWeights.Normal)); header.Children.Add(labels);
            title.Children.Add(header); Panel.SetZIndex(header, 2);
            var nav = Find(w, "PanTitleSelect") as StackPanel;
            if (nav != null)
            {
                nav.Height = 36; nav.Margin = new Thickness(19, 57, 0, 0); nav.VerticalAlignment = VerticalAlignment.Top;
                nav.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(nav, 0); Grid.SetColumnSpan(nav, 3);
            }
            var inner = Find(w, "PanTitleInner") as Grid;
            if (inner != null) { inner.Height = 40; inner.VerticalAlignment = VerticalAlignment.Bottom; inner.Margin = new Thickness(0, 0, 0, 8); }
            foreach (var n in new[] { "BtnTitleClose", "BtnTitleMin" })
            {
                var b = Find(w, n); if (b != null) { b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(0, 19, n == "BtnTitleClose" ? 17 : 51, 0); }
            }
            var line = new Border { Name = "InfiniteCLHeaderLine", Height = 1, Background = Line, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false }; title.Children.Add(line);
            var pointerRoot = Find(w, "PanForm") as Panel;
            if (pointerRoot != null)
            {
                RainScene.Install(pointerRoot, CityImage()); PointerLight.Install(pointerRoot);
                var weather = RainScene.CreateControls(pointerRoot); weather.Name = "InfiniteCLWeatherControls";
                weather.HorizontalAlignment = HorizontalAlignment.Right; weather.VerticalAlignment = VerticalAlignment.Top;
                weather.Margin = new Thickness(0,13,100,0); title.Children.Add(weather);
            }
            if(pointerRoot!=null) NavigationShell.Install(w,pointerRoot);
            if(pointerRoot!=null) LauncherMotion.Install(w,pointerRoot,Image());
            if(pointerRoot!=null) LaunchSuccess.Install(w,pointerRoot,Image());
            var clip = Get(w, "RectForm") as RectangleGeometry; if (clip != null) { clip.RadiusX = 16; clip.RadiusY = 16; }
            w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                w.MinWidth = 940; w.MinHeight = 630;
                if (w.Width < 1140) w.Width = Math.Min(1180, SystemParameters.WorkArea.Width - 60);
                if (w.Height < 740) w.Height = Math.Min(760, SystemParameters.WorkArea.Height - 50);
                var main = Find(w, "PanMain"); var form = Find(w, "PanForm");
                if (main != null && form != null) main.Height = Math.Max(0, form.ActualHeight - title.Height);
                w.InvalidateMeasure();
            }));
#if PREVIEW
            StartPreview(w);
#endif
        }
        static TextBlock T(string text, double size, Brush color, FontWeight weight)
        {
            return new TextBlock { Name = "InfiniteCLText", Text = text, FontSize = size, Foreground = color, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        }
        static Border Box(Brush back, double radius) { return new Border { Name = "InfiniteCLSurface", Background = back, CornerRadius = new CornerRadius(radius), BorderBrush = Line, BorderThickness = new Thickness(1) }; }
        static void Navigate(int page)
        {
            if (mainWindow == null) return;
            var n = Find(mainWindow, "BtnTitleSelect" + page); if (n == null) return;
            var m = n.GetType().GetMethod("SetChecked"); if (m != null) m.Invoke(n, new object[] { true, true, true });
        }
        static void MakeHome(FrameworkElement page)
        {
            DragonHome.Apply(page, Navigate, Image());
        }
#if PREVIEW
        // Diagnostics are compiled only into local QA builds, never into the delivered executable.
        static void StartPreview(Window w)
        {
            var path = Environment.GetEnvironmentVariable("INFINITECL_PREVIEW"); if (String.IsNullOrEmpty(path)) return;
            var pointerRoot = Find(w, "PanForm") as Panel;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; int step = -2;
            timer.Tick += delegate
            {
                try
                {
                    Directory.CreateDirectory(path);
                    if (step < 0) { if(pointerRoot != null) PointerLight.SetPreview(pointerRoot, new Point(620, 370)); step++; return; }
                    // QA-only: render the assembled UI behind any first-run protocol prompt.
                    // This neither accepts the protocol nor changes the production build.
                    var modal = Find(w,"PanMsg"); var visibility = modal == null ? Visibility.Collapsed : modal.Visibility;
                    var b = new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    var pointer = Find(w,"InfiniteCLPointerLight");
                    var pointerVisibility = pointer == null ? Visibility.Visible : pointer.Visibility;
                    // The QA-only hidden dialog otherwise leaves stale cached button outlines in this single render.
                    try { if(modal!=null) modal.Visibility=Visibility.Hidden; if(pointer!=null) pointer.Visibility=Visibility.Hidden; w.UpdateLayout(); b.Render(w); }
                    finally { if(modal!=null) modal.Visibility=visibility; if(pointer!=null) pointer.Visibility=pointerVisibility; }
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(b)); using(var fs = File.Create(System.IO.Path.Combine(path, step.ToString("00")+".png"))) png.Save(fs);
                    var report = new StringBuilder();
                    report.AppendLine("SUCCESS_NATIVE_SUBSCRIBED " + LaunchSuccess.IsSubscribed(w));
                    report.AppendLine("SUCCESS_OBSERVER_STATE " + LaunchSuccess.GetObserverState(w));
                    report.AppendLine("SUCCESS_PLAY_COUNT " + LaunchSuccess.GetPlayCount(w));
                    Dump(w,report,0); File.WriteAllText(System.IO.Path.Combine(path,step.ToString("00")+".txt"),report.ToString());
                    if (step == 0 && pointerRoot != null) RainScene.SetIntensity(pointerRoot,100);
                    else if(step == 1 && pointerRoot != null) RainScene.SetEnabled(pointerRoot,false);
                    else if(step == 2 && pointerRoot != null) { RainScene.SetEnabled(pointerRoot,true); RainScene.SetIntensity(pointerRoot,45); Navigate(1); }
                    else if(step == 3) Navigate(3);
                    else if(step == 4)
                    {
                        var system=Find(w,"ItemSystem");
                        if(system!=null) system.GetType().GetMethod("SetChecked",new[]{typeof(bool),typeof(bool),typeof(bool)}).Invoke(system,new object[]{true,false,false});
                    }
                    else { timer.Stop(); w.Close(); }
                    step++;
                }
                catch(Exception ex) { File.WriteAllText(System.IO.Path.Combine(path,"preview-error.txt"),ex.ToString()); timer.Stop(); }
            }; timer.Start();
        }
        static void Dump(DependencyObject o, StringBuilder text, int depth)
        {
            var f = o as FrameworkElement; if(f != null) { var t=f as TextBlock; State s; text.AppendLine(new String(' ',depth)+f.GetType().Name+" "+f.Name+" "+f.ActualWidth.ToString("0")+"x"+f.ActualHeight.ToString("0")+" "+f.Visibility+" styled="+States.TryGetValue(f,out s)+ (t==null?"":" fg="+t.Foreground+" "+t.Text)); if(t!=null) foreach(Inline line in t.Inlines) text.AppendLine(new String(' ',depth)+" inline="+line.Foreground); }
            if(o is Visual) for(int i=0;i<VisualTreeHelper.GetChildrenCount(o);i++) Dump(VisualTreeHelper.GetChild(o,i),text,Math.Min(depth+1,50));
        }
#endif
    }
}
