using InsightaAI.Agent.Security;

namespace InsightaAI.Agent.Tests.Security;

public sealed class SecretRedactionPipelineTests
{
    private readonly ISecretRedactor _redactor = SecretRedactionPipeline.CreateDefault();

    [Fact]
    public void Redact_JsonConfiguration_PreservesNonSensitiveFields()
    {
        const string secret = "database-password";
        var result = _redactor.Redact(
            $$"""{ "ConnectionStrings": { "Main": "Host=db;Database=app;Password={{secret}}" }, "Logging": { "Level": "Information" } }""",
            new RedactionContext { ToolName = "read_file", SourcePath = "appsettings.json", Format = SecretContentFormat.Json });

        Assert.True(result.WasRedacted);
        Assert.DoesNotContain(secret, result.Content);
        Assert.Contains("Host=db", result.Content);
        Assert.Contains("Database=app", result.Content);
        Assert.Contains("Information", result.Content);
    }

    [Theory]
    [InlineData("DB_PASSWORD=super-secret")]
    [InlineData("SECRET_KEY=super-secret")]
    [InlineData("     3\tAPI_KEY=super-secret")]
    [InlineData("password: super-secret")]
    [InlineData("key: super-secret")]
    [InlineData("auth:\n  token: super-secret")]
    [InlineData("     4\t  token: super-secret")]
    [InlineData("<Password>super-secret</Password>")]
    [InlineData("postgresql://app:super-secret@db.example/app")]
    public void Redact_TextFormats_RemovesSecretValues(string content)
    {
        var result = _redactor.Redact(
            content,
            new RedactionContext { ToolName = "bash", Format = SecretContentFormat.PlainText });

        Assert.True(result.WasRedacted);
        Assert.DoesNotContain("super-secret", result.Content);
        Assert.Contains("[REDACTED]", result.Content);
    }

    [Fact]
    public void Redact_PrivateKeyBlock_RemovesEntirePayload()
    {
        const string privateKey = "-----BEGIN PRIVATE KEY-----\nsecret-key-material\n-----END PRIVATE KEY-----";
        var result = _redactor.Redact(
            privateKey,
            new RedactionContext { ToolName = "bash", Format = SecretContentFormat.PlainText });

        Assert.DoesNotContain("secret-key-material", result.Content);
        Assert.Equal("[REDACTED]", result.Content);
    }

    [Fact]
    public void Redact_ReadFileLineNumberedJson_RemovesSensitiveValues()
    {
        const string apiKey = "runtime-api-key";
        const string password = "runtime-password";
        var lineNumberedContent = $$"""
            File: D:\\test\\config.json
            Lines: 0-4 of 4
            ---
                 0\t{
                 1\t  "apiKey": "{{apiKey}}",
                 2\t  "database": { "password": "{{password}}" }
                 3\t}
            """.Replace("\\t", "\t", StringComparison.Ordinal);

        var result = _redactor.Redact(
            lineNumberedContent,
            new RedactionContext { ToolName = "read_file", SourcePath = "D:\\test\\config.json", Format = SecretContentFormat.Json });

        Assert.DoesNotContain(apiKey, result.Content);
        Assert.DoesNotContain(password, result.Content);
        Assert.Contains("\"apiKey\": \"[REDACTED]\",", result.Content);
        Assert.Contains("\"password\": \"[REDACTED]\"", result.Content);
    }

    [Fact]
    public void Redact_ReadFileLineNumberedXml_RemovesSensitiveValues()
    {
        const string apiPassword = "runtime-api-password";
        const string authToken = "runtime-auth-token";
        var lineNumberedContent = $$"""
            File: D:\\test\\settings.xml
            Lines: 0-4 of 4
            ---
                 0\t<settings>
                 1\t  <add key="ApiPassword" value="{{apiPassword}}" />
                 2\t  <AuthToken>{{authToken}}</AuthToken>
                 3\t</settings>
            """.Replace("\\t", "\t", StringComparison.Ordinal);

        var result = _redactor.Redact(
            lineNumberedContent,
            new RedactionContext { ToolName = "read_file", SourcePath = "D:\\test\\settings.xml", Format = SecretContentFormat.Xml });

        Assert.DoesNotContain(apiPassword, result.Content);
        Assert.DoesNotContain(authToken, result.Content);
        Assert.Contains("[REDACTED]", result.Content);
    }

    [Fact]
    public void Redact_ReadFileWindowsLineNumberedKeyValue_RemovesSensitiveValues()
    {
        const string apiKey = "runtime-api-key";
        const string token = "runtime-token";
        var lineNumberedContent =
            $"File: D:\\test\\app.yaml\r\nLines: 0-3 of 3\r\n---\r\n     0\tAPI_KEY={apiKey}\r\n     1\t  token: {token}\r\n";

        var result = _redactor.Redact(
            lineNumberedContent,
            new RedactionContext { ToolName = "read_file", SourcePath = "D:\\test\\app.yaml", Format = SecretContentFormat.Yaml });

        Assert.DoesNotContain(apiKey, result.Content);
        Assert.DoesNotContain(token, result.Content);
        Assert.Contains("[REDACTED]", result.Content);
        Assert.Contains("\r\n", result.Content);
    }
}
