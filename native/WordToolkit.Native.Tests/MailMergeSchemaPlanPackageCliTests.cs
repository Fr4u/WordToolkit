using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class MailMergeSchemaPlanPackageCliTests
{
    [Fact]
    public void CliUsesStrictEngineContractAndNeverAcceptsRecordValues()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            MailMergePackageInspectionTests.CreatePackage(path);
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var request = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = fingerprint,
                source_columns = new[]
                {
                    new { name = "CustomerId", data_kind = "number" },
                    new { name = "FirstName", data_kind = "text" },
                },
            });
            var output = new StringWriter();
            var error = new StringWriter();

            var exit = MailMergeSchemaPlanPackageCli.Run(
                ["--request", "-", "--format", "json"],
                new StringReader(request),
                output,
                error
            );

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;
            Assert.Equal(
                MailMergeSchemaPlanWordPackageContract.Contract,
                root.GetProperty("operation_contract").GetString()
            );
            Assert.True(root.GetProperty("can_bind_schema").GetBoolean());
            Assert.False(root.GetProperty("execution_supported").GetBoolean());
            Assert.False(root.GetProperty("contains_record_values").GetBoolean());
            Assert.Equal(2, root.GetProperty("bindings").GetArrayLength());
            var disclosure = root.GetProperty("disclosure");
            Assert.False(disclosure.GetProperty("record_values_accepted").GetBoolean());
            Assert.False(disclosure.GetProperty("record_values_returned").GetBoolean());
            Assert.False(disclosure.GetProperty("word_opened").GetBoolean());
            Assert.False(disclosure.GetProperty("mail_merge_executed").GetBoolean());
            Assert.False(disclosure.GetProperty("data_sources_opened").GetBoolean());
            Assert.False(disclosure.GetProperty("queries_executed").GetBoolean());
            Assert.False(disclosure.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(disclosure.GetProperty("mutation_performed").GetBoolean());
            Assert.DoesNotContain("Provider=Sensitive", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("identity-first", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliRejectsUnknownFieldsDuplicateMembersAndStaleFingerprint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var unknownExit = MailMergeSchemaPlanPackageCli.Run(
            ["--request", "-"],
            new StringReader(
                "{\"local_path\":\"Z:\\\\missing.docx\",\"expected_package_fingerprint\":\""
                    + new string('0', 64)
                    + "\",\"source_columns\":[],\"record_values\":[]}"
            ),
            output,
            error
        );

        Assert.Equal(64, unknownExit);
        AssertErrorCode(error, "INVALID_INPUT");

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        var duplicateExit = MailMergeSchemaPlanPackageCli.Run(
            ["--request", "-"],
            new StringReader(
                "{\"local_path\":\"a.docx\",\"local_path\":\"b.docx\",\"expected_package_fingerprint\":\""
                    + new string('0', 64)
                    + "\",\"source_columns\":[]}"
            ),
            output,
            error
        );

        Assert.Equal(64, duplicateExit);
        AssertErrorCode(error, "INVALID_INPUT");

        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            MailMergePackageInspectionTests.CreatePackage(path);
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            var staleExit = MailMergeSchemaPlanPackageCli.Run(
                ["--request", "-"],
                new StringReader(JsonSerializer.Serialize(new
                {
                    local_path = path,
                    expected_package_fingerprint = new string('0', 64),
                    source_columns = Array.Empty<object>(),
                })),
                output,
                error
            );

            Assert.Equal(75, staleExit);
            AssertErrorCode(error, "VERSION_CONFLICT");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliRejectsUnknownDataKindsAndSourceColumnMembersBeforeFilesystemAccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var prefix = "{\"local_path\":\"Z:\\\\missing.docx\",\"expected_package_fingerprint\":\""
            + new string('0', 64)
            + "\",\"source_columns\":[";

        var kindExit = MailMergeSchemaPlanPackageCli.Run(
            ["--request", "-"],
            new StringReader(prefix + "{\"name\":\"A\",\"data_kind\":\"currency\"}]}"),
            output,
            error
        );
        Assert.Equal(64, kindExit);
        AssertErrorCode(error, "INVALID_INPUT");

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        var memberExit = MailMergeSchemaPlanPackageCli.Run(
            ["--request", "-"],
            new StringReader(prefix + "{\"name\":\"A\",\"value\":\"secret\"}]}"),
            output,
            error
        );
        Assert.Equal(64, memberExit);
        AssertErrorCode(error, "INVALID_INPUT");
    }

    private static void AssertErrorCode(StringWriter error, string expected)
    {
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            expected,
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-mail-merge-schema-cli-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }
}
