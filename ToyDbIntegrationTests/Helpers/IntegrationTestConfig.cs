namespace ToyDbIntegrationTests.Helpers;

public static class IntegrationTestConfig
{
    public static bool SkipCertificateValidation =>
        string.Equals(Environment.GetEnvironmentVariable("TOYDB_SKIP_CERT_VALIDATION"), "true", StringComparison.OrdinalIgnoreCase);
}
