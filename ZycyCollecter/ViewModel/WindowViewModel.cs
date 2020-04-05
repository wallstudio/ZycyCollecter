using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using ZycyCollecter.Utility;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Input;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Text;
using System;

namespace ZycyCollecter.ViewModel
{
    abstract class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName]string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public abstract Task LoadResourceAsync();
    }


    class PageViewModel : ViewModel
    {

        public int PageIndex { get; } = -1;
        
        ImageSource _pageImage = WPFUtility.fallBackImage;
        public ImageSource PageImage
        {
            get => _pageImage;
            private set
            {
                _pageImage = value;
                RaisePropertyChanged();
            }
        }

        public GeneralCommand TestCommand { get; } = new GeneralCommand();

        readonly Image pageImageResource;
        readonly string imageType;

        public PageViewModel(int pageIndex, Image pageImageResource, string imageType)
        {
            PageIndex = pageIndex;
            this.pageImageResource = pageImageResource;
            this.imageType = imageType;
            TestCommand.OnExecuted += async () => RaisePropertyChanged(nameof(PageImage));
        }

        public override async Task LoadResourceAsync()
        {
            if(await Task.Run(() => IsRotate180(pageImageResource)))
            {
                pageImageResource.RotateFlip(RotateFlipType.Rotate180FlipNone);
            }
            try
            {
                var corners = await Task.Run(() => DetectCorners(pageImageResource));
                var trimed = await Task.Run(() => TrimAndTranform(pageImageResource, corners));
                PageImage = trimed.ToImageSource();
            }
            catch(Exception e)
            {
                PageImage = await pageImageResource.ToImageSourceAsync();
                Debug.WriteLine(e);
            }
            Debug.WriteLine($"[{GetHashCode().ToString("X4")}] {PageIndex} {PageImage.Width}x{PageImage.Height}");
        }

        Point2f[] DetectCorners(Image imageResouce)
        {
            var log = new StringBuilder();
            Mat mat = new Bitmap(imageResouce).ToMat();

            // 下ごしらえ
            var resize = mat.Resize(new OpenCvSharp.Size(480, 480f / mat.Width * mat.Height));
            var gray = resize.CvtColor(ColorConversionCodes.RGBA2GRAY);
            var negative = (Mat)~gray;
            var binary = negative.Threshold(230, 255, ThresholdTypes.Binary);
            var open = binary.MorphologyEx(MorphTypes.Open, new Mat(), iterations: 2);
            var close = open.MorphologyEx(MorphTypes.Close, new Mat(), iterations: 2);
            var blured = close.GaussianBlur(new OpenCvSharp.Size(33, 33), 10, 10);
            var binary2 = blured.Threshold(blured.GetMedium(), 255, ThresholdTypes.Binary);
            var sobel = binary2.Laplacian(MatType.CV_8U, ksize: 5);
            var dilate = binary2.Dilate(new Mat(), iterations: 5);

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


            // 単純組み合わせを列挙して交点を求めコーナーを検出
            var linesCombi = fixedLines.GetCombination();
            var crosses = linesCombi.GetCross().Select(CvUtility.To2f).ToArray();
            var corners = CvUtility.Filter4Corner(crosses, resize.Size());
            log.AppendLine($"Crosses:\n    {crosses.ToStringJoin("\n    ")}");
            log.AppendLine($"Corners:\n    {corners.ToStringJoin("\n    ")}");

            // スケールを戻す
            var srcRectPoints = corners.Select(p => p * (mat.Width / (float)resize.Width)).ToArray();

            Debug.WriteLine(log.ToString());
            return srcRectPoints;
        }

        Bitmap TrimAndTranform(Image imageResouce, Point2f[] corners)
        {
            var mat = new Bitmap(imageResouce).ToMat();
            var trimed = mat.TrimAndFitBy4Cross(corners);
            return trimed.ToBitmap();
        }

        bool IsRotate180(Image imageResouce)
        {
            var log = new StringBuilder();
            var bitmap = new Bitmap(imageResouce);

            var (_, rawConfidience) = bitmap.ParseText();

            bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
            var (_, rotate180Confidience) = bitmap.ParseText();

            return rawConfidience < rotate180Confidience;
        }

        // TODO: 編集用のコマンドと表示
    }


    class BookViewModel : ViewModel
    {
        public int PageCount => Pages.Count;

        ImageSource _coverImage = WPFUtility.fallBackImage;
        public ImageSource CoverImage
        {
            get => _coverImage;
            private set
            {
                _coverImage = value;
                RaisePropertyChanged();
            }
        }

        public ObservableCollection<PageViewModel> Pages { get; } = new ObservableCollection<PageViewModel>();

        readonly string pdfFilePath;

        public BookViewModel(string pdfFilePath)
        {
            this.pdfFilePath = pdfFilePath;
            Pages.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(PageCount));
        }

        public override async Task LoadResourceAsync()
        {
            var imageEnumrable = await Task.Run(() => PDFUtility.GetImages(pdfFilePath));
            var images = imageEnumrable.ToArray();
            var pageImage = await images.FirstOrDefault().image?.ToImageSourceAsync();

            var pages = new List<ViewModel>();
            for (int i = 0; i < images.Length; i++)
            {
                var (image, type) = images[i];
                var pageVM = new PageViewModel(i + 1, image, type);
                Pages.Add(pageVM);
                pages.Add(pageVM);
            }

            foreach(var page in pages)
            {
                await page.LoadResourceAsync();
            }
        }
    }


    class WindwoViewModel : ViewModel
    {
        public ObservableCollection<BookViewModel> Books { get; } = new ObservableCollection<BookViewModel>();
        
        readonly string directory;

        public WindwoViewModel(string directory = null)
        {
            while(!Directory.Exists(directory))
            {
                var dialog = new CommonOpenFileDialog() { IsFolderPicker = true, };
                if(dialog.ShowDialog() != CommonFileDialogResult.Ok)
                {
                    Application.Current.Shutdown();
                    return;
                }
                directory = dialog.FileName;
            }
            this.directory = directory;
        }

        public override async Task LoadResourceAsync()
        {
            var files = Directory.GetFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly);
            var books = new List<ViewModel>();
            foreach (var file in files.Take(30))
            {
                var book = new BookViewModel(file);
                Books.Add(book);
                books.Add(book);
            }

            foreach (var book in books)
            {
                await book.LoadResourceAsync();
            }
        }
    }
}
