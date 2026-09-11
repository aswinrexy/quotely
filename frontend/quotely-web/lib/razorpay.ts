import type { PaymentOrder } from "@/types";

/**
 * The only Razorpay-aware code in the browser. The checkout script is fetched on demand — when
 * the customer actually chooses to pay — rather than loaded on every invoice view, and nothing
 * here decides what is charged: the order came from the server already priced.
 */

const CHECKOUT_SRC = "https://checkout.razorpay.com/v1/checkout.js";

interface RazorpayHandlerResponse {
  razorpay_payment_id: string;
  razorpay_order_id: string;
  razorpay_signature: string;
}

interface RazorpayInstance {
  open: () => void;
  on: (event: string, handler: (payload: unknown) => void) => void;
}

type RazorpayConstructor = new (options: Record<string, unknown>) => RazorpayInstance;

declare global {
  interface Window {
    Razorpay?: RazorpayConstructor;
  }
}

let loader: Promise<RazorpayConstructor> | null = null;

/** Loads the checkout script once per page, reusing the same promise for concurrent callers. */
function loadCheckout(): Promise<RazorpayConstructor> {
  if (typeof window === "undefined") return Promise.reject(new Error("Checkout needs a browser."));
  if (window.Razorpay) return Promise.resolve(window.Razorpay);

  loader ??= new Promise<RazorpayConstructor>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = CHECKOUT_SRC;
    script.async = true;
    script.onload = () => {
      if (window.Razorpay) resolve(window.Razorpay);
      else reject(new Error("The payment window could not be loaded."));
    };
    script.onerror = () => {
      loader = null;
      reject(new Error("The payment window could not be loaded. Please check your connection."));
    };
    document.body.appendChild(script);
  });

  return loader;
}

export interface CheckoutOutcome {
  /** The customer closed the window without paying. Not a failure — nothing was charged. */
  dismissed?: boolean;
  /** Handed straight to the server for verification; never interpreted here as success. */
  result?: {
    razorpayPaymentId: string;
    razorpayOrderId: string;
    razorpaySignature: string;
  };
  /** The provider reported a failed attempt before the window closed. */
  failure?: string;
}

/**
 * Opens checkout and resolves once the customer is done with it. Resolving with a result means
 * only "the provider handed back these values" — whether money moved is the server's to decide.
 */
export async function openCheckout(order: PaymentOrder): Promise<CheckoutOutcome> {
  const Razorpay = await loadCheckout();

  return new Promise<CheckoutOutcome>((resolve) => {
    let settled = false;
    const settle = (outcome: CheckoutOutcome) => {
      if (settled) return;
      settled = true;
      resolve(outcome);
    };

    const instance = new Razorpay({
      key: order.keyId,
      order_id: order.orderId,
      amount: order.amount,
      currency: order.currency,
      name: order.businessName,
      description: `Invoice ${order.invoiceNumber}`,
      prefill: {
        name: order.customerName ?? undefined,
        email: order.customerEmail ?? undefined,
        contact: order.customerContact ?? undefined,
      },
      notes: { invoiceNumber: order.invoiceNumber },
      theme: { color: "#171717" },
      handler: (response: RazorpayHandlerResponse) =>
        settle({
          result: {
            razorpayPaymentId: response.razorpay_payment_id,
            razorpayOrderId: response.razorpay_order_id,
            razorpaySignature: response.razorpay_signature,
          },
        }),
      modal: {
        ondismiss: () => settle({ dismissed: true }),
      },
    });

    instance.on("payment.failed", (payload: unknown) => {
      const description = (payload as { error?: { description?: string } } | undefined)?.error?.description;
      settle({ failure: description ?? "The payment could not be completed." });
    });

    instance.open();
  });
}
