using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using System;
using System.Net;
using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class HttpClientUtils
	{
		public static HttpClient BuildClientForBilibili(TimeSpan timeout, string userAgent, string? cookie, HttpClientHandler handler)
		{
			if (string.IsNullOrEmpty(userAgent))
			{
				userAgent = Constants.ChromeUserAgent;
			}
			var client = new HttpClient(handler, true);
			if (!handler.UseCookies)
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.Timeout = timeout;
			client.DefaultRequestHeaders.Accept.ParseAdd(@"application/json, text/javascript, */*; q=0.01");
			client.DefaultRequestHeaders.Referrer = new(@"https://live.bilibili.com/");
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

			return client;
		}

		public static HttpClient BuildClientForMultiThreadedDownloader(string? cookie, string userAgent, HttpClientHandler handler)
		{
			var client = new HttpClient(new RetryHandler(handler, 10), true);
			if (!handler.UseCookies)
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
			client.DefaultRequestHeaders.ConnectionClose = false;

			return client;
		}
	}
}
