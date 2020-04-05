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
            // TODO: 切り抜き
            // TODO: 回転

            PageImage = await pageImageResource.ToImageSourceAsync();
            Debug.WriteLine($"[{GetHashCode().ToString("X4")}] {PageIndex} {PageImage.Width}x{PageImage.Height}");
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
