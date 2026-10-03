using System;
using System.Collections.Generic;
using Xsolla.GetUpdates;
using UnityEngine;

namespace Xsolla.Core
{
	internal static class OrderTrackingService
	{
		private static readonly Dictionary<long, OrderTracker> Trackers = new Dictionary<long, OrderTracker>();
		private static readonly Dictionary<string, OrderTracker> TrackersBySku = new Dictionary<string, OrderTracker>();
		private static readonly Dictionary<string, OrderTracker> TrackersByToken = new Dictionary<string, OrderTracker>();

		public static void SetUnorderedPurchaseEvent(XsollaSettings settings, Action<PaymentEvent[]> onEvent, int intervalSec)
		{
			OrderTrackerByEventsUpdater.StartEndlessUpdater(settings, intervalSec, onEvent);
		}

		/// <summary>
		/// Starts status tracking for the specified order. The tracking mechanism varies based on the build platform.
		/// </summary>
		/// <param name="orderId">Order ID.</param>
		/// <param name="isUserInvolvedToPayment">Whether to use platform-specific methods for tracking, such as Web Sockets or Pay Station callbacks.</param>
		/// <param name="onSuccess">Callback, triggered when the order status is changed to `done`</param>
		/// <param name="onError">Callback, triggered when an error occurs during the order tracking.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void AddOrderForTracking(XsollaSettings settings, long orderId, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store)
		{
			var tracker = CreateTracker(settings, orderId, sku: null, token: null, isUserInvolvedToPayment, onSuccess, onError, sdkType);
			if (tracker != null)
				StartTracker(tracker);
		}
		
		public static void AddOrderForTrackingBySku(XsollaSettings settings, string sku, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store)
		{
			var tracker = CreateTracker(settings, orderId: -1, sku,  token: null, isUserInvolvedToPayment, onSuccess, onError, sdkType);
			if (tracker != null)
				StartTracker(tracker);
		}
		
		public static void AddOrderForTrackingByPaymentToken(XsollaSettings settings, string token, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store)
		{
			var tracker = CreateTracker(settings, orderId: -1, sku: null,  token: token, isUserInvolvedToPayment, onSuccess, onError, sdkType);
			if (tracker != null)
				StartTracker(tracker);
		}

		private static OrderTracker CreateTracker(XsollaSettings settings, long orderId, string sku, string token, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType)
		{
			if ((orderId != -1 && Trackers.ContainsKey(orderId)) || (sku != null && TrackersBySku.ContainsKey(sku)) || (token != null && TrackersByToken.ContainsKey(token)))
				return null;

			var trackingData = new OrderTrackingData(settings, orderId, sku, token, onSuccess, onError, sdkType);

			if (settings.EventApiEnabled)
				return new OrderTrackerByEvents(trackingData);

			if (Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor)
			{
				var isSafariBrowser = WebHelper.IsBrowserSafari();
				return settings.InAppBrowserEnabled && !isSafariBrowser
					? (OrderTracker)new OrderTrackerByPaystationCallbacks(trackingData)
					: (OrderTracker)new OrderTrackerByShortPolling(trackingData);
			}
			else
			{
				return new OrderTrackerByShortPolling(trackingData);
			}
		}

		/// <summary>
		/// Stops status tracking for all orders.
		/// </summary>
		public static void CancelAllOrdersFromTracking()
		{
			var trackers1 = new List<OrderTracker>(Trackers.Values);
			foreach (var tracker in trackers1)
				CancelOrderFromTracking(tracker.TrackingData);
			
			var trackers2 = new List<OrderTracker>(TrackersBySku.Values);
			foreach (var tracker in trackers2)
				CancelOrderFromTracking(tracker.TrackingData);
			
			var trackers3 = new List<OrderTracker>(TrackersByToken.Values);
			foreach (var tracker in trackers3)
				CancelOrderFromTracking(tracker.TrackingData);
			
			OrderTrackerByEventsUpdater.RemoveAllFromTracking();
		}
		
		/// <summary>
		/// Stops status tracking for all orders.
		/// </summary>
		public static void RemoveAllOrdersFromTracking(XsollaSettings settings)
		{
			OrderTrackerByEventsUpdater.RemoveAllFromTracking(settings);

			var trackers1 = new List<OrderTracker>(Trackers.Values);
			foreach (var tracker in trackers1)
				RemoveOrderFromTracking(tracker.TrackingData);
			
			var trackers2 = new List<OrderTracker>(TrackersBySku.Values);
			foreach (var tracker in trackers2)
				RemoveOrderFromTracking(tracker.TrackingData);
			
			var trackers3 = new List<OrderTracker>(TrackersByToken.Values);
			foreach (var tracker in trackers3)
				RemoveOrderFromTracking(tracker.TrackingData);
		}

		public static void CancelOrderFromTracking(OrderTrackingData trackingData)
		{
			trackingData.errorCallback?.Invoke(new Error(ErrorType.Aborted, errorMessage: "Order cancelled."));
			RemoveOrderFromTracking(trackingData);
		}

		/// <summary>
		/// Stops status tracking for specified order.
		/// </summary>
		/// <param name="orderId">Order ID.</param>
		public static void RemoveOrderFromTracking(OrderTrackingData trackingData)
		{
			if (trackingData.orderId != -1 && Trackers.TryGetValue(trackingData.orderId, out var tracker))
			{
				tracker.Stop();
				Trackers.Remove(trackingData.orderId);
			}
			else if (!string.IsNullOrEmpty(trackingData.sku) && TrackersBySku.TryGetValue(trackingData.sku, out var trackerSku))
			{
				trackerSku.Stop();
				TrackersBySku.Remove(trackingData.sku);
			}
			else if (!string.IsNullOrEmpty(trackingData.token) && TrackersByToken.TryGetValue(trackingData.token, out var trackerToken))
			{
				trackerToken.Stop();
				TrackersByToken.Remove(trackingData.token);
			}
		}

		public static void ReplaceTracker(OrderTracker oldTracker, OrderTracker newTracker)
		{
			RemoveOrderFromTracking(oldTracker.TrackingData);
			StartTracker(newTracker);
		}
		
		private static void StartTracker(OrderTracker tracker)
		{
			XDebug.Log(tracker.TrackingData.settings, $"Order tracker started: {tracker.GetType().Name}");
			if (tracker.TrackingData.orderId != -1)
				Trackers.Add(tracker.TrackingData.orderId, tracker);
			else if (tracker.TrackingData.sku != null)
				TrackersBySku.Add(tracker.TrackingData.sku, tracker);
			else if (tracker.TrackingData.token != null)
				TrackersByToken.Add(tracker.TrackingData.token, tracker);
			tracker.Start();
		}
	}
}
