namespace GUNRPG.Security;

public sealed record SignedRunValidation(
    RunValidationSignature Validation,
    ServerCertificate Certificate);
