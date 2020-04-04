using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZycyCollecter.ViewModel.Mocks
{

    public class PageMock
    {
        public ImageSource Img { get; } = Imaging.CreateBitmapSourceFromHBitmap(
            Properties.Resources.fallback_image_icon.GetHbitmap(),
            IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        public int PageIndex { get; } = -1;

        public PageMock() { }
        public PageMock(int pageIndex) => this.PageIndex = pageIndex;
    }


    public class BookMock
    {
        public ObservableCollection<PageMock> Pages { get; } = new ObservableCollection<PageMock>();
        public string PageCount => Pages.Count.ToString();

        public BookMock() : this(10) { }
        public BookMock(int pageCount)
        {
            for (int i = 0; i < pageCount; i++)
            {
                Pages.Add(new PageMock(i));
            }
        }
    }


    public class WindowMock
    {
        public ObservableCollection<BookMock> Books { get; } = new ObservableCollection<BookMock>();

        public WindowMock() : this(5) { }
        public WindowMock(int bookCount)
        {
            Random rand = new Random();
            for (int i = 0; i < bookCount; i++)
            {
                Books.Add(new BookMock(rand.Next(10, 15)));
            }
        }
    }

}
