using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using OpCodes = dnlib.DotNet.Emit.OpCodes;

namespace PCL { public class PageSetupSystem:Grid{} public class PageOtherAbout:Grid{} public class PageInstanceMod:Grid{} }
class VerifyLauncherUpdateDisabled
{
    static int checks,failed;
    static void Check(bool ok,string message){checks++;if(!ok)failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+message);}
    static MethodDef Method(ModuleDef module,string type,string name){return module.Find(type,false).Methods.Single(m=>m.Name==name);}
    static void Flush(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    [STAThread] static int Main(string[] args)
    {
        using(var module=ModuleDefMD.Load(args[0]))
        {
            foreach(string name in new[]{"UpdateCheckByButton","UpdateStart","UpdateRestart","UpdateReplace"})
            {var method=Method(module,"PCL.ModSecret",name);Check(method.Body.Instructions.Count==1&&method.Body.Instructions[0].OpCode==OpCodes.Ret&&method.Body.ExceptionHandlers.Count==0,"self-update entry cannot run I/O or prompt: "+name);}
            foreach(var item in new[]{new[]{"PCL.FormMain","ShowUpdateLog"},new[]{"PCL.PageSetupSystem","ComboSystemUpdate_SelectionChanged"}})
            {var method=Method(module,item[0],item[1]);Check(method.Body.Instructions.Count==1&&method.Body.Instructions[0].OpCode==OpCodes.Ret,"launcher changelog/update-warning entry is inert: "+item[1]);}
            var status=Method(module,"PCL.PageSetupSystem","IsLauncherNewest");
            bool shape=status.Body.Instructions.Count==3&&status.Body.Instructions[0].OpCode==OpCodes.Ldc_I4_1&&status.Body.Instructions[1].OpCode==OpCodes.Newobj&&status.Body.Instructions[2].OpCode==OpCodes.Ret;
            Check(shape,"upstream expiry test is local and has no cache/network calls");
            var function=new DynamicMethod("LocalUpdateStatus",typeof(bool?),Type.EmptyTypes);var generator=function.GetILGenerator();
            generator.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);generator.Emit(System.Reflection.Emit.OpCodes.Newobj,typeof(bool?).GetConstructor(new[]{typeof(bool)}));generator.Emit(System.Reflection.Emit.OpCodes.Ret);
            Check(shape&&((Func<bool?>)function.CreateDelegate(typeof(Func<bool?>)))()==true,"disabled version-expiry path returns a valid nullable value without requiring native app startup");
            var server=Method(module,"PCL.ModSecret","ServerSub");var il=server.Body.Instructions;
            int key=il.Select((value,index)=>new{value,index}).Single(x=>x.value.OpCode==OpCodes.Ldstr&&(string)x.value.Operand=="isUpdate").index;
            int store=Enumerable.Range(key+1,12).First(i=>il[i].OpCode==OpCodes.Stloc||il[i].OpCode==OpCodes.Stloc_S);
            var gate=il[store+2];var skip=gate.Operand as Instruction;
            Check(il[store+1].IsLdloc()&&Object.ReferenceEquals(il[store+1].GetLocal(server.Body.Variables),il[store].GetLocal(server.Body.Variables))&&gate.OpCode==OpCodes.Brtrue&&skip!=null,"update-notice flag has a dedicated conditional bypass before any dialog is built");
            int destination=il.IndexOf(skip);
            Check(destination>store&&il.Skip(destination).Take(8).Any(i=>i.Operand is IMethod&&((IMethod)i.Operand).Name=="MoveNext"),"update notices bypass the body to the existing foreach continuation, preserving ordinary notice processing");
            Check(il.Skip(store+3).Take(8).Any(i=>i.OpCode==OpCodes.Ldstr&&((string)i.Operand).StartsWith("重要等级")),"non-update notices retain their original processing route");
            Check(il.Any(i=>i.OpCode==OpCodes.Ldstr&&((string)i.Operand).Contains("minecraft/download.json"))&&il.Any(i=>i.OpCode==OpCodes.Ldstr&&((string)i.Operand).Contains("ServerConfig.json")),"game catalog and shared server configuration downloads remain present");
            Check(Method(module,"PCL.ModSecret","DownloadLatestPCL").Body.Instructions.Count>50,"explicit modpack-export launcher resource helper is preserved independently of self-update");
        }
        var theme=Assembly.LoadFrom(Path.GetFullPath(args[1]));theme.GetType("InfiniteCL.Theme").GetMethod("Install").Invoke(null,null);
        var page=new PCL.PageSetupSystem();var rows=new Grid();page.Children.Add(rows);
        rows.RowDefinitions.Add(new RowDefinition{Height=new GridLength(28)});rows.RowDefinitions.Add(new RowDefinition{Height=new GridLength(9)});rows.RowDefinitions.Add(new RowDefinition{Height=new GridLength(28)});
        rows.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(150)});rows.ColumnDefinitions.Add(new ColumnDefinition());
        var label=new TextBlock{Text="启动器更新"};rows.Children.Add(label);
        var combo=new ComboBox{Name="ComboSystemUpdate"};combo.Items.Add("自动下载");combo.SelectedIndex=0;Grid.SetColumn(combo,1);rows.Children.Add(combo);
        var announcement=new TextBlock{Text="启动器公告"};Grid.SetRow(announcement,2);rows.Children.Add(announcement);
        var panel=new StackPanel{Margin=new Thickness(25),VerticalAlignment=VerticalAlignment.Top};panel.Children.Add(page);
        var update=new Button{Name="BtnSystemUpdate",Content="检查更新"};page.Children.Add(update);
        var export=new Button{Content="导出设置",Height=35};int exportClicks=0;export.Click+=delegate{exportClicks++;};panel.Children.Add(export);
        var modsPage=new PCL.PageInstanceMod();var modUpdate=new Button{Name="BtnSystemUpdate",Content="检查模组更新",Height=35};int modClicks=0;modUpdate.Click+=delegate{modClicks++;};modsPage.Children.Add(modUpdate);panel.Children.Add(modsPage);
        var aboutPage=new PCL.PageOtherAbout();var aboutUpdate=new Button{Name="BtnAboutUpdate",Content="检查启动器更新"};aboutPage.Children.Add(aboutUpdate);panel.Children.Add(aboutPage);
        var root=new Grid{Background=Brushes.Black};root.Children.Add(panel);
        var window=new Window{Content=root,Width=720,Height=430,Left=-16000,Top=-16000,ShowInTaskbar=false,ShowActivated=false};window.Show();Flush();Flush();
        Check(combo.Visibility==Visibility.Collapsed&&!combo.IsEnabled&&label.Visibility==Visibility.Collapsed&&rows.RowDefinitions[0].Height.Value==0&&rows.RowDefinitions[1].Height.Value==0,"launcher update row and its spacer are removed from real WPF layout");
        Check(update.Visibility==Visibility.Collapsed&&!update.IsEnabled&&aboutUpdate.Visibility==Visibility.Collapsed&&!aboutUpdate.IsEnabled,"both settings and About update buttons are unavailable");
        Check(combo.SelectedIndex==0&&announcement.IsVisible,"hidden update controls do not rewrite settings values or hide ordinary announcements");
        Check(modUpdate.IsVisible&&modUpdate.IsEnabled,"similarly named mod update control on a game page remains usable");
        export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));modUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(exportClicks==1&&modClicks==1,"settings export and mod-update click handlers still execute exactly once");
        combo.Visibility=Visibility.Visible;theme.GetType("InfiniteCL.LauncherUpdateUi").GetMethod("Apply").Invoke(null,new object[]{combo});
        Check(combo.Visibility==Visibility.Collapsed,"later attempts to show the launcher update row are suppressed");
        var image=new RenderTargetBitmap(720,430,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory(args[2]);using(var file=File.Create(Path.Combine(args[2],"NoUpdateSettings.png")))encoder.Save(file);
        window.Close();Console.WriteLine("UPDATE_DISABLED_VERIFICATION "+(failed==0?"PASSED":"FAILED")+" "+(checks-failed)+"/"+checks);return failed==0?0:1;
    }
}
