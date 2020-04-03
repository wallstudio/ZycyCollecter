using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ZycyCollecter.Properties;

namespace ZycyCollecter
{
    /// <summary>
    /// PageItem.xaml の相互作用ロジック
    /// </summary>
    public partial class PageItem : UserControl
    {

        public PageItem()
        {
            InitializeComponent(); 
        }
    }

    public class MockData
    {
        public ImageSource img { get; } = Imaging.CreateBitmapSourceFromHBitmap(
            Properties.Resources.fallback_image_icon.GetHbitmap(),
            IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
    }
}