using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Windows.Input;
using System.Drawing.Imaging;

namespace ZycyUtility
{

    public class GeneralComparer<T> : IComparer<T>
    {
        Func<T, T, int> comparerImplement;

        public GeneralComparer(Func<T, T, int> comparerImplement) => this.comparerImplement = comparerImplement;

        public int Compare([AllowNull] T x, [AllowNull] T y) => comparerImplement(x, y);
    }


    public class GenericInfoContainer : List<GenericInfoContainer>
    {
        Dictionary<string, object> map = new Dictionary<string, object>();
        public object this[string key]
        {
            get => map[key];
            set => map[key] = value;
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"Children:{Count}; ");
            foreach (var kv in map)
            {
                sb.Append($"{kv.Key}=>{kv.Value}; ");
            }
            return sb.ToString();
        }
    }

    public class GeneralCommand : ICommand
    {
        public event EventHandler CanExecuteChanged;

        public event Action OnExecuted;

        public bool CanExecute(object parameter) => OnExecuted != null;

        public void Execute(object parameter) => OnExecuted();
    }

}
