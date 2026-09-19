import type { PaymentOrder, SubscriptionCheckout } from "@/types";

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

/**
 * What checkout hands back after a SUBSCRIPTION mandate is authorised.
 *
 * Note the second field: a subscription returns razorpay_subscription_id where a one-off payment
 * returns razorpay_order_id. They are different flows with different signatures, which is why
 * they have separate types here rather than one loose shape.
 */
interface RazorpaySubscriptionHandlerResponse {
  razorpay_payment_id: string;
  razorpay_subscription_id: string;
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


export interface SubscriptionCheckoutOutcome {
  /** The business closed the window without authorising. Nothing was set up, nothing charged. */
  dismissed?: boolean;
  /** Handed straight to the server to verify. Never treated here as "it worked". */
  result?: {
    razorpaySubscriptionId: string;
    razorpayPaymentId: string;
    razorpaySignature: string;
  };
  failure?: string;
}

/**
 * Opens checkout for a SaaS subscription mandate — the business authorising Quotely to charge
 * ₹150 a month.
 *
 * Embedded rather than a redirect to Razorpay's hosted short_url, for one concrete reason:
 * subscriptions have no callback_url parameter, so a redirect leaves the business sitting on
 * Razorpay's page with no way back and no confirmation in Quotely. Embedded checkout returns the
 * signed result to this page, which hands it to the server to verify — and the business sees the
 * outcome where they started.
 *
 * short_url stays as the fallback for when the script cannot load at all.
 */
export async function openSubscriptionCheckout(
  checkout: SubscriptionCheckout,
  account: { name?: string | null; email?: string | null },
): Promise<SubscriptionCheckoutOutcome> {
  const Razorpay = await loadCheckout();

  return new Promise<SubscriptionCheckoutOutcome>((resolve) => {
    let settled = false;
    const settle = (outcome: SubscriptionCheckoutOutcome) => {
      if (settled) return;
      settled = true;
      resolve(outcome);
    };

    const instance = new Razorpay({
      key: checkout.keyId,
      // A subscription is authorised by id; there is no order and no amount to pass. Razorpay
      // reads what to charge, and when, from the plan and the subscription's start date.
      subscription_id: checkout.subscriptionId,
      name: "Quotely",
      description: checkout.firstChargeAt
        ? `${checkout.planName} — first payment ${new Date(checkout.firstChargeAt).toLocaleDateString()}`
        : checkout.planName,
      prefill: {
        name: account.name ?? undefined,
        email: account.email ?? undefined,
      },
      theme: { color: "#171717" },
      handler: (response: RazorpaySubscriptionHandlerResponse) =>
        settle({
          result: {
            razorpaySubscriptionId: response.razorpay_subscription_id,
            razorpayPaymentId: response.razorpay_payment_id,
            razorpaySignature: response.razorpay_signature,
          },
        }),
      modal: {
        ondismiss: () => settle({ dismissed: true }),
      },
    });

    instance.on("payment.failed", (payload: unknown) => {
      const description = (payload as { error?: { description?: string } } | undefined)?.error?.description;
      settle({ failure: description ?? "The mandate could not be set up." });
    });

    instance.open();
  });
}
