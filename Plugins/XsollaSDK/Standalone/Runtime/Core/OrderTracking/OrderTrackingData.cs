using System;

namespace Xsolla.Core
{
	internal class OrderTrackingData
	{
		public long orderId { get; private set; }
		public readonly string sku;
		public readonly string token;
		public readonly Action<OrderStatus> successCallback;
		public readonly Action<Error> errorCallback;
		public readonly SdkType sdkType;
		public Error error { get; private set; } 
		public readonly XsollaSettings settings;

		public OrderTrackingData(XsollaSettings settings, long orderId, string sku, string token, Action<OrderStatus> successCallback, Action<Error> errorCallback, SdkType sdkType)
		{
			this.orderId = orderId;
			this.sku = sku;
			this.token = token;
			this.successCallback = successCallback;
			this.errorCallback = errorCallback;
			this.sdkType = sdkType;
			this.error = null;
			this.settings = settings;
		}
		
		public long OrderId
		{
			get => orderId;
			set => orderId = value;
		}
		
		public Error Error
		{
			get => error;
			set => error = value;
		}
	}
}
