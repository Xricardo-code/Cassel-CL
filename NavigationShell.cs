using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace InfiniteCL
{
    // Presentation-only navigation. The original radio-button group and its events
    // remain the launcher navigation authority; this rail is an accessible view.
    public static class NavigationShell
    {
        const double RailWidth = 76;
        static readonly ConditionalWeakTable<Window, Controller> Controllers = new ConditionalWeakTable<Window, Controller>();
        static readonly Brush Canvas = Brush("#F00A0908"), Edge = Brush("#594A31"),
            Normal = Brush("#B7AD9B"), Active = Brush("#E6CE94"),
            ActiveBack = Brush("#403F301A"), HoverBack = Brush("#30362B1D"), Transparent = Brushes.Transparent;

        public static void Install(Window window, Panel form)
        {
            if (window == null || form == null) return;
            Controller current;
            if (Controllers.TryGetValue(window, out current)) return;
            var controller = new Controller(window, form);
            if (!controller.Install()) { controller.Dispose(); return; }
            Controllers.Add(window, controller);
        }

        static Brush Brush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze(); return brush;
        }

        static FrameworkElement Find(DependencyObject root, string name)
        {
            var element = root as FrameworkElement;
            if (element != null && element.Name == name) return element;
            if (!(root is Visual)) return null;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var result = Find(VisualTreeHelper.GetChild(root, index), name);
                if (result != null) return result;
            }
            return null;
        }

        sealed class Entry
        {
            public FrameworkElement Original;
            public Button Proxy;
            public Border Face, Indicator;
            public TextBlock Label;
            public Path Icon;
            public PropertyInfo Checked;
            public MethodInfo Select;
        }

        sealed class Controller : IDisposable
        {
            readonly Window window;
            readonly Panel form;
            readonly List<Action> remove = new List<Action>();
            readonly List<Entry> entries = new List<Entry>();
            FrameworkElement content, leftBackground, nativeHeader, title, nativeModal;
            Border rail;
            bool disposed, widthChanging, syncQueued;

            public Controller(Window window, Panel form) { this.window = window; this.form = form; }

            public bool Install()
            {
                var nativeNav = Find(window, "PanTitleSelect") as Panel;
                content = Find(window, "PanMain");
                leftBackground = Find(window, "PanLeft");
                nativeHeader = Find(window, "PanTitleMain");
                title = Find(window, "PanTitle");
                nativeModal = Find(window, "PanMsg");
                if (nativeNav == null || content == null || title == null) return false;

                title.Height = 64;
                nativeNav.Opacity = 0;
                nativeNav.IsHitTestVisible = false;
                // Keep every native child and its index for PageChange's selection logic.
                foreach (UIElement child in nativeNav.Children) child.Focusable = false;

                var inner = Find(window, "PanTitleInner");
                if (inner != null)
                {
                    inner.Height = 40; inner.VerticalAlignment = VerticalAlignment.Bottom;
                    inner.Margin = new Thickness(335, 0, 0, 10);
                    inner.MaxWidth = Math.Max(250, window.ActualWidth - 700);
                }

                rail = new Border
                {
                    Name = "InfiniteCLNavigationRail", Width = RailWidth,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Canvas, BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 1, 0)
                };
                // Native dialogs are the final form child. Keep the new rail below
                // their original visual/input layer, including the first-run EULA.
                Grid.SetRow(rail, 1); Panel.SetZIndex(rail, 0);
                var railGrid = new Grid { Name = "InfiniteCLRailGrid" };
                railGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                railGrid.RowDefinitions.Add(new RowDefinition());
                railGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var buttons = new StackPanel { Name = "InfiniteCLRailButtons", Margin = new Thickness(7, 17, 7, 0) };
                railGrid.Children.Add(buttons);
                var end = new TextBlock
                {
                    Name = "InfiniteCLRailSignature", Text = "C C L", FontSize = 9,
                    Foreground = Brush("#C8AA72"), HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20), IsHitTestVisible = false
                };
                Grid.SetRow(end, 2); railGrid.Children.Add(end); rail.Child = railGrid;

                string[] names = { "启程", "档案", "联机", "设置", "执行部" };
                string[] descriptions = { "启动游戏", "下载与安装资源", "联机", "启动器设置", "帮助、工具与关于" };
                string[] icons = {
                    "M4,3 L20,12 4,21 Z",
                    "M12,3 L12,16 M6,10 L12,16 18,10 M4,18 L4,22 20,22 20,18",
                    "M3,9 C8,4 16,4 21,9 M6,13 C10,9 14,9 18,13 M9,17 C11,15 13,15 15,17 M12,21 L12,21.1",
                    "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21",
                    "M4,4 L9,4 9,9 4,9 Z M15,4 L20,4 20,9 15,9 Z M4,15 L9,15 9,20 4,20 Z M15,15 L20,15 20,20 15,20 Z"
                };
                for (int index = 0; index < names.Length; index++)
                {
                    var original = Find(window, "BtnTitleSelect" + index);
                    if (original == null) continue;
                    var entry = CreateEntry(original, index, names[index], descriptions[index], icons[index]);
                    if (entry == null) continue;
                    entries.Add(entry); buttons.Children.Add(entry.Proxy);
                    Watch(original, UIElement.VisibilityProperty, QueueSync);
                    Watch(original, UIElement.IsEnabledProperty, QueueSync);
                    // Native Check has signature (Object, Boolean), independent of its delegate name.
                    var checkEvent = original.GetType().GetEvent("Check");
                    if (checkEvent != null)
                    {
                        var handler = Delegate.CreateDelegate(checkEvent.EventHandlerType, this,
                            typeof(Controller).GetMethod("OriginalChecked", BindingFlags.Instance | BindingFlags.NonPublic), false);
                        if (handler != null)
                        {
                            checkEvent.AddEventHandler(original, handler);
                            remove.Add(delegate { checkEvent.RemoveEventHandler(original, handler); });
                        }
                    }
                }
                if (entries.Count == 0) return false;

                SetLeftMargin(content);
                if (leftBackground != null) SetLeftMargin(leftBackground);
                Watch(content, FrameworkElement.WidthProperty, AdjustWidth);
                Watch(content, FrameworkElement.HeightProperty, AdjustWidth);
                Watch(title, FrameworkElement.HeightProperty, AdjustWidth);
                if (nativeHeader != null)
                {
                    Watch(nativeHeader, UIElement.IsHitTestVisibleProperty, QueueSync);
                    Watch(nativeHeader, UIElement.VisibilityProperty, QueueSync);
                }
                if (nativeModal != null) Watch(nativeModal, UIElement.VisibilityProperty, QueueSync);
                SizeChangedEventHandler changed = delegate { AdjustWidth(); UpdateInnerWidth(); };
                form.SizeChanged += changed; remove.Add(delegate { form.SizeChanged -= changed; });
                RoutedEventHandler unloaded = delegate { Dispose(); };
                form.Unloaded += unloaded; remove.Add(delegate { form.Unloaded -= unloaded; });
                EventHandler closed = delegate { Dispose(); };
                window.Closed += closed; remove.Add(delegate { window.Closed -= closed; });
                int modalIndex = nativeModal == null ? -1 : form.Children.IndexOf(nativeModal);
                if (modalIndex >= 0) form.Children.Insert(modalIndex, rail);
                else form.Children.Add(rail);
                AdjustWidth(); UpdateInnerWidth(); Sync();
                return true;
            }

            Entry CreateEntry(FrameworkElement original, int index, string name, string description, string geometry)
            {
                var entry = new Entry { Original = original };
                entry.Checked = original.GetType().GetProperty("Checked");
                entry.Select = original.GetType().GetMethod("SetChecked", new[] { typeof(bool), typeof(bool), typeof(bool) });
                if (entry.Checked == null || entry.Select == null) return null;
                entry.Face = new Border { Name = "InfiniteCLRailFace", CornerRadius = new CornerRadius(5), Background = Transparent };
                var faceGrid = new Grid { Name = "InfiniteCLRailFaceGrid" };
                var stack = new StackPanel { Name = "InfiniteCLRailItem", VerticalAlignment = VerticalAlignment.Center };
                entry.Icon = new Path
                {
                    Name = "InfiniteCLRailIcon", Data = Geometry.Parse(geometry), Stroke = Normal,
                    StrokeThickness = 1.55, StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Width = 22, Height = 22, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6)
                };
                entry.Label = new TextBlock
                {
                    Name = "InfiniteCLRailLabel", Text = name, FontSize = 10, Foreground = Normal,
                    HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Medium
                };
                stack.Children.Add(entry.Icon); stack.Children.Add(entry.Label); faceGrid.Children.Add(stack);
                entry.Indicator = new Border
                {
                    Name = "InfiniteCLRailIndicator", Width = 2, Height = 22,
                    Background = Active, HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(1), Visibility = Visibility.Hidden
                };
                faceGrid.Children.Add(entry.Indicator); entry.Face.Child = faceGrid;
                entry.Proxy = new Button
                {
                    Name = "InfiniteCLNav" + index, Height = 64, Margin = new Thickness(0, 0, 0, 9),
                    Padding = new Thickness(0), Background = Transparent, BorderThickness = new Thickness(0),
                    Content = entry.Face, Cursor = Cursors.Hand, ToolTip = description,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                System.Windows.Automation.AutomationProperties.SetName(entry.Proxy, description);
                var template = new ControlTemplate(typeof(Button));
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
                template.VisualTree = presenter; entry.Proxy.Template = template;
                entry.Proxy.Click += delegate
                {
                    if (!entry.Proxy.IsEnabled) return;
                    try { entry.Select.Invoke(entry.Original, new object[] { true, true, true }); }
                    finally { QueueSync(); }
                };
                entry.Proxy.MouseEnter += delegate { Paint(entry); };
                entry.Proxy.MouseLeave += delegate { Paint(entry); };
                entry.Proxy.GotKeyboardFocus += delegate { Paint(entry); };
                entry.Proxy.LostKeyboardFocus += delegate { Paint(entry); };
                return entry;
            }

            void SetLeftMargin(FrameworkElement element)
            {
                var margin = element.Margin;
                element.SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(RailWidth, margin.Top, margin.Right, margin.Bottom));
            }

            void UpdateInnerWidth()
            {
                var inner = Find(window, "PanTitleInner");
                if (inner != null) inner.MaxWidth = Math.Max(180, form.ActualWidth - 660);
            }

            void AdjustWidth()
            {
                if (disposed || widthChanging || content == null) return;
                double width = form.ActualWidth;
                if (width <= RailWidth || Double.IsNaN(width)) return;
                double target = Math.Max(0, width - RailWidth);
                widthChanging = true;
                try
                {
                    if (Double.IsNaN(content.Width) || Math.Abs(content.Width - target) >= .01)
                        content.SetCurrentValue(FrameworkElement.WidthProperty, target);
                    double headerHeight = Double.IsNaN(title.Height) ? title.ActualHeight : title.Height;
                    double height = Math.Max(0, form.ActualHeight - headerHeight);
                    if (form.ActualHeight > headerHeight && (Double.IsNaN(content.Height) || Math.Abs(content.Height - height) >= .01))
                        content.SetCurrentValue(FrameworkElement.HeightProperty, height);
                }
                finally { widthChanging = false; }
            }

            void Watch(DependencyObject source, DependencyProperty property, Action action)
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(property, source.GetType());
                if (descriptor == null) return;
                EventHandler handler = delegate { if (!disposed) action(); };
                descriptor.AddValueChanged(source, handler);
                remove.Add(delegate { descriptor.RemoveValueChanged(source, handler); });
            }

            void OriginalChecked(object sender, bool raisedByMouse) { QueueSync(); }

            void QueueSync()
            {
                if (disposed || syncQueued) return;
                syncQueued = true;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    syncQueued = false; if (!disposed) Sync();
                }));
            }

            void Sync()
            {
                foreach (var entry in entries)
                {
                    entry.Proxy.Visibility = entry.Original.Visibility;
                    entry.Proxy.IsEnabled = entry.Original.IsEnabled &&
                        (nativeHeader == null || (nativeHeader.IsHitTestVisible && nativeHeader.Visibility == Visibility.Visible)) &&
                        (nativeModal == null || nativeModal.Visibility != Visibility.Visible);
                    entry.Proxy.Opacity = entry.Proxy.IsEnabled ? 1 : .45;
                    Paint(entry);
                }
            }

            void Paint(Entry entry)
            {
                bool selected = Object.Equals(entry.Checked.GetValue(entry.Original, null), true);
                bool hover = entry.Proxy.IsMouseOver || entry.Proxy.IsKeyboardFocusWithin;
                entry.Face.Background = selected ? ActiveBack : hover ? HoverBack : Transparent;
                entry.Face.BorderBrush = entry.Proxy.IsKeyboardFocusWithin ? Active : Transparent;
                entry.Face.BorderThickness = new Thickness(1);
                entry.Indicator.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
                entry.Icon.Stroke = selected || hover ? Active : Normal;
                entry.Label.Foreground = selected || hover ? Active : Normal;
            }

            public void Dispose()
            {
                if (disposed) return; disposed = true;
                foreach (var action in remove) action();
                remove.Clear();
                if (rail != null) form.Children.Remove(rail);
                Controllers.Remove(window);
            }
        }
    }
}
