using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows.Input;
using System.Collections;

namespace ZycyCollecter.ViewModel
{
    abstract class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName]string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public abstract Task LoadResourceAsync();
    }
}
