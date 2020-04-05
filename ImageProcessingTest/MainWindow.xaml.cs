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
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Window = System.Windows.Window;
using Microsoft.WindowsAPICodePack.Dialogs;
using ZycyUtility;

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
        string pdf;

        Func<Task<Bitmap>> proc => DetectRect;

        public MainWindow(string pdf, int index) : this()
        {
            this.pdf = pdf;
            pageIndex.Minimum = 0;
            pageIndex.Maximum = PDFUtility.GetImages(pdf).Count() - 1;
            pageIndex.Value = index;
        }

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

            Bitmap bitmap = null;
            try
            {
                bitmap = await proc(); // destsに詰める
            }
            catch(Exception e)
            {
                displayText.Text += $"\n\n{e}";
            }

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
                data.Add(logBitmap.ToImageSource());
            }

            if (bitmap == null)
            {
                selected ??= dests.Keys.Last();
                bitmap = await Task.Run(() => dests[selected].ToBitmap());
                displayImage.Source = bitmap.ToImageSource();
            }

            Title = "Completed";

            lock (loadingLock)
            {
                isLoading = false;
            }
        }

        void ShowFallback()
        {
            displayImage.Source = WPFUtility.fallBackImage;
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
            try
            {
                await Task.Run(() =>
                {
                    Mat mat = bitmap.ToMat();
                    log.AppendLine($"RawSize: {mat.Width}x{mat.Height}");

                    // 下ごしらえ
                    var resize = RegistDest("resize", mat.Resize(new OpenCvSharp.Size(480, 480f / mat.Width * mat.Height)));
                    var gray = RegistDest("gray", resize.CvtColor(ColorConversionCodes.RGBA2GRAY));
                    var negative = RegistDest("negative", ~gray);
                    var binary = RegistDest("binary", negative.Threshold(230, 255, ThresholdTypes.Binary));
                    var open = RegistDest("open", binary.MorphologyEx(MorphTypes.Open, new Mat(), iterations: 2));
                    var close = RegistDest("close", open.MorphologyEx(MorphTypes.Close, new Mat(), iterations: 2));
                    var blured = RegistDest("blured", close.GaussianBlur(new OpenCvSharp.Size(33, 33), 10, 10));
                    var binary2 = RegistDest("binary2", blured.Threshold(blured.GetMedium(), 255, ThresholdTypes.Binary));
                    var sobel = RegistDest("sobel", binary2.Laplacian(MatType.CV_8U, ksize: 5));
                    var dilate = RegistDest("dilate", binary2.Dilate(new Mat(), iterations: 5));

                    var touches = dilate.GetTouchWallEdges().ToArray();
                    log.AppendLine($"touch: {touches.Length}");

                    var linesSet = sobel.SearchHouhLines(4 - touches.Length);

                    // 重複と斜めを消して評価
                    var filted = linesSet
                        .Select(ls => ls.DistinctSimmiler(sobel.Width / 3, 20).IgnoreDiagonally(10))
                        .ToArray();
                    var goodLines = filted.FirstOrDefault(ls => ls.Count >= 4 - touches.Length);
                    var okLiness = filted.OrderBy(ls => ls.Count).FirstOrDefault(ls => ls.Count > 4 - touches.Length);
                    var badLines = filted.OrderByDescending(ls => ls.Count).First();
                    var lines = goodLines ?? okLiness ?? badLines;
                    var fixedLines = lines
                        .Select(CvUtility.To2Point)
                        .Concat(touches.Select(r => ((double)r.Left, (double)r.Top, (double)r.Right, (double)r.Bottom)));
                    log.AppendLine($"{nameof(goodLines)}: {goodLines?.Count}");
                    log.AppendLine($"{nameof(okLiness)}: {okLiness?.Count}");
                    log.AppendLine($"{nameof(badLines)}: {badLines?.Count}");
                    log.AppendLine($"{nameof(fixedLines)}:\n     {fixedLines.ToStringJoin("\n    ")}");

                    Mat lined = null;
                    {
                        lined = resize.CvtColor(ColorConversionCodes.RGBA2RGB);
                        foreach ((double x1, double y1, double x2, double y2) in fixedLines)
                        {
                            lined.Line((int)x1, (int)y1, (int)x2, (int)y2, Scalar.Green);
                        }
                    }

                    // 単純組み合わせを列挙して交点を求めコーナーを検出
                    var linesCombi = fixedLines.GetCombination();
                    var crosses = linesCombi.GetCross().Select(CvUtility.To2f).ToArray();
                    var corners = CvUtility.Filter4Corner(crosses, resize.Size());
                    log.AppendLine($"Crosses:\n    {crosses.ToStringJoin("\n    ")}");
                    log.AppendLine($"Corners:\n    {corners.ToStringJoin("\n    ")}");

                    {
                        foreach (var p in corners)
                        {
                            lined.Circle(new Point(p.X, p.Y), 5, Scalar.Aqua);
                        }
                        RegistDest("lined", lined);
                    }

                    var unscaledRectPoints = corners.Select(p => p * (mat.Width / (float)resize.Width)).ToArray();
                    log.AppendLine($"UnscaledCrosses:\n    {unscaledRectPoints.ToStringJoin("\n    ")}");

                    {
                        var unscaledLined = mat.CvtColor(ColorConversionCodes.RGBA2RGB);
                        foreach (var p in unscaledRectPoints)
                        {
                            unscaledLined.Circle(new Point(p.X, p.Y), 10, Scalar.Aqua, thickness: 20);
                        }
                        RegistDest("unscaledLined", unscaledLined);
                    }


                    Mat parspective = mat.TrimAndFitBy4Cross(unscaledRectPoints);
                    RegistDest("parspective", parspective);
                });
            }
            finally
            {
                displayText.Text = log.ToString();
            }

            return null;
        }

        async Task<ImageSource> GetSampleImage()
        {
            var bitmap = await GetBitmap();
            var imageSource = bitmap.ToImageSource();
            return imageSource;
        }

        async Task<Bitmap> GetBitmap()
        {
            while(string.IsNullOrEmpty(pdf))
            {
                var dialog = new CommonOpenFileDialog();
                var filter = new CommonFileDialogFilter("PDF", "pdf");
                dialog.Filters.Add(filter);
                if(dialog.ShowDialog() != CommonFileDialogResult.Ok)
                {
                    Application.Current.Shutdown();
                    return null;
                }
                pdf = dialog.FileName;
            }

            var image = await Task.Run(() => PDFUtility.GetImages(pdf));
            pageIndex.Maximum = image.Count() - 1;
            var index = (int)pageIndex.Value;
            var bitmap = await Task.Run(() => new Bitmap(image.ToArray()[index].image));
            return bitmap;
        }

      
    }
}
