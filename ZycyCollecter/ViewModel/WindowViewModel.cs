using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZycyCollecter.Utility;

namespace ZycyCollecter.ViewModel
{
    abstract class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void RaisePropertyChanged([CallerMemberName]string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    class PageViewModel : ViewModel
    {
        
        public int PageIndex { get; } = -1;
        public ImageSource PageImage { get; } = WPFUtility.fallBackImage;

        public PageViewModel(int pageIndex, ImageSource pageImage)
        {
            PageIndex = pageIndex;
            PageImage = pageImage ?? WPFUtility.fallBackImage;
        }

        // TODO: 編集用のコマンドと表示
    }


    class BookViewModel : ViewModel
    {
        public int PageCount => Pages.Count;
        public ImageSource PageImage => Pages.FirstOrDefault()?.PageImage ?? WPFUtility.fallBackImage;
        public ObservableCollection<PageViewModel> Pages { get; } = new ObservableCollection<PageViewModel>();


        BookViewModel() { }

        public static async Task<BookViewModel> NewAysnc(string file)
        {
            var viewModel = new BookViewModel();

            var imageEnumrable = await Task.Run(() => PDFUtility.GetImages(file));
            var images = imageEnumrable.ToArray();
            for (int i = 0; i < images.Length; i++)
            {
                var (image, _) = images[i];
                var pageImage = await image.ToImageSourceAsync();
                var pageVM = new PageViewModel(i + 1, pageImage);
                viewModel.Pages.Add(pageVM);
                Debug.WriteLine($"{Path.GetFileName(file)} {pageVM.PageIndex}/{images.Length}");
            }

            return viewModel;
        }
    }


    class WindwoViewModel : ViewModel
    {
        public ObservableCollection<BookViewModel> Books { get; } = new ObservableCollection<BookViewModel>();

        public WindwoViewModel(string directory)
        {
            _ = LoadBooksAsync(directory);
        }

        async Task LoadBooksAsync(string directory)
        {
            var files = Directory.GetFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly);
            foreach (var file in files.Take(10))
            {
                var book = await BookViewModel.NewAysnc(file);
                Books.Add(book);
                Debug.WriteLine($"{directory}");
            }
        }
    }
}
