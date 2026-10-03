using System;
using JetBrains.Annotations;

namespace Xsolla.Core
{
	[Serializable]
	internal class OrderStatus
	{
		public long order_id;
		public string status;
		public OrderContent content;
		
		[CanBeNull] public string transaction_id;
		[CanBeNull] public string receipt;
	}
}
