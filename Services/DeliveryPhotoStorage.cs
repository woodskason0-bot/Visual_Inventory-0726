using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Http;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Saves delivery photos outside the publish folder, so a republish never touches them.
    /// Program.cs serves RootPath at RequestPathPrefix.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class DeliveryPhotoStorage
    {
        public const string RootPath = @"C:\VIS_Image-Uploads";
        public const string RequestPathPrefix = "/delivery-photos";

        // Caps stored size. A label photo stays readable well below camera resolution.
        private const int MaxDimension = 1600;

        /// <summary>Resizes, re-encodes, saves under a new GUID name. Returns the filename, not a path.</summary>
        public static string Save(IFormFile photo)
        {
            Directory.CreateDirectory(RootPath);

            using var source = Image.FromStream(photo.OpenReadStream());

            double scale = Math.Min(1.0, (double)MaxDimension / Math.Max(source.Width, source.Height));
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));

            using var resized = new Bitmap(width, height);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, width, height);
            }

            string fileName = $"{Guid.NewGuid()}.jpg";
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
            resized.Save(Path.Combine(RootPath, fileName), GetJpegCodec(), encoderParams);

            return fileName;
        }

        private static ImageCodecInfo GetJpegCodec()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
                if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
            throw new InvalidOperationException("JPEG codec not found.");
        }

        public static string UrlFor(string fileName) => $"{RequestPathPrefix}/{fileName}";
    }
}
