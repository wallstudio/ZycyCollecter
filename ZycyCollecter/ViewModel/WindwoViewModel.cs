using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ZycyUtility;
using System.Collections.Generic;
using System;
using System.Windows.Threading;

namespace ZycyCollecter.ViewModel
{
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
