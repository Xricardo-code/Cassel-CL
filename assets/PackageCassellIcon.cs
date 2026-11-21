using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Packages the approved rounded PNG into Windows ICO sizes; no emblem redraw.
internal static class PackageCassellIcon
{
    static readonly int[] Sizes = {16,24,32,48,64,128,256};
    static BitmapSource Source;
    static BitmapSource Resize(int size)
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) dc.DrawImage(Source,new Rect(0,0,size,size));
        var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    static byte[] Png(BitmapSource bitmap)
    {
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=new MemoryStream()) {encoder.Save(stream); return stream.ToArray();}
    }
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length!=2) {Console.Error.WriteLine("Usage: PackageCassellIcon input.png output-folder");return 2;}
        var input=new BitmapImage(); input.BeginInit(); input.CacheOption=BitmapCacheOption.OnLoad;
        input.UriSource=new Uri(Path.GetFullPath(args[0])); input.EndInit(); input.Freeze(); Source=input;
        if(input.PixelWidth!=input.PixelHeight) throw new InvalidDataException("Icon must be square.");
        var rgba=new FormatConvertedBitmap(input,PixelFormats.Bgra32,null,0);
        var pixels=new byte[input.PixelWidth*input.PixelHeight*4]; rgba.CopyPixels(pixels,input.PixelWidth*4,0);
        int n=input.PixelWidth;
        if(pixels[3]!=0 || pixels[(n-1)*4+3]!=0 || pixels[((n-1)*n)*4+3]!=0 || pixels[(n*n-1)*4+3]!=0)
            throw new InvalidDataException("The rounded icon must have transparent outer corners.");
        Directory.CreateDirectory(args[1]);
        var frames=new byte[Sizes.Length][];
        for(int i=0;i<Sizes.Length;i++) frames[i]=Png(Resize(Sizes[i]));
        using(var writer=new BinaryWriter(File.Create(Path.Combine(args[1],"Cassell.ico"))))
        {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)frames.Length);
            int offset=6+16*frames.Length;
            for(int i=0;i<frames.Length;i++)
            {
                writer.Write((byte)(Sizes[i]==256?0:Sizes[i]));writer.Write((byte)(Sizes[i]==256?0:Sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;
            }
            foreach(var frame in frames) writer.Write(frame);
        }
        File.WriteAllBytes(Path.Combine(args[1],"Cassell-512.png"),Png(Resize(512)));
        var sheet=new DrawingVisual();
        using(var dc=sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30,30,30)),null,new Rect(0,0,780,320));
            int x=20;
            for(int i=Sizes.Length-1;i>=0;i--)
            {
                int size=Sizes[i];dc.DrawImage(Resize(size),new Rect(x,20+(256-size)/2.0,size,size));
                dc.DrawText(new FormattedText(size+" px",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),11,Brushes.LightGray),new Point(x,291));x+=size+20;
            }
        }
        var preview=new RenderTargetBitmap(780,320,96,96,PixelFormats.Pbgra32);preview.Render(sheet);
        File.WriteAllBytes(Path.Combine(args[1],"Cassell-icon-preview.png"),Png(preview));
        Console.WriteLine("ICON_PACKAGED 7 sizes; transparent corners confirmed; PNG "+n+"x"+n);
        return 0;
    }
}
