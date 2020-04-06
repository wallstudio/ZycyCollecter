using System.Linq;
using System.Windows;
using System.IO;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace ZycyUtility
{
    public static class SystemUtility
    {

        public static string PickDirectory(string defaultDirectory = null)
        {
            var directory = defaultDirectory;
            while (!Directory.Exists(directory))
            {
                var dialog = new CommonOpenFileDialog() { IsFolderPicker = true, };
                if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                {
                    Application.Current.Shutdown();
                }
                directory = dialog.FileName;
            }

            return directory;
        }

        public static string[] PickFiles(string defaultDirectory = null)
        {
            var files = new string[0];
            while (files?.Length == 0)
            {
                var dialog = new CommonOpenFileDialog() { Multiselect = true };
                if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                {
                    Application.Current.Shutdown();
                }
                files = dialog.FileNames.ToArray();
            }

            return files;
        }

    }

}
