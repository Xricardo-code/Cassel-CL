using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfiniteCL
{
    // Only routine presentation wording. Never edits user text, credits, URLs, settings or identifiers.
    public static class InterfaceCopy
    {
        public static void Apply(FrameworkElement element)
        {
            for(var parent=element;parent!=null;parent=Parent(parent))
                if(parent.GetType().FullName=="PCL.PageOtherAbout" || parent.GetType().FullName=="PCL.PageOtherHelp") return;
            var tip=element.ToolTip as string;
            if(tip!=null && tip.IndexOf("PCL",StringComparison.Ordinal)>=0 && tip.IndexOf("http",StringComparison.OrdinalIgnoreCase)<0 &&
                tip.IndexOf("赞助",StringComparison.Ordinal)<0 && tip.IndexOf("龙猫",StringComparison.Ordinal)<0)
                element.SetCurrentValue(FrameworkElement.ToolTipProperty,tip.Replace("Plain Craft Launcher","启动器").Replace("PCL","启动器"));
            if(element.GetType().FullName=="PCL.MyCheckBox")
            {
                var p=element.GetType().GetProperty("Text",BindingFlags.Instance|BindingFlags.Public);
                if(p!=null && Object.Equals(p.GetValue(element,null),"打开启动器时显示 PCL 图标"))
                    p.SetValue(element,"启动时显示 Cassell CL 学院徽记",null);
            }
        }
        static FrameworkElement Parent(FrameworkElement f)
        {
            return f.Parent as FrameworkElement ?? (f is Visual ? VisualTreeHelper.GetParent(f) as FrameworkElement : null);
        }
    }
}
