using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using ZycyUtility;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Input;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Text;
using System;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Collections;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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

        Brush _background = Brushes.White;
        public Brush Background
        {
            get => _background;
            set
            {
                _background = value;
                RaisePropertyChanged();
            }
        }

        public bool? IsRotate180 { get; private set; } = null;

        public GeneralCommand TestCommand { get; } = new GeneralCommand();
        public GeneralCommand OpenDebugCommand { get; } = new GeneralCommand();

        readonly Image pageImageResource;
        Bitmap correctedPageBitmap;
        readonly string imageType;

        public PageViewModel(int pageIndex, Image pageImageResource, string imageType, string pdf)
        {
            PageIndex = pageIndex;
            this.pageImageResource = pageImageResource;
            this.imageType = imageType;
            OpenDebugCommand.OnExecuted += () => new ImageProcessingTest.MainWindow(pdf, pageIndex - 1).Show();
            TestCommand.OnExecuted += () => _ = SaveAsync(@"C:\Users\huser\Desktop", "pdf");
        }

        public override async Task LoadResourceAsync()
        {
            if (correctedPageBitmap == null)
            {
                await Task.Run(() => PreparThreadSafe());
            }
            PageImage = correctedPageBitmap.ToImageSource();
            Debug.WriteLine($"[{GetHashCode().ToString("X4")}] {PageIndex} {PageImage.Width}x{PageImage.Height}");
        }

        public void PreparThreadSafe()
        {
            if(correctedPageBitmap != null)
            {
                return;
            }
            IsRotate180 = CheckIsRotate180(pageImageResource);
            Background = IsRotate180 is null ? Brushes.Gray : Brushes.White;
            if (IsRotate180 == true)
            {
                pageImageResource.RotateFlip(RotateFlipType.Rotate180FlipNone);
            }

            try
            {
                var corners = DetectCorners(pageImageResource);
                var trimed = TrimAndTranform(pageImageResource, corners);
                correctedPageBitmap = trimed;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                correctedPageBitmap = new Bitmap(pageImageResource);
            }
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

        bool? CheckIsRotate180(Image imageResouce)
        {
            var log = new StringBuilder();
            var bitmap = new Bitmap(imageResouce);

            var (_, rawConfidience) = bitmap.ParseText();

            bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
            var (_, rotate180Confidience) = bitmap.ParseText();

            if(rawConfidience < 0.5 && rotate180Confidience < 0.5)
            {
                return null; // 認識失敗
            }

            return rawConfidience < rotate180Confidience;
        }

        public async Task Rotate180(bool isRotate180)
        {
            while (correctedPageBitmap == null)
            {
                await Task.Delay(1000);
            }

            if ((IsRotate180 == null && isRotate180) || ((bool)IsRotate180 ^ isRotate180))
            {
                correctedPageBitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
                PageImage = correctedPageBitmap.ToImageSource();
            }
        }

        public async Task SaveAsync(string directory, string prefix)
        {
            var filename = $"{prefix}{PageIndex}.{imageType}";
            var path = Path.Combine(directory, filename);
            if(!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await (PageImage as BitmapSource).SaveAsync(path);
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

        public GeneralCommand SaveCommand { get; } = new GeneralCommand();

        readonly string pdfFilePath;
        bool isOddOrientation;

        public BookViewModel(string pdfFilePath)
        {
            this.pdfFilePath = pdfFilePath;
            Pages.CollectionChanged += (s, e) => RaisePropertyChanged(nameof(PageCount));
            SaveCommand.OnExecuted += () => _ = SaveAsync();
        }

        public override async Task LoadResourceAsync()
        {
            var imageEnumrable = await Task.Run(() => PDFUtility.GetImages(pdfFilePath));
            var images = imageEnumrable.ToArray();
            CoverImage = await images.FirstOrDefault().image?.ToImageSourceAsync();

            var pages = new List<PageViewModel>();
            for (int i = 0; i < images.Length; i++)
            {
                var (image, type) = images[i];
                var pageVM = new PageViewModel(i + 1, image, type, pdfFilePath);
                Pages.Add(pageVM);
                pages.Add(pageVM);
            }

            var dispatcher = Application.Current.Dispatcher;
            await Task.Run(() =>
            {
                Parallel.ForEach(
                    source: pages,
                    parallelOptions: new ParallelOptions() { },
                    body: page =>
                    {
                        page.PreparThreadSafe();
                        dispatcher.Invoke(page.LoadResourceAsync);
                    });
            });

            CollectOrientation();

            Debug.WriteLine($"{Path.GetFileName(pdfFilePath)}（{PageCount}）が{(isOddOrientation ? "Odd" : "Even")}で終了");
        }

        void CollectOrientation()
        {
            var rotations = Pages.Select(p => p.IsRotate180);
            var rotationsWithoutCover = rotations.Skip(1).SkipLast(1).ToArray();
            var oddPattern = Enumerable.Range(0, rotationsWithoutCover.Count()).Select(i => i % 2 == 1);
            var evenPattern = Enumerable.Range(0, rotationsWithoutCover.Count()).Select(i => i % 2 == 0);
            var oddDistance = rotationsWithoutCover.Zip(oddPattern).Where(z => z.First != null && z.First != z.Second);
            var evenDistance = rotationsWithoutCover.Zip(evenPattern).Where(z => z.First != null && z.First != z.Second);
            var isOddOrientation = oddDistance.Count() < evenDistance.Count();

            var zip = Pages.Skip(1).SkipLast(1).Zip(isOddOrientation ? oddPattern : evenPattern);
            foreach (var (page, orientation) in zip)
            {
                _ = page.Rotate180(orientation);
            }

            this.isOddOrientation = isOddOrientation;
        }

        public async Task SaveAsync(string parentDirectory = null)
        {
            parentDirectory = SystemUtility.PickDirectory(parentDirectory);
            var directory = Path.Combine(parentDirectory, Path.GetFileNameWithoutExtension(pdfFilePath));
            var tasks = Pages.Select(page => page.SaveAsync(directory, "pdf")).ToList();
            foreach(var task in tasks)
            {
                await task;
            }
        }
    }


    class WindwoViewModel : ViewModel
    {
        public ObservableCollection<BookViewModel> Books { get; } = new ObservableCollection<BookViewModel>();

        double _progress = 0;
        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                RaisePropertyChanged();
            }
        }
        string _spendTime = "not start";
        public string SpendTime
        {
            get => _spendTime;
            set
            {
                _spendTime = value;
                RaisePropertyChanged();
            }
        }


        public GeneralCommand SaveCommand { get; } = new GeneralCommand();

        readonly string[] files;

        public WindwoViewModel()
        {
            files = SystemUtility.PickFiles();

            SaveCommand.OnExecuted += async () => _ = await SaveBooks();
        }
        public WindwoViewModel(string directory)
        {
            directory = SystemUtility.PickDirectory(directory);
            files = Directory.GetFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly);

            SaveCommand.OnExecuted += async () => _ = await SaveBooks();
        }

        public override async Task LoadResourceAsync()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            var timer = new DispatcherTimer() { Interval = new TimeSpan(0, 0, 0, 0, 50), };
            timer.Tick += (s, e) => SpendTime = stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff");
            timer.Start();

            var books = new List<ViewModel>();
            foreach (var file in files)
            {
                var book = new BookViewModel(file);
                Books.Add(book);
                books.Add(book);
            }

            for (int i = 0; i < books.Count; i++)
            {
                var book = books[i];
                await book.LoadResourceAsync();
                Progress = (i + 1) / (double)books.Count;
            }

            timer.Stop();
            stopwatch.Stop();
        }

        public async Task<string> SaveBooks()
        {
            string directory = SystemUtility.PickDirectory();
            foreach (var book in Books)
            {
                await book.SaveAsync(directory);
            }

            return directory;
        }
    }
}
