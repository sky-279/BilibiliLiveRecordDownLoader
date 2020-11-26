using BilibiliApi.Clients;
using BilibiliApi.Model.Login.QrCode.GetLoginUrl;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.Logging;
using QRCoder;
using ReactiveUI;
using Splat;
using System;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
	public partial class QrCodeLoginDialog
	{
		private readonly IDisposable _loginInfoMonitor;
		private readonly ILogger _logger;
		private readonly BililiveApiClient _apiClient;
		private readonly GetLoginUrlData _data;

		public string? Cookie { get; private set; }

		public QrCodeLoginDialog(GetLoginUrlData data)
		{
			_logger = Locator.Current.GetService<ILogger<QrCodeLoginDialog>>();
			_apiClient = Locator.Current.GetService<BililiveApiClient>();
			_data = data;

			InitializeComponent();

			using var qrGenerator = new QRCodeGenerator();
			using var qrCodeData = qrGenerator.CreateQrCode(data.url, QRCodeGenerator.ECCLevel.H, true);
			using var qrCode = new XamlQRCode(qrCodeData);
			QrCodeImage.Source = qrCode.GetGraphic(20);

			_loginInfoMonitor = CreateMonitor();

			PrimaryButtonCommand = ReactiveCommand.CreateFromTask(GetLoginInfoAsync);
		}

		private IDisposable CreateMonitor()
		{
#pragma warning disable VSTHRD101
			return Observable.Interval(TimeSpan.FromSeconds(3)).ObserveOnDispatcher().Subscribe(async _ => await GetLoginInfoAsync());
#pragma warning restore VSTHRD101
		}

		private async Task GetLoginInfoAsync()
		{
			try
			{
				Cookie = await _apiClient.GetLoginInfoAsync(_data.oauthKey!);
				if (!string.IsNullOrEmpty(Cookie))
				{
					Dispose();
				}
			}
			catch (HttpRequestException ex)
			{
				_logger.LogDebug(ex.Message);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"获取登录信息失败");
			}
		}

		public override void Dispose()
		{
			_loginInfoMonitor.Dispose();
			base.Dispose();
		}
	}
}
