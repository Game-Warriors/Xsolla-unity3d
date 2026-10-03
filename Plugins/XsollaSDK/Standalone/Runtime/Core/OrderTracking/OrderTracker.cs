using System;
using System.Collections.Generic;

namespace Xsolla.Core
{
	internal abstract class OrderTracker
	{
		private bool _isCheckInProgress;
		private bool _isGetOrderIdInProgress;

		public readonly OrderTrackingData TrackingData;

		public abstract void Start();

		public abstract void Stop();

		protected void RemoveSelfFromTracking()
		{
			OrderTrackingService.RemoveOrderFromTracking(TrackingData);
		}

		protected void CheckOrderStatus(Action<OrderStatus> onDone = null, Action onCancel = null, Action<Error> onError = null)
		{
			if (!TrackingData.settings.XsollaToken.Exists)
			{
				XDebug.LogWarning(TrackingData.settings, "No Token in order status check. Check cancelled");
				onCancel?.Invoke();
				return;
			}

			if (!CheckOrderIdExists())
				return;
			
			if (_isCheckInProgress) // Prevent double check
				return;
			
			if (TrackingData.orderId == 0 && TrackingData.Error != null)
			{
				onError?.Invoke(TrackingData.Error);
				return;
			}

			_isCheckInProgress = true;

			OrderStatusService.GetOrderStatus(
				TrackingData.settings,
				TrackingData.orderId,
				status =>
				{
					_isCheckInProgress = false;
					HandleOrderStatus(status, onDone, onCancel);
				},
				error =>
				{
					_isCheckInProgress = false;
					onError?.Invoke(error);
				},
				TrackingData.sdkType,
				token: TrackingData.token
			);
		}
		
		protected bool CheckOrderIdExists()
		{
			if (TrackingData.OrderId == -1 && string.IsNullOrEmpty(TrackingData.sku) && string.IsNullOrEmpty(TrackingData.token))
			{
				XDebug.LogError(TrackingData.settings, "Order ID is not set and no token provided. Check cancelled");
				return false;
			}
			
			if (TrackingData.OrderId == -1 && !string.IsNullOrEmpty(TrackingData.token))
			{
				if (_isGetOrderIdInProgress) // Prevent double get order ID
					return false;

				_isGetOrderIdInProgress = true;

				OrderStatusService.GetOrderInfo(
					TrackingData.settings,
					accessToken: TrackingData.token,
					TrackingData.sdkType,
					onSuccess: orderInfo =>
					{
						_isGetOrderIdInProgress = false;
						if (orderInfo.TryAsDone(out var done))
							TrackingData.OrderId = done.orderId;
					},
					onFailure: error =>
					{
						_isGetOrderIdInProgress = false;
						if (error.ErrorType == ErrorType.OrderInfoDoneButInvalidOrderId ||
						    error.ErrorType == ErrorType.OrderInfoDoneButInvalidInvoiceId)
						{
							TrackingData.OrderId = 0;
							TrackingData.Error = error;
						}
					}
				);

				return false;
			}

			return true;
		}

		protected static void HandleOrderStatus(OrderStatus status, Action<OrderStatus> onDone, Action onCancel)
		{
			switch (status.status)
			{
				case "done":
					onDone?.Invoke(status);
					break;
				case "canceled":
					onCancel?.Invoke();
					break;
			}
		}

		protected OrderTracker(OrderTrackingData trackingData)
		{
			TrackingData = trackingData;
		}
	}
}
