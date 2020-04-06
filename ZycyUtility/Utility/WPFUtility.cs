using System;
using System.Drawing;
using System.Threading.Tasks;
using ZycyUtility.Properties;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ZycyUtility
{
    public static class WPFUtility
    {

        public static readonly ImageSource fallBackImage = Resources.fallback_image_icon.ToImageSource();

        public static async Task<ImageSource> ToImageSourceAsync(this Image source)
        {
            var bitmap = await Task.Run(() => new Bitmap(source));
            return bitmap.ToImageSource();
        }

        /// <summary>
        /// ImageSourceの作成はメインスレッドにしないといけない
        /// </summary>
        public static ImageSource ToImageSource(this Bitmap source)
        {
            return Imaging.CreateBitmapSourceFromHBitmap(source.GetHbitmap(),
                IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        public static async Task SaveAsync(this BitmapSource source, string path)
        {
            var bitmap = source.ToBitmap();
            await Task.Run(() => bitmap.Save(path));
        }

        public static Bitmap ToBitmap(this BitmapSource source)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * ((source.Format.BitsPerPixel + 7) / 8);  // 行の長さは色深度によらず8の倍数のため
            IntPtr intPtr = IntPtr.Zero;
            try
            {
                intPtr = Marshal.AllocCoTaskMem(height * stride);
                source.CopyPixels(new Int32Rect(0, 0, width, height), intPtr, height * stride, stride);
                using var bitmap = new Bitmap(width, height, stride, PixelFormat.Format32bppArgb, intPtr);
                return new Bitmap(bitmap); // Coメモリ -> Managedメモリへ
            }
            finally
            {
                if (intPtr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(intPtr);
            }
        }
    }

}
