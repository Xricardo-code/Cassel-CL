// Behavioral QA against the delivered theme assembly. Product source is not linked.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCL { public sealed class PageLaunchRight : UserControl { } }
sealed class MotionCommand : ICommand
{
    public int Calls;
    public bool CanExecute(object value) { return true; }
    public void Execute(object value) { Calls++; }
    public event EventHandler CanExecuteChanged { add { } remove { } }
}
sealed class MotionButton : Button { public void Fire() { OnClick(); } }
sealed class MotionModel : INotifyPropertyChanged
{
    string caption = "原生启动功能";
    public string Caption { get { return caption; } set { caption=value; if(PropertyChanged!=null) PropertyChanged(this,new PropertyChangedEventArgs("Caption")); } }
    public event PropertyChangedEventHandler PropertyChanged;
}

static class VerifyLauncherMotion
{
    static Assembly theme;
    static Type motion;
    static int checks, failures;
    static string output;
    static readonly List<Window> open = new List<Window>();
    static bool Animate { get { return SystemParameters.ClientAreaAnimation; } }
    static void Check(bool pass,string label)
    { checks++; if(!pass) failures++; Console.WriteLine((pass?"PASS ":"FAIL ")+label); }
    static object Call(string name,params object[] values)
    { return motion.GetMethod(name,BindingFlags.Public|BindingFlags.Static).Invoke(null,values); }
    static string Phase(Window w) { return (string)Call("GetPhase",w); }
    static bool Playing(Window w) { return (bool)Call("IsPlaying",w); }
    static int Active(Window w) { return (int)Call("GetActiveAnimationCount",w); }
    static string Hash(string path)
    { using(var file=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-",""); }
    static BitmapSource Resource(string name)
    {
        using(var stream=theme.GetManifestResourceStream(name))
        {
            if(stream==null) throw new InvalidDataException("Missing resource "+name);
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
            image.StreamSource=stream; image.EndInit(); image.Freeze(); return image;
        }
    }
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var element=root as FrameworkElement; if(element!=null) yield return element;
        if(!(root is Visual)) yield break;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Elements(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    static FrameworkElement Find(DependencyObject root,string name)
    { return Elements(root).FirstOrDefault(e=>e.Name==name); }
    static void Flush(int milliseconds)
    {
        if(milliseconds>0)
        {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick+=delegate { timer.Stop(); frame.Continue=false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        for(int i=0;i<2;i++)
        {
            var frame=new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate { frame.Continue=false; }));
            Dispatcher.PushFrame(frame);
        }
    }
    static RenderTargetBitmap Snapshot(FrameworkElement element)
    {
        element.UpdateLayout();
        int width=(int)Math.Ceiling(element.ActualWidth),height=(int)Math.Ceiling(element.ActualHeight);
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(element);
        image.Freeze(); return image;
    }
    static void Save(BitmapSource image,string path)
    {
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using(var stream=File.Create(path)) encoder.Save(stream);
    }
    static byte[] Render(FrameworkElement element,string path)
    {
        var image=Snapshot(element); if(path!=null) Save(image,path);
        int width=image.PixelWidth,height=image.PixelHeight;
        var pixels=new byte[width*height*4]; image.CopyPixels(pixels,width*4,0); return pixels;
    }
    static bool Different(byte[] a,byte[] b)
    {
        if(a.Length!=b.Length) return true;
        int count=0; for(int i=0;i<a.Length;i++) if(Math.Abs(a[i]-b[i])>3 && ++count>400) return true;
        return false;
    }
    static bool Reachable(Host host,FrameworkElement target)
    {
        if(target==null || !target.IsVisible || target.ActualWidth<1 || target.ActualHeight<1) return false;
        var p=target.TranslatePoint(new Point(target.ActualWidth/2,target.ActualHeight/2),host.Root);
        var item=host.Root.InputHitTest(p) as DependencyObject;
        while(item!=null)
        {
            if(Object.ReferenceEquals(item,target)) return true;
            item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);
        }
        return false;
    }
    static FrameworkElement[] Owned(Host host)
    {
        string[] names={"InfiniteCLDragonSurfaceFolio","InfiniteCLDragonSurfaceCommand01","InfiniteCLDragonSurfaceCommand02","InfiniteCLDragonSurfaceCommand03","InfiniteCLHomeCredits","InfiniteCLArchiveFooter"};
        return names.Select(n=>Find(host.Page,n)).Where(e=>e!=null).ToArray();
    }
    sealed class Host
    {
        public Window Window;
        public Grid Root;
        public Grid Modal;
        public PCL.PageLaunchRight Page;
        public StackPanel NativePanel;
        public MotionButton Native;
        public MotionModel Model=new MotionModel();
        public MotionCommand Command=new MotionCommand();
        public Transform NativeTransform=new TranslateTransform(3,2);
        public int NativeClicks;
        public FrameworkElement[] Owned;
        public Transform[] BaseTransforms;
        public double[] BaseOpacity;
        public int Navigate;
        public Host(bool modalVisible,bool show)
        {
            Root=new Grid { Name="PanForm", Background=new ImageBrush(Resource("InfiniteCL.City.png")) { Stretch=Stretch.UniformToFill } };
            var backdrop=new Border { Background=new SolidColorBrush(Color.FromArgb(70,0,0,0)),IsHitTestVisible=false }; Root.Children.Add(backdrop);
            var layout=new Grid { Name="NativeLayout",Margin=new Thickness(24,68,24,20) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(225) });
            layout.ColumnDefinitions.Add(new ColumnDefinition()); Root.Children.Add(layout);
            var side=new StackPanel { Name="NativeLeft", Margin=new Thickness(0,24,20,0) };
            side.Children.Add(new TextBlock { Text="CASSELL CL",Foreground=Brushes.BurlyWood,FontSize=22,Margin=new Thickness(0,0,0,17) });
            side.Children.Add(new TextBlock { Text="卡塞尔学院 · 执行部\n穿过雨幕，前往新的世界。",Foreground=Brushes.LightGray,FontSize=12,LineHeight=23,Margin=new Thickness(0,0,0,30) });
            Native=new MotionButton { Name="NativeLaunch",Height=50,RenderTransform=NativeTransform,Opacity=.87,Background=new SolidColorBrush(Color.FromRgb(200,170,114)) };
            Native.SetBinding(ContentControl.ContentProperty,new Binding("Caption") { Source=Model }); Native.Command=Command;
            Native.Click+=delegate { NativeClicks++; }; side.Children.Add(Native); layout.Children.Add(side);
            NativePanel=new StackPanel { Name="PanMain" };
            Page=new PCL.PageLaunchRight { Content=new ScrollViewer { Content=NativePanel,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden } };
            Grid.SetColumn(Page,1); layout.Children.Add(Page);
            // DragonHome discovers the native content through the visual tree.
            // Build the host templates before Apply while the window remains unshown.
            Page.Measure(new Size(780,650)); Page.Arrange(new Rect(0,0,780,650)); Page.UpdateLayout();
            theme.GetType("InfiniteCL.DragonHome",true).GetMethod("Apply").Invoke(null,new object[] { Page,new Action<int>(delegate(int id) { Navigate=id; }),Resource("InfiniteCL.png") });
            Owned=VerifyLauncherMotion.Owned(this); BaseTransforms=Owned.Select(e=>e.RenderTransform).ToArray(); BaseOpacity=Owned.Select(e=>e.Opacity).ToArray();
            var header=new TextBlock { Text="CASSELL CL  /  世界树档案",Foreground=Brushes.BurlyWood,FontSize=12,Margin=new Thickness(27,25,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false }; Root.Children.Add(header);
            var label=new TextBlock { Name="TestSceneCaption",Text="动画演示 · 测试场景",Foreground=Brushes.Gray,FontSize=10,Margin=new Thickness(0,0,12,7),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false }; Panel.SetZIndex(label,90); Root.Children.Add(label);
            Modal=new Grid { Name="PanMsg",Visibility=modalVisible?Visibility.Visible:Visibility.Collapsed,Background=new SolidColorBrush(Color.FromArgb(238,12,12,15)) };
            Modal.Children.Add(new Button { Content="原生弹窗 · 保持优先",Width=220,Height=70 }); Root.Children.Add(Modal); Panel.SetZIndex(Modal,100);
            Window=new Window { Width=1100,Height=740,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Content=Root,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false,Title="Cassell CL 动画测试场景" };
            open.Add(Window); Call("Install",Window,Root,Resource("InfiniteCL.png")); if(show) { Window.Show(); Flush(40); }
        }
        public void Close() { Window.Close(); Flush(30); open.Remove(Window); }
        public bool NativeUntouched()
        {
            return Object.ReferenceEquals(Native.RenderTransform,NativeTransform) && Math.Abs(Native.Opacity-.87)<.0001 && !Native.HasAnimatedProperties &&
                BindingOperations.IsDataBound(Native,ContentControl.ContentProperty) && Object.ReferenceEquals(Native.Command,Command);
        }
        public bool Restored()
        {
            return Owned.Select((e,i)=>Object.ReferenceEquals(e.RenderTransform,BaseTransforms[i]) && Math.Abs(e.Opacity-BaseOpacity[i])<.0001 && !e.HasAnimatedProperties).All(v=>v);
        }
    }
    static void Skip(Host host)
    {
        var skip=Find(host.Root,"InfiniteCLMotionSkip") as Button;
        if(skip!=null) skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    static void Esc(Host host)
    {
        var args=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(host.Window),Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.PreviewKeyDownEvent };
        host.Window.RaiseEvent(args);
    }
    static void Tests()
    {
        Console.WriteLine("SYSTEM_CLIENT_AREA_ANIMATION "+Animate);
        var first=new Host(false,false);
        Check(!Playing(first.Window) && Find(first.Root,"InfiniteCLMotionOverlay")==null,"before Show the extension does not create a blocking intro or run animations");
        Call("Install",first.Window,first.Root,Resource("InfiniteCL.png")); Call("Install",first.Window,first.Root,Resource("InfiniteCL.png"));
        first.Window.Show(); Flush(120);
        Check(first.Owned.Length==6,"the shipped DragonHome supplies all six intended entrance surfaces");
        Check(Elements(first.Root).Count(e=>e.Name=="InfiniteCLMotionOverlay")<=1,"repeated installation creates at most one intro overlay");
        Check(first.NativeUntouched(),"intro preserves native opacity, transform, command and content binding");
        if(Animate)
        {
            Check(Phase(first.Window)=="intro" && Playing(first.Window),"normal motion starts with the visible intro phase");
            var skip=Find(first.Root,"InfiniteCLMotionSkip");
            Check(Reachable(first,skip),"intro offers a reachable skip button");
            var a=Render(first.Root,Path.Combine(output,"motion-intro-early.png")); Flush(240);
            var b=Render(first.Root,Path.Combine(output,"motion-intro-later.png"));
            Check(Different(a,b),"rendered intro frames visibly change over real dispatcher time");
            Skip(first); Flush(60);
            Check(Find(first.Root,"InfiniteCLMotionOverlay")==null,"skip removes the intro overlay promptly");
            Check(Phase(first.Window)=="entering" || Phase(first.Window)=="idle","skip proceeds to the entrance without restarting the intro");
            Check(Phase(first.Window)=="entering" && Active(first.Window)>0 && !first.Restored(),"entrance actively changes at least one owned home surface before restoring it");
        }
        else Check(!Playing(first.Window),"system disabled animations are respected without changing system settings");
        Flush(900);
        Check(Phase(first.Window)=="idle" && !Playing(first.Window) && Active(first.Window)==0,"finished entrance has no active motion work");
        Check(first.Restored(),"finished entrance restores every owned surface's original opacity and transform object");
        Check(first.NativeUntouched(),"native properties and bindings remain unchanged after entrance");
        Check(Reachable(first,first.Native),"native launch control remains reachable by input hit testing after entrance");
        first.Model.Caption="绑定仍可更新"; Flush(20);
        first.Native.Fire();
        Check(first.Native.Content.Equals("绑定仍可更新") && first.NativeClicks==1 && first.Command.Calls==1,"native binding, click handler and command still execute after entrance");
        var command=Find(first.Page,"InfiniteCLDragonSurfaceCommand01");
        command.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
        Check(first.Navigate==1,"the real DragonHome navigation event remains connected after entrance");
        first.Page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); first.Page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Flush(80);
        Check(Phase(first.Window)=="idle" && Find(first.Root,"InfiniteCLMotionOverlay")==null,"repeat Loaded events on an already visible home do not replay the intro or entrance");
        first.Page.Visibility=Visibility.Collapsed; Flush(30); first.Page.Visibility=Visibility.Visible; Flush(80);
        Check(Find(first.Root,"InfiniteCLMotionOverlay")==null,"returning to home never recreates the startup intro");
        Flush(900);
        Check(first.Restored() && Active(first.Window)==0 && first.NativeUntouched(),"return entrance finishes cleanly and leaves native controls unchanged");
        first.Close();
        Check(Phase(first.Window)=="disposed" && Active(first.Window)==0,"closing an idle window disposes motion state");

        var blocked=new Host(true,true); Flush(160);
        Check(Find(blocked.Root,"InfiniteCLMotionOverlay")==null && !Playing(blocked.Window),"an initial native modal is never covered or delayed by the intro");
        Check(blocked.Modal.Visibility==Visibility.Visible && blocked.Restored(),"initial modal priority retains original visibility and readable surface values");
        blocked.Modal.Visibility=Visibility.Collapsed; Flush(250);
        Check(Find(blocked.Root,"InfiniteCLMotionOverlay")==null,"dismissing an initial native modal does not reveal a delayed startup screen");
        blocked.Close();

        if(Animate)
        {
            var esc=new Host(false,true); Flush(90); Esc(esc); Flush(80);
            Check(Find(esc.Root,"InfiniteCLMotionOverlay")==null,"Escape removes the intro overlay"); Flush(900);
            Check(esc.Restored() && !Playing(esc.Window),"Escape completes a clean entrance rather than leaving hidden home surfaces"); esc.Close();

            var modal=new Host(false,true); Flush(100); modal.Modal.Visibility=Visibility.Visible; Flush(80);
            Check(Find(modal.Root,"InfiniteCLMotionOverlay")==null && !Playing(modal.Window) && Active(modal.Window)==0,"a modal appearing during intro immediately cancels motion and gets priority");
            Check(modal.Restored() && modal.NativeUntouched(),"modal interruption restores all owned surfaces without editing native controls");
            modal.Modal.Visibility=Visibility.Collapsed; Flush(250);
            Check(Find(modal.Root,"InfiniteCLMotionOverlay")==null,"dismissing an interrupting modal does not restart the intro"); modal.Close();

            var enterModal=new Host(false,true); Flush(100); Skip(enterModal); Flush(90); enterModal.Modal.Visibility=Visibility.Visible; Flush(90);
            Check(!Playing(enterModal.Window) && Active(enterModal.Window)==0 && enterModal.Restored(),"a modal appearing during entrance cancels clocks and restores base values"); enterModal.Close();

            var close=new Host(false,true); Flush(100); close.Close();
            Check(Phase(close.Window)=="disposed" && Active(close.Window)==0 && Find(close.Root,"InfiniteCLMotionOverlay")==null,"closing during intro releases the overlay, active animation work and window hooks");
            var closeEnter=new Host(false,true); Flush(80); Skip(closeEnter); Flush(100); closeEnter.Close();
            Check(Phase(closeEnter.Window)=="disposed" && Active(closeEnter.Window)==0 && closeEnter.Restored(),"closing during entrance releases animation work and restores owned property bases");

            var natural=new Host(false,true); Flush(2800);
            Check(!Playing(natural.Window) && Phase(natural.Window)=="idle" && Find(natural.Root,"InfiniteCLMotionOverlay")==null,"unskipped startup naturally reaches the usable home within three seconds");
            Check(natural.Restored() && Active(natural.Window)==0,"natural completion removes all animation clocks and restores every base value");
            Render(natural.Root,Path.Combine(output,"motion-final-home.png")); natural.Close();
        }
    }
    static void Demo()
    {
        string frames=Path.Combine(output,"demo-frames"); Directory.CreateDirectory(frames);
        var host=new Host(false,false); host.Window.Width=1000; host.Window.Height=710;
        theme.GetType("InfiniteCL.RainScene",true).GetMethod("Install").Invoke(null,new object[] { host.Root,Resource("InfiniteCL.City.png") });
        host.Window.Show(); Flush(1);
        var captured=new List<BitmapSource>(); var timestamps=new List<string> { "frame,target_ms,actual_ms,phase" };
        var watch=Stopwatch.StartNew();
        // This offscreen WPF fixture renders in about 70 ms. Ten frames per
        // second preserves real animation timing without slowing dispatcher time.
        const int frameCount=35,frameMilliseconds=100;
        for(int i=0;i<frameCount;i++)
        {
            int target=i*frameMilliseconds,wait=target-(int)watch.ElapsedMilliseconds; if(wait>0) Flush(wait); else Flush(1);
            timestamps.Add(i+","+target+","+watch.ElapsedMilliseconds+","+Phase(host.Window));
            captured.Add(Snapshot(host.Root));
        }
        watch.Stop();
        for(int i=0;i<captured.Count;i++) Save(captured[i],Path.Combine(frames,"frame-"+i.ToString("000")+".png"));
        File.WriteAllLines(Path.Combine(output,"motion-demo-timing.csv"),timestamps);
        Console.WriteLine("DEMO_FRAMES "+frames);
        Console.WriteLine("DEMO_FRAME_COUNT "+frameCount);
        Console.WriteLine("DEMO_FRAME_INTERVAL_MS "+frameMilliseconds);
        Console.WriteLine("DEMO_ELAPSED_MS "+watch.ElapsedMilliseconds);
        Console.WriteLine("DEMO_FINAL_PHASE "+Phase(host.Window)); host.Close();
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if(args.Length<2) throw new ArgumentException("Expected production theme DLL, output directory, optional --demo.");
            var dll=Path.GetFullPath(args[0]); output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            Console.WriteLine("THEME_SHA256 "+Hash(dll)); theme=Assembly.LoadFrom(dll); motion=theme.GetType("InfiniteCL.LauncherMotion",true);
            if(args.Length>2 && args[2]=="--demo") Demo(); else Tests();
            Console.WriteLine("CHECKS "+checks+" FAILURES "+failures); return failures==0?0:1;
        }
        catch(Exception exception) { Console.WriteLine(exception); return 2; }
        finally { foreach(var window in open.ToArray()) { try { window.Close(); } catch { } } }
    }
}
