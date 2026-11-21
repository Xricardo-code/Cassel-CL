using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfiniteCL
{
    // Filters this page's optional acknowledgement display; no core resources or notices are erased.
    public static class AboutCredits
    {
        static readonly ConditionalWeakTable<FrameworkElement, object> Applied = new ConditionalWeakTable<FrameworkElement, object>();
        static readonly BitmapSource RicardoAvatar = LoadRicardoAvatar();
        public static void Apply(FrameworkElement page)
        {
            if(page == null || page.GetType().FullName != "PCL.PageOtherAbout") return;
            object marker; if(Applied.TryGetValue(page,out marker)) return;
            var main=Find(page,"PanMain") as Panel; if(main==null) return;
            FrameworkElement about=null; Grid authorGrid=null; int authorRow=-1;
            foreach(UIElement child in main.Children)
            {
                var element=child as FrameworkElement;
                if(element!=null && Title(element)=="关于")
                { about=element; authorGrid=FindAuthorGrid(element,out authorRow); if(authorGrid!=null) break; }
            }
            // If a different upstream layout is encountered, do not hide unrecognised content.
            if(about==null || authorGrid==null || authorRow<0) return;
            foreach(UIElement child in authorGrid.Children)
                if(Grid.GetRow(child)!=authorRow) child.SetCurrentValue(UIElement.VisibilityProperty,Visibility.Collapsed);
            for(int i=0;i<authorGrid.RowDefinitions.Count;i++)
                if(i!=authorRow) { authorGrid.RowDefinitions[i].Height=new GridLength(0); authorGrid.RowDefinitions[i].MinHeight=0; }
            main.Children.Insert(main.Children.IndexOf(about)+1, CreateRicardoCard());
            foreach(UIElement child in main.Children)
            {
                var element=child as FrameworkElement; if(element==null) continue;
                var title=Title(element);
                if(title=="特别鸣谢" || title=="赞助者") element.SetCurrentValue(UIElement.VisibilityProperty,Visibility.Collapsed);
                else if(title=="法律信息" || title=="许可与版权声明")
                {
                    var collapsed=element.GetType().GetProperty("IsSwapped",BindingFlags.Instance|BindingFlags.Public);
                    if(collapsed!=null && collapsed.CanWrite && collapsed.PropertyType==typeof(bool)) collapsed.SetValue(element,true,null);
                }
            }
            Applied.Add(page,new object());
        }
        static Brush Ink(string color)
        { var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        static BitmapSource LoadRicardoAvatar()
        {
            using(var stream=typeof(AboutCredits).Assembly.GetManifestResourceStream("InfiniteCL.Ricardo.jpg"))
            {
                if(stream==null) return null;
                var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                image.StreamSource=stream; image.EndInit(); image.Freeze(); return image;
            }
        }
        static FrameworkElement CreateRicardoCard()
        {
            var card=new Border { Name="InfiniteCLRicardoAbout", Background=Ink("#EE111113"),
                BorderBrush=Ink("#463D2E"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(7),
                Padding=new Thickness(18,15,18,16), Margin=new Thickness(0,0,0,15) };
            var content=new StackPanel {Name="InfiniteCLRicardoAboutContent"};
            content.Children.Add(new TextBlock {Name="InfiniteCLRicardoAboutHeading",Text="二改作者 / DERIVATIVE AUTHOR",
                FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Ink("#C8AA72"),Margin=new Thickness(0,0,0,13)});
            var row=new Grid {Name="InfiniteCLRicardoAboutRow"};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(56)});
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var avatar=new Image {Name="InfiniteCLRicardoAboutAvatar",Source=RicardoAvatar,Width=56,Height=56,
                Stretch=Stretch.UniformToFill,VerticalAlignment=VerticalAlignment.Center,
                Clip=new EllipseGeometry(new Point(28,28),28,28)};
            System.Windows.Automation.AutomationProperties.SetName(avatar,"Ricardo 二改作者头像"); row.Children.Add(avatar);
            var labels=new StackPanel {Name="InfiniteCLRicardoAboutLabels",Margin=new Thickness(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            labels.Children.Add(new TextBlock {Name="InfiniteCLRicardoAboutName",Text="Ricardo",FontSize=16,
                FontWeight=FontWeights.SemiBold,Foreground=Ink("#EEE7DA")});
            labels.Children.Add(new TextBlock {Name="InfiniteCLRicardoAboutBio",
                Text="一个默默无闻的衰仔，Github同名同头像，暂时没有发布该启动器",FontSize=12,LineHeight=22,
                Foreground=Ink("#ADA28F"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
            Grid.SetColumn(labels,1); row.Children.Add(labels); content.Children.Add(row);
            content.Children.Add(new Border {Name="InfiniteCLAboutDisclaimerDivider",Height=1,
                Background=Ink("#463D2E"),Margin=new Thickness(0,14,0,11)});
            content.Children.Add(new TextBlock {Name="InfiniteCLAboutDisclaimerHeading",Text="免责声明",
                FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Ink("#C8AA72")});
            content.Children.Add(new TextBlock {Name="InfiniteCLAboutDisclaimerText",
                Text="纯免费，仅二改，一切归原作者龙腾猫跃所有",FontSize=12,LineHeight=22,
                Foreground=Ink("#ADA28F"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
            card.Child=content; return card;
        }
        static string Title(FrameworkElement element)
        {
            var property=element.GetType().GetProperty("Title",BindingFlags.Instance|BindingFlags.Public);
            return property==null ? null : property.GetValue(element,null) as string;
        }
        static Grid FindAuthorGrid(DependencyObject root,out int row)
        {
            row=-1; var grid=root as Grid;
            if(grid!=null && grid.RowDefinitions.Count>0)
                foreach(UIElement child in grid.Children)
                {
                    var element=child as FrameworkElement;
                    if(element!=null && Title(element)=="龙腾猫跃") { row=Grid.GetRow(child); return grid; }
                }
            if(root is Visual)
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                { var found=FindAuthorGrid(VisualTreeHelper.GetChild(root,i),out row); if(found!=null) return found; }
            row=-1; return null;
        }
        static FrameworkElement Find(DependencyObject root,string name)
        {
            var element=root as FrameworkElement; if(element!=null && element.Name==name) return element;
            if(root is Visual) for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            { var found=Find(VisualTreeHelper.GetChild(root,i),name); if(found!=null) return found; }
            return null;
        }
    }
}
