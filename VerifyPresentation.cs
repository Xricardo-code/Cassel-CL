// Behavioral regression checks against the compiled presentation extension.
// Uses real WPF load/unload, bindings, command dispatch, inline content and animation clocks.
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

internal sealed class TestModel : INotifyPropertyChanged
{
    string text = "initial version";
    Brush foreground = Brushes.Black, background = Brushes.White;
    public string Text { get { return text; } set { text = value; Changed("Text"); } }
    public Brush Foreground { get { return foreground; } set { foreground = value; Changed("Foreground"); } }
    public Brush Background { get { return background; } set { background = value; Changed("Background"); } }
    public event PropertyChangedEventHandler PropertyChanged;
    void Changed(string property) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(property)); }
}
internal sealed class TestCommand : ICommand
{
    public int Executions;
    public object Parameter;
    public bool CanExecute(object parameter) { return true; }
    public void Execute(object parameter) { Executions++; Parameter = parameter; }
    public event EventHandler CanExecuteChanged { add { } remove { } }
}
internal sealed class TestButton : Button { public void Fire() { OnClick(); } }

namespace PCL
{
    // Full names and inheritance match the custom-control presentation contracts.
    public class MyButton : Grid { }
    public class MyRadioButton : Border { public bool Checked { get; set; } }
}

internal static class VerifyPresentation
{
    static int checks, failures;
    static Type theme;
    static void Check(bool success, string label)
    {
        checks++;
        if (!success) failures++;
        Console.WriteLine((success ? "PASS " : "FAIL ") + label);
    }
    static void Flush()
    {
        // The production scanner coalesces layout changes for 120 ms. Pump the real
        // dispatcher beyond that interval so late templates and unload events settle.
        var settle = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += delegate { timer.Stop(); settle.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(settle);
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
    static Color ColorOf(Brush brush)
    {
        var solid = brush as SolidColorBrush;
        return solid == null ? Colors.Transparent : solid.Color;
    }
    static bool Is(Brush brush, string hex) { return ColorOf(brush) == (Color)ColorConverter.ConvertFromString(hex); }
    static object State(FrameworkElement element)
    {
        if (element == null) return null;
        var table = theme.GetField("States", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        object[] parameters = { element, null };
        return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, parameters) ? parameters[1] : null;
    }
    static int WatcherCount(object state)
    {
        return state == null ? -1 : ((System.Collections.ICollection)state.GetType().GetField("Remove").GetValue(state)).Count;
    }
    static Window Host(UIElement content)
    {
        return new Window { Content = content, Width = 600, Height = 500, Left = -16000, Top = -16000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
    }
    static IEnumerable<TextBlock> TextDescendants(DependencyObject root)
    {
        var text = root as TextBlock; if (text != null) yield return text;
        if (!(root is Visual)) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in TextDescendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string path = args.Length == 0 ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InfiniteCL.Theme.dll") : args[0];
            theme = Assembly.LoadFrom(System.IO.Path.GetFullPath(path)).GetType("InfiniteCL.Theme", true);
            var install = theme.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
            install.Invoke(null, null); install.Invoke(null, null);
            var model = new TestModel();
            var root = new StackPanel { Background = Brushes.White, DataContext = model };
            var input = new TextBox();
            input.SetBinding(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            input.SetBinding(Control.BackgroundProperty, new Binding("Background"));
            input.SetBinding(Control.ForegroundProperty, new Binding("Foreground"));
            int textEvents = 0; input.TextChanged += delegate { textEvents++; }; root.Children.Add(input);
            var nestedText = new TextBlock { Text = "nested content", Foreground = Brushes.Black };
            var nested = new Border { Background = Brushes.White, BorderBrush = Brushes.Blue, Child = nestedText };
            root.Children.Add(nested);
            var transparent = new Border { Background = Brushes.Transparent, Height = 12, IsHitTestVisible = true }; root.Children.Add(transparent);
            var error = new TextBlock { Text = "error", Foreground = Brushes.Red }; root.Children.Add(error);
            var warning = new TextBlock { Text = "warning", Foreground = Brushes.Orange }; root.Children.Add(warning);
            var visual = new DrawingImage(new GeometryDrawing(Brushes.Green, null, new RectangleGeometry(new Rect(0, 0, 5, 5))));
            var image = new Image { Name = "GameArtwork", Source = visual, Width = 24, Height = 24 }; root.Children.Add(image);
            var imageBrush = new ImageBrush(visual);
            var artPanel = new Border { Background = imageBrush, Height = 10 }; root.Children.Add(artPanel);
            // Original ModMain toast animation casts Background to LinearGradientBrush
            // and mutates its original GradientStop instances on each animation frame.
            var toastGradient = new LinearGradientBrush(Colors.White, Colors.LightGray, 0);
            var toastStop0 = toastGradient.GradientStops[0]; var toastStop1 = toastGradient.GradientStops[1];
            var toast = new Border { Background = toastGradient, Height = 12 }; root.Children.Add(toast);
            var command = new TestCommand();
            var button = new TestButton { Content = "Download", Command = command, CommandParameter = "1.21.4", Background = Brushes.White };
            int clicks = 0; button.Click += delegate { clicks++; }; root.Children.Add(button);
            var templateText = new FrameworkElementFactory(typeof(TextBlock));
            templateText.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            templateText.SetBinding(TextBlock.TextProperty, new Binding("."));
            var combo = new ComboBox { Foreground = Brushes.Black, ItemTemplate = new DataTemplate { VisualTree = templateText }, SelectedIndex = 0 };
            combo.Items.Add("1.21.4"); combo.Items.Add("1.20.6"); combo.SelectedIndex = 0; root.Children.Add(combo);
            var launch = new PCL.MyButton { Name = "BtnLaunch" };
            var launchLabel = new TextBlock { Name = "LabText", Foreground = Brushes.Black, Text = "启动游戏" };
            var launchSurface = new Border { Name = "PanFore", Background = Brushes.Blue, BorderBrush = Brushes.Blue, Child = launchLabel };
            launch.Children.Add(launchSurface); root.Children.Add(launch);
            var radioLabel = new TextBlock { Foreground = Brushes.Black, Text = "下载" };
            var radio = new PCL.MyRadioButton { Checked = true, Background = Brushes.Blue, Child = radioLabel }; root.Children.Add(radio);
            int linkClicks = 0;
            var run = new Run("original release notes") { Foreground = Brushes.Red };
            var link = new Hyperlink(run) { NavigateUri = new Uri("https://example.invalid/original") };
            link.Click += delegate(object sender, RoutedEventArgs e) { linkClicks++; e.Handled = true; };
            var rich = new TextBlock(); rich.Inlines.Add(link); root.Children.Add(rich);
            var window = Host(root); window.Show(); Flush();

            Check(Is(root.Background, "#EE111113") && Is(nested.Background, "#EE111113") && Is(nestedText.Foreground, "#EEE7DA"), "nested existing surfaces and text become readable");
            Check(BindingOperations.IsDataBound(input, TextBox.TextProperty) && BindingOperations.IsDataBound(input, Control.BackgroundProperty) && BindingOperations.IsDataBound(input, Control.ForegroundProperty), "text and brush binding expressions survive styling");
            Check(Object.ReferenceEquals(model.Background, Brushes.White) && Object.ReferenceEquals(model.Foreground, Brushes.Black), "presentation changes do not mutate bound source values");
            Check(input.Text == "initial version" && Is(input.Background, "#0C0C0E") && Is(input.Foreground, "#EEE7DA"), "bound input values render correctly");
            int eventsBefore = textEvents; model.Text = "selected 1.21.4"; Flush();
            Check(input.Text == model.Text && textEvents > eventsBefore, "source updates still reach input and original TextChanged handler");
            input.Text = "edited by user"; Flush();
            Check(model.Text == "edited by user", "two-way user edits still update the original model");
            button.Fire();
            Check(clicks == 1 && command.Executions == 1 && (string)command.Parameter == "1.21.4", "existing click handlers, ICommand and command parameters execute once");
            Check(Object.ReferenceEquals(transparent.Background, Brushes.Transparent) && transparent.IsHitTestVisible, "transparent hit-test surface remains intact");
            Check(Object.ReferenceEquals(image.Source, visual) && image.Visibility == Visibility.Visible && Object.ReferenceEquals(artPanel.Background, imageBrush), "original artwork and image-brush content remain visible and unchanged");
            Check(Object.ReferenceEquals(toast.Background, toastGradient) && !toastGradient.IsFrozen && Object.ReferenceEquals(toastGradient.GradientStops[0], toastStop0) && Object.ReferenceEquals(toastGradient.GradientStops[1], toastStop1), "original mutable toast gradient and its stop instances are preserved");
            int toastFrames = 0; Exception toastFailure = null;
            var toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            toastTimer.Tick += delegate {
                try
                {
                    var originalGradient = (LinearGradientBrush)toast.Background;
                    originalGradient.GradientStops[0].Color = Color.FromRgb((byte)(255 - toastFrames * 10), 190, 140);
                    originalGradient.GradientStops[1].Color = Color.FromRgb(200, (byte)(220 - toastFrames * 10), 170);
                    toastFrames++; if (toastFrames == 3) toastTimer.Stop();
                }
                catch (Exception ex) { toastFailure = ex; toastTimer.Stop(); }
            };
            toastTimer.Start(); Flush();
            Check(toastFrames == 3 && toastFailure == null && Object.ReferenceEquals(toast.Background, toastGradient) && toastStop0.Color == Color.FromRgb(235,190,140) && toastStop1.Color == Color.FromRgb(200,200,170), "original-style toast animation can cast and write gradient stops on later frames");
            bool toastStillWritable = false;
            if (toastFailure == null)
            {
                toastStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Colors.White, Colors.Orange, TimeSpan.FromSeconds(20)));
                Flush();
                bool active = toastStop0.HasAnimatedProperties && Object.ReferenceEquals(toast.Background, toastGradient);
                toastStop0.BeginAnimation(GradientStop.ColorProperty, null); toastStop0.Color = Colors.Cyan; toastStop1.Color = Colors.Magenta; Flush();
                toastStillWritable = active && toastStop0.Color == Colors.Cyan && toastStop1.Color == Colors.Magenta;
            }
            Check(toastStillWritable, "gradient-stop animation survives and original stop values remain writable afterward");
            Check(Is(error.Foreground, "#F1A2A0"), "red errors remain distinguishable and readable");
            var warningColor = ColorOf(warning.Foreground);
            Check(warningColor.R > warningColor.G && warningColor.G > warningColor.B, "orange warnings retain a warm warning hue: " + warningColor);
            Check(Is(launchSurface.Background, "#C8AA72") && Is(launchLabel.Foreground, "#211A10"), "launch action retains high-contrast accent treatment");
            Check(Is(radio.Background, "#31291D") && Is(radioLabel.Foreground, "#C8AA72") && radio.Checked, "checked navigation state remains selected and unchanged");
            radio.Checked = false; radio.Background = Brushes.White; radioLabel.Foreground = Brushes.Black; Flush();
            Check(ColorOf(radio.Background).A == 0 && Is(radioLabel.Foreground, "#ADA28F") && !radio.Checked, "later navigation state updates remain visible");
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, link));
            Check(linkClicks == 1 && link.NavigateUri.AbsoluteUri == "https://example.invalid/original" && run.Text == "original release notes" && Is(run.Foreground, "#F1A2A0"), "inline text, URI, hyperlink click handler and error styling remain intact");
            combo.IsDropDownOpen = true; Flush();
            var popupItem = combo.ItemContainerGenerator.ContainerFromIndex(1) as ComboBoxItem;
            var popupTexts = popupItem == null ? new TextBlock[0] : TextDescendants(popupItem).Where(t => t.Text == "1.20.6").ToArray();
            Check(combo.IsDropDownOpen && popupTexts.Length > 0 && popupTexts.All(t => State(t) != null && Is(t.Foreground, "#EEE7DA")), "newly opened popup templates are discovered and remain readable");
            combo.SelectedIndex = 1; combo.IsDropDownOpen = false; Flush();
            var selectedTexts = TextDescendants(combo).Where(t => t.Text == "1.20.6").ToArray();
            Check((string)combo.SelectedItem == "1.20.6" && selectedTexts.Length > 0 && selectedTexts.All(t => State(t) != null && Is(t.Foreground, "#EEE7DA")), "later selection preserves data and styles newly generated selected text");

            model.Foreground = Brushes.Red; model.Background = Brushes.LightBlue; Flush();
            Check(Is(input.Foreground, "#F1A2A0") && Is(input.Background, "#0C0C0E") && BindingOperations.IsDataBound(input, Control.ForegroundProperty), "later bound warning and surface updates stay styled without replacing bindings");
            model.Foreground = Brushes.Black; Flush();
            Check(Is(input.Foreground, "#EEE7DA"), "later bound normal text recovers its readable text color");
            launch.IsEnabled = false; launchSurface.Background = Brushes.LightGray; launchLabel.Foreground = Brushes.Gray; Flush();
            Check(!launch.IsEnabled && Is(launchSurface.Background, "#28231C") && Is(launchLabel.Foreground, "#ADA28F"), "disabled launch state stays disabled and visually distinct");
            nested.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(20)));
            nested.BeginAnimation(Border.BackgroundProperty, new ObjectAnimationUsingKeyFrames {
                Duration = new Duration(TimeSpan.FromSeconds(20)), KeyFrames = { new DiscreteObjectKeyFrame(Brushes.White, KeyTime.FromTimeSpan(TimeSpan.Zero)) }
            }); Flush();
            Check(Is(nested.Background, "#EE111113") && !DependencyPropertyHelper.GetValueSource(nested, Border.BackgroundProperty).IsAnimated && DependencyPropertyHelper.GetValueSource(nested, UIElement.OpacityProperty).IsAnimated, "color animations cannot erase readability while opacity animations keep running");

            var state = State(nested); int watchers = WatcherCount(state);
            nested.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, nested)); Flush();
            Check(state != null && Object.ReferenceEquals(state, State(nested)) && WatcherCount(State(nested)) == watchers, "repeated Loaded and Install calls do not duplicate property subscriptions");
            var lateLabel = new TextBlock { Text = "late generated heading", Foreground = Brushes.Black };
            root.Children.Add(lateLabel); Flush();
            Check(State(lateLabel) != null && Is(lateLabel.Foreground, "#EEE7DA"), "late-generated bare text without a Loaded subscriber is discovered and readable");
            var detached = new Border { Name = "DynamicProbe", Background = Brushes.White }; root.Children.Add(detached); Flush();
            object detachedState = State(detached);
            detached.Background = Brushes.Blue; // Schedule a presentation update, then unload before it runs.
            detached.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, detached));
            root.Children.Remove(detached); Flush();
            bool dead = detachedState != null && !(bool)detachedState.GetType().GetField("Alive").GetValue(detachedState);
            Check(dead && State(detached) == null && Object.ReferenceEquals(detached.Background, Brushes.Blue), "unload removes subscriptions and cancels queued presentation work (old=" + (detachedState != null) + ", dead=" + dead + ", state=" + (State(detached) != null) + ", background=" + ColorOf(detached.Background) + ")");
            detached.Background = Brushes.White; Flush();
            Check(Object.ReferenceEquals(detached.Background, Brushes.White), "detached controls are no longer restyled");
            root.Children.Add(detached); Flush();
            Check(State(detached) != null && !Object.ReferenceEquals(detachedState, State(detached)) && Is(detached.Background, "#EE111113"), "reloaded controls receive a fresh single set of subscriptions");
            int closing = 0; window.Closing += delegate { closing++; }; window.Close(); Flush();
            Check(closing == 1 && State(nested) == null && State(input) == null && State(rich) == null, "real window closure retains Closing handler and cleans child subscriptions");
            window.Content = null;
            var nextWindow = Host(root); nextWindow.Show(); Flush();
            Check(State(nested) != null && State(input) != null && BindingOperations.IsDataBound(input, TextBox.TextProperty) && input.Text == model.Text, "moving the original page to a new window reattaches styling and retains bindings");
            nextWindow.Close(); Flush();
            Console.WriteLine("PRESENTATION_VERIFICATION " + (failures == 0 ? "PASSED" : "FAILED") + " " + (checks - failures) + "/" + checks);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("HARNESS_ERROR " + ex); return 2;
        }
    }
}
