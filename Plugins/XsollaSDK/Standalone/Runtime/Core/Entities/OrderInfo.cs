using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xsolla.Core
{
    internal abstract class OrderInfo
    {
        public sealed class Pending : OrderInfo { }

        public sealed class Canceled : OrderInfo { }

        public sealed class Done : OrderInfo
        {
            public readonly long orderId;
            public readonly long invoiceId;

            public Done(long orderId, long invoiceId)
            {
                this.orderId = orderId;
                this.invoiceId = invoiceId;
            }
        }

        public bool TryAsDone(out Done done)
        {
            done = default;

            if (this is Done) {
                done = (Done)this;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves a Pay Station payment-status response into the state of its order.
        /// </summary>
        /// <remarks>
        /// The latest done invoice wins; without one, the latest invoice decides. Order and invoice IDs pass
        /// through as the 64-bit values the response carries.
        /// </remarks>
        public static bool TryFromResponse(Response response, out OrderInfo orderInfo, out Error error)
        {
            var invoice = FindInvoice(response?.invoices_data);

            orderInfo = null;
            error = null;

            if (invoice == null)
            {
                error = new Error(
                    errorType: ErrorType.OrderInfoNoInvoices,
                    errorMessage: "No invoices found in the response"
                );
            }
            else if (!Enum.IsDefined(typeof(Response.Status), invoice.status))
            {
                error = new Error(
                    errorType: ErrorType.OrderInfoInvalidStatus,
                    errorMessage: "Invalid invoice status value: " + invoice.status
                );
            }
            else if ((Response.Status)invoice.status == Response.Status.Processing)
            {
                orderInfo = new Pending();
            }
            else if ((Response.Status)invoice.status != Response.Status.Done)
            {
                orderInfo = new Canceled();
            }
            else if (invoice.invoice_id < 0)
            {
                error = new Error(
                    errorType: ErrorType.OrderInfoDoneButInvalidInvoiceId,
                    errorMessage: "Invalid invoice ID: " + invoice.invoice_id
                );
            }
            else if (invoice.order_id < 0)
            {
                error = new Error(
                    errorType: ErrorType.OrderInfoDoneButInvalidOrderId,
                    errorMessage: "Invalid order ID: " + invoice.order_id,
                    data: new Dictionary<string, string>
                    {
                        { "invoice_id", invoice.invoice_id.ToString() }
                    }
                );
            }
            else
            {
                orderInfo = new Done(invoice.order_id, invoice.invoice_id);
            }

            return error == null;
        }

        private static Response.InvoiceData FindInvoice(Response.InvoiceData[] invoices)
        {
            if (invoices == null || invoices.Length == 0)
                return null;

            for (var i = invoices.Length - 1; i >= 0; i--)
            {
                if ((Response.Status)invoices[i].status == Response.Status.Done)
                    return invoices[i];
            }

            return invoices[invoices.Length - 1];
        }

        [Serializable]
        internal sealed class Response
        {
            [SerializeField] public InvoiceData[] invoices_data = Array.Empty<InvoiceData>();

            [Serializable]
            public sealed class InvoiceData
            {
                [SerializeField] public long invoice_id;
                [SerializeField] public int status;
                [SerializeField] public long order_id = -1;
            }

            public enum Status
            {
                [UsedImplicitly] Created = 1,
                Processing = 2,
                Done = 3,
                [UsedImplicitly] Canceled = 4,
                [UsedImplicitly] Error = 5,
                [UsedImplicitly] Authorized = 6,
                [UsedImplicitly] XsollaRefund = 7,
                [UsedImplicitly] XsollaRefundFailed = 8,
                [UsedImplicitly] Test = 9,
                [UsedImplicitly] Fraud = 10,
                [UsedImplicitly] CheckLenya = 11,
                [UsedImplicitly] Held = 12,
                [UsedImplicitly] Denied = 13,
                [UsedImplicitly] Stop = 14,
                [UsedImplicitly] Lost = 15,
                [UsedImplicitly] PartiallyRefunded = 16
            }
        }

        
    }
}
