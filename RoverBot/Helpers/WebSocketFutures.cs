﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using System.Text;
using System.IO;

using WebSocketSharp;

using Binance.Net;
using Binance.Net.Enums;

using WebSocket = WebSocketSharp.WebSocket;

using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

using Timer = System.Timers.Timer;

namespace RoverBot
{
	public static class WebSocketFutures
	{
		public const string Symbol = "ETHUSDT";
		
		public const decimal Percent = 1.0185m;

		private static object LockRecordsFile = new object();

		private static WebSocket KlineStream = default;

		private static Timer InternalTimer = default;
		
		#region Buffer

		private static object LockBuffer = new object();

		private static List<decimal> buffer = default;

		private static DateTime BufferUpdationTime = default;

		public static List<decimal> Buffer
		{
			get
			{
				lock(LockBuffer)
				{
					return buffer;
				}
			}

			set
			{
				lock(LockBuffer)
				{
					buffer = value;

					BufferUpdationTime = DateTime.Now;
				}
			}
		}

		#endregion

		#region History

		private static object LockHistory = new object();

		private static List<Candle> history = default;

		public static List<Candle> History
		{
			get
			{
				lock(LockHistory)
				{
					return history;
				}
			}

			set
			{
				lock(LockHistory)
				{
					history = value;
				}

				if(CheckHistory())
				{
					Buffer = new List<decimal>(value.Select(x => x.Close));
				}
			}
		}

		public const int HistoryCount = 180;

		#endregion

		#region CurrentPrice

		private static object LockCurrentPrice = new object();

		private static decimal currentPrice = default;

		public static decimal CurrentPrice
		{
			get
			{
				lock(LockCurrentPrice)
				{
					return currentPrice;
				}
			}

			private set
			{
				lock(LockCurrentPrice)
				{
					if(value != currentPrice)
					{
						if(value > 0.0m)
						{
							currentPrice = value;
						}
					}
				}
			}
		}

		#endregion

		#region LastKlineUpdated

		private static object LockLastKlineUpdated = new object();

		private static DateTime lastKlineUpdated;

		public static DateTime LastKlineUpdated
		{
			get
			{
				lock(LockLastKlineUpdated)
				{
					return lastKlineUpdated;
				}
			}

			set
			{
				lock(LockLastKlineUpdated)
				{
					lastKlineUpdated = value;
				}
			}
		}

		#endregion

		#region StartUpdationTime

		private static object LockStartUpdationTime = new object();

		private static DateTime startUpdationTime;

		public static DateTime StartUpdationTime
		{
			get
			{
				lock(LockStartUpdationTime)
				{
					return startUpdationTime;
				}
			}

			set
			{
				lock(LockStartUpdationTime)
				{
					startUpdationTime = value;
				}
			}
		}

		#endregion

		#region StopUpdationTime

		private static object LockStopUpdationTime = new object();

		private static DateTime stopUpdationTime;

		public static DateTime StopUpdationTime
		{
			get
			{
				lock(LockStopUpdationTime)
				{
					return stopUpdationTime;
				}
			}

			set
			{
				lock(LockStopUpdationTime)
				{
					stopUpdationTime = value;
				}
			}
		}

		#endregion

		#region CloseAction

		private static object LockCloseAction = new object();

		private static bool closeAction;

		public static bool CloseAction
		{
			get
			{
				lock(LockCloseAction)
				{
					return closeAction;
				}
			}

			set
			{
				lock(LockCloseAction)
				{
					closeAction = value;
				}
			}
		}

		#endregion

		#region Errors

		private static object LockErrors = new object();

		private static int errors = default;

		public static int Errors
		{
			get
			{
				lock(LockErrors)
				{
					return errors;
				}
			}

			set
			{
				lock(LockErrors)
				{
					errors = value;
				}

				const int MaxErrors = 2;

				if(value >= MaxErrors)
				{
					Task.Run(() =>
					{
						BinanceFutures.RestartRoverBot();
					});
				}
			}
		}

		#endregion

		static WebSocketFutures()
		{
			try
			{
				StartInternalTimer();

				UpdateRecords();
			}
			catch(Exception exception)
			{
				Logger.Write("WebSocketFutures: " + exception.Message);
			}
		}

		private static void StartInternalTimer()
		{
			try
			{
				InternalTimer = new Timer();

				InternalTimer.Interval = 30000;

				InternalTimer.Elapsed += InternalTimerElapsed;

				InternalTimer.Start();
			}
			catch(Exception exception)
			{
				Logger.Write("StartInternalTimer: " + exception.Message);
			}
		}

		private static void InternalTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			try
			{
				CheckKlineStream();
			}
			catch(Exception exception)
			{
				Logger.Write("InternalTimerElapsed: " + exception.Message);
			}
		}

		public static bool StartKlineStream()
		{
			try
			{
				StopKlineStream();

				string symbol = Symbol.ToLower();

				string url = "wss://fstream.binance.com/stream?streams=" + symbol + "@kline_1m";				

				KlineStream = new WebSocket(url);

				KlineStream.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;

				KlineStream.OnMessage += OnKlineUpdated;

				KlineStream.OnError += OnKlineSocketError;

				KlineStream.OnClose += OnKlineStreamClosed;

				KlineStream.Connect();

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("StartKlineStream: " + exception.Message);

				return false;
			}
		}

		public static bool StopKlineStream()
		{
			try
			{
				if(KlineStream != default)
				{
					try
					{
						CloseAction = true;

						KlineStream.Close();
					}
					catch(Exception exception)
					{
						Logger.Write("StopKlineStream: " + exception.Message);
					}
				}

				KlineStream = default;

				CloseAction = default;

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("StopKlineStream: " + exception.Message);

				return false;
			}
		}

		private static void OnKlineSocketError(object sender, ErrorEventArgs e)
		{
			try
			{
				Logger.Write("OnKlineSocketError: " + e.Message);

				StartKlineStream();
			}
			catch(Exception exception)
			{
				Logger.Write("OnKlineSocketError: " + exception.Message);
			}
		}

		private static void OnKlineStreamClosed(object sender, CloseEventArgs e)
		{
			try
			{
				if(CloseAction == default)
				{
					Thread.Sleep(2000);

					Logger.Write("OnKlineStreamClosed: Kline Stream Closed");

					StartKlineStream();
				}
			}
			catch(Exception exception)
			{
				Logger.Write("OnKlineStreamClosed: " + exception.Message);
			}
		}

		private static void OnKlineUpdated(object sender, MessageEventArgs e)
		{
			try
			{
				string str = e.Data;

				KlineTickerStream record = JsonSerializer.Deserialize<KlineTickerStream>(str);

				if(record.Data.GetTime(out DateTime time))
				{
					if(time != LastKlineUpdated)
					{
						if(Buffer != default)
						{
							const double BufferExpirationTime = 60.0;

							if(BufferUpdationTime.AddSeconds(BufferExpirationTime) >= DateTime.Now)
							{
								if(CurrentPrice > 0.0m)
								{
									Buffer.Add(CurrentPrice);

									CheckEntryPoint(LastKlineUpdated, Buffer);
								}
							}
							else
							{
								Logger.Write("Buffer Expired");
							}
						}

						LastKlineUpdated = time;
						
						Task.Run(() =>
						{
							Thread.Sleep(4000);

							StartUpdationTime = DateTime.Now;

							LoadHistory(Symbol, HistoryCount).Wait();
						});
					}
				}

				if(record.Data.GetPrice(out decimal price))
				{
					CurrentPrice = price;
				}
			}
			catch(Exception exception)
			{
				Logger.Write("OnKlineUpdated: " + exception.Message);
			}
		}
		
		private static void CheckKlineStream()
		{
			try
			{
				if(KlineStream == default)
				{
					Logger.Write("CheckKlineStream: Invalid Stream");

					StartKlineStream();

					return;
				}

				const double KlineExpiration = 120.0;

				if(StartUpdationTime.AddSeconds(KlineExpiration) <= DateTime.Now)
				{
					Logger.Write("CheckKlineStream: Kline Updation Failed [1]");

					StartKlineStream();

					++Errors;

					return;
				}

				if(StopUpdationTime.AddSeconds(KlineExpiration) <= DateTime.Now)
				{
					Logger.Write("CheckKlineStream: Kline Updation Failed [2]");

					StartKlineStream();

					++Errors;

					return;
				}

				Errors = default;
			}
			catch(Exception exception)
			{
				Logger.Write("CheckKlineStream: " + exception.Message);
			}
		}
		
		private static void CheckEntryPoint(DateTime time, List<decimal> list)
		{
			try
			{
				bool state = true;

				decimal deviation = default;

				decimal quota = default;

				state = state && GetDeviationFactor(list, 140, out deviation);

				state = state && GetQuota(list, 28, out quota);
				
				if(state)
				{
					Task.Run(() =>
					{
						WriteRecords(time, deviation, quota);
					});

					if(quota >= 0.998m)
					{
						if(deviation >= 2.128m)
						{
							decimal price = list.Last();

							decimal takeProfit = Percent * price;

							Task.Run(() =>
							{
								BinanceFutures.OnEntryPointDetected(price, takeProfit);
							});
						}
					}
				}
				else
				{
					Logger.Write("CheckEntryPoint: Invalid Model");
				}
			}
			catch(Exception exception)
			{
				Logger.Write("CheckEntryPoint: " + exception.Message);
			}
		}

		private static void WriteRecords(DateTime time, decimal deviation, decimal quota)
		{
			try
			{
				StringBuilder stringBuilder = new StringBuilder();

				string str = time.ToString("dd.MM.yyyy HH:mm");

				stringBuilder.Append(str);
				stringBuilder.Append("\t");
				
				stringBuilder.Append(Format(deviation, 4));
				stringBuilder.Append("\t");
				
				stringBuilder.Append(Format(quota, 4));
				stringBuilder.Append("\n");

				lock(LockRecordsFile)
				{
					const string path = "Records.txt";

					File.AppendAllText(path, stringBuilder.ToString());
				}
			}
			catch(Exception exception)
			{
				Logger.Write("WriteRecords: " + exception.Message);
			}
		}

		private static void UpdateRecords()
		{
			try
			{
				const string path = "Records.txt";

				if(File.Exists(path) == false)
				{
					File.AppendAllText(path, "");
				}
			}
			catch(Exception exception)
			{
				Logger.Write("UpdateRecords: " + exception.Message);
			}
		}

		private static bool GetAverage(List<decimal> history, int window, out decimal average)
		{
			average = default;

			try
			{
				int index = history.Count-3;

				for(int i=index-window+1; i<index; ++i)
				{
					decimal price = history[i];

					average += price;
				}

				average /= window;

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("GetAverage: " + exception.Message);

				return false;
			}
		}

		private static bool GetDeviation(List<decimal> history, int window, out decimal average, out decimal deviation)
		{
			average = default;

			deviation = default;

			try
			{
				int index = history.Count-3;

				if(GetAverage(history, window, out average) == false)
				{
					return false;
				}
				
				deviation = default;

				for(int i=index-window+1; i<index; ++i)
				{
					decimal price = history[i];

					deviation += (price - average) * (price - average);
				}

				deviation = (decimal)Math.Sqrt((double)deviation / (window - 1));

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("GetDeviation: " + exception.Message);

				return false;
			}
		}

		private static bool GetDeviationFactor(List<decimal> history, int window, out decimal factor)
		{
			factor = default;

			try
			{
				int index = history.Count-3;

				if(GetDeviation(history, window, out decimal average, out decimal deviation) == false)
				{
					return false;
				}

				decimal delta = average - history[index];

				factor = delta / deviation;

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("GetDeviationFactor: " + exception.Message);

				return false;
			}
		}

		private static bool GetQuota(List<decimal> history, int window, out decimal quota)
		{
			quota = default;

			try
			{
				decimal more = default;

				decimal less = default;

				int index = history.Count-1;

				decimal price2 = history[index];

				for(int i=index-window+1; i<index; ++i)
				{
					decimal price1 = history[i];

					decimal delta = Math.Abs(price1 - price2);

					if(price1 > price2)
					{
						more += delta;
					}
					else
					{
						less += delta;
					}
				}

				if(more + less != default)
				{
					quota = more / (more + less);
				}

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("GetQuota: " + exception.Message);

				return false;
			}
		}

		private static async Task<bool> LoadHistory(string symbol, int count)
		{
			List<Candle> history = new List<Candle>();
			
			try
			{
				const int attempts = 2;

				for(int i=default; i<attempts; ++i)
				{
					BinanceClient client = new BinanceClient();
					
					var response = await client.FuturesUsdt.Market.GetKlinesAsync(symbol, KlineInterval.OneMinute, limit: count);
					
					if(response.Success)
					{
						foreach(var record in response.Data)
						{
							if(record.CloseTime.ToLocalTime() < LastKlineUpdated)
							{
								history.Add(new Candle(record.CloseTime.ToLocalTime(), record.Open, record.Close, record.Low, record.High));
							}
						}

						History = history;

						if(i == 1)
						{
							return true;
						}
					}
					else
					{
						Logger.Write("LoadHistory: " + response.Error.Message);
					}
				}

				return false;
			}
			catch(Exception exception)
			{
				Logger.Write("LoadHistory: " + exception.Message);
				
				return false;
			}
		}

		private static bool CheckHistory()
		{
			try
			{
				if(History == default)
				{
					Logger.Write("CheckHistory: Invalid Buffer");

					return false;
				}

				const int SizeBorder = 160;

				if(History.Count < SizeBorder)
				{
					Logger.Write("CheckHistory: Invalid Sequence");

					return false;
				}

				const double HistoryExpiration = 60.0;

				for(int i=default; i<History.Count-1; ++i)
				{
					if(History[i+1].CloseTime > History[i].CloseTime.AddSeconds(HistoryExpiration).AddMilliseconds(1.0))
					{
						Logger.Write("CheckHistory: Wrong Sequence");

						return false;
					}
				}

				StopUpdationTime = DateTime.Now;

				TimeSpan timeSpan = StopUpdationTime - StartUpdationTime;

				const double HistoryExpired = 6.0;

				if(timeSpan.TotalSeconds >= HistoryExpired)
				{
					string error = string.Format("CheckHistory: History Expired [{0}]", Format(timeSpan.TotalSeconds, 4));

					Logger.Write(error);

					return false;
				}

				return true;
			}
			catch(Exception exception)
			{
				Logger.Write("CheckHistory: " + exception.Message);

				return false;
			}
		}

		private static string Format(double value, int sign = 4)
		{
			try
			{
				sign = Math.Max(sign, 0);
				sign = Math.Min(sign, 8);

				return string.Format(CultureInfo.InvariantCulture, "{0:F" + sign + "}", value);
			}
			catch(Exception exception)
			{
				Logger.Write("Format: " + exception.Message);

				return "Invalid Format";
			}
		}

		private static string Format(decimal value, int sign = 4)
		{
			try
			{
				sign = Math.Max(sign, 0);
				sign = Math.Min(sign, 8);

				return string.Format(CultureInfo.InvariantCulture, "{0:F" + sign + "}", value);
			}
			catch(Exception exception)
			{
				Logger.Write("Format: " + exception.Message);

				return "Invalid Format";
			}
		}
	}
}

