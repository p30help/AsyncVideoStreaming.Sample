using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;

namespace AsynVideoStreaming.Models
{
    public class VideoStream
    {
        private readonly string _filename;
        private long _contentLength;

        public long FileLength
        {
            get { return _contentLength; }
        }

        public VideoStream(string videoPath)
        {
            _filename = videoPath;
            using (var video = File.Open(_filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _contentLength = video.Length;
            }
        }

        public async void WriteToStream(Stream outputStream,
            HttpContent content, TransportContext context)
        {
            try
            {
                var buffer = new byte[65536];

                using (var video = File.Open(_filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var length = (int)video.Length;
                    var bytesRead = 1;

                    while (length > 0 && bytesRead > 0)
                    {
                        bytesRead = video.Read(buffer, 0, Math.Min(length, buffer.Length));
                        await outputStream.WriteAsync(buffer, 0, bytesRead);
                        length -= bytesRead;
                    }
                }
            }
            catch (HttpException)
            {
                return;
            }
            finally
            {
                outputStream.Close();
            }
        }
    }
}