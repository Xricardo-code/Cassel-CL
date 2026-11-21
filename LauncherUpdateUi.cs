using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfiniteCL
{
    public static class LauncherUpdateUi
    {
        static FrameworkElement Parent(FrameworkElement element)
        { return element.Parent as FrameworkElement ?? (element is Visual ? VisualTreeHelper.GetParent(element) as FrameworkElement : null); }
        static bool InPage(FrameworkElement element,string name)
        { for(int i=0;element!=null && i<30;i++,element=Parent(element)) if(element.GetType().FullName==name)return true; return false; }
        static void Hide(FrameworkElement element)
        {
            if(element.Visibility!=Visibility.Collapsed) element.SetCurrentValue(UIElement.VisibilityProperty,Visibility.Collapsed);
            if(element.IsEnabled) element.SetCurrentValue(UIElement.IsEnabledProperty,false);
        }
        public static void Apply(FrameworkElement element)
        {
            if(element==null)return;
            var text=element as TextBlock;
            if(element.Name!="BtnSystemUpdate" && element.Name!="ItemSystemUpdateDownload" && element.Name!="ComboSystemUpdate" &&
                element.Name!="BtnAboutUpdate" && !(text!=null && text.Text=="启动器更新"))return;
            bool settings=InPage(element,"PCL.PageSetupSystem");
            if(settings && (element.Name=="BtnSystemUpdate" || element.Name=="ItemSystemUpdateDownload"))
            {
                Hide(element);
                var buttons=Parent(element) as Grid;
                if(element.Name=="BtnSystemUpdate" && buttons!=null && buttons.ColumnDefinitions.Count>2)
                {int column=Grid.GetColumn(element);buttons.ColumnDefinitions[column].SharedSizeGroup=null;buttons.ColumnDefinitions[column].Width=new GridLength(0);buttons.ColumnDefinitions[column].MinWidth=0;}
            }
            if(element.Name=="BtnAboutUpdate" && InPage(element,"PCL.PageOtherAbout")) Hide(element);
            if(!settings)return;
            if(element.Name!="ComboSystemUpdate" && !(text!=null && text.Text=="启动器更新"))return;
            Hide(element);
            var grid=Parent(element) as Grid; if(grid==null)return;
            int row=Grid.GetRow(element);
            foreach(UIElement child in grid.Children)
                if(Grid.GetRow(child)==row){var entry=child as FrameworkElement;if(entry!=null)Hide(entry);}
            if(row<grid.RowDefinitions.Count){grid.RowDefinitions[row].Height=new GridLength(0);grid.RowDefinitions[row].MinHeight=0;}
            if(row+1<grid.RowDefinitions.Count && grid.RowDefinitions[row+1].Height.IsAbsolute && grid.RowDefinitions[row+1].Height.Value<=10)
            {grid.RowDefinitions[row+1].Height=new GridLength(0);grid.RowDefinitions[row+1].MinHeight=0;}
        }
    }
}
