using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
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

            var bitmap = await DetectRect(); // destsに詰める

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
            Bitmap bitmap = await GetBitmap();
            var minLengthRate = Slider0.Value;
            var maxDistanceRate = Slider1.Value;
            var threadtholdRate = Slider2.Value;
            var binalizeThreadsholdRate = Slider3.Value;
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
                var dilate = RegistDest("dilate", binary2.Dilate(new Mat(), iterations: 10));
                var edgeRects = new[] { new Rect(0, 0, dilate.Width, 1), new Rect(0, 0, 1, dilate.Height), new Rect(0, dilate.Height - 1, dilate.Width, 1), new Rect(dilate.Width - 1, 0, 1, dilate.Height), };
                var touchCount = edgeRects.Count(rect =>
                {
                    var edge = RegistDest($"Edge-{rect}", dilate.Clone(rect));
                    edge.MinMaxIdx(out var min, out var max);
                    return min != max;
                });
                var linesSet = SearchHouhLines(sobel, touchCount);

                // 重複と斜めを消して評価
                var filted = linesSet
                    .Select(ls =>
                    {
                        var unique = DistinctSimmiler(ls, sobel.Width / 3, 20);
                        return IgnoreDiagonally(unique, 10);
                    })
                    .ToArray();
                var lines = filted
                    .FirstOrDefault(ls => ls.Count >= 4 - touchCount);
                lines ??= filted.OrderBy(ls => ls.Count).FirstOrDefault(ls => ls.Count > 4 - touchCount);
                lines ??= filted.First();

                var lined = resize.CvtColor(ColorConversionCodes.RGBA2RGB);
                foreach (var (x1, y1, x2, y2) in lines.Select(To2Point))
                {
                    lined.Line(x1, y1, x2, y2, Scalar.Green);
                }
                RegistDest("lined", lined);
            });
            return null;
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

        static (int x1, int y1, int x2, int y2) To2Point(LineSegmentPolar line)
        {
            var a = Math.Cos(line.Theta);
            var b = Math.Sin(line.Theta);
            var x0 = a * line.Rho;
            var y0 = b * line.Rho;
            var x1 = (int)(x0 + 1000 * (-b));
            var y1 = (int)(y0 + 1000 * (a));
            var x2 = (int)(x0 - 1000 * (-b));
            var y2 = (int)(y0 - 1000 * (a));
            return (x1, y1, x2, y2);
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

        class GeneralComparer<T> : IComparer<T>
        {
            Func<T, T, int> comparerImplement;

            public GeneralComparer(Func<T, T, int> comparerImplement) => this.comparerImplement = comparerImplement;

            public int Compare([AllowNull] T x, [AllowNull] T y) => comparerImplement(x, y);
        }
    }
}
