// Independent native-event fixture. Loads the final delivered DLL; no product source is linked.
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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCL
{
    public sealed class FormMain : Window { public bool Hidden; }
    public sealed class PageLaunchRight : UserControl { }
    public enum LoadState { Waiting, Loading, Finished, Failed, Aborted, Canceled }
    public enum WatcherState { Loading, Running, Ended }
    public class LoaderBase
    {
        public LoadState State;
        object parent;
        public bool ThrowParent;
        public object Parent { get { if(ThrowParent) throw new InvalidOperationException("fixture getter failure"); return parent; } set { parent=value; } }
        Action<LoaderBase,LoadState,LoadState> changed;
        public event Action<LoaderBase,LoadState,LoadState> OnStateChangedUi { add { changed+=value; } remove { changed-=value; } }
        public int SubscriberCount { get { return changed==null?0:changed.GetInvocationList().Length; } }
        public void Raise(LoadState next)
        {
            LoadState old=State; State=next;
            if(changed!=null) changed(this,next,old);
        }
    }
    public sealed class LoaderCombo : LoaderBase { }
    public sealed class McWatcher
    {
        public WatcherState State;
        public Process GameProcess;
        public LoaderBase Loader;
        // These deliberately match the private native watcher readiness fields.
        bool IsWindowAppeared, IsWindowFinished;
        IntPtr WindowHandle;
        public void Ready(bool appeared,bool finished,IntPtr handle)
        { IsWindowAppeared=appeared; IsWindowFinished=finished; WindowHandle=handle; }
    }
    public static class ModLaunch
    {
        public static LoaderCombo McLaunchLoader;
        public static LoaderBase McLaunchLoaderReal;
        public static McWatcher McLaunchWatcher;
        public static Process McLaunchProcess;
    }
}
sealed class SuccessButton : Button { public void Fire() { OnClick(); } }
sealed class SuccessCommand : ICommand
{
    public int Calls;
    public bool CanExecute(object arg) { return true; }
    public void Execute(object arg) { Calls++; }
    public event EventHandler CanExecuteChanged { add { } remove { } }
}
sealed class SuccessModel : INotifyPropertyChanged
{
    string caption="启动游戏";
    public string Caption { get { return caption; } set { caption=value; if(PropertyChanged!=null) PropertyChanged(this,new PropertyChangedEventArgs("Caption")); } }
    public event PropertyChangedEventHandler PropertyChanged;
}
static class VerifyLaunchSuccess
{
    static Assembly theme;
    static Type effect;
    static string output;
    static int checks,failures;
    static readonly List<Window> open=new List<Window>();
    static object Call(string name,params object[] args) { return effect.GetMethod(name,BindingFlags.Public|BindingFlags.Static).Invoke(null,args); }
    static bool Playing(Window w) { return (bool)Call("IsPlaying",w); }
    static bool Subscribed(Window w) { return (bool)Call("IsSubscribed",w); }
    static int Count(Window w) { return (int)Call("GetPlayCount",w); }
    static string State(Window w) { return (string)Call("GetObserverState",w); }
    static object Controller(Window w)
    {
        var table=effect.GetField("Controllers",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var args=new object[] { w,null }; table.GetType().GetMethod("TryGetValue").Invoke(table,args); return args[1];
    }
    static object Internal(Window w,string field) { var c=Controller(w); return c.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c); }
    static bool IdleWork(Window w)
    {
        return !((DispatcherTimer)Internal(w,"timer")).IsEnabled &&
            ((System.Collections.ICollection)Internal(w,"clocks")).Count==0 && !(bool)Internal(w,"watchingLayout");
    }
    static void Check(bool value,string label) { checks++; if(!value) failures++; Console.WriteLine((value?"PASS ":"FAIL ")+label); }
    static string Hash(string path) { using(var file=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-",""); }
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
        var e=root as FrameworkElement; if(e!=null) yield return e;
        if(!(root is Visual)) yield break;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Elements(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    static FrameworkElement Overlay(Host host) { return Elements(host.Root).FirstOrDefault(e=>e.Name=="InfiniteCLLaunchSuccessOverlay"); }
    static void Flush(int milliseconds)
    {
        if(milliseconds>0)
        {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick+=delegate { timer.Stop(); frame.Continue=false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        for(int i=0;i<2;i++) { var f=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate { f.Continue=false; })); Dispatcher.PushFrame(f); }
    }
    static BitmapSource Snapshot(FrameworkElement root)
    {
        root.UpdateLayout();
        var image=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(root); image.Freeze(); return image;
    }
    static void Save(BitmapSource image,string path)
    { var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using(var stream=File.Create(path)) encoder.Save(stream); }
    static byte[] Pixels(BitmapSource image) { var pixels=new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(pixels,image.PixelWidth*4,0); return pixels; }
    static bool Different(BitmapSource a,BitmapSource b)
    { var x=Pixels(a); var y=Pixels(b); int count=0; for(int i=0;i<x.Length && i<y.Length;i++) if(Math.Abs(x[i]-y[i])>3 && ++count>400) return true; return false; }
    static bool Reachable(Host host,FrameworkElement target)
    {
        var point=target.TranslatePoint(new Point(target.ActualWidth/2,target.ActualHeight/2),host.Root);
        var item=host.Root.InputHitTest(point) as DependencyObject;
        while(item!=null) { if(Object.ReferenceEquals(item,target)) return true; item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item); }
        return false;
    }
    sealed class Host
    {
        public PCL.FormMain Window;
        public Grid Root,Modal;
        public SuccessButton Native;
        public SuccessCommand Command=new SuccessCommand();
        public SuccessModel Model=new SuccessModel();
        public Transform NativeTransform=new TranslateTransform(2,3);
        public int Clicks;
        public PCL.LoaderCombo Outer;
        public Window Game;
        public IntPtr GameHandle;
        public PCL.LoaderBase Real;
        public PCL.McWatcher Watcher;
        public Host(bool show,bool install,bool preexisting)
        {
            Outer=new PCL.LoaderCombo { State=PCL.LoadState.Waiting };
            PCL.ModLaunch.McLaunchLoader=Outer; PCL.ModLaunch.McLaunchLoaderReal=null; PCL.ModLaunch.McLaunchWatcher=null; PCL.ModLaunch.McLaunchProcess=null;
            Game=new Window { Width=240,Height=160,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Content=new TextBlock { Text="Native game window fixture" } };
            open.Add(Game); Game.Show(); GameHandle=new WindowInteropHelper(Game).Handle;
            Root=new Grid { Name="PanForm",Background=new ImageBrush(Resource("InfiniteCL.City.png")) { Stretch=Stretch.UniformToFill } };
            Root.Children.Add(new Border { Background=new SolidColorBrush(Color.FromArgb(80,0,0,0)),IsHitTestVisible=false });
            var layout=new Grid { Margin=new Thickness(24,68,24,20) }; layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(225) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Root.Children.Add(layout);
            var side=new StackPanel { Margin=new Thickness(0,24,20,0) }; layout.Children.Add(side);
            side.Children.Add(new TextBlock { Text="CASSELL CL",Foreground=Brushes.BurlyWood,FontSize=22,Margin=new Thickness(0,0,0,17) });
            side.Children.Add(new TextBlock { Text="卡塞尔学院 · 执行部\n穿过雨幕，前往新的世界。",Foreground=Brushes.LightGray,FontSize=12,LineHeight=23,Margin=new Thickness(0,0,0,30) });
            Native=new SuccessButton { Name="NativeLaunch",Height=50,RenderTransform=NativeTransform,Opacity=.87,Background=new SolidColorBrush(Color.FromRgb(200,170,114)) };
            Native.SetBinding(ContentControl.ContentProperty,new Binding("Caption") { Source=Model }); Native.Command=Command; Native.Click+=delegate { Clicks++; }; side.Children.Add(Native);
            var panel=new StackPanel { Name="PanMain" }; var page=new PCL.PageLaunchRight { Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden } }; Grid.SetColumn(page,1); layout.Children.Add(page);
            page.Measure(new Size(780,650)); page.Arrange(new Rect(0,0,780,650)); page.UpdateLayout();
            theme.GetType("InfiniteCL.DragonHome",true).GetMethod("Apply").Invoke(null,new object[] { page,new Action<int>(delegate {}),Resource("InfiniteCL.png") });
            Root.Children.Add(new TextBlock { Text="CASSELL CL  /  世界树档案",Foreground=Brushes.BurlyWood,FontSize=12,Margin=new Thickness(27,25,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false });
            var caption=new TextBlock { Name="TestSceneCaption",Text="启动成功演示 · 测试场景",Foreground=Brushes.LightGray,FontSize=11,Margin=new Thickness(0,0,12,7),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false }; Panel.SetZIndex(caption,1000); Root.Children.Add(caption);
            Modal=new Grid { Name="PanMsg",Visibility=Visibility.Collapsed,Background=new SolidColorBrush(Color.FromArgb(245,12,12,15)) }; Modal.Children.Add(new Button { Content="原生弹窗 · 保持优先",Width=220,Height=70 }); Root.Children.Add(Modal); Panel.SetZIndex(Modal,100);
            Window=new PCL.FormMain { Content=Root,Width=1100,Height=740,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Title="Cassell CL 启动成功测试场景" }; open.Add(Window);
            if(preexisting) { Fresh(); Outer.State=PCL.LoadState.Finished; }
            if(install) Install(); if(show) { Window.Show(); Flush(40); }
        }
        public void Install() { Call("Install",Window,Root,Resource("InfiniteCL.png")); }
        public void Arm() { Outer.Raise(PCL.LoadState.Loading); Flush(1); }
        public void Fresh()
        {
            Real=new PCL.LoaderBase { State=PCL.LoadState.Finished,Parent=Outer };
            var process=Process.GetCurrentProcess();
            Watcher=new PCL.McWatcher { State=PCL.WatcherState.Running,GameProcess=process,Loader=new PCL.LoaderBase { State=PCL.LoadState.Finished,Parent=Real } }; Watcher.Ready(true,true,GameHandle);
            PCL.ModLaunch.McLaunchLoaderReal=Real; PCL.ModLaunch.McLaunchWatcher=Watcher; PCL.ModLaunch.McLaunchProcess=process;
        }
        public void Finish() { Outer.Raise(PCL.LoadState.Finished); Flush(220); }
        public void Success() { Arm(); Fresh(); Finish(); }
        public bool NativeUntouched() { return Object.ReferenceEquals(Native.RenderTransform,NativeTransform) && Math.Abs(Native.Opacity-.87)<.0001 && !Native.HasAnimatedProperties && BindingOperations.IsDataBound(Native,ContentControl.ContentProperty) && Object.ReferenceEquals(Native.Command,Command); }
        public void Close() { Window.Close(); Game.Close(); Flush(30); open.Remove(Window); open.Remove(Game); }
    }
    static void Reject(string label,Action<Host> change)
    {
        var h=new Host(true,true,false); h.Arm(); h.Fresh(); change(h); h.Finish();
        Check(Count(h.Window)==0 && !Playing(h.Window) && Overlay(h)==null,label); h.Close();
        Check(h.Outer.SubscriberCount==0,"cleanup after "+label);
    }
    static void Tests()
    {
        Console.WriteLine("SYSTEM_CLIENT_AREA_ANIMATION "+SystemParameters.ClientAreaAnimation);
        var initial=new Host(true,true,true); Flush(180);
        Check(Subscribed(initial.Window) && initial.Outer.SubscriberCount==1,"install hooks the real native outer event exactly once");
        Check(Count(initial.Window)==0 && !Playing(initial.Window) && Overlay(initial)==null && IdleWork(initial.Window),"preexisting running game never plays startup success or starts idle work");
        initial.Install(); initial.Install(); Flush(20);
        Check(initial.Outer.SubscriberCount==1,"repeated install keeps one native event subscription");
        initial.Outer.Raise(PCL.LoadState.Finished); Flush(100);
        Check(Count(initial.Window)==0,"Finished without a new Loading transition is ignored"); initial.Close();
        Check(!Subscribed(initial.Window) && initial.Outer.SubscriberCount==0,"close detaches native observer event");

        Reject("inner Canceled rejects outer Finished",delegate(Host h) { h.Real.State=PCL.LoadState.Canceled; });
        Reject("inner Failed rejects outer Finished",delegate(Host h) { h.Real.State=PCL.LoadState.Failed; });
        Reject("inner Loading rejects outer Finished",delegate(Host h) { h.Real.State=PCL.LoadState.Loading; });
        Reject("log-only Running without appeared flag is insufficient",delegate(Host h) { h.Watcher.Ready(false,false,h.GameHandle); });
        Reject("splash window before finished flag is insufficient",delegate(Host h) { h.Watcher.Ready(true,false,h.GameHandle); });
        Reject("zero game window handle is insufficient",delegate(Host h) { h.Watcher.Ready(true,true,IntPtr.Zero); });
        Reject("non-Running watcher rejects success",delegate(Host h) { h.Watcher.State=PCL.WatcherState.Loading; });
        Reject("mismatched watcher loader parent rejects success",delegate(Host h) { h.Watcher.Loader.Parent=new PCL.LoaderCombo(); });
        Reject("mismatched watcher loader rejects success",delegate(Host h) { h.Watcher.Loader=new PCL.LoaderBase(); });
        Reject("mismatched watcher process rejects success",delegate(Host h) { h.Watcher.GameProcess=Process.GetCurrentProcess(); });
        Reject("missing launch process rejects success",delegate(Host h) { PCL.ModLaunch.McLaunchProcess=null; });
        using(var exited=Process.Start(new ProcessStartInfo("cmd.exe","/c exit 0") { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden }))
        {
            exited.WaitForExit();
            Reject("exited game process rejects success",delegate(Host h) { h.Watcher.GameProcess=exited; PCL.ModLaunch.McLaunchProcess=exited; });
        }
        using(var foreign=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--fixture-child") { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden }))
        {
            Reject("game HWND owned by a different live process rejects success",delegate(Host h) { Check(!foreign.HasExited,"foreign process ownership fixture is still alive"); h.Watcher.GameProcess=foreign; PCL.ModLaunch.McLaunchProcess=foreign; });
            foreign.WaitForExit();
        }
        Reject("invalid nonzero game HWND rejects success",delegate(Host h) { h.Watcher.Ready(true,true,new IntPtr(1)); });
        Reject("closed game window rejects stale nonzero handle",delegate(Host h) { h.Game.Close(); });
        Reject("reflection getter failure is fail-closed",delegate(Host h) { h.Watcher.Loader.ThrowParent=true; });

        foreach(var terminal in new[] { PCL.LoadState.Failed,PCL.LoadState.Canceled,PCL.LoadState.Aborted })
        {
            var h=new Host(true,true,false); h.Arm(); h.Fresh(); h.Outer.Raise(terminal); Flush(100);
            Check(Count(h.Window)==0 && !Playing(h.Window),"outer "+terminal+" never plays success"); h.Outer.Raise(PCL.LoadState.Finished); Flush(90);
            Check(Count(h.Window)==0,"late Finished after "+terminal+" is consumed"); h.Close();
        }
        var old=new Host(true,true,true); var oldWatcher=old.Watcher; old.Arm(); old.Fresh(); old.Watcher=oldWatcher; PCL.ModLaunch.McLaunchWatcher=oldWatcher; old.Finish();
        Check(Count(old.Window)==0,"replaced inner loader cannot reuse preexisting watcher"); old.Close();
        var oldReal=new Host(true,true,true); var savedReal=oldReal.Real; oldReal.Arm(); oldReal.Fresh(); oldReal.Real=savedReal; oldReal.Watcher.Loader.Parent=savedReal; PCL.ModLaunch.McLaunchLoaderReal=savedReal; oldReal.Finish();
        Check(Count(oldReal.Window)==0,"new watcher cannot reuse preexisting real loader"); oldReal.Close();

        foreach(string condition in new[] { "hidden","minimized","native-hidden","modal","unshown" })
        {
            var h=new Host(condition!="unshown",true,false); h.Arm(); h.Fresh();
            if(condition=="hidden") h.Window.Hide();
            if(condition=="minimized") h.Window.WindowState=WindowState.Minimized;
            if(condition=="native-hidden") h.Window.Hidden=true;
            if(condition=="modal") h.Modal.Visibility=Visibility.Visible;
            bool active=h.Window.IsActive; h.Finish();
            Check(Count(h.Window)==0 && !Playing(h.Window) && h.Window.IsActive==active,condition+" consumes success without activating the launcher");
            h.Window.Hidden=false; h.Modal.Visibility=Visibility.Collapsed; h.Window.WindowState=WindowState.Normal; h.Window.Show(); Flush(220);
            Check(Count(h.Window)==0 && Overlay(h)==null,"restoring "+condition+" never replays consumed success"); h.Close();
        }

        var good=new Host(true,true,false); var before=Snapshot(good.Root); bool wasActive=good.Window.IsActive;
        int originalEvents=0; PCL.LoadState originalNew=PCL.LoadState.Waiting,originalOld=PCL.LoadState.Waiting;
        Action<PCL.LoaderBase,PCL.LoadState,PCL.LoadState> originalHandler=delegate(PCL.LoaderBase sender,PCL.LoadState next,PCL.LoadState previous) { if(Object.ReferenceEquals(sender,good.Outer)) originalEvents++; originalNew=next; originalOld=previous; };
        good.Outer.OnStateChangedUi+=originalHandler; good.Success();
        Console.WriteLine("VALID_STATE "+State(good.Window));
        Check(Count(good.Window)==1 && Playing(good.Window),"fresh native Loading -> Finished with ready live game window plays once");
        Check(originalEvents==2 && originalNew==PCL.LoadState.Finished && originalOld==PCL.LoadState.Loading,"existing native launch-state handler keeps its sender state arguments and calls");
        good.Outer.OnStateChangedUi-=originalHandler;
        Check(Overlay(good)!=null && !Overlay(good).IsHitTestVisible,"success overlay is present and never receives pointer input");
        Check(good.Window.IsActive==wasActive,"success presentation does not activate the launcher");
        Check(Reachable(good,good.Native),"original launch button stays reachable during success animation");
        Check(good.NativeUntouched(),"success does not change native transform opacity bindings or command");
        good.Model.Caption="原生绑定仍可更新"; Flush(10); good.Native.Fire();
        Check(good.Clicks==1 && good.Command.Calls==1 && (string)good.Native.Content=="原生绑定仍可更新","original native click handler command and binding still execute unchanged");
        var early=Snapshot(good.Root); Save(early,Path.Combine(output,"success-early.png")); Flush(240); var late=Snapshot(good.Root); Save(late,Path.Combine(output,"success-later.png"));
        Check(Different(before,early) && Different(early,late),"final assembly produces visibly changing success frames");
        good.Outer.Raise(PCL.LoadState.Finished); Flush(70);
        Check(Count(good.Window)==1,"duplicate Finished does not replay or restart success"); Flush(2300);
        Check(!Playing(good.Window) && Overlay(good)==null && good.NativeUntouched() && IdleWork(good.Window),"natural finish removes overlay clocks and leaves no idle work or changed native controls");
        Console.WriteLine("AFTER_FINISH_STATE "+State(good.Window));
        good.Success(); Check(Count(good.Window)==2,"a second new launch plays exactly once"); good.Window.Hide(); Flush(60);
        Check(!Playing(good.Window) && Overlay(good)==null,"hiding during success immediately removes animation overlay"); good.Window.Show(); Flush(100);
        Check(Count(good.Window)==2 && !Playing(good.Window),"show after interruption does not replay success"); good.Close();
        Check(!Subscribed(good.Window) && good.Outer.SubscriberCount==0 && !Playing(good.Window) && IdleWork(good.Window),"closing releases clocks overlay and native event hooks");

        var delayed=new Host(true,true,false); delayed.Arm(); delayed.Fresh(); delayed.Watcher.Ready(true,false,delayed.GameHandle); delayed.Finish();
        Check(Count(delayed.Window)==0,"outer Finished waits while real game window is not ready"); delayed.Watcher.Ready(true,true,delayed.GameHandle); Flush(220);
        Check(Count(delayed.Window)==1 && Playing(delayed.Window),"bounded readiness observer handles delayed native game window readiness"); delayed.Close();
        var timeout=new Host(true,true,false); timeout.Arm(); timeout.Fresh(); timeout.Watcher.Ready(false,false,IntPtr.Zero); timeout.Finish();
        var deadline=(DateTime)Internal(timeout.Window,"deadline");
        Check(deadline>DateTime.UtcNow && deadline-DateTime.UtcNow<=TimeSpan.FromMinutes(5),"readiness polling has a bounded deadline");
        var controller=Controller(timeout.Window); controller.GetType().GetField("deadline",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,DateTime.UtcNow.AddMilliseconds(-1)); Flush(220);
        Console.WriteLine("TIMEOUT_STATE "+State(timeout.Window)); timeout.Watcher.Ready(true,true,timeout.GameHandle); Flush(240);
        Check(Count(timeout.Window)==0 && !Playing(timeout.Window) && IdleWork(timeout.Window),"expired readiness window stops polling and cannot play late success"); timeout.Close();
        var duringModal=new Host(true,true,false); duringModal.Success(); duringModal.Modal.Visibility=Visibility.Visible; Flush(80);
        Check(!Playing(duringModal.Window) && Overlay(duringModal)==null && duringModal.Modal.IsVisible,"native modal interrupts success and retains priority"); duringModal.Modal.Visibility=Visibility.Collapsed; Flush(100);
        Check(Count(duringModal.Window)==1 && !Playing(duringModal.Window),"modal dismissal cannot replay consumed success"); duringModal.Close();
        foreach(string interruption in new[] { "minimized","native-hidden","disabled" })
        {
            var h=new Host(true,true,false); h.Success();
            if(interruption=="minimized") h.Window.WindowState=WindowState.Minimized;
            if(interruption=="native-hidden") h.Window.Hidden=true;
            if(interruption=="disabled") h.Window.IsHitTestVisible=false;
            Flush(220);
            Check(Count(h.Window)==1 && !Playing(h.Window) && Overlay(h)==null && IdleWork(h.Window),interruption+" during success cancels clocks overlay and active polling"); h.Close();
        }
        var closePlaying=new Host(true,true,false); closePlaying.Success(); Check(Playing(closePlaying.Window),"close-during-animation fixture is playing"); closePlaying.Close();
        Check(!Playing(closePlaying.Window) && Overlay(closePlaying)==null && IdleWork(closePlaying.Window) && closePlaying.Outer.SubscriberCount==0,"closing during success removes all presentation work and native subscription");
        var closing=new Host(true,true,false); closing.Arm(); closing.Fresh(); closing.Close(); closing.Outer.Raise(PCL.LoadState.Finished); Flush(100);
        Check(Count(closing.Window)==0 && !Subscribed(closing.Window) && Overlay(closing)==null,"closing before Finished suppresses success and removes observer");
    }
    static void Demo()
    {
        string frames=Path.Combine(output,"demo-frames"); Directory.CreateDirectory(frames);
        var h=new Host(true,true,false); h.Window.Width=1000; h.Window.Height=710; Flush(70);
        var captured=new List<BitmapSource>(); var timing=new List<string> { "frame,target_ms,actual_ms,state,play_count" };
        h.Arm(); h.Fresh(); var watch=Stopwatch.StartNew();
        const int frameCount=26,frameMilliseconds=100;
        for(int i=0;i<frameCount;i++)
        {
            int target=i*frameMilliseconds,wait=target-(int)watch.ElapsedMilliseconds; if(wait>0) Flush(wait); else Flush(1);
            if(i==2) h.Outer.Raise(PCL.LoadState.Finished);
            timing.Add(i+","+target+","+watch.ElapsedMilliseconds+","+State(h.Window)+","+Count(h.Window)); captured.Add(Snapshot(h.Root));
        }
        watch.Stop();
        for(int i=0;i<captured.Count;i++) Save(captured[i],Path.Combine(frames,"frame-"+i.ToString("000")+".png"));
        File.WriteAllLines(Path.Combine(output,"success-demo-timing.csv"),timing);
        Console.WriteLine("DEMO_FRAME_COUNT "+frameCount); Console.WriteLine("DEMO_ELAPSED_MS "+watch.ElapsedMilliseconds); Console.WriteLine("DEMO_PLAY_COUNT "+Count(h.Window)); Console.WriteLine("DEMO_FINAL_STATE "+State(h.Window)); h.Close();
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==1 && args[0]=="--fixture-child") { System.Threading.Thread.Sleep(1800); return 0; }
            if(args.Length<2) throw new ArgumentException("Expected final theme DLL, QA directory, optional --demo.");
            string dll=Path.GetFullPath(args[0]); output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            Console.WriteLine("THEME_SHA256 "+Hash(dll)); theme=Assembly.LoadFrom(dll); effect=theme.GetType("InfiniteCL.LaunchSuccess",true);
            if(args.Length>2 && args[2]=="--demo") Demo(); else Tests();
            Console.WriteLine("CHECKS "+checks+" FAILURES "+failures); return failures==0?0:1;
        }
        catch(Exception e) { Console.WriteLine(e); return 2; }
        finally { foreach(var w in open.ToArray()) { try { w.Close(); } catch {} } }
    }
}
