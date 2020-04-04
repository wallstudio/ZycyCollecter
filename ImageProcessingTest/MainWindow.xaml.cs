using System;
using System.Collections.Generic;
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

namespace ImageProcessingTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OCRBasic;
        }

        void ShowFallback(object sender, EventArgs args)
        {
            displayImage.Source = Utility.fallBackImage;
        }

        async void ShowFromPDFAsync(object sender, EventArgs args)
        {
            displayImage.Source = await GetSampleImage();
        }

        async void OCRBasic(object sender, EventArgs args)
        {
            displayImage.Source = await GetSampleImage();
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
        }

        static async Task<ImageSource> GetSampleImage()
        {
            var bitmap = await GetBitmap();
            var imageSource = Utility.CreateImageSource(bitmap);
            return imageSource;
        }

        static async Task<Bitmap> GetBitmap()
        {
            var image = await Task.Run(() => PDF.GetImages(@"C:\Users\huser\Desktop\book\hoge0081.PDF"));
            var bitmap = await Task.Run(() => new Bitmap(image.ToArray()[9].image));
            return bitmap;
        }
    }
}
