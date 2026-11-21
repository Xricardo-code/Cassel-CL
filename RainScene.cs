using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace InfiniteCL
{
    // The scene is entirely presentation: no launcher state, input interception or network calls.
    public static class RainScene
    {
        static readonly ConditionalWeakTable<Panel, Scene> Scenes = new ConditionalWeakTable<Panel, Scene>();
        static readonly Brush TextInk = Solid(236, 230, 216);
        static readonly Brush MutedInk = Solid(180, 170, 152);
        static readonly Brush Accent = Solid(200, 170, 114);
        static readonly Brush ControlBack = Solid(18, 15, 11);
        static readonly Brush ControlEdge = Solid(93, 76, 46);
        static Brush Solid(byte r, byte g, byte b)
        { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }

        sealed class Scene
        {
            public bool Enabled = true;
            public double Intensity = 45;
            public RainLayer Layer;
            public Image Image;
            public readonly List<ControlSet> Controls = new List<ControlSet>();
            public bool Updating;
            public void Refresh()
            {
                if (Layer != null) Layer.Refresh();
                Updating = true;
                try
                {
                    foreach (var controls in Controls)
                    {
                        controls.Toggle.IsChecked = Enabled;
                        controls.Slider.Value = Intensity;
                        controls.Label.Text = !Enabled ? "已关闭" : Intensity <= 0 ? "无雨" :
                            (Intensity < 30 ? "小雨" : Intensity < 70 ? "中雨" : "大雨") + " · " + Math.Round(Intensity) + "%";
                        controls.Toggle.Content = Enabled ? "雨幕  开" : "雨幕  关";
                    }
                }
                finally { Updating = false; }
            }
        }
        sealed class ControlSet
        {
            public ToggleButton Toggle;
            public Slider Slider;
            public TextBlock Label;
        }
        static Scene Get(Panel root)
        {
            if (root == null) throw new ArgumentNullException("root");
            return Scenes.GetValue(root, delegate { return new Scene(); });
        }

        public static void Install(Panel root, ImageSource background)
        {
            var scene = Get(root);
            if (scene.Layer != null)
            { if (scene.Image != null) scene.Image.Source = background; scene.Refresh(); return; }
            var backdrop = new Grid { Name = "InfiniteCLRainBackground", IsHitTestVisible = false, Focusable = false, ClipToBounds = true };
            var image = new Image { Name = "InfiniteCLRainCityImage", Source = background, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            backdrop.Background = Solid(8, 7, 6);
            backdrop.Children.Add(image); scene.Image = image;
            var shade = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(176, 8, 7, 6), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(96, 8, 7, 6), .58));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(65, 8, 7, 6), 1)); shade.Freeze();
            backdrop.Children.Add(new Border { Name = "InfiniteCLRainCityShade", Background = shade, IsHitTestVisible = false });
            Panel.SetZIndex(backdrop, -20); Grid.SetRowSpan(backdrop, 32); Grid.SetColumnSpan(backdrop, 32);
            root.Children.Insert(0, backdrop);
            var layer = new RainLayer(scene) { Name = "InfiniteCLRainOverlay", IsHitTestVisible = false, Focusable = false };
            scene.Layer = layer;
            Panel.SetZIndex(layer, 900); Grid.SetRowSpan(layer, 32); Grid.SetColumnSpan(layer, 32);
            root.Children.Add(layer); scene.Refresh();
        }

        public static FrameworkElement CreateControls(Panel root)
        {
            var scene = Get(root);
            var panel = new StackPanel { Name = "InfiniteCLRainControls", Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Panel.SetZIndex(panel, 1000);
            var toggle = new ToggleButton { Name = "InfiniteCLRainToggle", Height = 26, MinWidth = 66, Padding = new Thickness(9, 2, 9, 2),
                Background = ControlBack, BorderBrush = ControlEdge, BorderThickness = new Thickness(1), Foreground = TextInk,
                FontSize = 11, FontFamily = new FontFamily("Microsoft YaHei UI"), Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "开启或关闭动态雨幕。", Template = CreateToggleTemplate() };
            toggle.SetValue(AutomationPropertiesName(), "动态雨幕开关");
            var slider = new Slider { Name = "InfiniteCLRainIntensity", Minimum = 0, Maximum = 100, Value = scene.Intensity,
                Width = 88, Height = 26, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Accent, Background = ControlEdge, SmallChange = 5, LargeChange = 15, IsMoveToPointEnabled = true,
                TickFrequency = 5, ToolTip = "雨量：0–100。雨量同时影响雨滴数量、速度和亮度。", Template = CreateSliderTemplate() };
            slider.SetValue(AutomationPropertiesName(), "雨量强度");
            var label = new TextBlock { Name = "InfiniteCLRainLevel", Width = 73, FontSize = 10,
                Foreground = MutedInk, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Microsoft YaHei UI") };
            var controls = new ControlSet { Toggle = toggle, Slider = slider, Label = label };
            scene.Controls.Add(controls);
            toggle.Checked += delegate { if (!scene.Updating) SetEnabled(root, true); };
            toggle.Unchecked += delegate { if (!scene.Updating) SetEnabled(root, false); };
            slider.ValueChanged += delegate { if (!scene.Updating) SetIntensity(root, slider.Value); };
            panel.Children.Add(toggle); panel.Children.Add(slider); panel.Children.Add(label);
            scene.Refresh(); return panel;
        }
        static ControlTemplate CreateToggleTemplate()
        {
            // The launcher's implicit ToggleButton template is an icon-only disclosure arrow.
            // A local content template keeps this switch readable in the actual application.
            var frame = new FrameworkElementFactory(typeof(Border), "InfiniteCLRainSwitchBorder");
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter), "InfiniteCLRainSwitchContent");
            content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            frame.AppendChild(content);
            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = frame };
            var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            active.Setters.Add(new Setter(Border.BackgroundProperty, Solid(47, 37, 21), "InfiniteCLRainSwitchBorder"));
            active.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "InfiniteCLRainSwitchBorder"));
            template.Triggers.Add(active);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Solid(56, 44, 28), "InfiniteCLRainSwitchBorder"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "InfiniteCLRainSwitchBorder"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, Solid(30, 24, 16), "InfiniteCLRainSwitchBorder"));
            template.Triggers.Add(pressed);
            return template;
        }
        static ControlTemplate CreateSliderTemplate()
        {
            // Keep the native Slider/Track/Thumb contract, while isolating its
            // palette from the host application's blue implicit control style.
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
 <ControlTemplate.Resources>
  <Style x:Key='RainTrackButton' TargetType='{x:Type RepeatButton}'>
   <Setter Property='Focusable' Value='False'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'>
    <Grid Background='Transparent'><Border Height='2' VerticalAlignment='Center' Background='{TemplateBinding Background}'/></Grid>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
 </ControlTemplate.Resources>
 <Grid Height='26' Background='Transparent'>
  <Track x:Name='PART_Track' Height='18' VerticalAlignment='Center' Orientation='Horizontal'
   Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
   IsDirectionReversed='{TemplateBinding IsDirectionReversed}'
   Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
   <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource RainTrackButton}' Background='#C8AA72' Command='{x:Static Slider.DecreaseLarge}'/></Track.DecreaseRepeatButton>
   <Track.Thumb><Thumb Width='9' Height='16' Background='#C8AA72' BorderBrush='#E6CE94'>
    <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
     <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='2'/>
     <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#E6CE94'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Thumb.Template>
   </Thumb></Track.Thumb>
   <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource RainTrackButton}' Background='#5D4C2E' Command='{x:Static Slider.IncreaseLarge}'/></Track.IncreaseRepeatButton>
  </Track>
 </Grid>
</ControlTemplate>");
        }
        static DependencyProperty AutomationPropertiesName()
        { return System.Windows.Automation.AutomationProperties.NameProperty; }
        public static void SetEnabled(Panel root, bool enabled) { var scene = Get(root); scene.Enabled = enabled; scene.Refresh(); }
        public static bool GetEnabled(Panel root) { return Get(root).Enabled; }
        public static void SetIntensity(Panel root, double intensity)
        {
            var scene = Get(root); scene.Intensity = Double.IsNaN(intensity) ? 0 : Math.Max(0, Math.Min(100, intensity)); scene.Refresh();
        }
        public static double GetIntensity(Panel root) { return Get(root).Intensity; }
        // Diagnostics do not alter the scene; used by offline UI verification.
        public static bool IsAnimating(Panel root) { var l = Get(root).Layer; return l != null && l.Animating; }
        public static int GetParticleCount(Panel root) { var l = Get(root).Layer; return l == null ? 0 : l.Count; }
        public static long GetFrameCount(Panel root) { var l = Get(root).Layer; return l == null ? 0 : l.FrameCount; }

        struct Drop { public double X, Y, Depth, Phase; }
        sealed class RainLayer : FrameworkElement
        {
            const int Capacity = 350;
            readonly Scene scene;
            readonly DrawingVisual visual = new DrawingVisual();
            readonly Drop[] drops = new Drop[Capacity];
            readonly Random random = new Random(943127);
            readonly Pen[] pens = new Pen[12];
            Window owner;
            double lastFrame = -1;
            double elapsed;
            int shadeBucket = -1;
            public int Count { get; private set; }
            public bool Animating { get; private set; }
            public long FrameCount { get; private set; }
            public RainLayer(Scene value)
            {
                scene = value; ClipToBounds = true;
                AddVisualChild(visual); AddLogicalChild(visual);
                Loaded += OnLoaded; Unloaded += OnUnloaded;
                IsVisibleChanged += delegate { Refresh(); };
                IsEnabledChanged += delegate { Refresh(); };
                SizeChanged += delegate { Refresh(); };
            }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index)
            { if (index != 0) throw new ArgumentOutOfRangeException("index"); return visual; }
            void OnLoaded(object sender, RoutedEventArgs args)
            {
                owner = Window.GetWindow(this);
                if (owner != null) owner.StateChanged += OnWindowStateChanged;
                Refresh();
            }
            void OnUnloaded(object sender, RoutedEventArgs args)
            {
                if (owner != null) owner.StateChanged -= OnWindowStateChanged;
                owner = null; Stop();
            }
            void OnWindowStateChanged(object sender, EventArgs args) { Refresh(); }
            bool CanRun
            {
                get { return IsLoaded && IsVisible && IsEnabled && ActualWidth > 0 && ActualHeight > 0 &&
                    scene.Enabled && scene.Intensity > 0 && (owner == null || owner.WindowState != WindowState.Minimized); }
            }
            public void Refresh()
            {
                if (!CanRun) { Stop(); return; }
                UpdatePens();
                int target = Math.Max(1, Math.Min(Capacity, (int)Math.Round(Capacity * Math.Pow(scene.Intensity / 100, 1.16) *
                    Math.Max(.30, Math.Min(1, ActualWidth * ActualHeight / (1180 * 780))))));
                for (int i = Count; i < target; i++) Reset(i, true);
                Count = target;
                if (!Animating)
                {
                    lastFrame = -1; Animating = true;
                    CompositionTarget.Rendering += RenderFrame;
                }
                Draw();
            }
            void UpdatePens()
            {
                int bucket = Math.Max(0, Math.Min(20, (int)Math.Round(scene.Intensity / 5)));
                if (shadeBucket == bucket) return;
                shadeBucket = bucket;
                for (int i = 0; i < pens.Length; i++)
                {
                    double depth = .3 + i / 11.0 * .7;
                    int alpha = (int)((14 + bucket * 1.6) * (.35 + depth * .65));
                    var brush = new SolidColorBrush(Color.FromArgb((byte)alpha, 218, 216, 206)); brush.Freeze();
                    var pen = new Pen(brush, .4 + depth * .65) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    pen.Freeze(); pens[i] = pen;
                }
            }
            void Stop()
            {
                if (Animating) { CompositionTarget.Rendering -= RenderFrame; Animating = false; }
                lastFrame = -1; Count = 0;
                using (visual.RenderOpen()) { }
            }
            void Reset(int index, bool anywhere)
            {
                drops[index] = new Drop { X = random.NextDouble() * (ActualWidth + 130),
                    Y = anywhere ? random.NextDouble() * ActualHeight : -random.NextDouble() * 90,
                    Depth = .3 + random.NextDouble() * .7, Phase = random.NextDouble() * Math.PI * 2 };
            }
            void RenderFrame(object sender, EventArgs args)
            {
                // Visibility and window state changes normally unsubscribe before another frame.
                if (!CanRun) { Stop(); return; }
                var rendering = args as RenderingEventArgs; if (rendering == null) return;
                double now = rendering.RenderingTime.TotalSeconds;
                if (lastFrame >= 0 && now - lastFrame < 1.0 / 30 - .0001) return;
                double dt = lastFrame < 0 ? 1.0 / 30 : Math.Max(0, Math.Min(.08, now - lastFrame));
                lastFrame = now; elapsed += dt;
                double speed = 260 + scene.Intensity * 6.2;
                for (int i = 0; i < Count; i++)
                {
                    var drop = drops[i];
                    double distance = speed * (.35 + drop.Depth * .75) * dt;
                    drop.Y += distance; drop.X -= distance * .19;
                    drops[i] = drop;
                    if (drop.Y > ActualHeight + 38 || drop.X < -60) Reset(i, false);
                }
                Draw(); FrameCount++;
            }
            void Draw()
            {
                using (var dc = visual.RenderOpen())
                {
                    double length = 7 + scene.Intensity * .135;
                    for (int i = 0; i < Count; i++)
                    {
                        var d = drops[i];
                        int penIndex = Math.Max(0, Math.Min(11, (int)((d.Depth - .3) / .7 * 11)));
                        double streak = length * (.5 + d.Depth * .9);
                        // Far streaks stay fine and quiet; near streaks supply the moving rain texture.
                        dc.DrawLine(pens[penIndex], new Point(d.X, d.Y), new Point(d.X + streak * .19, d.Y - streak));
                    }
                }
            }
        }
    }
}
