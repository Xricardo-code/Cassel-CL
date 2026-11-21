// Runs the About-credit filter against the original page's real WPF hierarchy.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCL
{
    public class PageOtherAbout : Grid { }
    public class MyCard : Grid
    {
        public string Title { get; set; }
        public bool CanSwap { get; set; }
        bool swapped;
        public bool IsSwapped
        {
            get { return swapped; }
            set { swapped = value; foreach (UIElement child in Children) child.Visibility = value ? Visibility.Collapsed : Visibility.Visible; }
        }
    }
    public class MyListItem : Border
    {
        public string Title { get; set; }
        public string Info { get; set; }
    }
}

static class VerifyAboutCredits
{
    static int checks, failures;
    static MethodInfo apply;
    static void Check(bool good, string label)
    { checks++; if (!good) failures++; Console.WriteLine((good ? "PASS " : "FAIL ") + label); }
    static void Flush()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
    static void Apply(FrameworkElement element) { apply.Invoke(null, new object[] { element }); }
    static T Place<T>(Grid grid, T item, int row, int column) where T : UIElement
    { Grid.SetRow(item, row); Grid.SetColumn(item, column); grid.Children.Add(item); return item; }
    static PCL.MyListItem Item(string name, string title, string info)
    { return new PCL.MyListItem { Name = name, Title = title, Info = info, Child = new TextBlock { Text = title + " · " + info }, Height = 40 }; }
    static Image Avatar(string name)
    {
        var drawing = new GeometryDrawing(Brushes.Silver, null, new EllipseGeometry(new Point(17, 17), 17, 17));
        return new Image { Name = name, Width = 34, Height = 34, Source = new DrawingImage(drawing), Margin = new Thickness(3) };
    }
    static Hyperlink Link(string caption, string uri)
    { return new Hyperlink(new Run(caption)) { NavigateUri = new Uri(uri) }; }
    static TextBlock TextLink(Hyperlink link)
    { var text = new TextBlock(); text.Inlines.Add(link); return text; }
    static PCL.MyCard Card(string title, UIElement content)
    { var card = new PCL.MyCard { Title = title, Margin = new Thickness(0, 0, 0, 15) }; card.Children.Add(content); return card; }
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var element = root as FrameworkElement; if (element != null) yield return element;
        if (!(root is Visual)) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static bool Visible(UIElement child)
    {
        for (DependencyObject current = child; current != null; current = VisualTreeHelper.GetParent(current))
        { var element = current as UIElement; if (element != null && element.Visibility != Visibility.Visible) return false; }
        return true;
    }
    static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    { return Elements(root).OfType<T>().SingleOrDefault(element => element.Name == name); }
    static bool AvatarPixelsMatch(Assembly assembly, BitmapSource actual)
    {
        if (actual == null) return false;
        using (var stream = assembly.GetManifestResourceStream("InfiniteCL.Ricardo.jpg"))
        {
            if (stream == null) return false;
            var expected = new BitmapImage(); expected.BeginInit(); expected.CacheOption = BitmapCacheOption.OnLoad;
            expected.StreamSource = stream; expected.EndInit(); expected.Freeze();
            if (actual.PixelWidth != expected.PixelWidth || actual.PixelHeight != expected.PixelHeight) return false;
            var first = new FormatConvertedBitmap(actual, PixelFormats.Bgra32, null, 0);
            var second = new FormatConvertedBitmap(expected, PixelFormats.Bgra32, null, 0);
            int stride = actual.PixelWidth * 4; var a = new byte[stride * actual.PixelHeight]; var b = new byte[a.Length];
            first.CopyPixels(a, stride, 0); second.CopyPixels(b, stride, 0); return a.SequenceEqual(b);
        }
    }
    static bool Circle(Image image)
    {
        if (image == null || image.Width <= 0 || image.Width != image.Height || image.Stretch != Stretch.UniformToFill) return false;
        var circle = image.Clip as EllipseGeometry; double radius = image.Width / 2;
        return circle != null && circle.Center == new Point(radius, radius) && circle.RadiusX == radius && circle.RadiusY == radius;
    }
    static bool InViewport(FrameworkElement element, ScrollViewer scroll)
    {
        if (element == null || !Visible(element) || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var point = element.TranslatePoint(new Point(), scroll);
        return point.X >= 0 && point.Y >= 0 && point.X + element.ActualWidth <= scroll.ActualWidth + .5 && point.Y + element.ActualHeight <= scroll.ViewportHeight + .5;
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args.Length == 0 ? "tools/InfiniteCL.Theme.dll" : args[0]));
            apply = assembly.GetType("InfiniteCL.AboutCredits", true).GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
            Apply(null);
            var page = new PCL.PageOtherAbout();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var main = new StackPanel { Name = "PanMain", Margin = new Thickness(25, 10, 25, 10) }; scroll.Content = main; page.Children.Add(scroll);
            var grid = new Grid { Margin = new Thickness(21, 40, 21, 16) };
            for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            var author = Place(grid, Item("OriginalAuthor", "龙腾猫跃", "Plain Craft Launcher 的开发者！"), 0, 1);
            var coauthor = Place(grid, Item("CoAuthor", "HerobrineXia", "Plain Craft Launcher 的协力开发者！"), 1, 1);
            var version = Place(grid, Item("ItemAboutPcl", "Plain Craft Launcher 2", "当前版本：%VERSION%"), 2, 1);
            var authorAvatar = Place(grid, Avatar("OriginalAvatar"), 0, 0);
            var coauthorAvatar = Place(grid, Avatar("CoauthorAvatar"), 1, 0);
            var versionAvatar = Place(grid, Avatar("VersionAvatar"), 2, 0);
            var supportLink = Link("赞助 PCL !", "https://meloong.com/afd/a/LTCat");
            var support = Place(grid, TextLink(supportLink), 0, 2);
            var update = Place(grid, new Button { Name = "BtnAboutUpdate", Content = "检查更新" }, 2, 2);
            int updateCalls = 0; update.Click += delegate { updateCalls++; };
            var about = Card("关于", grid); main.Children.Add(about);
            var thanksItem = Item("ThanksPerson", "bangbang93", "提供 BMCLAPI 镜像源");
            var thanks = Card("特别鸣谢", thanksItem); main.Children.Add(thanks);
            var sponsorsItem = new TextBlock { Text = "赞助者示例、另一个赞助者" };
            var sponsors = Card("赞助者", sponsorsItem); main.Children.Add(sponsors);
            var legalText = new TextBlock { Text = "Copyright © 成都瓜皮龙科技有限公司\n非 MINECRAFT 官方产品。未经 MOJANG 或 MICROSOFT 批准。" };
            var legalLink = Link("用户协议与免责声明", "https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-RnU9");
            var legalPanel = new StackPanel(); legalPanel.Children.Add(legalText); legalPanel.Children.Add(TextLink(legalLink));
            var legal = Card("法律信息", legalPanel); legal.CanSwap = true; legal.IsSwapped = true; main.Children.Add(legal);
            var licenseText = new TextBlock { Text = "Newtonsoft.Json\nCopyright © 2007 James Newton-King\nLicensed under the MIT License." };
            var licenseLink = Link("查看许可文档", "https://opensource.org/licenses/MIT");
            var licensePanel = new StackPanel(); licensePanel.Children.Add(licenseText); licensePanel.Children.Add(TextLink(licenseLink));
            var license = Card("许可与版权声明", licensePanel); license.CanSwap = true; license.IsSwapped = true; main.Children.Add(license);
            int supportClicks = 0, legalClicks = 0, licenseClicks = 0;
            supportLink.Click += delegate { supportClicks++; }; legalLink.Click += delegate { legalClicks++; }; licenseLink.Click += delegate { licenseClicks++; };
            string originalLegal = legalText.Text, originalLicense = licenseText.Text, originalAuthorInfo = author.Info;
            var oldChildren = main.Children.Cast<UIElement>().ToArray();
            var oldAboutChildren = grid.Children.Cast<UIElement>().ToArray();
            var oldAuthorSource = authorAvatar.Source;
            Apply(page);
            assembly.GetType("InfiniteCL.Theme", true).GetMethod("Install").Invoke(null, null);
            var window = new Window { Content = page, Width = 900, Height = 600, Left = -16000, Top = -16000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show(); Flush();
            // A ScrollViewer's visual children are created by its loaded template.
            // Theme's production Loaded handler must retry the initial pre-template no-op.
            window.UpdateLayout(); Flush();
            Check(about.Visibility == Visibility.Visible && author.Title == "龙腾猫跃" && author.Info == originalAuthorInfo && Visible(author), "About retains the original author credit and unmodified description");
            Check(Visible(authorAvatar) && Object.ReferenceEquals(authorAvatar.Source, oldAuthorSource), "original author avatar remains visible with its original image source");
            Check(coauthor.Visibility == Visibility.Collapsed && coauthorAvatar.Visibility == Visibility.Collapsed, "coauthor name, description and avatar are hidden together");
            Check(version.Visibility == Visibility.Collapsed && versionAvatar.Visibility == Visibility.Collapsed && update.Visibility == Visibility.Collapsed, "launcher metadata row, avatar and update control are hidden together");
            Check(grid.RowDefinitions[1].Height.Value == 0 && grid.RowDefinitions[2].Height.Value == 0 && grid.RowDefinitions[1].ActualHeight == 0 && grid.RowDefinitions[2].ActualHeight == 0, "removed credit rows occupy zero real WPF layout height");
            Check(grid.RowDefinitions[0].ActualHeight >= 40 && author.ActualHeight > 0 && support.ActualWidth > 0, "remaining original-author row and support link render without clipping");
            Check(support.Visibility == Visibility.Visible && Visible(support) && supportLink.NavigateUri.AbsoluteUri == "https://meloong.com/afd/a/LTCat", "original-author support link remains visible with the original target");
            Check(thanks.Visibility == Visibility.Collapsed && sponsors.Visibility == Visibility.Collapsed && !Visible(thanksItem) && !Visible(sponsorsItem), "special thanks and sponsor names are absent from the visible page");
            var ricardo = Find<Border>(page, "InfiniteCLRicardoAbout");
            var ricardoName = Find<TextBlock>(page, "InfiniteCLRicardoAboutName");
            var ricardoBio = Find<TextBlock>(page, "InfiniteCLRicardoAboutBio");
            var ricardoAvatar = Find<Image>(page, "InfiniteCLRicardoAboutAvatar");
            Check(Elements(page).OfType<PCL.MyListItem>().Where(Visible).Select(e => e.Title)
                .Concat(Elements(page).OfType<TextBlock>().Where(e => e.Name == "InfiniteCLRicardoAboutName" && Visible(e)).Select(e => e.Text))
                .SequenceEqual(new[] { "龙腾猫跃", "Ricardo" }), "visible person credits are exactly the original author and Ricardo");
            Check(ricardo != null && Visible(ricardo) && ricardo.Parent == main && main.Children.IndexOf(ricardo) == main.Children.IndexOf(about) + 1 && main.Children.IndexOf(ricardo) < main.Children.IndexOf(legal), "dedicated Ricardo card follows About and precedes the original legal disclosures");
            Check(ricardo != null && Elements(ricardo).OfType<TextBlock>().Any(e => e.Text.StartsWith("二改作者")) && ricardoName != null && ricardoName.Text == "Ricardo" && ricardoBio != null && ricardoBio.Text == "一个默默无闻的衰仔，Github同名同头像，暂时没有发布该启动器" && !Elements(ricardo).OfType<TextBlock>().SelectMany(e => e.Inlines.OfType<Hyperlink>()).Any(), "Ricardo role, name and complete requested biography are present, with no invented GitHub link");
            var ricardoBitmap = ricardoAvatar == null ? null : ricardoAvatar.Source as BitmapSource;
            Check(ricardoBitmap != null && ricardoBitmap.IsFrozen && ricardoBitmap.PixelWidth == 1000 && ricardoBitmap.PixelHeight == 928 && AvatarPixelsMatch(assembly, ricardoBitmap), "About avatar preserves every decoded pixel of the existing embedded Ricardo JPEG");
            string avatarName = ricardoAvatar == null ? "" : System.Windows.Automation.AutomationProperties.GetName(ricardoAvatar);
            Check(Circle(ricardoAvatar) && avatarName.Contains("Ricardo") && avatarName.Contains("头像"), "Ricardo avatar has a circular crop and descriptive accessibility name");
            Check(InViewport(ricardo, scroll) && InViewport(ricardoName, scroll) && InViewport(ricardoBio, scroll) && ricardoBio.TextWrapping == TextWrapping.Wrap && ricardoBio.TextTrimming == TextTrimming.None && ricardoBio.ActualHeight + ricardoBio.Margin.Top + ricardoBio.Margin.Bottom + .5 >= ricardoBio.DesiredSize.Height, "Ricardo card, name and wrapping biography fit completely in the first 900x600 viewport");
            Check(legal.Visibility == Visibility.Visible && license.Visibility == Visibility.Visible && legal.IsSwapped && license.IsSwapped && legal.CanSwap && license.CanSwap, "legal and license disclosure cards retain their existing collapsed and expandable state");
            Check(legalText.Text == originalLegal && licenseText.Text == originalLicense && legalLink.NavigateUri.AbsoluteUri == "https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-RnU9" && licenseLink.NavigateUri.AbsoluteUri == "https://opensource.org/licenses/MIT", "legal copyright, component license text and source URLs remain byte-for-byte unchanged");
            var bitmap = new RenderTargetBitmap(900, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AboutCredits.png"))) encoder.Save(output);
            legal.IsSwapped = false; license.IsSwapped = false; window.UpdateLayout(); Flush();
            Check(Visible(legalText) && Visible(licenseText) && legalPanel.ActualHeight > 0 && licensePanel.ActualHeight > 0, "legal and license contents can still be expanded into real visible layout");
            supportLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent)); legalLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent)); licenseLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            Check(supportClicks == 1 && legalClicks == 1 && licenseClicks == 1, "original support and disclosure link event handlers still execute once");
            Check(main.Children.Cast<UIElement>().Where(child => !Object.ReferenceEquals(child, ricardo)).SequenceEqual(oldChildren) && grid.Children.Cast<UIElement>().SequenceEqual(oldAboutChildren), "native card and row object instances remain attached in their original order for loaded handlers");
            version.Info = "当前版本：2.10.1（348.50）";
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(version.Info == "当前版本：2.10.1（348.50）" && version.Parent == grid && updateCalls == 1, "retained hidden native version and update objects remain valid for existing handlers");
            var rows = grid.RowDefinitions.Select(row => row.Height).ToArray();
            var visibilities = Elements(page).Select(element => element.Visibility).ToArray();
            Apply(page); Apply(page); window.UpdateLayout(); Flush();
            Check(rows.SequenceEqual(grid.RowDefinitions.Select(row => row.Height)) && visibilities.SequenceEqual(Elements(page).Select(element => element.Visibility)) && !legal.IsSwapped && !license.IsSwapped, "repeated filtering is stable and preserves the user's disclosure expansion state");
            Check(Elements(page).Count(element => element.Name == "InfiniteCLRicardoAbout") == 1 && Object.ReferenceEquals(Find<Border>(page, "InfiniteCLRicardoAbout"), ricardo), "repeated Apply keeps exactly one Ricardo card and preserves its original instance");
            var ordinary = new Grid(); var ordinaryMain = new StackPanel { Name = "PanMain" }; var ordinaryThanks = Card("特别鸣谢", new TextBlock { Text = "其他页面内容" }); ordinaryMain.Children.Add(ordinaryThanks); ordinary.Children.Add(ordinaryMain);
            Apply(ordinary); Apply(author); Apply(ordinaryThanks);
            Check(ordinaryThanks.Visibility == Visibility.Visible && ordinaryMain.Children.Count == 1 && author.Visibility == Visibility.Visible, "unrelated pages and individual controls with matching card titles are unaffected");
            var home = (FrameworkElement)assembly.GetType("InfiniteCL.HomeCredits", true).GetMethod("Create").Invoke(null, null);
            var homeItems = Elements(home).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Apply(home);
            Check(home.Visibility == Visibility.Visible && homeItems.SequenceEqual(Elements(home).OfType<TextBlock>().Select(text => text.Text)) && homeItems.Contains("Ricardo") && homeItems.Contains("龙腾猫跃"), "the separately requested home author card still retains Ricardo and original author attribution");
            Check(bitmap.PixelWidth == 900 && bitmap.PixelHeight == 600, "filtered About hierarchy renders successfully to a reviewable WPF image");
            window.Close(); Flush();
            Console.WriteLine("ABOUT_CREDITS_VERIFICATION " + (failures == 0 ? "PASSED" : "FAILED") + " " + (checks - failures) + "/" + checks);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine("HARNESS_ERROR " + ex); return 2; }
    }
}
