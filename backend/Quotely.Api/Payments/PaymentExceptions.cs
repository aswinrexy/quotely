namespace Quotely.Api.Payments;

/// <summary>
/// A failure inside the provider integration. Callers translate these into the application's
/// normal ApiException envelope, so a provider's own error text never reaches a customer.
/// </summary>
public class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The provider's signature did not match. Always treated as hostile input.</summary>
public class PaymentSignatureException : Exception
{
    public PaymentSignatureException(string message) : base(message) { }
}

/// <summary>
/// The merchant's credentials were refused, or are missing. Separated from
/// <see cref="PaymentProviderException"/> because the two call for opposite responses: a provider
/// hiccup should be retried, whereas a rejected credential should stop the retries and mark the
/// connection as needing attention.
/// </summary>
public class PaymentCredentialException : Exception
{
    public PaymentCredentialException(string message, Exception? inner = null) : base(message, inner) { }
}
