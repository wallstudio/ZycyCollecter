using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using Tesseract;
using Path = System.IO.Path;
using ZycyCollecter.Tesseract;

namespace ZycyUtility
{
    public static class TesseractUtility
    {
        static readonly Regex successTextPattern = new Regex(@"[^\s\n\r]");

        public static (string text, float confidience) ParseText(this Bitmap bitmap)
        {
            var exeDirctory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var traineddata =  Path.Combine(exeDirctory, @"tessdata");
            using var tesseranct = new TesseractEngine(traineddata, "jpn_vert+jpn+eng");
            using var page = tesseranct.Process(PixConverter.ToPix(bitmap));

            var text = page.GetText();
            if(successTextPattern.IsMatch(text))
            {
                return (text, page.GetMeanConfidence());
            }
            else
            {
                return (string.Empty, -1);
            }
        }
    
    }

}
