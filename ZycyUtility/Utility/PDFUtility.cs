using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ZycyUtility
{
    public static class PDFUtility
    {
        public class ImageRenderListener : IRenderListener
        {
            public readonly List<PdfImageObject> Buffer = new List<PdfImageObject>();

            public void RenderImage(ImageRenderInfo renderInfo) => Buffer.Add(renderInfo.GetImage());

            public void BeginTextBlock() { }
            public void EndTextBlock() { }
            public void RenderText(TextRenderInfo renderInfo) { }
        }

        public static IEnumerable<(Image image, string type)> GetImages(string filePath)
        {
            using (var reader = new PdfReader(filePath))
            {
                var parser = new PdfReaderContentParser(reader);
                var renderListener = new ImageRenderListener();
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    parser.ProcessContent(i, renderListener);
                }

                return renderListener.Buffer
                    .Select(info => (info.GetDrawingImage(), info.GetFileType())).ToArray();
            }
        }

        public static void SaveImagesToPDF(IEnumerable<(Image image, string type)> images)
            => throw new NotImplementedException();
    }

}
