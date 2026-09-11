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
