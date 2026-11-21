using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace InfiniteCL
{
    // Presentation only. No timer, native hooks, persistence, or input handling.
    public static class PointerLight
    {
        static readonly ConditionalWeakTable<Panel, LightLayer> Layers = new ConditionalWeakTable<Panel, LightLayer>();
        static bool watchingLoads;

        public static void Install(Panel root)
        {
            if (root == null) throw new ArgumentNullException("root");
            root.Dispatcher.VerifyAccess();
            LightLayer layer;
            if (!Layers.TryGetValue(root, out layer))
            {
                layer = new LightLayer(root);
                Layers.Add(root, layer);
                root.Children.Add(layer);
            }
            else if (VisualTreeHelper.GetParent(layer) == null) root.Children.Add(layer);

            if (!watchingLoads)
            {
                watchingLoads = true;
                // Loaded is a direct routed event. This discovers newly created cards
                // without scanning the complete tree on each LayoutUpdated.
                EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler(TargetLoaded), true);
            }
            if (root.IsLoaded) layer.Activate();
        }

        // Optional deterministic QA API. Coordinates are relative to the installed root.
        // It is never called automatically by the launcher.
        public static void SetPreview(Panel root, Point point)
        {
            Install(root);
            LightLayer layer;
            if (Layers.TryGetValue(root, out layer)) layer.Preview(point);
        }

        public static void ClearPreview(Panel root)
        {
            if (root == null) return;
            root.Dispatcher.VerifyAccess();
            LightLayer layer;
            if (Layers.TryGetValue(root, out layer)) layer.EndPreview();
        }

        static bool IsSurface(FrameworkElement element)
        {
            if (element is Border && (element.Name == "InfiniteCLSurface" || element.Name.StartsWith("InfiniteCLDragonSurface", StringComparison.Ordinal))) return true;
            for (Type type = element.GetType(); type != null; type = type.BaseType)
                if (type.FullName == "PCL.MyCard" || type.FullName == "PCL.MyButton") return true;
            return false;
        }

        static void TargetLoaded(object sender, RoutedEventArgs args)
        {
            FrameworkElement element = sender as FrameworkElement;
            if (element == null || !IsSurface(element)) return;
            DependencyObject current = element;
            while (current != null)
            {
                Panel panel = current as Panel;
                LightLayer layer;
                if (panel != null && Layers.TryGetValue(panel, out layer))
                {
                    layer.DiscoverAfterLayout();
                    return;
                }
                if (!(current is Visual)) return;
                current = VisualTreeHelper.GetParent(current);
            }
        }

        sealed class Surface
        {
            internal FrameworkElement Element;
            internal Rect Bounds;
            internal Rect Visible;
            internal double Radius;
            internal double Opacity;
            internal RectangleGeometry Outline;
            internal RectangleGeometry Clip;
        }

        sealed class LightLayer : FrameworkElement
        {
            const double Radius = 210;
            readonly Panel root;
            readonly DrawingVisual drawing = new DrawingVisual();
            readonly RadialGradientBrush ambient;
            readonly RadialGradientBrush edge;
            readonly Pen edgePen;
            readonly List<Surface> surfaces = new List<Surface>();
            readonly MouseEventHandler moveHandler;
            DispatcherOperation pending;
            bool active;
            bool discover = true;
            bool preview;
            bool pointerInside;
            Size recordedSize;

            internal LightLayer(Panel root)
            {
                this.root = root;
                Name = "InfiniteCLPointerLight";
                IsHitTestVisible = false;
                Focusable = false;
                ClipToBounds = true;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRow(this, 0);
                Grid.SetRowSpan(this, 2);
                Grid.SetColumnSpan(this, 2);
                Panel.SetZIndex(this, 1000);
                AddVisualChild(drawing);
                drawing.Opacity = 0;

                // Absolute coordinates keep the same light source across every edge.
                ambient = Radial(new[] {
                    new GradientStop(Color.FromArgb(31, 245, 248, 250), 0),
                    new GradientStop(Color.FromArgb(22, 245, 248, 250), .24),
                    new GradientStop(Color.FromArgb(9, 245, 248, 250), .60),
                    new GradientStop(Color.FromArgb(0, 245, 248, 250), 1)
                });
                edge = Radial(new[] {
                    new GradientStop(Color.FromArgb(94, 250, 252, 255), 0),
                    new GradientStop(Color.FromArgb(55, 250, 252, 255), .40),
                    new GradientStop(Color.FromArgb(0, 250, 252, 255), 1)
                });
                edgePen = new Pen(edge, 1.15);
                moveHandler = MouseMoved;
                root.Loaded += RootLoaded;
                root.Unloaded += RootUnloaded;
            }

            static RadialGradientBrush Radial(GradientStop[] stops)
            {
                RadialGradientBrush brush = new RadialGradientBrush();
                brush.MappingMode = BrushMappingMode.Absolute;
                brush.RadiusX = Radius;
                brush.RadiusY = Radius;
                brush.SpreadMethod = GradientSpreadMethod.Pad;
                foreach (GradientStop stop in stops) brush.GradientStops.Add(stop);
                return brush;
            }

            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index)
            {
                if (index != 0) throw new ArgumentOutOfRangeException("index");
                return drawing;
            }
            protected override Size MeasureOverride(Size availableSize) { return new Size(); }

            void RootLoaded(object sender, RoutedEventArgs args) { Activate(); }
            void RootUnloaded(object sender, RoutedEventArgs args) { Deactivate(); }

            internal void Activate()
            {
                if (active) return;
                active = true;
                discover = true;
                root.AddHandler(Mouse.MouseMoveEvent, moveHandler, true);
                root.MouseEnter += MouseEntered;
                root.MouseLeave += MouseLeft;
                root.LayoutUpdated += LayoutChanged;
                root.SizeChanged += RootSizeChanged;
                QueueRefresh();
                if (!preview) UpdatePoint(Mouse.GetPosition(root), root.IsMouseOver);
            }

            void Deactivate()
            {
                if (!active) return;
                active = false;
                root.RemoveHandler(Mouse.MouseMoveEvent, moveHandler);
                root.MouseEnter -= MouseEntered;
                root.MouseLeave -= MouseLeft;
                root.LayoutUpdated -= LayoutChanged;
                root.SizeChanged -= RootSizeChanged;
                if (pending != null && pending.Status == DispatcherOperationStatus.Pending) pending.Abort();
                pending = null;
                surfaces.Clear();
                pointerInside = false;
                preview = false;
                drawing.Opacity = 0;
                using (DrawingContext context = drawing.RenderOpen()) { }
            }

            internal void DiscoverAfterLayout()
            {
                discover = true;
                // Loaded is raised after arrange; a coalesced refresh is safe here too.
                if (active) QueueRefresh();
            }

            void LayoutChanged(object sender, EventArgs args) { QueueRefresh(); }
            void RootSizeChanged(object sender, SizeChangedEventArgs args) { QueueRefresh(); }
            void QueueRefresh()
            {
                if (!active || (pending != null && pending.Status == DispatcherOperationStatus.Pending)) return;
                pending = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Refresh));
            }

            void Collect(DependencyObject node)
            {
                if (Object.ReferenceEquals(node, this)) return;
                FrameworkElement element = node as FrameworkElement;
                if (element != null && IsSurface(element)) surfaces.Add(new Surface { Element = element });
                if (!(node is Visual)) return;
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++) Collect(VisualTreeHelper.GetChild(node, i));
            }

            static double SurfaceRadius(FrameworkElement element)
            {
                for (Type type = element.GetType(); type != null; type = type.BaseType)
                {
                    if (type.FullName == "PCL.MyButton") return 10;
                    if (type.FullName == "PCL.MyCard") return 14;
                }
                Border border = element as Border;
                return border == null ? 12 : border.CornerRadius.TopLeft;
            }

            bool ReadSurface(Surface surface, Rect rootBounds, out Rect bounds, out Rect visible, out double opacity)
            {
                bounds = Rect.Empty;
                visible = rootBounds;
                opacity = 1;
                FrameworkElement element = surface.Element;
                if (!element.IsLoaded || !element.IsVisible || element.ActualWidth < 4 || element.ActualHeight < 4) return false;
                try
                {
                    bounds = element.TransformToAncestor(root).TransformBounds(new Rect(new Size(element.ActualWidth, element.ActualHeight)));
                    if (!bounds.IntersectsWith(rootBounds)) return false;
                    DependencyObject ancestor = element;
                    while (ancestor != null && !Object.ReferenceEquals(ancestor, root))
                    {
                        FrameworkElement parent = ancestor as FrameworkElement;
                        if (parent != null)
                        {
                            opacity *= parent.Opacity;
                            if (opacity < .005) return false;
                            // Avoid drawing a scrolled card's edge outside its viewport.
                            if (!Object.ReferenceEquals(parent, element) && (parent.ClipToBounds || parent is ScrollViewer))
                            {
                                Rect clip = parent.TransformToAncestor(root).TransformBounds(new Rect(new Size(parent.ActualWidth, parent.ActualHeight)));
                                visible.Intersect(clip);
                                if (visible.IsEmpty || !bounds.IntersectsWith(visible)) return false;
                            }
                        }
                        ancestor = VisualTreeHelper.GetParent(ancestor);
                    }
                    return Object.ReferenceEquals(ancestor, root);
                }
                catch (InvalidOperationException) { return false; }
            }

            void Refresh()
            {
                pending = null;
                if (!active) return;
                bool changed = discover;
                if (discover)
                {
                    surfaces.Clear();
                    Collect(root);
                    discover = false;
                }
                Size size = new Size(Math.Max(0, root.ActualWidth), Math.Max(0, root.ActualHeight));
                if (size != recordedSize) { recordedSize = size; changed = true; }
                Rect rootBounds = new Rect(size);
                for (int i = surfaces.Count - 1; i >= 0; i--)
                {
                    Surface surface = surfaces[i];
                    Rect bounds, visible;
                    double opacity;
                    if (!ReadSurface(surface, rootBounds, out bounds, out visible, out opacity))
                    {
                        if (surface.Outline != null) { surface.Outline = null; changed = true; }
                        // Release removed page controls; they are rediscovered on Loaded.
                        if (!surface.Element.IsLoaded) surfaces.RemoveAt(i);
                        continue;
                    }
                    double radius = SurfaceRadius(surface.Element);
                    if (surface.Outline != null && surface.Bounds == bounds && surface.Visible == visible &&
                        surface.Radius == radius && Math.Abs(surface.Opacity - opacity) < .001) continue;
                    surface.Bounds = bounds;
                    surface.Visible = visible;
                    surface.Radius = radius;
                    surface.Opacity = opacity;
                    Rect strokeBounds = bounds;
                    strokeBounds.Inflate(-.65, -.65);
                    surface.Outline = new RectangleGeometry(strokeBounds, radius, radius);
                    surface.Outline.Freeze();
                    surface.Clip = new RectangleGeometry(visible);
                    surface.Clip.Freeze();
                    changed = true;
                }
                if (changed) RecordDrawing(rootBounds);
            }

            void RecordDrawing(Rect rootBounds)
            {
                using (DrawingContext context = drawing.RenderOpen())
                {
                    context.DrawRectangle(ambient, null, rootBounds);
                    foreach (Surface surface in surfaces)
                    {
                        if (surface.Outline == null) continue;
                        context.PushClip(surface.Clip);
                        context.PushOpacity(surface.Opacity);
                        context.DrawGeometry(null, edgePen, surface.Outline);
                        context.Pop();
                        context.Pop();
                    }
                }
            }

            void MouseMoved(object sender, MouseEventArgs args)
            {
                if (!preview) UpdatePoint(args.GetPosition(root), true);
            }
            void MouseEntered(object sender, MouseEventArgs args)
            {
                if (!preview) UpdatePoint(args.GetPosition(root), true);
            }
            void MouseLeft(object sender, MouseEventArgs args)
            {
                if (!preview) UpdatePoint(new Point(), false);
            }

            void UpdatePoint(Point point, bool inside)
            {
                pointerInside = inside && !Double.IsNaN(point.X) && !Double.IsNaN(point.Y) &&
                    !Double.IsInfinity(point.X) && !Double.IsInfinity(point.Y) &&
                    point.X >= 0 && point.Y >= 0 && point.X <= root.ActualWidth && point.Y <= root.ActualHeight;
                if (pointerInside)
                {
                    // Updating a DrawingVisual brush resource schedules WPF rendering
                    // without InvalidateVisual (which would also invalidate Arrange).
                    // Mouse movement therefore never causes layout or tree discovery.
                    ambient.Center = point;
                    ambient.GradientOrigin = point;
                    edge.Center = point;
                    edge.GradientOrigin = point;
                }
                drawing.Opacity = pointerInside && active ? 1 : 0;
            }

            internal void Preview(Point point) { preview = true; UpdatePoint(point, true); }
            internal void EndPreview() { preview = false; UpdatePoint(Mouse.GetPosition(root), root.IsMouseOver); }
        }
    }
}
