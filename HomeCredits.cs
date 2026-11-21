using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfiniteCL
{
    // Home attribution is separate from, and does not replace, the original About credits.
    public static class HomeCredits
    {
        static readonly Brush Text = B("#ECE6D8"), Muted = B("#B4AA98"), Accent = B("#C8AA72");
        static readonly BitmapSource OriginalAvatar = Load("InfiniteCL.OriginalAuthor.jpg");
        static readonly BitmapSource RicardoAvatar = Load("InfiniteCL.Ricardo.jpg");
        static Brush B(string color) { var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        static BitmapSource Load(string resource)
        {
            using(var stream=typeof(HomeCredits).Assembly.GetManifestResourceStream(resource))
            {
                if(stream==null) return null;
                var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                image.StreamSource=stream; image.EndInit(); image.Freeze(); return image;
            }
        }
        static TextBlock Label(string name,string text,double size,Brush foreground,FontWeight weight)
        { return new TextBlock {Name=name,Text=text,FontSize=size,Foreground=foreground,FontWeight=weight,TextWrapping=TextWrapping.Wrap}; }
        public static FrameworkElement Create()
        {
            var card=new Border {Name="InfiniteCLHomeCredits",Margin=new Thickness(0,18,0,0),Padding=new Thickness(18,14,18,14),
                Background=B("#E00F0D0B"),BorderBrush=B("#655235"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5)};
            var content=new StackPanel {Name="InfiniteCLCreditsContent"};
            var title=Label("InfiniteCLCreditsHeading","来源与创作  /  ORIGIN ARCHIVE",11,Accent,FontWeights.SemiBold); content.Children.Add(title);
            var subtitle=Label("InfiniteCLOriginNotice","此启动器基于 PCL 二次创作",10,Muted,FontWeights.Normal); subtitle.Margin=new Thickness(0,4,0,12); content.Children.Add(subtitle);
            var row=new Grid {Name="InfiniteCLAuthors"};
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(24)}); row.ColumnDefinitions.Add(new ColumnDefinition());
            var original=Author("Original","龙腾猫跃","原作作者 / PCL",OriginalAvatar);
            var ricardo=Author("Ricardo","Ricardo","二改作者 / Cassell CL",RicardoAvatar);
            row.Children.Add(original); Grid.SetColumn(ricardo,2); row.Children.Add(ricardo);
            var divider=new Border {Name="InfiniteCLCreditDivider",Width=1,Background=B("#54442E"),Margin=new Thickness(0,3,0,3)}; Grid.SetColumn(divider,1); row.Children.Add(divider);
            content.Children.Add(row);
            var linkText=new TextBlock {Name="InfiniteCLOriginalLink",FontSize=10,Foreground=Accent,Margin=new Thickness(0,10,0,0)};
            var link=new Hyperlink(new Run("原作主页与支持作者 ↗")) {NavigateUri=new Uri("https://meloong.com/afd/a/LTCat"),Foreground=Accent};
            link.RequestNavigate+=delegate(object sender,System.Windows.Navigation.RequestNavigateEventArgs args)
            { Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) {UseShellExecute=true}); args.Handled=true; };
            linkText.Inlines.Add(link); content.Children.Add(linkText); card.Child=content;
            return card;
        }
        static FrameworkElement Author(string id,string name,string role,BitmapSource avatar)
        {
            var row=new Grid {Name="InfiniteCL"+id+"Author"};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(46)}); row.ColumnDefinitions.Add(new ColumnDefinition());
            var image=new Image {Name="InfiniteCL"+id+"Avatar",Source=avatar,Width=44,Height=44,Stretch=Stretch.UniformToFill,
                VerticalAlignment=VerticalAlignment.Center,Clip=new EllipseGeometry(new Point(22,22),22,22)};
            System.Windows.Automation.AutomationProperties.SetName(image,name+"头像"); row.Children.Add(image);
            var labels=new StackPanel {Name="InfiniteCL"+id+"AuthorLabels",Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            labels.Children.Add(Label("InfiniteCL"+id+"Name",name,14,Text,FontWeights.SemiBold));
            var roleLabel=Label("InfiniteCL"+id+"Role",role,10,Muted,FontWeights.Normal); roleLabel.Margin=new Thickness(0,5,0,0); labels.Children.Add(roleLabel);
            Grid.SetColumn(labels,1); row.Children.Add(labels); return row;
        }
    }
}
