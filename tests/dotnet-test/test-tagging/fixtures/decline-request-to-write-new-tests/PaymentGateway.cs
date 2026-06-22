namespace Payments;

public sealed class PaymentGateway
{
    public Task<ChargeResult> ChargeAsync(string cardToken, decimal amount) => throw new NotImplementedException();
    public Task<RefundResult> RefundAsync(string chargeId) => throw new NotImplementedException();
}

public record ChargeResult(string ChargeId, bool Success);
public record RefundResult(string RefundId, bool Success);
