namespace Nevma.Contracts.Identity;

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword);

public sealed record ConfirmEmailRequest(
    string Email,
    string Token);

public sealed record RequestEmailConfirmationRequest(string Email);
