using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
using ZycyCollecter.ViewModel;
using ZycyCollecter.ViewModel.Mocks;

namespace ZycyCollecter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                //DataContext = new WindowMock(5);

                const string directory = @"C:\Users\huser\Desktop\book";
                var viewModel = new WindwoViewModel(directory);
                DataContext = viewModel;
            };
        }
    }
}
