namespace ClaudeVS
{
    using System;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    internal static class ClipboardImageFile
    {
        private const string PngClipboardFormat = "PNG";
        private const string FileNamePrefix = "clipboard-";
        private const int MaxFileAgeInDays = 7;

        public static string SaveFromClipboard()
        {
            bool hasPng = Clipboard.ContainsData(PngClipboardFormat);
            if (!hasPng && !Clipboard.ContainsImage())
            {
                return null;
            }

            string directory = Path.Combine(Path.GetTempPath(), "ClaudeVS", "ClipboardImages");
            Directory.CreateDirectory(directory);
            DeleteOldFiles();

            string filePath = Path.Combine(directory, $"{FileNamePrefix}{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            if (!hasPng || !TryWritePng())
            {
                WriteBitmap();
            }

            return filePath;

            bool TryWritePng()
            {
                var png = Clipboard.GetData(PngClipboardFormat) as Stream;
                if (png == null || png.Length == 0)
                {
                    return false;
                }

                using (var file = File.Create(filePath))
                {
                    png.CopyTo(file);
                }

                return true;
            }

            void WriteBitmap()
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(Clipboard.GetImage(), PixelFormats.Bgr24, null, 0)));
                using (var file = File.Create(filePath))
                {
                    encoder.Save(file);
                }
            }

            void DeleteOldFiles()
            {
                DateTime oldestAllowed = DateTime.Now.AddDays(-MaxFileAgeInDays);
                foreach (string oldFile in Directory.GetFiles(directory, FileNamePrefix + "*.png"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(oldFile) < oldestAllowed)
                        {
                            File.Delete(oldFile);
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
    }
}
