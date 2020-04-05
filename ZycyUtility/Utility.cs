using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;
using Rect = OpenCvSharp.Rect;
using Path = System.IO.Path;
using System.Windows.Input;
using Size = OpenCvSharp.Size;
using ZycyCollecter.Tesseract;
using ZycyUtility.Properties;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
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
                using var bitmap = new Bitmap(width, height, stride, PixelFormat.Format24bppRgb, intPtr);
                return new Bitmap(bitmap); // Coメモリ -> Managedメモリへ
            }
            finally
            {
                if (intPtr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(intPtr);
            }
        }
    }


    public static class CollectionUtility
    {

        public static T Sum<T>(this IEnumerable<T> source, Func<T, T, T> adder)
        {
            var sum = default(T);
            foreach (var item in source)
            {
                sum = adder(sum, item);
            }
            return sum;
        }

        public static IEnumerable<(T, T)> GetCombination<T>(this IEnumerable<T> source)
        {
            var buf = new LinkedList<T>(source);
            var dst = new List<(T, T)>();
            while (buf.Count > 0)
            {
                var a = buf.First();
                buf.Remove(a);
                foreach (var b in buf)
                {
                    dst.Add((a, b));
                }
            }
            return dst;
        }

        public static string ToStringJoin<T>(this IEnumerable<T> enumrable, string separator)
            => string.Join(separator, enumrable.Select(e => e.ToString()));
    
        public static IEnumerable<IEnumerable<T>> GroupByCount<T>(this IEnumerable<T> source, int maxCount)
        {
            var taged = source.Select((e, i) => new { g = i / maxCount, e });
            var grouped = taged.GroupBy(e => e.g);
            var typeReveted = grouped.Select(g => g.Select(e => e.e));
            return typeReveted;
        }
    
    }


    public static class CvUtility
    {

        public static IEnumerable<Point2d> GetCross(this IEnumerable<((double x1, double y1, double x2, double y2), (double x1, double y1, double x2, double y2))> lines)
        {
            var newBuffer = new List<Point2d>();
            foreach (var line in lines)
            {
                (double x, double y) p1 = (line.Item1.x1, line.Item1.y1);
                (double x, double y) p2 = (line.Item1.x2, line.Item1.y2);
                (double x, double y) q1 = (line.Item2.x1, line.Item2.y1);
                (double x, double y) q2 = (line.Item2.x2, line.Item2.y2);
                try
                {
                    if (p1 == p2 || q1 == q2)
                    {
                        Debug.WriteLine($"重複点 {line.Item1} {line.Item2}");
                        continue;
                    }

                    // 傾き
                    var kp = (p2.x - p1.x) == 0
                        ? int.MaxValue
                        : (p2.y - p1.y) / (p2.x - p1.x);
                    var kq = (q2.x - q1.x) == 0
                        ? int.MaxValue
                        : (q2.y - q1.y) / (q2.x - q1.x);
                    if (kp == kq)
                    {
                        continue; // 平行
                    }

                    // yオフセット
                    var cp = kp * (0 - p1.x) + p1.y;
                    var cq = kq * (0 - q1.x) + q1.y;

                    // 交点
                    //     0 = kp * (x - 0) - (y - cp)
                    //  -) 0 = kq * (x - 0) - (y - cq)
                    // -------------------------
                    //  0 = (kp - kq) * x + (cp - cq)
                    var x = -(cp - cq) / (kp - kq);
                    var y = kp * (x - 0) + cp;

                    if (Math.Abs(x) > 10000 || Math.Abs(y) > 10000)
                    {
                        continue; // やたら遠いものは平行とみなす
                    }

                    newBuffer.Add(new Point2d(x, y));
                }
                catch (ArithmeticException)
                {
                    continue;
                }
            }
            return newBuffer;
        }

        public static IList<LineSegmentPolar> DistinctSimmiler(this IList<LineSegmentPolar> source, double distance, double degree)
        {
            List<LineSegmentPolar> newBuffer = new List<LineSegmentPolar>();
            for (int i = 0; i < source.Count; i++)
            {
                var target = source[i];
                bool isExistSimmler = newBuffer.Any(ls =>
                {
                    // rhoは原点から直線までの距離
                    var rho = Math.Abs(Math.Abs(target.Rho) - Math.Abs(ls.Rho));
                    var thetaA = Math.Min(Math.PI - Math.Abs(target.Theta % Math.PI), Math.Abs(target.Theta % Math.PI));
                    var thetaB = Math.Min(Math.PI - Math.Abs(ls.Theta % Math.PI), Math.Abs(ls.Theta % Math.PI));
                    var theta = Math.Abs(thetaA - thetaB);
                    bool rhoSimmiler = rho < distance;
                    bool thetaSimmler = theta < Math.PI / 180 * degree;
                    return rhoSimmiler && thetaSimmler;
                });

                if (!isExistSimmler)
                {
                    newBuffer.Add(target);
                }
            }

            return newBuffer;
        }

        public static IList<LineSegmentPolar> IgnoreDiagonally(this IList<LineSegmentPolar> source, double degree)
        {
            var newBuffer = new List<LineSegmentPolar>();
            foreach (var line in source)
            {
                var theta = Math.Min((Math.PI / 2) - Math.Abs(line.Theta % (Math.PI / 2)), Math.Abs(line.Theta % (Math.PI / 2)));
                if (theta < Math.PI / 180 * degree)
                {
                    newBuffer.Add(line);
                }
            }
            return newBuffer;
        }

        public static IEnumerable<IList<LineSegmentPolar>> SearchHouhLines(this Mat src, int detectEdgeCount)
        {
            var linesSet = new List<IList<LineSegmentPolar>>();
            for (int min = 0, max = src.Width, counter = 0; min + 1 < max; counter++)
            {
                var center = (min + max) / 2;
                // 距離分解能,角度分解能,閾値,線分の最小長さ,2点が同一線分上にあると見なす場合に許容される最大距離
                IList<LineSegmentPolar> _lines = src.HoughLines(1, Math.PI / 360, center, 0, 5);
                linesSet.Add(_lines);
                Debug.WriteLine($"{counter} {min}-{center}-{max} {_lines.Count}({_lines.Count})");
                if (_lines.Count == detectEdgeCount)
                {
                    break;
                }
                else if (_lines.Count > detectEdgeCount)
                {
                    min = center; // 閾値が甘すぎるので大きくする
                }
                else
                {
                    max = center; // 閾値が渋すぎたので小さくする
                }
            }

            return linesSet.OrderBy(ls => ls.Count);
        }

        public static (double x1, double y1, double x2, double y2) To2Point(this LineSegmentPolar line)
        {
            var a = Math.Cos(line.Theta);
            var b = Math.Sin(line.Theta);
            var x0 = a * line.Rho;
            var y0 = b * line.Rho;
            var x1 = x0 + 1000 * (-b);
            var y1 = y0 + 1000 * (a);
            var x2 = x0 - 1000 * (-b);
            var y2 = y0 - 1000 * (a);
            return (x1, y1, x2, y2);
        }

        public static Mat TrimAndFitBy4Cross(this Mat mat, Point2f[] crosses)
        {
            // 左上始点の時計回りに双方を合わせる
            var srcRectSum = crosses.Sum((c0, c1) => c0 + c1);
            (double x, double y) = (srcRectSum.X / crosses.Length, srcRectSum.Y / crosses.Length);
            var srcRectPoints = new Point2f[]
            {
                crosses.First(c => c.X < x && c.Y < y), crosses.First(c => c.X > x && c.Y < y),
                crosses.First(c => c.X > x && c.Y > y), crosses.First(c => c.X < x && c.Y > y),
            };
            var dstRectPoints = new Point2f[]
                { new Point2f(0, 0), new Point2f(mat.Width, 0), new Point2f(mat.Width, mat.Height), new Point2f(0, mat.Height), };
            
            var matrix = Cv2.GetPerspectiveTransform(srcRectPoints, dstRectPoints);
            var parspective = mat.Clone();
            Cv2.WarpPerspective(mat, parspective, matrix, parspective.Size());
            return parspective;
        }

        public static IEnumerable<Rect> GetTouchWallEdges(this Mat dilate)
        {
            var edgeRects = new[]
            {
                new Rect(0, 0, dilate.Width, 1),
                new Rect(0, 0, 1, dilate.Height),
                new Rect(0, dilate.Height - 1, dilate.Width, 1),
                new Rect(dilate.Width - 1, 0, 1, dilate.Height),
            };
            return edgeRects.Where(rect => dilate.Clone(rect).IsPainted());
        }

        public static Point2f To2f(this Point2d d) => new Point2f((float)d.X, (float)d.Y);
    
        public static (double min, double max) GetMinMax(this Mat mat)
        {
            mat.MinMaxIdx(out var _min, out var _max);
            return (_min, _max);
        }

        public static double GetMedium(this Mat mat)
        {
            var (min, max) = mat.GetMinMax();
            return (min + max) / 2;
        }

        public static bool IsPainted(this Mat mat)
        {
            var (min, max) = mat.GetMinMax();
            return min != max;
        }

        public static IEnumerable<Point2f> Filter4Corner(IEnumerable<Point2f> crosses, Size size)
        {
            var regulered = crosses
                .Select(c => new Point2d(c.X / size.Width, c.Y / size.Height)).ToList()
                .Where(c => c.X >= -0.1 && c.X <= 1.1 && c.Y >= -0.1 && c.Y <= 1.1);
            var center = new Point2d(
                x: (regulered.Select(p => p.X).Max() + regulered.Select(p => p.X).Min()) / 2,
                y: (regulered.Select(p => p.Y).Max() + regulered.Select(p => p.Y).Min()) / 2);
            var mostFar4 = regulered.OrderByDescending(p => Math.Abs(p.X - center.X) + Math.Abs(p.Y - center.Y)).Take(4);
            return mostFar4.Select(c => new Point2f((float)(c.X * size.Width), (float)(c.Y * size.Height)));
        }
    }


    public static class TesseractUtility
    {
        static readonly Regex successTextPattern = new Regex(@"[^\s\n\r]");

        public static (string text, float confidience) ParseText(this Bitmap bitmap)
        {
            var exeDirctory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var traineddata =  Path.Combine(exeDirctory, @"tessdata");
            using var tesseranct = new TesseractEngine(traineddata, "jpn_vert+jpn+eng");
            using var page = tesseranct.Process(PixConverter.ToPix(bitmap));

            var text = page.GetText();
            if(successTextPattern.IsMatch(text))
            {
                return (text, page.GetMeanConfidence());
            }
            else
            {
                return (string.Empty, -1);
            }
        }
    
    }


    public static class PDFUtility
    {
        public class ImageRenderListener : IRenderListener
        {
            public readonly List<PdfImageObject> Buffer = new List<PdfImageObject>();

            public void RenderImage(ImageRenderInfo renderInfo) => Buffer.Add(renderInfo.GetImage());

            public void BeginTextBlock() { }
            public void EndTextBlock() { }
            public void RenderText(TextRenderInfo renderInfo) { }
        }

        public static IEnumerable<(Image image, string type)> GetImages(string filePath)
        {
            using (var reader = new PdfReader(filePath))
            {
                var parser = new PdfReaderContentParser(reader);
                var renderListener = new ImageRenderListener();
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    parser.ProcessContent(i, renderListener);
                }

                return renderListener.Buffer
                    .Select(info => (info.GetDrawingImage(), info.GetFileType())).ToArray();
            }
        }

        public static void SaveImagesToPDF(IEnumerable<(Image image, string type)> images)
            => throw new NotImplementedException();
    }


    public class GeneralComparer<T> : IComparer<T>
    {
        Func<T, T, int> comparerImplement;

        public GeneralComparer(Func<T, T, int> comparerImplement) => this.comparerImplement = comparerImplement;

        public int Compare([AllowNull] T x, [AllowNull] T y) => comparerImplement(x, y);
    }


    public class GenericInfoContainer : List<GenericInfoContainer>
    {
        Dictionary<string, object> map = new Dictionary<string, object>();
        public object this[string key]
        {
            get => map[key];
            set => map[key] = value;
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"Children:{Count}; ");
            foreach (var kv in map)
            {
                sb.Append($"{kv.Key}=>{kv.Value}; ");
            }
            return sb.ToString();
        }
    }

    public class GeneralCommand : ICommand
    {
        public event EventHandler CanExecuteChanged;

        public event Action OnExecuted;

        public bool CanExecute(object parameter) => OnExecuted != null;

        public void Execute(object parameter) => OnExecuted();
    }

}
