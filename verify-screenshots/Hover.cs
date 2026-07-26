using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;

class Hover {
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    static void Main(string[] args) {
        int x = int.Parse(args[0]);
        int y = int.Parse(args[1]);
        int delayMs = int.Parse(args[2]);
        string outFile = args[3];
        SetCursorPos(x, y);
        Thread.Sleep(delayMs);
        POINT p;
        GetCursorPos(out p);
        Console.WriteLine("Cursor at: " + p.X + "," + p.Y);
        Rectangle bounds = new Rectangle(x - 150, y - 150, 350, 300);
        using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            }
            bmp.Save(outFile, ImageFormat.Png);
        }
        Console.WriteLine("Saved: " + outFile);
    }
}