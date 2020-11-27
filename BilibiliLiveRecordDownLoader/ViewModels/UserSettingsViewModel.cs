using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class UserSettingsViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"UserSettings";
		public IScreen HostScreen { get; }

		#region 字段

		private string _loginStatus = @"未知";
		private SolidColorBrush _loginStatusForeground = Constants.YellowBrush;

		#endregion

		#region 属性

		public string LoginStatus
		{
			get => _loginStatus;
			set => this.RaiseAndSetIfChanged(ref _loginStatus, value);
		}

		public SolidColorBrush LoginStatusForeground
		{
			get => _loginStatusForeground;
			set => this.RaiseAndSetIfChanged(ref _loginStatusForeground, value);
		}

		#endregion

		#region Command

		public ReactiveCommand<Unit, Unit> QrCodeLoginCommand { get; }
		public ReactiveCommand<Unit, Unit> CheckLoginCommand { get; }

		#endregion

		private readonly ILogger _logger;
		private readonly BililiveApiClient _apiClient;

		public readonly Config Config;

		public UserSettingsViewModel(
			IScreen hostScreen,
			ILogger<UserSettingsViewModel> logger,
			Config config,
			BililiveApiClient apiClient)
		{
			HostScreen = hostScreen;
			_logger = logger;
			Config = config;
			_apiClient = apiClient;

			QrCodeLoginCommand = ReactiveCommand.CreateFromTask(QrCodeLoginAsync);
			CheckLoginCommand = ReactiveCommand.CreateFromTask(CheckLoginAsync);
		}

		private async Task QrCodeLoginAsync(CancellationToken token)
		{
			try
			{
				LoginStatus = @"正在获取二维码...";
				LoginStatusForeground = Constants.YellowBrush;
				var data = await _apiClient.GetLoginUrlDataAsync(token);
				LoginStatus = @"请扫描二维码";
				using var dialog = new QrCodeLoginDialog(data);
				await dialog.ShowAsync();
				if (!string.IsNullOrEmpty(dialog.Cookie))
				{
					Config.Cookie = dialog.Cookie;
				}
				await CheckLoginCommand.Execute();
			}
			catch (Exception ex)
			{
				if (ex is HttpRequestException e)
				{
					LoginStatus = e.Message;
				}
				else
				{
					LoginStatus = @"二维码登录发生错误";
				}
				LoginStatusForeground = Constants.RedBrush;
				_logger.LogError(ex, LoginStatus);
			}
		}

		private async Task CheckLoginAsync(CancellationToken token)
		{
			try
			{
				LoginStatus = @"正在验证登录...";
				LoginStatusForeground = Constants.YellowBrush;
				if (await _apiClient.CheckLoginStatusAsync(token))
				{
					LoginStatus = @"已登录";
					LoginStatusForeground = Constants.GreenBrush;
				}
				else
				{
					LoginStatus = @"未登录";
					LoginStatusForeground = Constants.RedBrush;
				}
			}
			catch (Exception ex)
			{
				LoginStatus = @"验证登录发生错误";
				LoginStatusForeground = Constants.RedBrush;
				_logger.LogError(ex, LoginStatus);
			}
		}
	}
}