using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Transactions;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.Http;

namespace AsynVideoStreaming.Controllers
{
    public class MediaController : ApiController
    {
        #region Fields

        // This will be used in copying input stream to output stream.
        public const int ReadStreamBufferSize = 1024 * 1024;
        // We have a read-only dictionary for mapping file extensions and MIME names. 
        public static readonly IReadOnlyDictionary<string, string> MimeNames;
        // We will discuss this later.
        public static readonly IReadOnlyCollection<char> InvalidFileNameChars;
        // Where are your videos located at? Change the value to any folder you want.
        public static readonly string InitialDirectory;

        private const string ConnStr =
            @"Data Source=.\MSSQLSERVER2014;Integrated Security=True;Initial Catalog=TestFileStream";

        #endregion

        #region Constructors

        static MediaController()
        {
            var mimeNames = new Dictionary<string, string>();

            mimeNames.Add(".mp3", "audio/mpeg");    // List all supported media types; 
            mimeNames.Add(".mp4", "video/mp4");
            mimeNames.Add(".ogg", "application/ogg");
            mimeNames.Add(".ogv", "video/ogg");
            mimeNames.Add(".oga", "audio/ogg");
            mimeNames.Add(".wav", "audio/x-wav");
            mimeNames.Add(".webm", "video/webm");

            MimeNames = new ReadOnlyDictionary<string, string>(mimeNames);

            InvalidFileNameChars = Array.AsReadOnly(Path.GetInvalidFileNameChars());
            InitialDirectory = WebConfigurationManager.AppSettings["InitialDirectory"];
        }

        #endregion

        #region Actions

        [HttpGet]
        [Route("api/media/{fileName}")]
        public HttpResponseMessage Play(string fileName)
        {
            // This can prevent some unnecessary accesses. 
            // These kind of file names won't be existing at all. 
            if (string.IsNullOrWhiteSpace(fileName) || AnyInvalidFileNameChars(fileName))
                throw new HttpResponseException(HttpStatusCode.NotFound);
            //string videoPath = HostingEnvironment.MapPath(string.Format("~/Videos/{0}", fileName));


            //long totalLength = fileInfo.Length;

            RangeHeaderValue rangeHeader = base.Request.Headers.Range;
            HttpResponseMessage response = new HttpResponseMessage();

            response.Headers.AcceptRanges.Add("bytes");

            /*
            // The request will be treated as normal request if there is no Range header.
            if (rangeHeader == null || !rangeHeader.Ranges.Any())
            {
                response.StatusCode = HttpStatusCode.OK;
                response.Content = new PushStreamContent((outputStream, httpContent, transpContext)
                =>
                {
                    using (outputStream) // Copy the file to output stream straightforward. 
                    using (Stream inputStream = fileInfo.OpenRead())
                    {
                        try
                        {
                            inputStream.CopyTo(outputStream, ReadStreamBufferSize);
                        }
                        catch (Exception error)
                        {
                            Debug.WriteLine(error);
                        }
                    }
                }, GetMimeNameFromExt(fileInfo.Extension));

                response.Content.Headers.ContentLength = totalLength;
                return response;
            }
            */

            long start = 0, end = 0;


            var f = getData();

            // 1. If the unit is not 'bytes'.
            // 2. If there are multiple ranges in header value.
            // 3. If start or end position is greater than file length.
            if (rangeHeader.Unit != "bytes" || rangeHeader.Ranges.Count > 1 ||
                !TryReadRangeItem(rangeHeader.Ranges.First(), f.FileLength, out start, out end))
            {
                response.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable;
                response.Content = new StreamContent(Stream.Null);  // No content for this status.
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(f.FileLength);
                response.Content.Headers.ContentType = GetMimeNameFromExt(f.FileExt);

                return response;
            }

            var contentRange = new ContentRangeHeaderValue(start, end, f.FileLength);

            // We are now ready to produce partial content.
            response.StatusCode = HttpStatusCode.PartialContent;
            aaaa(response, start, end, f.MimeType);
            /*
            response.Content = new PushStreamContent(
                (outputStream, httpContent, transpContext) =>
                {
                    using (outputStream) // Copy the file to output stream in indicated range.
                    using (Stream inputStream = fileInfo.OpenRead())
                    {
                        CreatePartialContent(inputStream, outputStream, start, end);
                    }
                }, GetMimeNameFromExt(fileInfo.Extension));
            */

            response.Content.Headers.ContentLength = end - start + 1;
            response.Content.Headers.ContentRange = contentRange;

            return response;
        }

        #endregion

        private FileData getData()
        {
            try
            {

                const string SelectTSql = @"
                            SELECT 
                            FileData.PathName(),
                            GET_FILESTREAM_TRANSACTION_CONTEXT(),
                            FileExt,
                            MimeType,
                            FileLength
                            FROM pictures 
                            WHERE Id = @Id";

                using (SqlConnection conn = new SqlConnection(ConnStr))
                {
                    conn.Open();

                    var f = new FileData();

                    using (SqlCommand cmd = new SqlCommand(SelectTSql, conn))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value =
                            Guid.Parse("4893be9e-661f-43ea-9574-29741a3c453f");

                        using (SqlDataReader rdr = cmd.ExecuteReader())
                        {
                            rdr.Read();
                            f.ServerFilePath = rdr.GetSqlString(0).Value;
                            f.ServerTxn = rdr.GetSqlBinary(1).Value;
                            f.FileExt = rdr.GetSqlString(2).Value;
                            f.MimeType = rdr.GetSqlString(3).Value;
                            f.FileLength = rdr.GetSqlInt32(4).Value;
                            rdr.Close();
                        }
                    }
                }

                using (TransactionScope ts = new TransactionScope())
                {
                    using (SqlConnection conn = new SqlConnection(ConnStr))
                    {
                        conn.Open();

                        var f = new FileData();

                        using (SqlCommand cmd = new SqlCommand(SelectTSql, conn))
                        {
                            cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = Guid.Parse("4893be9e-661f-43ea-9574-29741a3c453f");

                            using (SqlDataReader rdr = cmd.ExecuteReader())
                            {
                                rdr.Read();
                                f.ServerFilePath = rdr.GetSqlString(0).Value;
                                f.ServerTxn = rdr.GetSqlBinary(1).Value;
                                f.FileExt = rdr.GetSqlString(2).Value;
                                f.MimeType = rdr.GetSqlString(3).Value;
                                f.FileLength = rdr.GetSqlInt32(4).Value;
                                rdr.Close();
                            }
                        }


                        using (SqlFileStream fs = new SqlFileStream(f.ServerFilePath, f.ServerTxn, FileAccess.Read))
                        {
                            f.FileStream = fs;
                        }

                        return f;
                    }
                    ts.Complete();
                }

                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }


        public class FileData
        {
            public string ServerFilePath { get; set; }
            public byte[] ServerTxn { get; set; }
            public int FileLength { get; set; }
            public string MimeType { get; set; }
            public string FileExt { get; set; }
            public Stream FileStream { get; set; }
        }

        private void aaaa(HttpResponseMessage response, long start, long end, string inpMimeType)
        {
            try
            {

                response.Content = new PushStreamContent(
                    (outputStream, httpContent, transpContext) =>
                    {

                        const string SelectTSql = @"
                            SELECT 
                            FileData.PathName(),
                            GET_FILESTREAM_TRANSACTION_CONTEXT(),
                            FileExt,
                            MimeType 
                            FROM pictures 
                            WHERE Id = @Id";

                        using (TransactionScope ts = new TransactionScope())
                        {
                            using (SqlConnection conn = new SqlConnection(ConnStr))
                            {
                                conn.Open();

                                string mimeType = null;
                                string fileExt = null;
                                string serverPath;
                                byte[] serverTxn;

                                using (SqlCommand cmd = new SqlCommand(SelectTSql, conn))
                                {
                                    cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = Guid.Parse("4893be9e-661f-43ea-9574-29741a3c453f");

                                    using (SqlDataReader rdr = cmd.ExecuteReader())
                                    {
                                        rdr.Read();
                                        serverPath = rdr.GetSqlString(0).Value;
                                        serverTxn = rdr.GetSqlBinary(1).Value;
                                        fileExt = rdr.GetSqlString(2).Value;
                                        mimeType = rdr.GetSqlString(3).Value;
                                        rdr.Close();
                                    }
                                }

                                //this.StreamPhotoImage(context, serverPath, serverTxn, mimeType);

                                using (outputStream) // Copy the file to output stream in indicated range.
                                using (SqlFileStream fs = new SqlFileStream(serverPath, serverTxn, FileAccess.Read))
                                {
                                    CreatePartialContent(fs, outputStream, start, end);
                                }

                            }
                            ts.Complete();
                        }
                        
                    }, inpMimeType);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private void StreamPhotoImage(HttpContext context, string serverPath, byte[] serverTxn, string contentType)
        {
            try
            {

                const int BlockSize = 1024 * 512;
                //const string JpegContentType = "image/jpeg";

                int bytesRead = 0;

                using (SqlFileStream fs = new SqlFileStream(serverPath, serverTxn, FileAccess.ReadWrite))
                {

                    byte[] buffer = new byte[BlockSize];

                    context.Response.BufferOutput = false;
                    context.Response.ContentType = contentType;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        context.Response.OutputStream.Write(buffer, 0, bytesRead);
                        context.Response.Flush();
                    }
                    fs.Close();

                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }


        #region Others

        private static bool AnyInvalidFileNameChars(string fileName)
        {
            return InvalidFileNameChars.Intersect(fileName).Any();
        }

        private static MediaTypeHeaderValue GetMimeNameFromExt(string ext)
        {
            string value;

            if (MimeNames.TryGetValue(ext.ToLowerInvariant(), out value))
                return new MediaTypeHeaderValue(value);
            else
                return new MediaTypeHeaderValue(MediaTypeNames.Application.Octet);
        }

        private static bool TryReadRangeItem(RangeItemHeaderValue range, long contentLength,
            out long start, out long end)
        {
            if (range.From != null)
            {
                start = range.From.Value;
                if (range.To != null)
                    end = range.To.Value;
                else
                    end = contentLength - 1;
            }
            else
            {
                end = contentLength - 1;
                if (range.To != null)
                    start = contentLength - range.To.Value;
                else
                    start = 0;
            }
            return (start < contentLength && end < contentLength);
        }

        private static void CreatePartialContent(Stream inputStream, Stream outputStream,
            long start, long end)
        {
            int count = 0;
            long remainingBytes = end - start + 1;
            long position = start;
            byte[] buffer = new byte[ReadStreamBufferSize];

            inputStream.Position = start;
            do
            {
                try
                {
                    if (remainingBytes > ReadStreamBufferSize)
                        count = inputStream.Read(buffer, 0, ReadStreamBufferSize);
                    else
                        count = inputStream.Read(buffer, 0, (int)remainingBytes);
                    outputStream.Write(buffer, 0, count);
                }
                catch (Exception error)
                {
                    Debug.WriteLine(error);
                    break;
                }
                position = inputStream.Position;
                remainingBytes = end - position + 1;
            } while (position <= end);
        }

        #endregion
    }
}