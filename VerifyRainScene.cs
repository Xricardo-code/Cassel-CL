using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal sealed class RainReachableButton : Button { public void Fire() { OnClick(); } }
internal static class VerifyRainScene
{
    static int checks, failures;
    static Type rain;
    static Grid root;
    static void Check(bool pass, string label)
    {
        checks++; if (!pass) failures++;
        Console.WriteLine((pass ? "PASS " : "FAIL ") + label);
    }
    static object Call(string method, params object[] values)
    { return rain.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, values); }
    static bool Enabled() { return (bool)Call("GetEnabled", root); }
    static bool Animating() { return (bool)Call("IsAnimating", root); }
    static int Count() { return (int)Call("GetParticleCount", root); }
    static long Frames() { return (long)Call("GetFrameCount", root); }
    static double Intensity() { return (double)Call("GetIntensity", root); }
    static void Flush(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
        for (int i = 0; i < 2; i++)
        {
            var drain = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { drain.Continue = false; }));
            Dispatcher.PushFrame(drain);
        }
    }
    static FrameworkElement Find(DependencyObject parent, string name)
    {
        var found = parent as FrameworkElement; if (found != null && found.Name == name) return found;
        if (!(parent is Visual)) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = Find(VisualTreeHelper.GetChild(parent,i),name); if (child != null) return child; }
        return null;
    }
    static bool Reaches(Point point, DependencyObject expected)
    {
        var target = root.InputHitTest(point) as DependencyObject;
        while (target != null && !Object.ReferenceEquals(target,expected))
        {
            if (target is Visual) target = VisualTreeHelper.GetParent(target);
            else
            {
                var content = target as ContentElement;
                var framework = target as FrameworkContentElement;
                target = (content == null ? null : ContentOperations.GetParent(content)) ??
                    (framework == null ? null : framework.Parent) ?? LogicalTreeHelper.GetParent(target);
            }
        }
        return Object.ReferenceEquals(target,expected);
    }
    static bool Reaches(FrameworkElement element)
    { return Reaches(element.TransformToAncestor(root).Transform(new Point(element.ActualWidth/2,element.ActualHeight/2)),element); }
    static bool VisibleCaption(DependencyObject parent, string text)
    {
        var label = parent as TextBlock;
        if (label != null && label.IsVisible && label.Text.Contains(text)) return true;
        if (!(parent is Visual)) return false;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (VisibleCaption(VisualTreeHelper.GetChild(parent,i),text)) return true;
        return false;
    }
    static void Click(ToggleButton toggle)
    { typeof(ToggleButton).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(toggle,null); }
    static byte[] Render(FrameworkElement element)
    {
        int width = (int)element.ActualWidth, height = (int)element.ActualHeight;
        var image = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(element);
        byte[] pixels = new byte[width*height*4]; image.CopyPixels(pixels,width*4,0); return pixels;
    }
    static bool Clear(FrameworkElement layer) { return Render(layer).All(value=>value==0); }
    static bool Near(double a,double b) { return Math.Abs(a-b)<.001; }
    [STAThread]
    static int Main(string[] args)
    {
        Window window = null;
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            rain = assembly.GetType("InfiniteCL.RainScene",true);
            var theme = assembly.GetType("InfiniteCL.Theme",false);
            if (theme != null) theme.GetMethod("Install",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
            root = new Grid { Width=720, Height=460, Background=Brushes.Transparent };
            // The original launcher has an implicit ToggleButton template used by
            // expander controls. Its caret-only visual discards Content entirely.
            var caret = new FrameworkElementFactory(typeof(TextBlock));
            caret.SetValue(TextBlock.TextProperty,"⌃");
            root.Resources[typeof(ToggleButton)] = new Style(typeof(ToggleButton)) { Setters = {
                new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ToggleButton)) { VisualTree=caret })
            } };
            var button = new RainReachableButton { Content="original launcher action", Width=200, Height=50,
                HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top, Margin=new Thickness(220,300,0,0) };
            int clicks=0; button.Click+=delegate { clicks++; }; root.Children.Add(button);
            var image = new DrawingImage(new GeometryDrawing(Brushes.DarkSlateGray,null,new RectangleGeometry(new Rect(0,0,720,460)))); image.Freeze();
            Call("Install",root,image); Call("Install",root,image);
            var controls=(FrameworkElement)Call("CreateControls",root); controls.VerticalAlignment=VerticalAlignment.Top;
            controls.Margin=new Thickness(10); root.Children.Add(controls);
            var backdrop=root.Children.OfType<FrameworkElement>().SingleOrDefault(e=>e.Name=="InfiniteCLRainBackground");
            var layer=root.Children.OfType<FrameworkElement>().SingleOrDefault(e=>e.Name=="InfiniteCLRainOverlay");
            Check(root.Children.Count==4 && backdrop!=null && layer!=null,"repeated Install retains exactly one city backdrop and rain overlay");
            Check(!backdrop.IsHitTestVisible && !backdrop.Focusable && Panel.GetZIndex(backdrop)<0 &&
                !layer.IsHitTestVisible && !layer.Focusable && Panel.GetZIndex(layer)>0,"city imagery and animated rain neither intercept input nor take focus");
            Check(Object.ReferenceEquals(root.Background,Brushes.Transparent) && Object.ReferenceEquals(root.Children[1],button),"install preserves the original root brush and action control");
            window=new Window { Content=root,Width=720,Height=460,Left=-16000,Top=-16000,ShowActivated=false,
                ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize };
            window.Show(); Flush(130);
            var toggle=(ToggleButton)Find(root,"InfiniteCLRainToggle");
            var slider=(Slider)Find(root,"InfiniteCLRainIntensity");
            var label=(TextBlock)Find(root,"InfiniteCLRainLevel");
            Check(toggle!=null && slider!=null && label!=null && toggle.IsChecked==Enabled() && Near(slider.Value,Intensity()),"actual toggle, slider and label reflect initial scene state");
            var hostProbe=new ToggleButton { Content="host content probe",IsHitTestVisible=false };
            root.Children.Add(hostProbe); Flush(40);
            Check(VisibleCaption(hostProbe,"⌃") && !VisibleCaption(hostProbe,"host content probe"),"host implicit caret-only template reproduces the original launcher integration constraint");
            root.Children.Remove(hostProbe); Flush(40);
            Check(toggle.ReadLocalValue(Control.TemplateProperty) is ControlTemplate && VisibleCaption(toggle,"雨幕"),"rain toggle keeps an explicit readable caption despite host implicit ToggleButton template");
            Check(Object.ReferenceEquals(((Image)Find(root,"InfiniteCLRainCityImage")).Source,image),"provided city artwork remains the actual background image");
            Check(Animating() && Count()>0 && !Clear(layer),"loaded enabled scene renders visible rain particles");
            Check(Reaches(new Point(320,325),button) && Reaches(toggle) && Reaches(slider),"original action and weather controls remain real input targets through the rain");
            button.Fire(); Check(clicks==1,"original action handler executes once beneath animated rain");
            slider.Value=20; Flush(60); int low=Count();
            Check(Near(Intensity(),20) && low>0 && label.Text.Contains("20"),"slider ValueChanged reaches actual scene intensity and visible label");
            slider.Value=100; Flush(60); int high=Count();
            Check(Near(Intensity(),100) && high>low && high<=350 && label.Text.Contains("100"),"increasing actual slider increases bounded particle density");
            long before=Frames(); byte[] first=Render(layer); var watch=Stopwatch.StartNew(); Flush(650); watch.Stop(); long advanced=Frames()-before;
            Check(advanced>0 && advanced<=Math.Ceiling(watch.Elapsed.TotalSeconds*30)+2,"rain moves on rendering frames and remains capped near 30 FPS ("+advanced+" frames / "+watch.ElapsedMilliseconds+"ms)");
            Check(!first.SequenceEqual(Render(layer)),"rendered rain position changes over time");
            Click(toggle); Flush(70); long stopped=Frames(); Flush(150);
            Check(!Enabled() && toggle.IsChecked==false && !Animating() && Count()==0 && Clear(layer) && Frames()==stopped && label.Text.Contains("关闭") && VisibleCaption(toggle,"关"),"real toggle off clears rain, updates the visible caption and stops all subsequent animation frames");
            Check(Object.ReferenceEquals(((Image)Find(root,"InfiniteCLRainCityImage")).Source,image),"disabling rain retains city artwork");
            Click(toggle); Flush(120);
            Check(Enabled() && toggle.IsChecked==true && Animating() && Count()==high && Frames()>stopped && VisibleCaption(toggle,"开"),"real toggle on updates its visible caption and resumes rain using the retained intensity");
            slider.Value=0; Flush(60); long zero=Frames(); Flush(120);
            Check(Enabled() && Near(Intensity(),0) && !Animating() && Count()==0 && Clear(layer) && Frames()==zero && label.Text.Contains("无雨"),"zero slider intensity clears rain and stops updates while retaining the enabled preference");
            Call("SetIntensity",root,55.0); Flush(90);
            Check(Near(slider.Value,55) && Near(Intensity(),55) && Animating() && Count()>0,"programmatic intensity updates synchronize visible slider and resume the effect");
            Call("SetIntensity",root,150.0); Check(Near(Intensity(),100) && Near(slider.Value,100),"intensity above maximum clamps to the slider range");
            Call("SetIntensity",root,Double.NaN); Flush(40); Check(Near(Intensity(),0) && !Animating() && Clear(layer),"invalid intensity safely becomes zero without scheduling animation");
            Call("SetIntensity",root,55.0); Flush(90);
            root.Visibility=Visibility.Hidden; Flush(70); long hidden=Frames(); Flush(100);
            Check(!Animating() && Count()==0 && Frames()==hidden,"hidden page unsubscribes from rendering and releases active particles");
            root.Visibility=Visibility.Visible; Flush(100);
            Check(Animating() && Count()>0 && Frames()>hidden,"visible page resumes the retained rain state");
            window.WindowState=WindowState.Minimized; Flush(90); long minimized=Frames(); Flush(100);
            Check(!Animating() && Count()==0 && Frames()==minimized,"minimized window stops rain rendering");
            window.WindowState=WindowState.Normal; Flush(120);
            Check(Animating() && Count()>0 && Frames()>minimized,"restoring the window resumes rain rendering");
            window.Content=null; Flush(70); long unloaded=Frames(); Flush(100);
            Check(!root.IsLoaded && !Animating() && Count()==0 && Frames()==unloaded,"unloaded page releases rendering subscription and stops frames");
            window.Content=root; Flush(110); Call("Install",root,image);
            Check(root.Children.Count==4 && Animating() && Count()>0 && Near(Intensity(),55) && Reaches(button),"reload preserves one layer set, intensity and original control reachability");
            window.Close(); window=null; Flush(50); long closed=Frames(); Flush(80);
            Check(!Animating() && Count()==0 && Frames()==closed,"closing the host stops and clears all rain rendering");
            Console.WriteLine("RAIN_SCENE_VERIFICATION "+(failures==0?"PASSED":"FAILED")+" "+(checks-failures)+"/"+checks);
            return failures==0?0:1;
        }
        catch(Exception exception) { Console.WriteLine("HARNESS_ERROR "+exception); return 2; }
        finally { if(window!=null) window.Close(); }
    }
}
