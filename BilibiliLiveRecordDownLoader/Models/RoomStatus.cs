using BilibiliApi.Clients;
using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.RoomInfo;
using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Http.Clients;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Models
{
	public class RoomStatus : ReactiveObject
	{
		private readonly ILogger _logger;
		private readonly BililiveApiClient _apiClient;
		private readonly Config _config;
		private readonly TaskListViewModel _taskList;

		private IDanmuClient? _danmuClient;
		private IDisposable? _httpMonitor;
		private IDisposable? _statusMonitor;
		private IDisposable? _enableMonitor;
		private IDisposable? _titleMonitor;
		private CancellationTokenSource _recordCts = new();

		#region 属性

		/// <summary>
		/// 是否启用录制
		/// </summary>
		[Reactive]
		public bool IsEnable { get; set; } = true;

		/// <summary>
		/// 短号
		/// </summary>
		[JsonIgnore]
		[Reactive]
		public long ShortId { get; set; }

		/// <summary>
		/// 房间号
		/// </summary>
		[Reactive]
		public long RoomId { get; set; } = 732;

		/// <summary>
		/// 主播名
		/// </summary>
		[JsonIgnore]
		[Reactive]
		public string? UserName { get; set; }

		/// <summary>
		/// 直播间标题
		/// </summary>
		[JsonIgnore]
		[Reactive]
		public string? Title { get; set; }

		/// <summary>
		/// 直播状态
		/// </summary>
		[JsonIgnore]
		[Reactive]
		public LiveStatus LiveStatus { get; set; } = LiveStatus.未知;

		/// <summary>
		/// 录制状态
		/// </summary>
		[JsonIgnore]
		[Reactive]
		public RecordStatus RecordStatus { get; set; }

		/// <summary>
		/// 是否开播提醒
		/// </summary>
		[Reactive]
		public bool IsNotify { get; set; }

		/// <summary>
		/// 弹幕重连间隔
		/// 单位 秒
		/// </summary>
		[Reactive]
		public double DanMuReconnectLatency { get; set; } = 2.0;

		/// <summary>
		/// Http 开播检查间隔
		/// 单位 秒
		/// </summary>
		[Reactive]
		public double HttpCheckLatency { get; set; } = 300.0;

		/// <summary>
		/// 直播重连间隔
		/// 单位 秒
		/// </summary>
		[Reactive]
		public double StreamReconnectLatency { get; set; } = 6.0;

		/// <summary>
		/// 直播连接超时
		/// 单位 秒
		/// </summary>
		[Reactive]
		public double StreamConnectTimeout { get; set; } = 3.0;

		/// <summary>
		/// 直播流超时
		/// 单位 秒
		/// </summary>
		[Reactive]
		public double StreamTimeout { get; set; } = 5.0;

		/// <summary>
		/// 速度
		/// </summary>
		[JsonIgnore]
		[Reactive]
		public string Speed { get; set; } = string.Empty;

		/// <summary>
		/// 弹幕服务器类型
		/// </summary>
		[Reactive]
		public DanmuClientType ClientType { get; set; } = DanmuClientType.SecureWebsocket;

		/// <summary>
		/// qn 参数
		/// </summary>
		[Reactive]
		public Qn Qn { get; set; } = Qn.原画;

		#endregion

		public RoomStatus()
		{
			_logger = DI.GetService<ILogger<RoomStatus>>();
			_config = DI.GetService<Config>();
			_apiClient = DI.GetService<BililiveApiClient>();
			_taskList = DI.GetService<TaskListViewModel>();
		}

		#region ApiRequest

		public async Task GetRoomInfoDataAsync(bool isThrow, CancellationToken token)
		{
			try
			{
				var data = await _apiClient.GetRoomInfoDataAsync(RoomId, token);
				CopyFromRoomInfoData(data);
			}
			catch (Exception ex)
			{
				if (!isThrow)
				{
					_logger.LogError(ex, $@"[{RoomId}] 获取房间信息出错");
				}
				else
				{
					throw;
				}
			}
		}

		private async Task GetAnchorInfoAsync(CancellationToken token)
		{
			try
			{
				var info = await _apiClient.GetAnchorInfoDataAsync(RoomId, token);
				UserName = info.uname;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 获取主播信息出错");
			}
		}

		public async Task RefreshStatusAsync(CancellationToken token)
		{
			try
			{
				await GetRoomInfoDataAsync(true, token);
				await GetAnchorInfoAsync(token);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 刷新房间状态出错");
			}
		}

		#endregion

		#region Start

		public void Start()
		{
			StartMonitor();
		}

		private void StartMonitor()
		{
			_statusMonitor = this.WhenAnyValue(x => x.LiveStatus).Subscribe(_ => StatusUpdatedAsync().NoWarning());
			_enableMonitor = this.WhenAnyValue(x => x.IsEnable).Subscribe(_ => EnableUpdated());
			this.RaisePropertyChanged(nameof(LiveStatus));
			_titleMonitor = this.WhenAnyValue(x => x.Title).Subscribe(title =>
			{
				if (title is not null)
				{
					_logger.LogInformation($@"[{RoomId}] [TitleChanged] {title}");
				}
			});
			this.RaisePropertyChanged(nameof(Title));
			BuildDanmuClientAsync().NoWarning();
			BuildHttpCheckMonitor();
		}

		private void BuildHttpCheckMonitor()
		{
			_httpMonitor = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(HttpCheckLatency)).Subscribe(_ => RefreshStatusAsync(default).NoWarning());
		}

		private async ValueTask BuildDanmuClientAsync()
		{
			_danmuClient = ClientType switch
			{
				DanmuClientType.TCP => DI.GetService<TcpDanmuClient>(),
				DanmuClientType.Websocket => DI.GetService<WsDanmuClient>(),
				_ => DI.GetService<WssDanmuClient>(),
			};
			_danmuClient.RetryInterval = TimeSpan.FromSeconds(DanMuReconnectLatency);
			_danmuClient.RoomId = RoomId;

			_danmuClient.Received.Subscribe(ParseDanmu);
			await _danmuClient.StartAsync();
		}

		private async Task StartRecordAsync()
		{
			lock (this)
			{
				if (RecordStatus != RecordStatus.未录制)
				{
					_logger.LogDebug($@"[{RoomId}] 重复录制，已跳过");
					return;
				}
				RecordStatus = RecordStatus.启动中;
				_recordCts = new CancellationTokenSource();
			}

			var token = _recordCts.Token;
			try
			{
				while (LiveStatus == LiveStatus.直播)
				{
					try
					{
						RecordStatus = RecordStatus.启动中;
						var urlData = await _apiClient.GetPlayUrlDataAsync(RoomId, (long)Qn, token);
						var url = urlData.durl!.First().url;

						await using var downloader = DI.GetService<HttpDownloader>();
						downloader.Target = new(url!);
						downloader.Client.Timeout = TimeSpan.FromSeconds(StreamConnectTimeout);

						await downloader.GetStreamAsync(token);
						RecordStatus = RecordStatus.录制中;
						var flv = Path.Combine(_config.MainDir, $@"{RoomId}", $@"{DateTime.Now:yyyyMMdd_HHmmss}.flv");
						downloader.OutFileName = flv;
						_logger.LogInformation($@"[{RoomId}] 开始录制");
						var lastDataReceivedTime = DateTime.Now;
						var speedMonitor = downloader.CurrentSpeed.Subscribe(b =>
						{
							Speed = $@"{Utils.Utils.CountSize(Convert.ToInt64(b))}/s";
							var now = DateTime.Now;
							if (b > 0.0)
							{
								lastDataReceivedTime = now;
							}
							else if (now - lastDataReceivedTime > TimeSpan.FromSeconds(StreamTimeout))
							{
								// ReSharper disable once AccessToDisposedClosure
								downloader.CloseStream();
								_logger.LogWarning($@"[{RoomId}] 网络不稳定，即将尝试重连");
							}
						});
						try
						{
							await downloader.DownloadAsync(token);
						}
						finally
						{
							speedMonitor.Dispose();
							_logger.LogInformation($@"[{RoomId}] 录制结束");
							ConvertToMp4Async(flv).NoWarning();
						}
					}
					catch (OperationCanceledException ex) when (ex.InnerException is not TimeoutException)
					{
						throw;
					}
					catch (IOException ex) when (ex.InnerException is SocketException { ErrorCode: (int)SocketError.OperationAborted })
					{
						// downloader stream manually closed
					}
					catch (Exception e)
					{
						if (e is HttpRequestException ex)
						{
							_logger.LogInformation($@"[{RoomId}] 尝试下载直播流时服务器返回了 {ex.StatusCode}");
						}
						else
						{
							_logger.LogError(e, $@"[{RoomId}] 尝试下载直播流错误");
						}
						await Task.Delay(TimeSpan.FromSeconds(StreamReconnectLatency), token);
					}
				}
				_logger.LogInformation($@"[{RoomId}] 不再录制");
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation($@"[{RoomId}] 录制已取消");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 录制出现错误");
			}
			finally
			{
				RecordStatus = RecordStatus.未录制;
				Speed = string.Empty;
			}
		}

		private async Task ConvertToMp4Async(string flv)
		{
			try
			{
				if (_config.IsAutoConvertMp4 && File.Exists(flv))
				{
					var args = string.Format(Utils.Constants.FFmpegCopyConvert, flv, Path.ChangeExtension(flv, @"mp4"));
					var task = new FFmpegTaskViewModel(args);
					await _taskList.AddTaskAsync(task, Path.GetPathRoot(flv) ?? string.Empty);
					_taskList.RemoveTask(task);
					if (_config.IsDeleteAfterConvert)
					{
						File.Delete(flv);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"转封装 MP4 时发生错误");
			}
		}

		#endregion

		#region Stop

		public void Stop()
		{
			StopMonitor();
			StopRecord();
		}

		private void StopMonitor()
		{
			_titleMonitor?.Dispose();
			_enableMonitor?.Dispose();
			_statusMonitor?.Dispose();
			_danmuClient?.DisposeAsync().NoWarning();
			_httpMonitor?.Dispose();
		}

		private void StopRecord()
		{
			_recordCts.Cancel();
		}

		#endregion

		#region PropertyUpdated

		private void ParseDanmu(DanmuPacket packet)
		{
			try
			{
				if (packet.Operation != Operation.SendMsgReply)
				{
					return;
				}

				var danMu = DanmuFactory.ParseJson(packet.Body.Span);
				if (danMu is null)
				{
					return;
				}

				var streamingStatus = danMu.IsStreaming();
				if (streamingStatus != LiveStatus.未知)
				{
					LiveStatus = streamingStatus;
				}

				var title = danMu.TitleChanged();
				if (title is not null)
				{
					Title = title;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 弹幕解析失败：{packet.Operation} {packet.ProtocolVersion} {Encoding.UTF8.GetString(packet.Body.Span)}");
			}
		}

		private async ValueTask StatusUpdatedAsync()
		{
			try
			{
				if (LiveStatus != LiveStatus.未知)
				{
					_logger.LogInformation($@"[{RoomId}] 直播状态：{LiveStatus}");
				}

				if (LiveStatus == LiveStatus.直播)
				{
					if (IsNotify)
					{
						MessageBus.Current.SendMessage(this);
					}
					if (IsEnable)
					{
						await StartRecordAsync();
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 启动/停止录制出现错误");
			}
		}

		private void EnableUpdated()
		{
			try
			{
				if (IsEnable)
				{
					if (LiveStatus == LiveStatus.直播)
					{
						StartRecordAsync().NoWarning();
					}
				}
				else
				{
					StopRecord();
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 启动/停止录制出现错误");
			}
		}

		#endregion

		#region Clone

		private void CopyFromRoomInfoData(RoomInfoData roomData)
		{
			RoomId = roomData.room_id;
			ShortId = roomData.short_id;
			LiveStatus = (LiveStatus)roomData.live_status;
			Title = roomData.title;
		}

		public RoomStatus Clone()
		{
			return new()
			{
				IsEnable = IsEnable,
				IsNotify = IsNotify,
				RoomId = RoomId,
				DanMuReconnectLatency = DanMuReconnectLatency,
				HttpCheckLatency = HttpCheckLatency,
				StreamReconnectLatency = StreamReconnectLatency,
				StreamConnectTimeout = StreamConnectTimeout,
				StreamTimeout = StreamTimeout,
				ClientType = ClientType,
				Qn = Qn
			};
		}

		public async ValueTask UpdateAsync(RoomStatus room)
		{
			IsEnable = room.IsEnable;
			IsNotify = room.IsNotify;
			//RoomId = room.RoomId;

			if (!DanMuReconnectLatency.Equals(room.DanMuReconnectLatency))
			{
				DanMuReconnectLatency = room.DanMuReconnectLatency;
				if (_danmuClient is not null)
				{
					_danmuClient.RetryInterval = TimeSpan.FromSeconds(DanMuReconnectLatency);
				}
			}

			if (!HttpCheckLatency.Equals(room.HttpCheckLatency))
			{
				HttpCheckLatency = room.HttpCheckLatency;
				_httpMonitor?.Dispose();
				BuildHttpCheckMonitor();
			}

			StreamReconnectLatency = room.StreamReconnectLatency;
			StreamConnectTimeout = room.StreamConnectTimeout;
			StreamTimeout = room.StreamTimeout;

			if (ClientType != room.ClientType)
			{
				ClientType = room.ClientType;
				if (_danmuClient is not null)
				{
					await _danmuClient.DisposeAsync();
				}
				await BuildDanmuClientAsync();
			}

			Qn = room.Qn;
		}

		#endregion

		#region Equals

		public override bool Equals(object? obj)
		{
			return obj is RoomStatus room && room.RoomId == RoomId;
		}

		public override int GetHashCode()
		{
			// ReSharper disable once NonReadonlyMemberInGetHashCode
			return HashCode.Combine(RoomId);
		}

		#endregion
	}
}
