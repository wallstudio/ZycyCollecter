using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using ZycyUtility;
using System.Windows;
using System.Collections.Generic;

namespace ZycyCollecter.ViewModel
{
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
}
