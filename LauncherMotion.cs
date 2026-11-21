using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace InfiniteCL
{
    // Motion belongs to this presentation layer. Native launcher controls, modal
    // decisions, bindings and commands are never animation targets.
    public static class LauncherMotion
    {
        static readonly ConditionalWeakTable<Window, Controller> Controllers = new ConditionalWeakTable<Window, Controller>();
        static readonly string[] SurfaceNames = {
            "InfiniteCLDragonSurfaceFolio", "InfiniteCLDragonSurfaceCommand01",
            "InfiniteCLDragonSurfaceCommand02", "InfiniteCLDragonSurfaceCommand03",
            "InfiniteCLHomeCredits", "InfiniteCLArchiveFooter"
        };

        public static void Install(Window window, Panel contentRoot, ImageSource crest)
        {
            if (window == null || contentRoot == null) return;
            Controller existing;
            if (Controllers.TryGetValue(window, out existing)) return;
            var controller = new Controller(window, contentRoot, crest);
            Controllers.Add(window, controller);
            controller.Attach();
        }

        public static bool IsPlaying(Window window)
        {
            Controller controller;
            return window != null && Controllers.TryGetValue(window, out controller) && controller.Playing;
        }

        public static string GetPhase(Window window)
        {
            Controller controller;
            return window != null && Controllers.TryGetValue(window, out controller) ? controller.Phase : "idle";
        }

        public static int GetActiveAnimationCount(Window window)
        {
            Controller controller;
            return window != null && Controllers.TryGetValue(window, out controller) ? controller.ActiveCount : 0;
        }

        static FrameworkElement Find(DependencyObject root, string name)
        {
            var element = root as FrameworkElement;
            if (element != null && element.Name == name) return element;
            if (!(root is Visual)) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var result = Find(VisualTreeHelper.GetChild(root, i), name);
                if (result != null) return result;
            }
            return null;
        }

        static Brush Brush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze(); return brush;
        }

        sealed class Surface
        {
            internal FrameworkElement Element;
            internal Transform Original;
            internal TransformGroup Wrapper;
        }

        sealed class Controller
        {
            readonly Window window;
            readonly Panel root;
            readonly ImageSource crest;
            readonly List<Action> clocks = new List<Action>();
            readonly List<Surface> surfaces = new List<Surface>();
            readonly DispatcherTimer timer;
            FrameworkElement home, modal;
            Grid overlay;
            Button skip;
            IInputElement priorFocus;
            DispatcherOperation queued;
            DateTime deadline;
            bool started, disposed, watchingLayout, homeWasVisible;
            internal string Phase = "idle";
            internal bool Playing { get { return Phase == "intro" || Phase == "entering"; } }
            internal int ActiveCount { get { return clocks.Count; } }

            internal Controller(Window window, Panel root, ImageSource crest)
            {
                this.window = window; this.root = root; this.crest = crest;
                timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher);
                timer.Interval = TimeSpan.FromMilliseconds(25);
                timer.Tick += Tick;
            }

            internal void Attach()
            {
                window.Loaded += WindowLoaded;
                window.IsVisibleChanged += WindowVisibility;
                window.Closed += WindowClosed;
                window.PreviewKeyDown += PreviewKey;
                root.LayoutUpdated += Layout;
                watchingLayout = true;
                Discover();
                QueueStart();
            }

            void QueueStart()
            {
                if (disposed || started || queued != null || !window.IsLoaded || !window.IsVisible) return;
                queued = window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    queued = null;
                    if (disposed || started || !window.IsLoaded || !window.IsVisible) return;
                    Discover(); started = true;
                    if (!SystemParameters.ClientAreaAnimation || ModalVisible()) { Finish(); return; }
                    BeginIntro();
                }));
            }

            void WindowLoaded(object sender, RoutedEventArgs args) { Discover(); QueueStart(); }
            void Layout(object sender, EventArgs args) { Discover(); QueueStart(); }
            void WindowVisibility(object sender, DependencyPropertyChangedEventArgs args)
            {
                if (disposed) return;
                if (!window.IsVisible) { if (Playing) Finish(); }
                else QueueStart();
            }

            void Discover()
            {
                if (disposed) return;
                if (home == null)
                {
                    home = Find(root, "InfiniteCLDragonHome");
                    if (home != null)
                    {
                        homeWasVisible = home.IsVisible;
                        home.IsVisibleChanged += HomeVisibility;
                    }
                }
                if (modal == null)
                {
                    modal = Find(window, "PanMsg");
                    if (modal != null) modal.IsVisibleChanged += ModalVisibility;
                }
                if (home != null && modal != null && watchingLayout)
                {
                    root.LayoutUpdated -= Layout; watchingLayout = false;
                }
            }

            bool ModalVisible() { return modal != null && modal.IsVisible; }

            void ModalVisibility(object sender, DependencyPropertyChangedEventArgs args)
            {
                if (!disposed && ModalVisible() && Playing) Finish();
            }

            void HomeVisibility(object sender, DependencyPropertyChangedEventArgs args)
            {
                bool visible = home != null && home.IsVisible;
                bool returned = visible && !homeWasVisible;
                homeWasVisible = visible;
                if (disposed || !started) return;
                if (!visible && Phase == "entering") Finish();
                else if (returned && Phase == "idle" && window.IsVisible && !ModalVisible() && SystemParameters.ClientAreaAnimation)
                    BeginEntrance();
            }

            void PreviewKey(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape && Phase == "intro" && !ModalVisible())
                {
                    args.Handled = true; BeginEntrance();
                }
            }

            void SkipClick(object sender, RoutedEventArgs args)
            {
                if (Phase == "intro") BeginEntrance();
            }

            void Tick(object sender, EventArgs args)
            {
                if (disposed) return;
                Discover();
                if (!window.IsVisible || ModalVisible() || !SystemParameters.ClientAreaAnimation) { Finish(); return; }
                if (DateTime.UtcNow < deadline) return;
                if (Phase == "intro") BeginEntrance();
                else Finish();
            }

            void Run(UIElement element, DependencyProperty property, AnimationTimeline animation)
            {
                animation.FillBehavior = FillBehavior.Stop;
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                clocks.Add(delegate { element.BeginAnimation(property, null); });
            }

            void Run(Animatable element, DependencyProperty property, AnimationTimeline animation)
            {
                animation.FillBehavior = FillBehavior.Stop;
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                clocks.Add(delegate { element.BeginAnimation(property, null); });
            }

            static DoubleAnimation Fade(double from, double to, double milliseconds)
            {
                return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
            }

            static DoubleAnimationUsingKeyFrames Delayed(double from, double to, int delay, int duration)
            {
                var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(delay + duration) };
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + duration)),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                return animation;
            }

            static TextBlock Label(string value, double size, string color)
            {
                return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color),
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false };
            }

            void BeginIntro()
            {
                if (disposed || ModalVisible()) { Finish(); return; }
                Phase = "intro"; priorFocus = Keyboard.FocusedElement;
                overlay = new Grid { Name = "InfiniteCLMotionOverlay", Margin = new Thickness(0, 64, 0, 0),
                    Background = Brush("#FA07090A"), ClipToBounds = true, Focusable = false };
                // Only the content below the live title controls is covered.
                Grid.SetRow(overlay, 0); Grid.SetColumn(overlay, 0);
                Grid.SetRowSpan(overlay, 100); Grid.SetColumnSpan(overlay, 100);
                Panel.SetZIndex(overlay, 80);

                var frame = new Border { BorderBrush = Brush("#423E3525"), BorderThickness = new Thickness(1),
                    Margin = new Thickness(25, 19, 25, 22), IsHitTestVisible = false };
                overlay.Children.Add(frame);
                var top = Label("CASSELL COLLEGE   /   EXECUTIVE ARCHIVE", 9, "#A59472");
                top.VerticalAlignment = VerticalAlignment.Top; top.Margin = new Thickness(18, 37, 18, 0);
                overlay.Children.Add(top);

                var composition = new StackPanel { VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(22, 38, 22, 52), IsHitTestVisible = false };
                var emblem = new Grid { Width = 262, Height = 262 };
                var ring = new IntroSeal { Opacity = .88, RenderTransformOrigin = new Point(.5, .5) };
                var rotation = new RotateTransform(-17); ring.RenderTransform = rotation;
                emblem.Children.Add(ring);
                var image = new Image { Source = crest, Width = 153, Height = 153, Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 1 };
                emblem.Children.Add(image); composition.Children.Add(emblem);
                var name = Label("CASSELL CL", 27, "#E1CEA2");
                name.FontFamily = new FontFamily("Georgia, Times New Roman"); name.Margin = new Thickness(0, 18, 0, 0);
                composition.Children.Add(name);
                var line = new Border { Height = 1, Width = 117, Background = Brush("#8F8F774D"), Margin = new Thickness(0, 17, 0, 15) };
                composition.Children.Add(line);
                var caption = Label("雨落狂流之暗", 16, "#D7D0BF");
                caption.FontFamily = new FontFamily("SimSun, Microsoft YaHei UI"); composition.Children.Add(caption);
                var sub = Label("世界树下  ·  准备启程", 9, "#8F8B80"); sub.Margin = new Thickness(0, 13, 0, 0);
                composition.Children.Add(sub); overlay.Children.Add(composition);

                skip = new Button { Name = "InfiniteCLMotionSkip", Content = "跳过开场  ·  Esc", FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Brush("#C7B991"),
                    Background = Brush("#E4121414"), BorderBrush = Brush("#75604D31"), BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 7, 16, 7), Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 42, 37), Focusable = true, IsTabStop = true };
                AutomationProperties.SetName(skip, "跳过开场动画");
                AutomationProperties.SetHelpText(skip, "按 Escape 或点击后进入主页。");
                skip.Click += SkipClick; overlay.Children.Add(skip); root.Children.Add(overlay);

                var opacity = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1650) };
                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(210)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1420))));
                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1650))));
                Run(overlay, UIElement.OpacityProperty, opacity);
                Run(rotation, RotateTransform.AngleProperty, Fade(-17, 10, 1650));
                Run(image, UIElement.OpacityProperty, Delayed(0, 1, 90, 670));
                Run(name, UIElement.OpacityProperty, Delayed(0, 1, 220, 570));
                Run(caption, UIElement.OpacityProperty, Delayed(0, 1, 390, 530));
                Run(sub, UIElement.OpacityProperty, Delayed(0, 1, 510, 490));
                deadline = DateTime.UtcNow.AddMilliseconds(1650); timer.Start();
            }

            void BeginEntrance()
            {
                if (disposed) return;
                ClearMotion();
                if (!window.IsVisible || ModalVisible() || !SystemParameters.ClientAreaAnimation) { Phase = "idle"; return; }
                Discover();
                Phase = "entering";
                for (int i = 0; i < SurfaceNames.Length; i++)
                {
                    var element = Find(root, SurfaceNames[i]);
                    if (element == null || !element.IsVisible) continue;
                    var translation = new TranslateTransform();
                    var original = element.RenderTransform;
                    var wrapper = new TransformGroup();
                    if (original != null) wrapper.Children.Add(original);
                    wrapper.Children.Add(translation);
                    var state = new Surface { Element = element, Original = original, Wrapper = wrapper };
                    surfaces.Add(state);
                    element.SetCurrentValue(UIElement.RenderTransformProperty, wrapper);
                    int delay = i * 43;
                    Run(element, UIElement.OpacityProperty, Delayed(0, element.Opacity, delay, 410));
                    Run(translation, TranslateTransform.YProperty, Delayed(i == 0 ? 14 : 10, 0, delay, 410));
                }
                if (surfaces.Count == 0) { Phase = "idle"; return; }
                deadline = DateTime.UtcNow.AddMilliseconds(660); timer.Start();
            }

            void ClearMotion()
            {
                timer.Stop();
                for (int i = clocks.Count - 1; i >= 0; i--) clocks[i]();
                clocks.Clear();
                foreach (var state in surfaces)
                    state.Element.SetCurrentValue(UIElement.RenderTransformProperty, state.Original);
                surfaces.Clear();
                bool skipHadFocus = skip != null && skip.IsKeyboardFocusWithin;
                if (skip != null) { skip.Click -= SkipClick; skip = null; }
                if (overlay != null) { root.Children.Remove(overlay); overlay.Children.Clear(); overlay = null; }
                if (skipHadFocus && !ModalVisible())
                {
                    var previous = priorFocus as UIElement;
                    if (previous != null && previous.IsVisible && previous.IsEnabled && previous.Focusable) previous.Focus();
                    else if (home != null) home.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
                priorFocus = null;
            }

            void Finish() { ClearMotion(); if (!disposed) Phase = "idle"; }

            void WindowClosed(object sender, EventArgs args)
            {
                if (disposed) return;
                disposed = true; ClearMotion(); Phase = "disposed";
                if (queued != null) { queued.Abort(); queued = null; }
                timer.Tick -= Tick;
                window.Loaded -= WindowLoaded;
                window.IsVisibleChanged -= WindowVisibility;
                window.Closed -= WindowClosed;
                window.PreviewKeyDown -= PreviewKey;
                if (watchingLayout) { root.LayoutUpdated -= Layout; watchingLayout = false; }
                if (home != null) home.IsVisibleChanged -= HomeVisibility;
                if (modal != null) modal.IsVisibleChanged -= ModalVisibility;
                home = null; modal = null; priorFocus = null;
            }
        }

        // Thin, antialiased linework remains sharp at every launcher size.
        sealed class IntroSeal : FrameworkElement
        {
            static readonly Pen Major = MakePen("#B7A47C", .8);
            static readonly Pen Minor = MakePen("#75654A", .55);
            static Pen MakePen(string color, double width)
            { var pen = new Pen(Brush(color), width); pen.Freeze(); return pen; }

            protected override void OnRender(DrawingContext drawing)
            {
                base.OnRender(drawing);
                var center = new Point(ActualWidth / 2, ActualHeight / 2);
                double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 5;
                if (radius <= 0) return;
                drawing.DrawEllipse(null, Major, center, radius, radius);
                drawing.DrawEllipse(null, Minor, center, radius - 7, radius - 7);
                drawing.DrawEllipse(null, Minor, center, radius * .77, radius * .77);
                for (int i = 0; i < 60; i++)
                {
                    double angle = i * Math.PI / 30;
                    double length = i % 5 == 0 ? 12 : 4;
                    drawing.DrawLine(i % 5 == 0 ? Major : Minor, Polar(center, radius - 10, angle), Polar(center, radius - 10 - length, angle));
                }
                for (int i = 0; i < 6; i++)
                {
                    double angle = i * Math.PI / 3;
                    drawing.DrawLine(Minor, Polar(center, radius * .88, angle), Polar(center, radius * .88, angle + Math.PI * 2 / 3));
                    drawing.DrawEllipse(Brush("#D0B989"), null, Polar(center, radius - 3, angle), 1.8, 1.8);
                }
            }

            static Point Polar(Point center, double radius, double angle)
            { return new Point(center.X + Math.Sin(angle) * radius, center.Y + Math.Cos(angle) * radius); }
        }
    }
}
