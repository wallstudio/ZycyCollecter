using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using ZycyCollecter;

namespace Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void SplitPDF()
        {
            
            string testSrc = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\");
            var testFile = Directory.GetFiles(testSrc, "*.pdf").FirstOrDefault();
            var images = PDF.GetImages(testFile);

            string testDir = Path.Combine(Environment.CurrentDirectory, "test_dest");
            if(!Directory.Exists(testDir))
            {
                Directory.CreateDirectory(testDir);
            }
            foreach (var file in Directory.GetFiles(testDir))
            {
                File.Delete(file);
            }

            int count = 0;
            foreach(var (image, type) in images)
            {
                var dstPath = Path.Combine(testDir, $"{count++.ToString("D3")}.{type}");
                image.Save(dstPath);
            }
        }
    }
}