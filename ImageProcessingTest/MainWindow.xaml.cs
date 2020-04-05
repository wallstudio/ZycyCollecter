using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tesseract;
using ZycyCollecter;
using ZycyCollecter.Tesseract;
using ZycyCollecter.ViewModel;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Window = System.Windows.Window;

namespace ImageProcessingTest
{
    public partial class MainWindow : Window
    {
        Dictionary<string, Mat> dests = new Dictionary<string, Mat>();
        Mat RegistDest(string name, Mat mat)
        {
            dests[name] = mat;
            Debug.WriteLine($"[Regist] {name}");
            return mat;
        }

        bool isLoading = false;
        object loadingLock = new object();


        public MainWindow()
        {
            InitializeComponent();
            Loaded += EventWrap;
            reflesh.Click += EventWrap;
            imageType.SelectionChanged += EventWrap;
            pageIndex.ValueChanged += EventWrap;
        }

        async void EventWrap(object sender, EventArgs args)
        {
            lock (loadingLock)
            {
                if (isLoading)
                {
                    return;
                }

                isLoading = true;
            }

            Title = "Loadeing";

            string selected = imageType.SelectedItem as string;

            foreach (var v in dests.Values)
            {
                v.Dispose();
            }
            dests.Clear();

            var bitmap = await ORCTraverser(); // destsに詰める

            imageType.Items.Clear();
            foreach (var k in dests.Keys.Reverse())
            {
                imageType.Items.Add(k);
            }

            var data = new ObservableCollection<ImageSource>();
            logImage.ItemsSource = data;
            foreach (var kv in dests.Reverse())
            {
                var logBitmap = await Task.Run(() => kv.Value.ToBitmap());
                data.Add(Utility.CreateImageSource(logBitmap));
            }

            if (bitmap == null)
            {
                selected ??= dests.Keys.Last();
                bitmap = await Task.Run(() => dests[selected].ToBitmap());
                displayImage.Source = Utility.CreateImageSource(bitmap);
            }

            Title = "Completed";

            lock (loadingLock)
            {
                isLoading = false;
            }
        }

        void ShowFallback()
        {
            displayImage.Source = Utility.fallBackImage;
        }

        async Task<Bitmap> ShowFromPDFAsync()
        {
            return await GetBitmap();
        }

        async Task<Bitmap> OCRBasic()
        {
            var traineddata = @"C:\Users\huser\Desktop\book\ZycyCollecter\ZycyCollecter\tessdata";

            var orientations = new RotateFlipType[]
            {
                RotateFlipType.RotateNoneFlipNone,
                RotateFlipType.Rotate90FlipNone,
                RotateFlipType.Rotate180FlipNone,
                RotateFlipType.Rotate270FlipNone,
            };
            foreach (var orientation in orientations)
            {
                var bitmap = await GetBitmap();
                bitmap.RotateFlip(orientation);
                using var tesseranct = new TesseractEngine(traineddata, "jpn");
                using var page = tesseranct.Process(PixConverter.ToPix(bitmap));
                var text = page.GetText().Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
                displayText.Text += $"\n[{orientation.ToString()}]\n{string.Join("\n", text)}\n";
            }

            return await GetBitmap();
        }

        async Task<Bitmap> ORCTraverser()
        {
            StringBuilder log = new StringBuilder();
            Bitmap bitmap = await GetBitmap();

            await Task.Run(() =>
            {
                Mat mat = bitmap.ToMat();
                RegistDest($"origin", mat);

                // OCR
                //var ocrResize = mat.Resize(new OpenCvSharp.Size(1024, 1024f / mat.Width * mat.Height));
                var ocrGray = RegistDest("ocrGray", mat.CvtColor(ColorConversionCodes.RGBA2GRAY));
                var ocrBinary = RegistDest("ocrBinary", ocrGray.Threshold(0, 255, ThresholdTypes.Otsu));
                var orientations = new[] { ocrBinary, ocrBinary.Flip(FlipMode.XY), };
                var traineddata = @"C:\Users\huser\Desktop\book\ZycyCollecter\ZycyCollecter\tessdata";
                var pages = new List<GenericInfoContainer>();
                for (int i = 0; i < orientations.Length; i++)
                {
                    var orientation = orientations[i];
                    using var tesseranct = new TesseractEngine(traineddata, "jpn_vert+jpn");
                    using var page = tesseranct.Process(PixConverter.ToPix(orientation.ToBitmap()));
                    orientation = orientation.CvtColor(ColorConversionCodes.RGBA2RGB);

                    var page_ = new GenericInfoContainer();
                    pages.Add(page_);
                    page_["text"] = page.GetText();
                    page_["Confidience"] = page.GetMeanConfidence();
                    page_["Matrix"] = orientation.Clone();
                    page_["RectMatrix"] = orientation.Clone();

                    using (var iterator = page.GetIterator())
                    {
                        GenericInfoContainer block = null, para = null, line = null, word = null, symbol = null;
                        do
                        {
                            do
                            {
                                do
                                {
                                    do
                                    {
                                        if (iterator.IsAtBeginningOf(PageIteratorLevel.Block))
                                        {
                                            // do whatever you need to do when a block (top most level result) is encountered.
                                            block = new GenericInfoContainer();
                                            page_?.Add(block);
                                            block["text"] = iterator.GetText(PageIteratorLevel.Block);
                                            block["Confidence"] = iterator.GetConfidence(PageIteratorLevel.Block);
                                            if (iterator.TryGetBoundingBox(PageIteratorLevel.Block, out var bounds))
                                            {
                                                // do whatever you want with bounding box for the symbol
                                                var rect = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);
                                                (page_["RectMatrix"] as Mat).Rectangle(rect, Scalar.Red, thickness: 50);
                                            }
                                        }
                                        if (iterator.IsAtBeginningOf(PageIteratorLevel.Para))
                                        {
                                            // do whatever you need to do when a paragraph is encountered.
                                            para = new GenericInfoContainer();
                                            block?.Add(para);
                                            para["text"] = iterator.GetText(PageIteratorLevel.Para);
                                            para["Confidence"] = iterator.GetConfidence(PageIteratorLevel.Para);
                                            if (iterator.TryGetBoundingBox(PageIteratorLevel.Para, out var bounds))
                                            {
                                                // do whatever you want with bounding box for the symbol
                                                var rect = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);
                                                (page_["RectMatrix"] as Mat).Rectangle(rect, Scalar.Green, thickness: 50);
                                            }
                                        }
                                        if (iterator.IsAtBeginningOf(PageIteratorLevel.TextLine))
                                        {
                                            // do whatever you need to do when a line of text is encountered is encountered.
                                            line = new GenericInfoContainer();
                                            para?.Add(line);
                                            line["text"] = iterator.GetText(PageIteratorLevel.TextLine);
                                            line["Confidence"] = iterator.GetConfidence(PageIteratorLevel.TextLine);
                                            if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
                                            {
                                                // do whatever you want with bounding box for the symbol
                                                var rect = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);
                                                (page_["RectMatrix"] as Mat).Rectangle(rect, Scalar.Yellow, thickness: 50);
                                            }
                                        }
                                        if (iterator.IsAtBeginningOf(PageIteratorLevel.Word))
                                        {
                                            // do whatever you need to do when a word is encountered is encountered.
                                            word = new GenericInfoContainer();
                                            line?.Add(word);
                                            word["text"] = iterator.GetText(PageIteratorLevel.Word);
                                            word["Confidence"] = iterator.GetConfidence(PageIteratorLevel.Word);
                                            if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                                            {
                                                // do whatever you want with bounding box for the symbol
                                                var rect = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);
                                                (page_["RectMatrix"] as Mat).Rectangle(rect, Scalar.Green, thickness: 50);
                                            }
                                        }

                                        {
                                            symbol = new GenericInfoContainer();
                                            word?.Add(symbol);
                                            symbol["text"] = iterator.GetText(PageIteratorLevel.Symbol);
                                            symbol["Confidence"] = iterator.GetConfidence(PageIteratorLevel.Symbol);
                                            if (iterator.TryGetBoundingBox(PageIteratorLevel.Symbol, out var bounds))
                                            {
                                                // do whatever you want with bounding box for the symbol
                                                var rect = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);
                                                (page_["RectMatrix"] as Mat).Rectangle(rect, Scalar.Pink, thickness: 50);
                                            }
                                        }

                                    }
                                    while (iterator.Next(PageIteratorLevel.Word, PageIteratorLevel.Symbol));
                                }
                                while (iterator.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word));
                            }
                            while (iterator.Next(PageIteratorLevel.Para, PageIteratorLevel.TextLine));
                        }
                        while (iterator.Next(PageIteratorLevel.Block, PageIteratorLevel.Para));
                    }
                    var text = page.GetText().Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
                    RegistDest($"orientation-{i}", page_["RectMatrix"] as Mat);
                    log.AppendLine($"Orientation-{i}:\n    {string.Join("\n    ", text)}");
                    log.AppendLine($"Confidience: {page.GetMeanConfidence()}");
                }

                var confidier = pages
                    .Where(p => new Regex(@"[^\s\n\r]").IsMatch(p["text"] as string))
                    .OrderByDescending(c => c["Confidience"])
                    .FirstOrDefault() as GenericInfoContainer;
                var result = (float)(confidier?["Confidience"] ?? 0f) > 0.5 ? confidier : null;
                var resultImage = (result?["Matrix"] as Mat);
                RegistDest($"result", resultImage ?? ~ocrGray);
                log.AppendLine($"Result: {result?["Confidience"]}");
            });

            displayText.Text = log.ToString();
            return null;
        }

        async Task<Bitmap> CVBasic()
        {
            var kernel = Math.Pow(2, Slider0.Value * 10);
            SliderLabel0.Text = kernel.ToString();
            var t1 = Slider1.Value * 256;
            var t2 = Slider2.Value * 256;
            Bitmap bitmap = await GetBitmap();
            await Task.Run(() =>
            {
                Mat mat = bitmap.ToMat();
                mat = ~mat.CvtColor(ColorConversionCodes.RGBA2GRAY);
                mat = mat.Blur(new OpenCvSharp.Size(kernel, kernel));
                mat = mat.Threshold(t1, t2, ThresholdTypes.Otsu);
                bitmap = mat.ToBitmap();
            });
            return bitmap;
        }

        async Task<Bitmap> DetectRect()
        {
            StringBuilder log = new StringBuilder();

            Bitmap bitmap = await GetBitmap();
            await Task.Run(() =>
            {
                Mat mat = bitmap.ToMat();
                var resize = RegistDest("resize", mat.Resize(new OpenCvSharp.Size(480, 480f / mat.Width * mat.Height)));
                var gray = RegistDest("gray", resize.CvtColor(ColorConversionCodes.RGBA2GRAY));
                var negative = RegistDest("negative", ~gray);
                var binary = RegistDest("binary", negative.Threshold(230, 255, ThresholdTypes.Binary));
                var open = RegistDest("open", binary.MorphologyEx(MorphTypes.Open, new Mat(), iterations: 2));
                var close = RegistDest("close", open.MorphologyEx(MorphTypes.Close, new Mat(), iterations: 2));
                var blured = RegistDest("blured", close.GaussianBlur(new OpenCvSharp.Size(33, 33), 10, 10));
                blured.MinMaxIdx(out var _min, out var _max);
                var binary2 = RegistDest("binary2", blured.Threshold((_min + _max) / 2, 255, ThresholdTypes.Binary));
                var sobel = RegistDest("sobel", binary2.Laplacian(MatType.CV_8U, ksize: 5));
                var dilate = RegistDest("dilate", binary2.Dilate(new Mat(), iterations: 5));
                
                // 画像端にぶつかっている辺をケア
                var edgeRects = new[] { new Rect(0, 0, dilate.Width, 1), new Rect(0, 0, 1, dilate.Height), new Rect(0, dilate.Height - 1, dilate.Width, 1), new Rect(dilate.Width - 1, 0, 1, dilate.Height), };
                var touches = edgeRects.Where(rect =>
                {
                    var edge = RegistDest($"Edge-{rect}", dilate.Clone(rect));
                    edge.MinMaxIdx(out var min, out var max);
                    return min != max;
                }).ToArray();
                log.AppendLine($"touch: {touches.Length}");

                var linesSet = SearchHouhLines(sobel, touches.Length);

                // 重複と斜めを消して評価
                var filted = linesSet
                    .Select(ls =>
                    {
                        var unique = DistinctSimmiler(ls, sobel.Width / 3, 20);
                        return IgnoreDiagonally(unique, 10);
                    }).ToArray();
                var lines = filted.FirstOrDefault(ls => ls.Count >= 4 - touches.Length);
                log.AppendLine($"HeigScoreLines: {lines?.Count}");
                lines ??= filted.OrderBy(ls => ls.Count).FirstOrDefault(ls => ls.Count > 4 - touches.Length);
                log.AppendLine($"CountMatchLines: {lines?.Count}");
                lines ??= filted.First();
                log.AppendLine($"FallbackAnyLines: {lines?.Count}");
                var fixedLines = lines.Select(To2Point).Concat(touches.Select(r => ((double)r.Left, (double)r.Top, (double)r.Right, (double)r.Bottom)));
                log.AppendLine($"FixedLines:\n     {string.Join("\n    ", fixedLines.Select(ls => ls.ToString()))}");
                var lined = resize.CvtColor(ColorConversionCodes.RGBA2RGB);
                foreach ((double x1, double y1, double x2, double y2) in fixedLines)
                {
                    lined.Line((int)x1, (int)y1, (int)x2, (int)y2, Scalar.Green);
                }

                // 単純組み合わせを列挙して交点を求めコーナーを検出
                var linesCombi = GetCombination(fixedLines);
                var crosses = GetCross(linesCombi).Select(d => new Point2f((float)d.X, (float)d.Y)).ToArray();
                log.AppendLine($"Crosses:\n    {string.Join("\n    ", crosses.Select(c => c.ToString()))}");
                foreach (var p in crosses)
                {
                    lined.Circle(new Point(p.X, p.Y), 5, Scalar.Aqua);
                }

                RegistDest("lined", lined);

                // 透視変換
                var srcRectSum = Sum(crosses, (c0, c1) => c0 + c1);
                (double x, double y) = (srcRectSum.X / crosses.Length, srcRectSum.Y / crosses.Length);
                var srcRectPoints = new Point2f[]
                {
                    crosses.First(c => c.X < x && c.Y < y) * (mat.Width / resize.Width),
                    crosses.First(c => c.X > x && c.Y < y) * (mat.Width / resize.Width),
                    crosses.First(c => c.X > x && c.Y > y) * (mat.Width / resize.Width),
                    crosses.First(c => c.X < x && c.Y > y) * (mat.Width / resize.Width),
                };
                var dstRectPoints = new Point2f[]
                    { new Point2f(0, 0), new Point2f(mat.Width, 0), new Point2f(mat.Width, mat.Height), new Point2f(0, mat.Height), };
                var matrix = Cv2.GetPerspectiveTransform(srcRectPoints, dstRectPoints);
                var parspective = mat.Clone();
                Cv2.WarpPerspective(mat, parspective, matrix, parspective.Size());
                RegistDest("parspective", parspective);
            });

            displayText.Text = log.ToString();
            return null;
        }

        static IEnumerable<(T, T)> GetCombination<T>(IEnumerable<T> source)
        {
            var buf = new LinkedList<T>(source);
            var dst = new List<(T, T)>();
            while(buf.Count > 0)
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

        static IEnumerable<IList<LineSegmentPolar>> SearchHouhLines(Mat src, int touchCount)
        {
            // 距離分解能,角度分解能,閾値,線分の最小長さ,2点が同一線分上にあると見なす場合に許容される最大距離
            var linesSet = new List<IList<LineSegmentPolar>>();
            for (int min = 0, max = src.Width, counter = 0; min + 1 < max; counter++)
            {
                var center = (min + max) / 2;
                IList<LineSegmentPolar> _lines = src.HoughLines(1, Math.PI / 360, center, 0, 0);
                linesSet.Add(_lines);
                Debug.WriteLine($"{counter} {min}-{center}-{max} {_lines.Count}({_lines.Count})");
                if (_lines.Count == 4 - touchCount)
                {
                    break;
                }
                else if (_lines.Count > 4 - touchCount)
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

        static (double x1, double y1, double x2, double y2) To2Point(LineSegmentPolar line)
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

        static IEnumerable<Point2d> GetCross(IEnumerable<((double x1, double y1, double x2, double y2), (double x1, double y1, double x2, double y2))> lines)
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
                    if(p1 == p2 || q1 == q2)
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
                    if(kp == kq)
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

                    if(Math.Abs(x) > 10000 || Math.Abs(y) > 10000)
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

        async Task<ImageSource> GetSampleImage()
        {
            var bitmap = await GetBitmap();
            var imageSource = Utility.CreateImageSource(bitmap);
            return imageSource;
        }

        async Task<Bitmap> GetBitmap()
        {
            var image = await Task.Run(() => PDF.GetImages(@"C:\Users\huser\Desktop\book\hoge0081.PDF"));
            pageIndex.Maximum = image.Count() - 1;
            var index = (int)pageIndex.Value;
            var bitmap = await Task.Run(() => new Bitmap(image.ToArray()[index].image));
            return bitmap;
        }

        static IList<LineSegmentPolar> DistinctSimmiler(IList<LineSegmentPolar> source, double distance, double degree)
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

                if(!isExistSimmler)
                {
                    newBuffer.Add(target);
                }
            }

            return newBuffer;
        }

        static IList<LineSegmentPolar> IgnoreDiagonally(IList<LineSegmentPolar> source, double degree)
        {
            var newBuffer = new List<LineSegmentPolar>();
            foreach (var line in source)
            {
                var theta = Math.Min((Math.PI / 2) - Math.Abs(line.Theta % (Math.PI / 2)), Math.Abs(line.Theta % (Math.PI / 2)));
                if(theta < Math.PI / 180 * degree)
                {
                    newBuffer.Add(line);
                }
            }
            return newBuffer;
        }

        static T Sum<T> (IEnumerable<T> source, Func<T, T, T> adder)
        {
            var sum = default(T);
            foreach(var item in source)
            {
                sum = adder(sum, item);
            }
            return sum;
        }
        
        class GenericInfoContainer : List<GenericInfoContainer>
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

        class GeneralComparer<T> : IComparer<T>
        {
            Func<T, T, int> comparerImplement;

            public GeneralComparer(Func<T, T, int> comparerImplement) => this.comparerImplement = comparerImplement;

            public int Compare([AllowNull] T x, [AllowNull] T y) => comparerImplement(x, y);
        }
    }
}
