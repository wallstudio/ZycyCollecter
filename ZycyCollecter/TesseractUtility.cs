using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Tesseract;
using ZycyCollecter.Tesseract;
using SD = System.Drawing;

namespace ZycyCollecter.Tesseract
{

    public static class PixConverter
    {
        private static readonly BitmapToPixConverter bitmapConverter = new BitmapToPixConverter();
        private static readonly PixToBitmapConverter pixConverter = new PixToBitmapConverter();

        /// <summary>
        /// Converts the specified <paramref name="pix"/> to a Bitmap.
        /// </summary>
        /// <param name="pix">The source image to be converted.</param>
        /// <returns>The converted pix as a <see cref="Bitmap"/>.</returns>
        public static Bitmap ToBitmap(Pix pix)
        {
            return pixConverter.Convert(pix);
        }

        /// <summary>
        /// Converts the specified <paramref name="img"/> to a Pix.
        /// </summary>
        /// <param name="img">The source image to be converted.</param>
        /// <returns>The converted bitmap image as a <see cref="Pix"/>.</returns>
        public static Pix ToPix(Bitmap img)
        {
            return bitmapConverter.Convert(img);
        }
    }


    public class BitmapToPixConverter
    {
        public BitmapToPixConverter()
        {
        }

        /// <summary>
        /// Converts the specified <paramref name="img"/> to a <see cref="Pix"/>.
        /// </summary>
        /// <param name="img">The source image to be converted.</param>
        /// <returns>The converted pix.</returns>
        public Pix Convert(Bitmap img)
        {
            var pixDepth = GetPixDepth(img.PixelFormat);
            var pix = Pix.Create(img.Width, img.Height, pixDepth);
            pix.XRes = (int)Math.Round(img.HorizontalResolution);
            pix.YRes = (int)Math.Round(img.VerticalResolution);

            BitmapData imgData = null;
            PixData pixData = null;
            try
            {
                // TODO: Set X and Y resolution

                if ((img.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                {
                    CopyColormap(img, pix);
                }

                // transfer data
                imgData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, img.PixelFormat);
                pixData = pix.GetData();

                if (imgData.PixelFormat == PixelFormat.Format32bppArgb)
                {
                    TransferDataFormat32bppArgb(imgData, pixData);
                }
                else if (imgData.PixelFormat == PixelFormat.Format24bppRgb)
                {
                    TransferDataFormat24bppRgb(imgData, pixData);
                }
                else if (imgData.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    TransferDataFormat8bppIndexed(imgData, pixData);
                }
                else if (imgData.PixelFormat == PixelFormat.Format1bppIndexed)
                {
                    TransferDataFormat1bppIndexed(imgData, pixData);
                }
                return pix;
            }
            catch (Exception)
            {
                pix.Dispose();
                throw;
            }
            finally
            {
                if (imgData != null)
                {
                    img.UnlockBits(imgData);
                }
            }
        }

        private void CopyColormap(Bitmap img, Pix pix)
        {
            var imgPalette = img.Palette;
            var imgPaletteEntries = imgPalette.Entries;
            var pixColormap = PixColormap.Create(pix.Depth);
            try
            {
                for (int i = 0; i < imgPaletteEntries.Length; i++)
                {
                    if (!pixColormap.AddColor(imgPaletteEntries[i].ToPixColor()))
                    {
                        throw new InvalidOperationException(String.Format("Failed to add colormap entry {0}.", i));
                    }
                }
                pix.Colormap = pixColormap;
            }
            catch (Exception)
            {
                pixColormap.Dispose();
                throw;
            }
        }

        private int GetPixDepth(PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case PixelFormat.Format1bppIndexed:
                    return 1;

                case PixelFormat.Format8bppIndexed:
                    return 8;

                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format24bppRgb:
                    return 32;

                default:
                    throw new InvalidOperationException(String.Format("Source bitmap's pixel format {0} is not supported.", pixelFormat));
            }
        }

        private unsafe void TransferDataFormat1bppIndexed(BitmapData imgData, PixData pixData)
        {
            var height = imgData.Height;
            var width = imgData.Width / 8;
            for (int y = 0; y < height; y++)
            {
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);

                for (int x = 0; x < width; x++)
                {
                    byte pixelVal = BitmapHelper.GetDataByte(imgLine, x);
                    PixData.SetDataByte(pixLine, x, pixelVal);
                }
            }
        }

        private unsafe void TransferDataFormat24bppRgb(BitmapData imgData, PixData pixData)
        {
            var imgFormat = imgData.PixelFormat;
            var height = imgData.Height;
            var width = imgData.Width;

            for (int y = 0; y < height; y++)
            {
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);

                for (int x = 0; x < width; x++)
                {
                    byte* pixelPtr = imgLine + x * 3;
                    byte blue = pixelPtr[0];
                    byte green = pixelPtr[1];
                    byte red = pixelPtr[2];
                    PixData.SetDataFourByte(pixLine, x, BitmapHelper.EncodeAsRGBA(red, green, blue, 255));
                }
            }
        }

        private unsafe void TransferDataFormat32bppArgb(BitmapData imgData, PixData pixData)
        {
            var imgFormat = imgData.PixelFormat;
            var height = imgData.Height;
            var width = imgData.Width;

            for (int y = 0; y < height; y++)
            {
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);

                for (int x = 0; x < width; x++)
                {
                    byte* pixelPtr = imgLine + (x << 2);
                    byte blue = *pixelPtr;
                    byte green = *(pixelPtr + 1);
                    byte red = *(pixelPtr + 2);
                    byte alpha = *(pixelPtr + 3);
                    PixData.SetDataFourByte(pixLine, x, BitmapHelper.EncodeAsRGBA(red, green, blue, alpha));
                }
            }
        }

        private unsafe void TransferDataFormat8bppIndexed(BitmapData imgData, PixData pixData)
        {
            var height = imgData.Height;
            var width = imgData.Width;

            for (int y = 0; y < height; y++)
            {
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);

                for (int x = 0; x < width; x++)
                {
                    byte pixelVal = *(imgLine + x);
                    PixData.SetDataByte(pixLine, x, pixelVal);
                }
            }
        }
    }


    public class PixToBitmapConverter
    {
        public Bitmap Convert(Pix pix, bool includeAlpha = false)
        {
            var pixelFormat = GetPixelFormat(pix);
            var depth = pix.Depth;
            var img = new Bitmap(pix.Width, pix.Height, pixelFormat);

            BitmapData imgData = null;
            PixData pixData = null;
            try
            {
                // TODO: Set X and Y resolution

                // transfer pixel data
                if ((pixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                {
                    TransferPalette(pix, img);
                }

                // transfer data
                pixData = pix.GetData();
                imgData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, pixelFormat);

                if (depth == 32)
                {
                    TransferData32(pixData, imgData, includeAlpha ? 0 : 255);
                }
                else if (depth == 16)
                {
                    TransferData16(pixData, imgData);
                }
                else if (depth == 8)
                {
                    TransferData8(pixData, imgData);
                }
                else if (depth == 1)
                {
                    TransferData1(pixData, imgData);
                }
                return img;
            }
            catch (Exception)
            {
                img.Dispose();
                throw;
            }
            finally
            {
                if (imgData != null)
                {
                    img.UnlockBits(imgData);
                }
            }
        }

        private unsafe void TransferData32(PixData pixData, BitmapData imgData, int alphaMask)
        {
            var imgFormat = imgData.PixelFormat;
            var height = imgData.Height;
            var width = imgData.Width;

            for (int y = 0; y < height; y++)
            {
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);

                for (int x = 0; x < width; x++)
                {
                    var pixVal = PixColor.FromRgba(pixLine[x]);

                    byte* pixelPtr = imgLine + (x << 2);
                    pixelPtr[0] = pixVal.Blue;
                    pixelPtr[1] = pixVal.Green;
                    pixelPtr[2] = pixVal.Red;
                    pixelPtr[3] = (byte)(alphaMask | pixVal.Alpha); // Allow user to include alpha or not
                }
            }
        }

        private unsafe void TransferData16(PixData pixData, BitmapData imgData)
        {
            var imgFormat = imgData.PixelFormat;
            var height = imgData.Height;
            var width = imgData.Width;

            for (int y = 0; y < height; y++)
            {
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);
                ushort* imgLine = (ushort*)imgData.Scan0 + (y * imgData.Stride);

                for (int x = 0; x < width; x++)
                {
                    ushort pixVal = (ushort)PixData.GetDataTwoByte(pixLine, x);

                    imgLine[x] = pixVal;
                }
            }
        }

        private unsafe void TransferData8(PixData pixData, BitmapData imgData)
        {
            var imgFormat = imgData.PixelFormat;
            var height = imgData.Height;
            var width = imgData.Width;

            for (int y = 0; y < height; y++)
            {
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);

                for (int x = 0; x < width; x++)
                {
                    byte pixVal = (byte)PixData.GetDataByte(pixLine, x);

                    imgLine[x] = pixVal;
                }
            }
        }

        private unsafe void TransferData1(PixData pixData, BitmapData imgData)
        {
            var imgFormat = imgData.PixelFormat;
            var height = imgData.Height;
            var width = imgData.Width / 8;

            for (int y = 0; y < height; y++)
            {
                uint* pixLine = (uint*)pixData.Data + (y * pixData.WordsPerLine);
                byte* imgLine = (byte*)imgData.Scan0 + (y * imgData.Stride);

                for (int x = 0; x < width; x++)
                {
                    byte pixVal = (byte)PixData.GetDataByte(pixLine, x);

                    imgLine[x] = pixVal;
                }
            }
        }

        private void TransferPalette(Pix pix, Bitmap img)
        {
            var pallete = img.Palette;
            var maxColors = pallete.Entries.Length;
            var lastColor = maxColors - 1;
            var colormap = pix.Colormap;
            if (colormap != null && colormap.Count <= maxColors)
            {
                var colormapCount = colormap.Count;
                for (int i = 0; i < colormapCount; i++)
                {
                    pallete.Entries[i] = colormap[i].ToDrawingColor();
                }
            }
            else
            {
                for (int i = 0; i < maxColors; i++)
                {
                    var value = (byte)(i * 255 / lastColor);
                    pallete.Entries[i] = SD.Color.FromArgb(value, value, value);
                }
            }
            // This is required to force the palette to update!
            img.Palette = pallete;
        }


        private PixelFormat GetPixelFormat(Pix pix)
        {
            switch (pix.Depth)
            {
                case 1: return PixelFormat.Format1bppIndexed;
                //case 2: return PixelFormat.Format4bppIndexed;
                //case 4: return PixelFormat.Format4bppIndexed;
                case 8: return PixelFormat.Format8bppIndexed;
                case 16: return PixelFormat.Format16bppGrayScale;
                case 32: return PixelFormat.Format32bppArgb;
                default: throw new InvalidOperationException(String.Format("Pix depth {0} is not supported.", pix.Depth));
            }
        }
    }


    public static class ColorConverter
    {
        public static SD.Color ToDrawingColor(this PixColor color)
        {
            return System.Drawing.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        }

        public static PixColor ToPixColor(this SD.Color color)
        {
            return new PixColor(color.R, color.G, color.B, color.A);
        }
    }

}
