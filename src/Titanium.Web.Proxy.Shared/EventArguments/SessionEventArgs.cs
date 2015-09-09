﻿using System;
using System.Text;
using System.IO;
using System.Net;
using Titanium.Web.Proxy.Helpers;
using System.Net.Sockets;
using Titanium.Web.Proxy.Exceptions;
using System.Linq;
using System.Collections.Generic;
using Titanium.Web.Proxy.Models;

namespace Titanium.Web.Proxy.EventArguments
{
    public class SessionEventArgs : EventArgs, IDisposable
    {

        internal int BUFFER_SIZE;

        internal TcpClient client { get; set; }
        internal Stream clientStream { get; set; }
        internal CustomBinaryReader clientStreamReader { get; set; }
        internal StreamWriter clientStreamWriter { get; set; }


        internal bool isHttps { get; set; }
        internal string requestURL { get; set; }
        internal string requestHostname { get; set; }

        internal int clientPort { get; set; }
        internal IPAddress clientIpAddress { get; set; }

        internal Encoding requestEncoding { get; set; }
        internal Version requestHttpVersion { get; set; }
        internal bool requestIsAlive { get; set; }
        internal bool cancelRequest { get; set; }
        internal byte[] requestBody { get; set; }
        internal string requestBodyString { get; set; }
        internal bool requestBodyRead { get; set; }
        internal List<HttpHeader> requestHeaders { get; set; }
        internal bool RequestLocked { get; set; }
        internal HttpWebRequest proxyRequest { get; set; }

        internal Encoding responseEncoding { get; set; }
        internal Stream responseStream { get; set; }
        internal byte[] responseBody { get; set; }
        internal string responseBodyString { get; set; }
        internal bool responseBodyRead { get; set; }
        internal List<HttpHeader> responseHeaders { get; set; }
        internal bool ResponseLocked { get; set; }
        internal HttpWebResponse serverResponse { get; set; }


        public int ClientPort { get { return this.clientPort; } }
        public IPAddress ClientIpAddress { get { return this.clientIpAddress; } }

        public bool IsHttps { get { return this.isHttps; } }

        public string RequestURL { get { return this.requestURL; } }
        public string RequestHostname { get { return this.requestHostname; } }

        public List<HttpHeader> RequestHeaders { get { return this.requestHeaders; } }
        public List<HttpHeader> ResponseHeaders { get { return this.responseHeaders; } }

        public int RequestContentLength
        {
            get
            {
                if (this.requestHeaders.Any(x => x.Name.ToLower() == "content-length"))
                {
                    int contentLen;
                    int.TryParse(this.requestHeaders.First(x => x.Name.ToLower() == "content-length").Value, out contentLen);
                    if (contentLen != 0)
                        return contentLen;
                }
                return -1;
            }
        }

        public string RequestMethod { get { return this.proxyRequest.Method; } }


        public HttpStatusCode ResponseStatusCode { get { return this.serverResponse.StatusCode; } }
        public string ResponseContentType { get { return this.responseHeaders.Any(x => x.Name.ToLower() == "content-type") ? this.responseHeaders.First(x => x.Name.ToLower() == "content-type").Value : null; } }


        internal SessionEventArgs(int bufferSize)
        {
            BUFFER_SIZE = bufferSize;
        }

        private void readRequestBody()
        {
            if ((proxyRequest.Method.ToUpper() != "POST" && proxyRequest.Method.ToUpper() != "PUT"))
            {
                throw new BodyNotFoundException("Request don't have a body." +
                     "Please verify that this request is a Http POST/PUT and request content length is greater than zero before accessing the body.");
            }

            if (requestBody == null)
            {
                bool isChunked = false;
                string requestContentEncoding = null;


                if (requestHeaders.Any(x => x.Name.ToLower() == "content-encoding"))
                {
                    requestContentEncoding = requestHeaders.First(x => x.Name.ToLower() == "content-encoding").Value;
                }

                if (requestHeaders.Any(x => x.Name.ToLower() == "transfer-encoding"))
                {
                    var transferEncoding = requestHeaders.First(x => x.Name.ToLower() == "transfer-encoding").Value.ToLower();
                    if (transferEncoding.Contains("chunked"))
                    {

                        isChunked = true;
                    }
                }


                if (requestContentEncoding == null && !isChunked)
                    requestBody = clientStreamReader.ReadBytes(RequestContentLength);
                else
                {
                    using (var requestBodyStream = new MemoryStream())
                    {
                        if (isChunked)
                        {
                            while (true)
                            {
                                var chuchkHead = clientStreamReader.ReadLine();
                                var chunkSize = int.Parse(chuchkHead, System.Globalization.NumberStyles.HexNumber);

                                if (chunkSize != 0)
                                {
                                    var buffer = clientStreamReader.ReadBytes(chunkSize);
                                    requestBodyStream.Write(buffer, 0, buffer.Length);

                                    var chunkTrail = clientStreamReader.ReadLine();
                                }
                                else
                                {
                                    clientStreamReader.ReadLine();
                                    break;
                                }

                            }
                            
                        }
                        try
                        {
                            switch (requestContentEncoding)
                            {
                                case "gzip":
                                    requestBody = CompressionHelper.DecompressGzip(requestBodyStream);
                                    break;
                                case "deflate":
                                    requestBody = CompressionHelper.DecompressDeflate(requestBodyStream);
                                    break;
                                case "zlib":
                                    requestBody = CompressionHelper.DecompressGzip(requestBodyStream);
                                    break;
                                default:
                                    requestBody = requestBodyStream.ToArray();
                                    break;
                            }
                        }
                        catch {
                            requestBody = requestBodyStream.ToArray();
                        }
                    }

                }

            }
            requestBodyRead = true;
        }
        private void readResponseBody()
        {
            if (responseBody == null)
            {

                switch (serverResponse.ContentEncoding)
                {
                    case "gzip":
                        responseBody = CompressionHelper.DecompressGzip(responseStream);
                        break;
                    case "deflate":
                        responseBody = CompressionHelper.DecompressDeflate(responseStream);
                        break;
                    case "zlib":
                        responseBody = CompressionHelper.DecompressZlib(responseStream);
                        break;
                    default:
                        responseBody = DecodeData(responseStream);
                        break;
                }

                responseBodyRead = true;

            }
        }


        //stream reader not recomended for images
        private byte[] DecodeData(Stream responseStream)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }

        }

        public Encoding GetRequestBodyEncoding()
        {
            if (RequestLocked) throw new Exception("You cannot call this function after request is made to server.");

            return requestEncoding;
        }

        public byte[] GetRequestBody()
        {
            if (RequestLocked) throw new Exception("You cannot call this function after request is made to server.");

            readRequestBody();
            return requestBody;


        }
        public string GetRequestBodyAsString()
        {
            if (RequestLocked) throw new Exception("You cannot call this function after request is made to server.");


            readRequestBody();

            if (requestBodyString == null)
            {
                requestBodyString = requestEncoding.GetString(requestBody);
            }
            return requestBodyString;



        }

        public void SetRequestBody(byte[] body)
        {
            if (RequestLocked) throw new Exception("You cannot call this function after request is made to server.");

            if (!requestBodyRead)
            {
                readRequestBody();
            }

            requestBody = body;
            requestBodyRead = true;
        }
        public void SetRequestBodyString(string body)
        {

            if (RequestLocked) throw new Exception("Youcannot call this function after request is made to server.");

            if (!requestBodyRead)
            {
                readRequestBody();
            }

            this.requestBody = requestEncoding.GetBytes(body);
            requestBodyRead = true;
        }

        public Encoding GetResponseBodyEncoding()
        {
            if (!RequestLocked) throw new Exception("You cannot call this function before request is made to server.");

            return responseEncoding;
        }

        public byte[] GetResponseBody()
        {
            if (!RequestLocked) throw new Exception("You cannot call this function before request is made to server.");

            readResponseBody();
            return responseBody;
        }
        public string GetResponseBodyAsString()
        {
            if (!RequestLocked) throw new Exception("You cannot call this function before request is made to server.");

            GetResponseBody();

            if (responseBodyString == null)
            {
                responseBodyString = responseEncoding.GetString(responseBody);
            }
            return responseBodyString;
        }
        public void SetResponseBody(byte[] body)
        {
            if (!RequestLocked) throw new Exception("You cannot call this function before request is made to server.");

            if (responseBody == null)
            {
                GetResponseBody();
            }

            responseBody = body;


        }
        public void SetResponseBodyString(string body)
        {
            if (!RequestLocked) throw new Exception("You cannot call this function before request is made to server.");

            if (responseBody == null)
            {
                GetResponseBody();
            }

            var bodyBytes = responseEncoding.GetBytes(body);
            SetResponseBody(bodyBytes);
        }


        public void Ok(string html)
        {
            if (RequestLocked) throw new Exception("You cannot call this function after request is made to server.");

            if (html == null)
                html = string.Empty;

            var result = Encoding.Default.GetBytes(html);

            StreamWriter connectStreamWriter = new StreamWriter(clientStream);
            var s = String.Format("HTTP/{0}.{1} {2} {3}", requestHttpVersion.Major, requestHttpVersion.Minor, 200, "Ok");
            connectStreamWriter.WriteLine(s);
            connectStreamWriter.WriteLine(String.Format("Timestamp: {0}", DateTime.Now.ToString()));
            connectStreamWriter.WriteLine("content-length: " + result.Length);
            connectStreamWriter.WriteLine("Cache-Control: no-cache, no-store, must-revalidate");
            connectStreamWriter.WriteLine("Pragma: no-cache");
            connectStreamWriter.WriteLine("Expires: 0");

            if (requestIsAlive)
            {
                connectStreamWriter.WriteLine("Connection: Keep-Alive");
            }
            else
                connectStreamWriter.WriteLine("Connection: close");

            connectStreamWriter.WriteLine();
            connectStreamWriter.Flush();

            clientStream.Write(result, 0, result.Length);


            cancelRequest = true;

        }

        public void Dispose()
        {
            if (this.proxyRequest != null)
                this.proxyRequest.Abort();

            if (this.responseStream != null)
                this.responseStream.Dispose();

            if (this.serverResponse != null)
                this.serverResponse.Close();
        }


    }

}