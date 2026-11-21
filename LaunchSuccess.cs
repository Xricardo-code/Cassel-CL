using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace InfiniteCL
{
    // This observer only reads native launch state. The launcher's own event,
    // process, visibility policy and success/failure decisions remain native.
    public static class LaunchSuccess
    {
        static readonly ConditionalWeakTable<Window, Controller> Controllers = new ConditionalWeakTable<Window, Controller>();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        public static void Install(Window window, Panel contentRoot, ImageSource crest)
        {
            if (window == null || contentRoot == null) return;
            Controller existing;
            if (Controllers.TryGetValue(window, out existing)) return;
            var controller = new Controller(window, contentRoot, crest);
            Controllers.Add(window, controller);
            controller.Attach();
        }

        public static bool IsSubscribed(Window window)
        {
            Controller value;
            return window != null && Controllers.TryGetValue(window, out value) && value.Subscribed;
        }

        public static bool IsPlaying(Window window)
        {
            Controller value;
            return window != null && Controllers.TryGetValue(window, out value) && value.Playing;
        }

        public static int GetPlayCount(Window window)
        {
            Controller value;
            return window != null && Controllers.TryGetValue(window, out value) ? value.PlayCount : 0;
        }

        public static string GetObserverState(Window window)
        {
            Controller value;
            return window != null && Controllers.TryGetValue(window, out value) ? value.ObserverState : "uninstalled";
        }

        static object Read(object target, string name)
        {
            if (target == null) throw new InvalidOperationException("Missing native instance.");
            return ReadMember(target.GetType(), target, name, false);
        }

        static object ReadMember(Type type, object target, string name, bool optional)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (target == null ? BindingFlags.Static : BindingFlags.Instance);
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, flags);
                if (field != null) return field.GetValue(target);
                var property = current.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target, null);
            }
            if (optional) return null;
            throw new MissingMemberException(type.FullName, name);
        }

        static string StateName(object target)
        {
            var state = Read(target, "State");
            if (state == null) throw new InvalidOperationException("Missing native state.");
            return state.ToString();
        }

        static FrameworkElement Find(DependencyObject node, string name)
        {
            var element = node as FrameworkElement;
            if (element != null && element.Name == name) return element;
            if (!(node is Visual)) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var found = Find(VisualTreeHelper.GetChild(node, i), name);
                if (found != null) return found;
            }
            return null;
        }

        static Brush Color(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value);
            brush.Freeze();
            return brush;
        }

        sealed class Controller
        {
            readonly Window window;
            readonly Panel root;
            readonly ImageSource crest;
            readonly DispatcherTimer timer;
            readonly List<Action> clocks = new List<Action>();
            Type launchType;
            object outerLoader, baselineReal, baselineWatcher, realLoader, watcher;
            EventInfo stateEvent;
            Delegate stateHandler;
            FrameworkElement modal;
            Grid overlay;
            DateTime deadline, readyAt, effectEnds;
            bool armed, pending, disposed, closing, watchingLayout;
            internal bool Subscribed;
            internal bool Playing { get { return overlay != null; } }
            internal int PlayCount;
            internal string ObserverState = "idle";

            internal Controller(Window window, Panel root, ImageSource crest)
            {
                this.window = window; this.root = root; this.crest = crest;
                timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
                timer.Interval = TimeSpan.FromMilliseconds(150);
                timer.Tick += Tick;
            }

            internal void Attach()
            {
                try
                {
                    // The concrete main-window assembly is also the native PCL assembly.
                    // Looking through every loaded assembly could select a stale copy.
                    launchType = window.GetType().Assembly.GetType("PCL.ModLaunch", false);
                    if (launchType == null) { ObserverState = "unavailable"; return; }
                    outerLoader = Native("McLaunchLoader");
                    if (outerLoader == null) { ObserverState = "unavailable"; return; }
                    for (var type = outerLoader.GetType(); type != null && stateEvent == null; type = type.BaseType)
                        stateEvent = type.GetEvent("OnStateChangedUi", BindingFlags.Instance | BindingFlags.Public |
                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (stateEvent == null) { ObserverState = "unavailable"; return; }
                    var invoke = stateEvent.EventHandlerType.GetMethod("Invoke");
                    var args = invoke.GetParameters();
                    if (invoke.ReturnType != typeof(void) || args.Length != 3)
                        throw new InvalidOperationException("Unexpected native event signature.");
                    var parameters = new ParameterExpression[3];
                    var boxed = new Expression[3];
                    for (int i = 0; i < 3; i++)
                    {
                        parameters[i] = Expression.Parameter(args[i].ParameterType, "arg" + i);
                        boxed[i] = Expression.Convert(parameters[i], typeof(object));
                    }
                    var sink = typeof(Controller).GetMethod("NativeState", BindingFlags.Instance | BindingFlags.NonPublic);
                    stateHandler = Expression.Lambda(stateEvent.EventHandlerType,
                        Expression.Call(Expression.Constant(this), sink, boxed), parameters).Compile();
                    stateEvent.GetAddMethod(true).Invoke(outerLoader, new object[] { stateHandler });
                    Subscribed = true;
                    window.IsVisibleChanged += VisibilityChanged;
                    window.IsHitTestVisibleChanged += HitTestChanged;
                    window.StateChanged += WindowStateChanged;
                    window.Closing += WindowClosing;
                    window.Closed += WindowClosed;
                    DiscoverModal();
                    // Deliberately do not infer a new attempt from an already running game.
                    ObserverState = "idle";
                }
                catch { Dispose("unavailable"); }
            }

            object Native(string name) { return ReadMember(launchType, null, name, false); }

            // Every path from a native event is guarded, including dispatcher shutdown.
            void NativeState(object sender, object newState, object oldState)
            {
                try
                {
                    if (disposed || !Object.ReferenceEquals(sender, outerLoader)) return;
                    string name = newState == null ? "" : newState.ToString();
                    if (window.Dispatcher.CheckAccess()) HandleState(name);
                    else if (!window.Dispatcher.HasShutdownStarted && !window.Dispatcher.HasShutdownFinished)
                        window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                        {
                            try { HandleState(name); } catch { FailClosed(); }
                        }));
                }
                catch { FailClosed(); }
            }

            void HandleState(string name)
            {
                if (disposed) return;
                if (!Object.ReferenceEquals(Native("McLaunchLoader"), outerLoader))
                { Cancel("loader-replaced"); return; }
                if (name == "Loading")
                {
                    Cancel("arming");
                    baselineReal = Native("McLaunchLoaderReal");
                    baselineWatcher = Native("McLaunchWatcher");
                    realLoader = null; watcher = null;
                    armed = true;
                    ObserverState = "armed";
                    return;
                }
                if (name != "Finished") { Cancel("canceled"); return; }
                if (!armed || pending || Playing) return;
                if (StateName(outerLoader) != "Finished") { Cancel("superseded"); return; }
                pending = true;
                readyAt = DateTime.MinValue;
                deadline = DateTime.UtcNow.AddMinutes(5);
                ObserverState = "pending";
                StartActive();
            }

            void StartActive()
            {
                if (!watchingLayout)
                {
                    root.LayoutUpdated += ActiveLayout;
                    watchingLayout = true;
                }
                timer.Start();
            }

            void StopActive()
            {
                if (pending || Playing) return;
                timer.Stop();
                if (watchingLayout)
                {
                    root.LayoutUpdated -= ActiveLayout;
                    watchingLayout = false;
                }
            }

            void DiscoverModal()
            {
                if (modal != null) return;
                modal = ReadMember(window.GetType(), window, "PanMsg", true) as FrameworkElement;
                if (modal == null) modal = Find(window, "PanMsg");
                if (modal != null) modal.IsVisibleChanged += ModalChanged;
            }

            bool CanPresent()
            {
                if (disposed || closing || !window.IsVisible || !window.IsHitTestVisible ||
                    window.WindowState == WindowState.Minimized) return false;
                object hidden = ReadMember(window.GetType(), window, "Hidden", true);
                if (hidden != null && (!(hidden is bool) || (bool)hidden)) return false;
                DiscoverModal();
                return modal == null || !modal.IsVisible;
            }

            void Tick(object sender, EventArgs args)
            {
                try
                {
                    if (disposed) return;
                    if (Playing)
                    {
                        if (!CanPresent()) { Cancel("interrupted"); return; }
                        if (DateTime.UtcNow >= effectEnds) { EndEffect(); ObserverState = "consumed"; }
                        return;
                    }
                    if (!pending) { StopActive(); return; }
                    if (DateTime.UtcNow >= deadline) { Cancel("timeout"); return; }
                    if (!CanPresent()) { Cancel("skipped-hidden-or-modal"); return; }
                    if (!Ready()) return;
                    if (readyAt == DateTime.MinValue) readyAt = DateTime.UtcNow;
                    if (LauncherMotion.IsPlaying(window))
                    {
                        if ((DateTime.UtcNow - readyAt).TotalSeconds >= 2) Cancel("skipped-startup");
                        else ObserverState = "waiting-startup";
                        return;
                    }
                    // Native state and visibility can change during layout/startup work.
                    if (!Ready() || !CanPresent()) { if (pending) Cancel("superseded"); return; }
                    pending = false; armed = false;
                    BeginEffect();
                }
                catch { FailClosed(); }
            }

            bool Ready()
            {
                if (!pending || !armed) return false;
                if (!Object.ReferenceEquals(Native("McLaunchLoader"), outerLoader) || StateName(outerLoader) != "Finished")
                { Cancel("superseded"); return false; }
                object currentReal = Native("McLaunchLoaderReal");
                if (currentReal == null || Object.ReferenceEquals(currentReal, baselineReal)) return false;
                if (realLoader == null) realLoader = currentReal;
                else if (!Object.ReferenceEquals(realLoader, currentReal)) { Cancel("superseded"); return false; }
                string realState = StateName(realLoader);
                if (realState == "Canceled" || realState == "Failed" || realState == "Waiting")
                { Cancel("inner-canceled"); return false; }
                if (realState != "Finished") return false;
                object currentWatcher = Native("McLaunchWatcher");
                if (currentWatcher == null || Object.ReferenceEquals(currentWatcher, baselineWatcher)) return false;
                if (watcher == null) watcher = currentWatcher;
                else if (!Object.ReferenceEquals(watcher, currentWatcher)) { Cancel("superseded"); return false; }
                object loader = Read(watcher, "Loader");
                if (loader == null || !Object.ReferenceEquals(Read(loader, "Parent"), realLoader))
                { Cancel("unrelated-watcher"); return false; }
                string watcherState = StateName(watcher);
                if (watcherState == "Ended" || watcherState == "Crashed" || watcherState == "Canceled" || watcherState == "Failed")
                { Cancel("game-ended"); return false; }
                var process = Read(watcher, "GameProcess") as Process;
                object currentProcess = Native("McLaunchProcess");
                if (process == null || currentProcess == null) return false;
                if (!Object.ReferenceEquals(process, currentProcess)) { Cancel("unrelated-process"); return false; }
                if (process.HasExited) { Cancel("game-ended"); return false; }
                if (watcherState != "Running") return false;
                // Running also covers log progress 5 and Forge's early splash screen.
                // A real completed game window is required in addition to Running.
                object appeared = Read(watcher, "IsWindowAppeared");
                object finished = Read(watcher, "IsWindowFinished");
                object handle = Read(watcher, "WindowHandle");
                if (!(appeared is bool) || !(bool)appeared || !(finished is bool) || !(bool)finished ||
                    !(handle is IntPtr) || (IntPtr)handle == IntPtr.Zero) return false;
                var windowHandle = (IntPtr)handle;
                uint windowProcessId;
                return IsWindow(windowHandle) && GetWindowThreadProcessId(windowHandle, out windowProcessId) != 0 &&
                    windowProcessId == (uint)process.Id;
            }

            void BeginEffect()
            {
                overlay = new Grid
                {
                    Name = "InfiniteCLLaunchSuccessOverlay", Width = 302, Height = 108,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(18, 24, 22, 0), IsHitTestVisible = false, Focusable = false,
                    RenderTransformOrigin = new Point(.5, .5)
                };
                Panel.SetZIndex(overlay, Int32.MaxValue);
                var gold = Color("#D8B96C");
                var border = new Border
                {
                    Background = Color("#F016191B"), BorderBrush = Color("#AD947047"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(15, 12, 15, 12),
                    IsHitTestVisible = false, Focusable = false
                };
                overlay.Children.Add(border);
                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(71) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                border.Child = content;
                var emblem = new Grid { Width = 61, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
                content.Children.Add(emblem);
                var ring = new Ellipse
                {
                    Width = 53, Height = 53, Stroke = gold, StrokeThickness = 1,
                    Opacity = .5, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1)
                };
                emblem.Children.Add(ring);
                if (crest != null)
                    emblem.Children.Add(new Image { Source = crest, Width = 35, Height = 42, Stretch = Stretch.Uniform, Opacity = .72 });
                else
                    emblem.Children.Add(new Path
                    {
                        Data = Geometry.Parse("M 28,7 L 28,48 M 28,18 L 17,12 M 28,25 L 13,20 M 28,32 L 18,29 M 28,18 L 39,12 M 28,25 L 43,20 M 28,32 L 38,29 M 28,43 L 18,51 M 28,43 L 38,51"),
                        Stroke = gold, StrokeThickness = 1.6, Width = 56, Height = 58,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    });
                var checkDisk = new Ellipse
                {
                    Width = 23, Height = 23, Fill = Color("#FF191D1D"), Stroke = gold, StrokeThickness = 1,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 1, 2)
                };
                emblem.Children.Add(checkDisk);
                var check = new Path
                {
                    Data = Geometry.Parse("M 2,7 L 6,11 L 14,2"), Stroke = gold, StrokeThickness = 2.1,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Width = 17, Height = 15, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 6)
                };
                emblem.Children.Add(check);
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                Grid.SetColumn(words, 1);
                words.Children.Add(new TextBlock
                {
                    Text = "游戏已启动", Foreground = Color("#F1E5C5"), FontSize = 20,
                    FontFamily = new FontFamily("Microsoft YaHei UI"), FontWeight = FontWeights.SemiBold
                });
                words.Children.Add(new TextBlock
                {
                    Text = "祝旅途顺利", Foreground = Color("#BAAA86"), FontSize = 12,
                    FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(1, 7, 0, 0)
                });
                content.Children.Add(words);
                root.Children.Add(overlay);
                PlayCount++;
                bool motion = SystemParameters.ClientAreaAnimation;
                effectEnds = DateTime.UtcNow.AddMilliseconds(motion ? 1650 : 650);
                ObserverState = motion ? "playing" : "playing-static";
                if (motion)
                {
                    var transforms = new TransformGroup();
                    var scale = new ScaleTransform(.94, .94);
                    var offset = new TranslateTransform(0, 8);
                    transforms.Children.Add(scale); transforms.Children.Add(offset);
                    overlay.RenderTransform = transforms;
                    var opacity = new DoubleAnimationUsingKeyFrames();
                    opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
                    opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1190))));
                    opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
                    Animate(overlay, UIElement.OpacityProperty, opacity);
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    Animate(scale, ScaleTransform.ScaleXProperty, new DoubleAnimation(.94, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
                    Animate(scale, ScaleTransform.ScaleYProperty, new DoubleAnimation(.94, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
                    Animate(offset, TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
                    var ringScale = (ScaleTransform)ring.RenderTransform;
                    Animate(ringScale, ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.48, TimeSpan.FromMilliseconds(1050)));
                    Animate(ringScale, ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.48, TimeSpan.FromMilliseconds(1050)));
                    Animate(ring, UIElement.OpacityProperty, new DoubleAnimation(.5, 0, TimeSpan.FromMilliseconds(1050)));
                    Animate(check, UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(150) });
                }
                StartActive();
            }

            void Animate(Animatable target, DependencyProperty property, AnimationTimeline animation)
            {
                target.BeginAnimation(property, animation);
                clocks.Add(delegate { target.BeginAnimation(property, null); });
            }

            void Animate(UIElement target, DependencyProperty property, AnimationTimeline animation)
            {
                target.BeginAnimation(property, animation);
                clocks.Add(delegate { target.BeginAnimation(property, null); });
            }

            void EndEffect()
            {
                foreach (var stop in clocks) { try { stop(); } catch { } }
                clocks.Clear();
                if (overlay != null) { root.Children.Remove(overlay); overlay = null; }
                StopActive();
            }

            void Cancel(string reason)
            {
                pending = false; armed = false;
                realLoader = null; watcher = null;
                EndEffect();
                ObserverState = reason;
                StopActive();
            }

            void FailClosed()
            {
                // Do not let reflected process/property access escape into native code.
                try
                {
                    if (!window.Dispatcher.CheckAccess())
                    {
                        if (!window.Dispatcher.HasShutdownStarted)
                            window.Dispatcher.BeginInvoke(new Action(delegate { try { Cancel("observer-error"); } catch { } }));
                        return;
                    }
                    Cancel("observer-error");
                }
                catch { }
            }

            void ActiveLayout(object sender, EventArgs args)
            {
                try { if ((pending || Playing) && !CanPresent()) Cancel("interrupted"); }
                catch { FailClosed(); }
            }
            void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
            {
                try { if (!window.IsVisible && (armed || pending || Playing)) Cancel("skipped-hidden-or-modal"); }
                catch { FailClosed(); }
            }
            void HitTestChanged(object sender, DependencyPropertyChangedEventArgs args)
            {
                try { if (!window.IsHitTestVisible && (armed || pending || Playing)) Cancel("interrupted"); }
                catch { FailClosed(); }
            }
            void WindowStateChanged(object sender, EventArgs args)
            {
                try { if (window.WindowState == WindowState.Minimized && (armed || pending || Playing)) Cancel("skipped-hidden-or-modal"); }
                catch { FailClosed(); }
            }
            void ModalChanged(object sender, DependencyPropertyChangedEventArgs args)
            {
                try { if (modal != null && modal.IsVisible && (armed || pending || Playing)) Cancel("skipped-hidden-or-modal"); }
                catch { FailClosed(); }
            }
            void WindowClosing(object sender, CancelEventArgs args)
            {
                try { closing = true; Cancel("closing"); }
                catch { FailClosed(); }
            }
            void WindowClosed(object sender, EventArgs args) { Dispose("closed"); }

            void Dispose(string reason)
            {
                if (disposed) return;
                disposed = true;
                try { Cancel(reason); } catch { timer.Stop(); }
                try
                {
                    if (Subscribed && stateEvent != null && stateHandler != null)
                        stateEvent.GetRemoveMethod(true).Invoke(outerLoader, new object[] { stateHandler });
                }
                catch { }
                Subscribed = false;
                window.IsVisibleChanged -= VisibilityChanged;
                window.IsHitTestVisibleChanged -= HitTestChanged;
                window.StateChanged -= WindowStateChanged;
                window.Closing -= WindowClosing;
                window.Closed -= WindowClosed;
                if (modal != null) modal.IsVisibleChanged -= ModalChanged;
                root.LayoutUpdated -= ActiveLayout;
                watchingLayout = false;
                timer.Tick -= Tick;
                timer.Stop();
                ObserverState = reason;
            }
        }
    }
}


