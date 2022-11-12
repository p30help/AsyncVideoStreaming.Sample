using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using AsynVideoStreaming.Models;
using System.Diagnostics;
using System.Web;
using System.Web.Hosting;

namespace AsynVideoStreaming.Controllers
{
    public class VideoController : ApiController
    {
        [Route("api/video/{ext}/{fileName}")]
        public HttpResponseMessage Get(string ext, string fileName)
        {
            string videoPath = HostingEnvironment.MapPath(string.Format("~/Videos/{0}.{1}", fileName, ext));
            if (File.Exists(videoPath))
            {
                var video = new VideoStream(videoPath);

                var response = Request.CreateResponse();

                response.Content = new PushStreamContent((Action<Stream, HttpContent, TransportContext>)video.WriteToStream,
                    new MediaTypeHeaderValue("video/" + ext));

                response.Content.Headers.Add("Content-Disposition", "attachment;filename=" + fileName);
                response.Content.Headers.Add("Content-Length", video.FileLength.ToString());

                return response;
            }
            else
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }
        }
    }
}
