using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InfiniteCL
{
    // A presentation-only home page. Native version, account and launch controls
    // stay on the launcher's existing left page.
    public static class DragonHome
    {
        static readonly Brush Ivory = B("#EEE7DA"), Gold = B("#C8AA72"), Dim = B("#9A9487"),
            Rule = B("#A6463D2E"), Paper = B("#E00A0B0B"), Subtle = B("#14000000"),
            Hover = B("#281B170F"), Red = B("#8B3E3B");
        static readonly FontFamily Serif = new FontFamily("Georgia, SimSun"),
            Sans = new FontFamily("Segoe UI, Microsoft YaHei UI");

        static Brush B(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze(); return brush;
        }
        static TextBlock Text(string name, string value, double size, Brush color)
        {
            return new TextBlock { Name = "InfiniteCL" + name, Text = value, FontSize = size,
                Foreground = color, FontFamily = Sans, TextWrapping = TextWrapping.Wrap };
        }
        static FrameworkElement Find(DependencyObject root, string name)
        {
            var element = root as FrameworkElement;
            if (element != null && element.Name == name) return element;
            if (!(root is Visual)) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = Find(VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }

        public static void Apply(FrameworkElement page, Action<int> navigate, ImageSource crest)
        {
            if (page == null || page.GetType().FullName != "PCL.PageLaunchRight") return;
            var parent = Find(page, "PanMain") as StackPanel;
            if (parent == null || Find(parent, "InfiniteCLDragonHome") != null) return;
            parent.Margin = new Thickness(25, 24, 25, 18);
            var home = new StackPanel { Name = "InfiniteCLDragonHome", Margin = new Thickness(0, 0, 0, 18),
                HorizontalAlignment = HorizontalAlignment.Stretch };

            // An archival folio, rather than a collection of floating cards.
            var folio = new Border { Name = "InfiniteCLDragonSurfaceFolio", Height = 249, Background = Paper,
                BorderBrush = Rule, BorderThickness = new Thickness(1), ClipToBounds = true };
            var hero = new Grid { Name = "InfiniteCLArchiveHero" };
            folio.Child = hero;
            var edge = new Border { Name = "InfiniteCLArchiveSpine", Width = 3, Background = Gold,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 22), Opacity = .65 };
            hero.Children.Add(edge);
            var seal = new Grid { Name = "InfiniteCLArchiveSeal", Width = 226, Height = 226,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, -9, 0), IsHitTestVisible = false };
            seal.Children.Add(new AlchemySeal { Name = "InfiniteCLAlchemyCircle", Opacity = .28 });
            seal.Children.Add(new Image { Name = "InfiniteCLCollegeCrest", Source = crest, Width = 159, Height = 159,
                Stretch = Stretch.Uniform, Opacity = .23 });
            hero.Children.Add(seal);

            var copy = new StackPanel { Name = "InfiniteCLArchiveCopy", Margin = new Thickness(25, 22, 23, 18),
                HorizontalAlignment = HorizontalAlignment.Stretch };
            hero.Children.Add(copy);
            var masthead = new Grid { Name = "InfiniteCLArchiveMasthead" };
            masthead.ColumnDefinitions.Add(new ColumnDefinition());
            masthead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var eyebrow = Text("ArchiveEyebrow", "CASSELL COLLEGE  /  EXECUTIVE ARCHIVE", 8.5, Gold);
            masthead.Children.Add(eyebrow);
            var edition = Text("ArchiveEdition", "JAVA EDITION", 8, Dim);
            edition.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(edition, 1); masthead.Children.Add(edition);
            copy.Children.Add(masthead);
            var topRule = new Border { Name = "InfiniteCLArchiveTopRule", Height = 1, Background = Rule,
                Margin = new Thickness(0, 13, 0, 0) }; copy.Children.Add(topRule);
            var chapter = Text("ArchiveChapter", "卡塞尔学院 · 执行部档案", 11, Dim);
            chapter.Margin = new Thickness(0, 20, 0, 0); copy.Children.Add(chapter);
            var heading = Text("DragonHeading", "雨落狂流之暗", 31, Ivory);
            heading.FontFamily = Serif; heading.FontWeight = FontWeights.Normal; heading.LineHeight = 43;
            heading.Margin = new Thickness(0, 8, 0, 0); copy.Children.Add(heading);
            var description = Text("ArchiveDescription", "穿过雨幕，去往世界树的另一端。", 11, Dim);
            description.Margin = new Thickness(1, 9, 0, 0); copy.Children.Add(description);
            var bottom = new Grid { Name = "InfiniteCLArchiveCatalog", Margin = new Thickness(0, 22, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var catalog = Text("ArchiveCatalogText", "世界树  /  炼金术  /  雨夜学院", 9, Gold); bottom.Children.Add(catalog);
            var tag = Text("ArchiveCatalogTag", "VOL. I  ·  RAIN", 8, Dim);
            Grid.SetColumn(tag, 1); bottom.Children.Add(tag); copy.Children.Add(bottom);
            home.Children.Add(folio);

            var commands = new Grid { Name = "InfiniteCLArchiveCommands", Margin = new Thickness(0, 16, 0, 0) };
            for (int i = 0; i < 3; i++) commands.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) commands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var download = Command(1, "01", "版本下载", "装备室 / VERSIONS", navigate);
            var tools = Command(4, "02", "帮助与工具", "档案馆 / UTILITIES", navigate);
            var settings = Command(3, "03", "启动器设置", "控制台 / PREFERENCES", navigate);
            var entries = new[] { download, tools, settings };
            for (int i = 0; i < entries.Length; i++) { Grid.SetColumn(entries[i], i); commands.Children.Add(entries[i]); }
            home.Children.Add(commands);

            // Preserve the user's requested original / derivative author credit.
            var credits = HomeCredits.Create();
            var creditFrame = credits as Border;
            if (creditFrame != null)
            {
                creditFrame.CornerRadius = new CornerRadius(0); creditFrame.Background = Paper;
                creditFrame.BorderBrush = Rule; creditFrame.Padding = new Thickness(16, 10, 16, 10);
            }
            StyleCredits(credits); home.Children.Add(credits);
            var footer = new Grid { Name = "InfiniteCLArchiveFooter", Margin = new Thickness(1, 12, 1, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            var stamp = new Border { Name = "InfiniteCLArchiveStamp", Width = 5, Height = 5, Background = Red,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }; footer.Children.Add(stamp);
            var caption = Text("ArchiveFooterText", "CASSELL CL   /   世界树下，每一次出发都有回响。", 9, Dim);
            Grid.SetColumn(caption, 1); footer.Children.Add(caption); home.Children.Add(footer);

            SizeChangedEventHandler resized = delegate
            {
                bool narrow = home.ActualWidth > 0 && home.ActualWidth < 550;
                heading.FontSize = narrow ? 27 : 31;
                seal.Opacity = narrow ? .64 : 1;
                edition.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                tag.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
                for (int i = 0; i < entries.Length; i++)
                {
                    Grid.SetColumn(entries[i], narrow ? 0 : i);
                    Grid.SetColumnSpan(entries[i], narrow ? 3 : 1);
                    Grid.SetRow(entries[i], narrow ? i : 0);
                    entries[i].Height = narrow ? 58 : 83;
                    entries[i].Margin = narrow ? new Thickness(0) : new Thickness(i == 0 ? 0 : 12, 0, 0, 0);
                }
            };
            home.SizeChanged += resized;
            parent.Children.Insert(0, home);
        }

        static void StyleCredits(DependencyObject root)
        {
            var text = root as TextBlock;
            if (text != null)
            {
                text.Foreground = text.Name.IndexOf("Name", StringComparison.Ordinal) >= 0 ||
                    text.Name == "InfiniteCLCreditsHeading" ? Ivory : Dim;
                foreach (System.Windows.Documents.Inline inline in text.Inlines)
                    if (inline is System.Windows.Documents.Hyperlink) inline.Foreground = Gold;
            }
            var border = root as Border;
            if (border != null && border.Name == "InfiniteCLCreditDivider") border.Background = Rule;
            if (!(root is Visual)) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) StyleCredits(VisualTreeHelper.GetChild(root, i));
        }

        static Border Command(int destination, string number, string label, string subtitle, Action<int> navigate)
        {
            var command = new Border { Name = "InfiniteCLDragonSurfaceCommand" + number,
                BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 1), Background = Subtle,
                Height = 83, Focusable = true, Cursor = Cursors.Hand, ToolTip = label };
            KeyboardNavigation.SetIsTabStop(command, true);
            AutomationProperties.SetName(command, label);
            AutomationProperties.SetHelpText(command, subtitle);
            var row = new Grid { Name = "InfiniteCLArchiveCommandGrid" + number, Margin = new Thickness(11, 10, 11, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            var index = Text("CommandIndex" + number, number, 10, Gold); index.FontFamily = Serif;
            index.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(index);
            var copy = new StackPanel { Name = "InfiniteCLCommandCopy" + number, VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(Text("CommandTitle" + number, label, 12, Ivory));
            var sub = Text("CommandSubtitle" + number, subtitle, 8, Dim); sub.Margin = new Thickness(0, 6, 0, 0);
            copy.Children.Add(sub); Grid.SetColumn(copy, 1); row.Children.Add(copy);
            var arrow = Text("CommandArrow" + number, "↗", 13, Gold); arrow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(arrow, 2); row.Children.Add(arrow); command.Child = row;
            Action refresh = delegate
            {
                bool active = command.IsMouseOver || command.IsKeyboardFocusWithin;
                command.BorderBrush = active ? Gold : Rule; command.Background = active ? Hover : Subtle;
            };
            command.MouseEnter += delegate { refresh(); }; command.MouseLeave += delegate { refresh(); };
            command.GotKeyboardFocus += delegate { refresh(); }; command.LostKeyboardFocus += delegate { refresh(); };
            command.MouseLeftButtonDown += delegate { command.Focus(); };
            command.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            { if (command.IsEnabled && navigate != null) { navigate(destination); args.Handled = true; } };
            command.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (!args.IsRepeat && command.IsEnabled && (args.Key == Key.Enter || args.Key == Key.Space))
                { if (navigate != null) navigate(destination); args.Handled = true; }
            };
            return command;
        }

        sealed class AlchemySeal : FrameworkElement
        {
            static readonly Pen Outer = P("#A18C63", .8), Inner = P("#A18C63", .55);
            static Pen P(string color, double width) { var pen = new Pen(B(color), width); pen.Freeze(); return pen; }
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                double x = ActualWidth / 2, y = ActualHeight / 2, r = Math.Min(ActualWidth, ActualHeight) / 2 - 4;
                if (r <= 0) return;
                dc.DrawEllipse(null, Outer, new Point(x, y), r, r);
                dc.DrawEllipse(null, Inner, new Point(x, y), r - 7, r - 7);
                dc.DrawEllipse(null, Inner, new Point(x, y), r * .71, r * .71);
                for (int i = 0; i < 24; i++)
                {
                    double angle = i * Math.PI / 12;
                    double inner = r - (i % 3 == 0 ? 15 : 11);
                    dc.DrawLine(Inner, new Point(x + Math.Sin(angle) * (r - 4), y + Math.Cos(angle) * (r - 4)),
                        new Point(x + Math.Sin(angle) * inner, y + Math.Cos(angle) * inner));
                }
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI * i / 3, b = a + Math.PI * 2 / 3;
                    dc.DrawLine(Inner, new Point(x + Math.Sin(a) * (r - 14), y + Math.Cos(a) * (r - 14)),
                        new Point(x + Math.Sin(b) * (r - 14), y + Math.Cos(b) * (r - 14)));
                }
            }
        }
    }
}
