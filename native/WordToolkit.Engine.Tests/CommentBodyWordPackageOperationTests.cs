using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Engine.Tests;

public sealed class CommentBodyWordPackageOperationTests
{
    private const string Word =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Word2010 =
        "http://schemas.microsoft.com/office/word/2010/wordml";

    [Fact]
    public void PublicPlanAndApplyRewriteOnlySelectedCommentBodyAcrossRuns()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "comments.docx");
            CreatePackage(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var review = new WordReviewGraphBuilder().Build(before, semantic);
            var root = review.Comments.Single(comment => comment.OoxmlId == "0");
            Assert.True(root.HasReactions);
            Assert.Equal("00000001", root.DurableId);
            var commands = new[]
            {
                new ReplaceCommentBodyTextCommand(
                    root.Id,
                    "split target",
                    "rewritten body",
                    ExpectedMatchCount: 1
                ),
            };
            var operation = new CommentBodyWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );

            var plan = operation.Plan(
                new CommentBodyEditPlanRequest(
                    path,
                    before.Fingerprint,
                    commands,
                    IncludeDetails: true
                )
            );

            Assert.Equal(CommentBodyWordPackageContract.PlanContract, plan.OperationContract);
            Assert.StartsWith("wcbplan_", plan.PlanId, StringComparison.Ordinal);
            Assert.True(plan.CanApply);
            Assert.True(plan.CandidateValidation.Performed);
            Assert.True(plan.CandidateValidation.NoNewErrors);
            Assert.Equal(1, plan.CommentCount);
            Assert.Equal(1, plan.MatchedOccurrenceCount);
            Assert.Equal(2, plan.TextNodeOperationCount);
            Assert.Equal(1, plan.ChangedPartCount);
            Assert.False(plan.RawTextReturned);
            Assert.False(plan.RawXmlReturned);
            Assert.False(plan.MutationPerformed);
            Assert.False(plan.WordOpened);
            var detail = Assert.Single(plan.CommentEdits!);
            Assert.Equal(root.Id, detail.CommentId);
            Assert.Equal(64, detail.BeforeBodySha256.Length);
            Assert.Equal(64, detail.AfterBodySha256.Length);
            Assert.DoesNotContain("split target", WordToolkitOperationJson.Serialize(plan));
            Assert.DoesNotContain("rewritten body", WordToolkitOperationJson.Serialize(plan));
            var repeated = new CommentBodyWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            ).Plan(new CommentBodyEditPlanRequest(
                path,
                before.Fingerprint,
                commands,
                IncludeDetails: true
            ));
            Assert.Equal(plan.PlanId, repeated.PlanId);
            Assert.Equal(plan.ResultPackageFingerprint, repeated.ResultPackageFingerprint);

            var applied = operation.Apply(
                new CommentBodyEditApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    commands,
                    KeepBackup: true
                )
            );

            Assert.True(applied.Applied);
            Assert.False(applied.NoOp);
            Assert.Equal(CommentBodyWordPackageContract.ApplyContract, applied.OperationContract);
            Assert.Equal(["word/comments.xml"], applied.ChangedEntryNames);
            Assert.NotNull(applied.BackupPath);
            Assert.True(File.Exists(applied.BackupPath));
            var after = reader.Read(path);
            Assert.Equal(plan.ResultPackageFingerprint, after.Fingerprint);
            foreach (var entry in before.Entries.Where(entry => entry.Name != "word/comments.xml"))
            {
                Assert.Equal(
                    entry.Content.ToArray(),
                    after.Entries.Single(candidate => candidate.Name == entry.Name)
                        .Content.ToArray()
                );
            }
            var afterSemantic = new WordSemanticProjector().Project(after);
            var afterReview = new WordReviewGraphBuilder().Build(after, afterSemantic);
            var changed = afterReview.Comments.Single(comment => comment.Id == root.Id);
            Assert.Equal("prefix rewritten body suffix", changed.Text);
            Assert.Equal(root.Author, changed.Author);
            Assert.Equal(root.DurableId, changed.DurableId);
            Assert.Equal(root.IsDone, changed.IsDone);
            Assert.Equal(root.HasReactions, changed.HasReactions);
            Assert.Equal(root.AnchorIds, changed.AnchorIds);
            Assert.Equal(
                review.Comments.Single(comment => comment.OoxmlId == "1").Text,
                afterReview.Comments.Single(comment => comment.OoxmlId == "1").Text
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NeverMatchesAcrossStructuralOrRenderedCommentBoundaries()
    {
        var cases = new Dictionary<string, (string Body, string ExpectedCode)>(
            StringComparer.Ordinal
        )
        {
            ["paragraph"] = (
                "<w:p w14:paraId=\"C0000003\"><w:r><w:t>foo</w:t></w:r></w:p>"
                + "<w:p w14:paraId=\"C0000001\"><w:r><w:t>bar</w:t></w:r></w:p>",
                "MATCH_COUNT_MISMATCH"
            ),
            ["tab"] = (
                "<w:p w14:paraId=\"C0000001\"><w:r><w:t>foo</w:t><w:tab/><w:t>bar</w:t></w:r></w:p>",
                "MATCH_COUNT_MISMATCH"
            ),
            ["break"] = (
                "<w:p w14:paraId=\"C0000001\"><w:r><w:t>foo</w:t><w:br/><w:t>bar</w:t></w:r></w:p>",
                "MATCH_COUNT_MISMATCH"
            ),
            ["table_cell"] = (
                "<w:tbl><w:tr><w:tc><w:p w14:paraId=\"C0000003\"><w:r><w:t>foo</w:t></w:r></w:p></w:tc>"
                + "<w:tc><w:p w14:paraId=\"C0000001\"><w:r><w:t>bar</w:t></w:r></w:p></w:tc></w:tr></w:tbl>",
                "UNSAFE_EDIT"
            ),
            ["field"] = (
                "<w:p w14:paraId=\"C0000001\"><w:r><w:t>foo</w:t><w:fldChar w:fldCharType=\"begin\"/>"
                + "<w:instrText> DATE </w:instrText><w:fldChar w:fldCharType=\"separate\"/><w:t>bar</w:t>"
                + "<w:fldChar w:fldCharType=\"end\"/></w:r></w:p>",
                "MATCH_COUNT_MISMATCH"
            ),
            ["content_control"] = (
                "<w:p w14:paraId=\"C0000001\"><w:r><w:t>foo</w:t></w:r><w:sdt><w:sdtPr/>"
                + "<w:sdtContent><w:r><w:t>controlled</w:t></w:r></w:sdtContent></w:sdt>"
                + "<w:r><w:t>bar</w:t></w:r></w:p>",
                "MATCH_COUNT_MISMATCH"
            ),
        };
        var directory = TemporaryDirectory();
        try
        {
            foreach (var (name, boundary) in cases)
            {
                var path = Path.Combine(directory, $"{name}.docx");
                CreatePackage(path, rootCommentBodyXml: boundary.Body);
                var reader = new OpcPackageReader();
                var before = reader.Read(path);
                var semantic = new WordSemanticProjector().Project(before);
                var comment = new WordReviewGraphBuilder().Build(before, semantic)
                    .Comments.Single(item => item.OoxmlId == "0");
                var error = Assert.Throws<WordToolkitOperationException>(() =>
                    new CommentBodyWordPackageOperation(
                        new MicrosoftOpenXmlPackageValidator()
                    ).Plan(new CommentBodyEditPlanRequest(
                        path,
                        before.Fingerprint,
                        [new ReplaceCommentBodyTextCommand(comment.Id, "foobar", "X")]
                    ))
                );
                Assert.Equal(boundary.ExpectedCode, error.Code);
                Assert.Equal(before.Fingerprint, reader.Read(path).Fingerprint);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailsClosedOnAmbiguityDriftUnknownFieldsAndMissingValidator()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "closed.docx");
            CreatePackage(path);
            var before = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var comment = new WordReviewGraphBuilder().Build(before, semantic)
                .Comments.Single(item => item.OoxmlId == "0");
            var command = new ReplaceCommentBodyTextCommand(
                comment.Id,
                "split target",
                "safe replacement"
            );
            var withoutValidator = new CommentBodyWordPackageOperation();
            var blocked = withoutValidator.Plan(
                new CommentBodyEditPlanRequest(path, before.Fingerprint, [command])
            );
            Assert.False(blocked.CanApply);
            Assert.Contains("schema_validator_unavailable", blocked.ApplyBlockedReasons);
            var applyBlocked = Assert.Throws<WordToolkitOperationException>(() =>
                withoutValidator.Apply(
                    new CommentBodyEditApplyRequest(
                        path,
                        before.Fingerprint,
                        blocked.PlanId,
                        [command]
                    )
                )
            );
            Assert.Equal("VALIDATOR_REQUIRED", applyBlocked.Code);

            var operation = new CommentBodyWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var mismatch = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Plan(
                    new CommentBodyEditPlanRequest(
                        path,
                        before.Fingerprint,
                        [command with { ExpectedMatchCount = 2 }]
                    )
                )
            );
            Assert.Equal("MATCH_COUNT_MISMATCH", mismatch.Code);
            var plan = operation.Plan(
                new CommentBodyEditPlanRequest(path, before.Fingerprint, [command])
            );
            var drift = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(
                    new CommentBodyEditApplyRequest(
                        path,
                        before.Fingerprint,
                        plan.PlanId,
                        [command with { ReplacementText = "different" }]
                    )
                )
            );
            Assert.Equal("PLAN_MISMATCH", drift.Code);

            var json = $$"""
                {
                  "local_path": {{System.Text.Json.JsonSerializer.Serialize(path)}},
                  "expected_package_fingerprint": "{{before.Fingerprint}}",
                  "commands": [{
                    "type": "replace_comment_body_text",
                    "comment_id": "{{comment.Id}}",
                    "find_text": "split target",
                    "replacement_text": "new",
                    "expected_match_count": 1
                  }]
                }
                """;
            Assert.Single(CommentBodyEditOperationJson.ParsePlanRequest(json).Commands);
            var unknown = Assert.Throws<WordToolkitOperationException>(() =>
                CommentBodyEditOperationJson.ParsePlanRequest(
                    json.Replace(
                        "\"expected_match_count\": 1",
                        "\"expected_match_count\": 1, \"raw_xml\": true",
                        StringComparison.Ordinal
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", unknown.Code);
            Assert.Equal(before.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);

            var duplicatePath = Path.Combine(directory, "duplicate.docx");
            CreatePackage(duplicatePath, duplicateCommentId: true);
            var duplicatePackage = new OpcPackageReader().Read(duplicatePath);
            var duplicateSemantic = new WordSemanticProjector().Project(duplicatePackage);
            var duplicateComment = new WordReviewGraphBuilder().Build(
                duplicatePackage,
                duplicateSemantic
            ).Comments.First();
            var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Plan(new CommentBodyEditPlanRequest(
                    duplicatePath,
                    duplicatePackage.Fingerprint,
                    [new ReplaceCommentBodyTextCommand(
                        duplicateComment.Id,
                        "split target",
                        "blocked"
                    )]
                ))
            );
            Assert.Equal("UNSAFE_EDIT", duplicate.Code);

            var revisionPath = Path.Combine(directory, "revision.docx");
            CreatePackage(revisionPath, trackedCommentText: true);
            var revisionPackage = new OpcPackageReader().Read(revisionPath);
            var revisionSemantic = new WordSemanticProjector().Project(revisionPackage);
            var revisionComment = new WordReviewGraphBuilder().Build(
                revisionPackage,
                revisionSemantic
            ).Comments.Single(item => item.OoxmlId == "0");
            var revision = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Plan(new CommentBodyEditPlanRequest(
                    revisionPath,
                    revisionPackage.Fingerprint,
                    [new ReplaceCommentBodyTextCommand(
                        revisionComment.Id,
                        "split target",
                        "blocked"
                    )]
                ))
            );
            Assert.Equal("UNSAFE_EDIT", revision.Code);

            var signedPath = Path.Combine(directory, "signed.docx");
            CreatePackage(signedPath);
            using (var signedArchive = ZipFile.Open(signedPath, ZipArchiveMode.Update))
            {
                WriteEntry(signedArchive, "_xmlsignatures/sig1.xml", "<Signature/>");
            }
            var signedPackage = new OpcPackageReader().Read(signedPath);
            var signedSemantic = new WordSemanticProjector().Project(signedPackage);
            var signedComment = new WordReviewGraphBuilder().Build(
                signedPackage,
                signedSemantic
            ).Comments.Single(item => item.OoxmlId == "0");
            var signedCommand = new ReplaceCommentBodyTextCommand(
                signedComment.Id,
                "split target",
                "blocked"
            );
            var signedPlan = operation.Plan(new CommentBodyEditPlanRequest(
                signedPath,
                signedPackage.Fingerprint,
                [signedCommand]
            ));
            Assert.False(signedPlan.CanApply);
            Assert.Contains("digital_signature_present", signedPlan.ApplyBlockedReasons);
            var signed = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new CommentBodyEditApplyRequest(
                    signedPath,
                    signedPackage.Fingerprint,
                    signedPlan.PlanId,
                    [signedCommand]
                ))
            );
            Assert.Equal("SIGNED_PACKAGE", signed.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProtectedCommentEditRequiresExactPlanBoundAuthorization()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "protected.docx");
            CreatePackage(path, settingsXml: SettingsXml(
                "<w:documentProtection w:edit=\"comments\" w:enforcement=\"1\"/>"
            ));
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var comment = new WordReviewGraphBuilder().Build(before, semantic)
                .Comments.Single(item => item.OoxmlId == "0");
            var command = new ReplaceCommentBodyTextCommand(
                comment.Id,
                "split target",
                "authorized replacement"
            );
            var operation = new CommentBodyWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );

            var plan = operation.Plan(
                new CommentBodyEditPlanRequest(path, before.Fingerprint, [command])
            );

            Assert.False(plan.CanApply);
            Assert.True(plan.ApplyBlocked);
            Assert.True(plan.Protection.BaseDocumentProtectionEnforced);
            Assert.Equal("comments", plan.Protection.BaseDocumentProtectionEditMode);
            Assert.True(plan.Protection.AuthorizationRequired);
            Assert.False(plan.Protection.HasMalformedProtectionMetadata);
            Assert.Equal(plan.PlanId, plan.ProtectionAuthorizationId);
            Assert.Equal(["protected_edit_authorization"], plan.RequiredAuthorizations);
            Assert.Contains(
                "protected_document_edit_not_authorized",
                plan.ApplyBlockedReasons
            );
            var serializedPlan = WordToolkitOperationJson.Serialize(plan);
            Assert.DoesNotContain("cryptPassword", serializedPlan, StringComparison.Ordinal);
            Assert.DoesNotContain("saltValue", serializedPlan, StringComparison.Ordinal);

            var beforeBytes = File.ReadAllBytes(path);
            var denied = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new CommentBodyEditApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    [command],
                    KeepBackup: true,
                    ProtectedEditAuthorization: "wcbplan_wrong"
                ))
            );
            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            var details = Assert.IsType<CommentBodyEditPolicyBlockDetails>(denied.Details);
            Assert.Equal(plan.PlanId, details.PlanId);
            Assert.Equal(
                ["protected_document_edit_not_authorized"],
                details.BlockCodes
            );
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));

            var applied = operation.Apply(new CommentBodyEditApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                [command],
                KeepBackup: false,
                ProtectedEditAuthorization: plan.ProtectionAuthorizationId
            ));

            Assert.True(applied.Applied);
            Assert.False(applied.NoOp);
            Assert.Equal(["protected_edit_authorization"], applied.ExplicitAuthorizations);
            Assert.NotEqual(before.Fingerprint, reader.Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedProtectionMetadataCannotBeOverriddenAndDoesNotWrite()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "malformed-protection.docx");
            CreatePackage(path, settingsXml: SettingsXml(
                "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\" w:bogus=\"x\"/>"
            ));
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var comment = new WordReviewGraphBuilder().Build(before, semantic)
                .Comments.Single(item => item.OoxmlId == "0");
            var command = new ReplaceCommentBodyTextCommand(
                comment.Id,
                "split target",
                "blocked replacement"
            );
            var operation = new CommentBodyWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var plan = operation.Plan(
                new CommentBodyEditPlanRequest(path, before.Fingerprint, [command])
            );

            Assert.True(plan.Protection.HasMalformedProtectionMetadata);
            Assert.Null(plan.ProtectionAuthorizationId);
            Assert.Empty(plan.RequiredAuthorizations);
            Assert.Contains("protection_metadata_malformed", plan.ApplyBlockedReasons);
            var beforeBytes = File.ReadAllBytes(path);

            var denied = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Apply(new CommentBodyEditApplyRequest(
                    path,
                    before.Fingerprint,
                    plan.PlanId,
                    [command],
                    ProtectedEditAuthorization: plan.PlanId
                ))
            );

            Assert.Equal("EDIT_POLICY_BLOCKED", denied.Code);
            var details = Assert.IsType<CommentBodyEditPolicyBlockDetails>(denied.Details);
            Assert.Equal(["protection_metadata_malformed"], details.BlockCodes);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProtectedNoOpDoesNotRequireAuthorizationOrWriteBackup()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "protected-no-op.docx");
            CreatePackage(path, settingsXml: SettingsXml(
                "<w:documentProtection w:edit=\"readOnly\"/>"
            ));
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var comment = new WordReviewGraphBuilder().Build(before, semantic)
                .Comments.Single(item => item.OoxmlId == "1");
            var command = new ReplaceCommentBodyTextCommand(
                comment.Id,
                "reply unchanged",
                "reply unchanged"
            );
            var operation = new CommentBodyWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            );
            var plan = operation.Plan(
                new CommentBodyEditPlanRequest(path, before.Fingerprint, [command])
            );

            Assert.False(plan.HasChanges);
            Assert.True(plan.CanApply);
            Assert.False(plan.Protection.AuthorizationRequired);
            Assert.Null(plan.ProtectionAuthorizationId);
            var beforeBytes = File.ReadAllBytes(path);

            var result = operation.Apply(new CommentBodyEditApplyRequest(
                path,
                before.Fingerprint,
                plan.PlanId,
                [command],
                KeepBackup: true
            ));

            Assert.False(result.Applied);
            Assert.True(result.NoOp);
            Assert.False(result.MutationPerformed);
            Assert.Null(result.BackupPath);
            Assert.Empty(result.ChangedEntryNames);
            Assert.Empty(result.ExplicitAuthorizations);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(
        string path,
        bool duplicateCommentId = false,
        bool trackedCommentText = false,
        string? rootCommentBodyXml = null,
        string? settingsXml = null
    )
    {
        rootCommentBodyXml ??=
            $"<w:p w14:paraId=\"C0000001\">{(trackedCommentText ? "<w:ins w:id=\"9\" w:author=\"Alice\">" : string.Empty)}"
            + "<w:r><w:t>prefix split tar</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>get suffix</w:t></w:r>"
            + $"{(trackedCommentText ? "</w:ins>" : string.Empty)}</w:p>";
        var settingsOverride = settingsXml is null
            ? string.Empty
            : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>";
        var settingsRelationship = settingsXml is null
            ? string.Empty
            : "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            $"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="bin" ContentType="application/octet-stream"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
              <Override PartName="/word/commentsExtended.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml"/>
              <Override PartName="/word/commentsIds.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml"/>
              <Override PartName="/word/commentsExtensible.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml"/>
              <Override PartName="/word/people.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml"/>
              {settingsOverride}
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml"/></Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            $"""
            <w:document xmlns:w="{Word}"><w:body><w:p><w:commentRangeStart w:id="0"/><w:r><w:t>anchor</w:t></w:r><w:commentRangeEnd w:id="0"/><w:r><w:commentReference w:id="0"/></w:r><w:r><w:commentReference w:id="1"/></w:r></w:p></w:body></w:document>
            """
        );
        WriteEntry(
            archive,
            "word/comments.xml",
            $"""
            <w:comments xmlns:w="{Word}" xmlns:w14="{Word2010}">
              <w:comment w:id="0" w:author="Alice" w:initials="A">{rootCommentBodyXml}</w:comment>
              <w:comment w:id="{(duplicateCommentId ? "0" : "1")}" w:author="Bob" w:initials="B"><w:p w14:paraId="C0000002"><w:r><w:t>reply unchanged</w:t></w:r></w:p></w:comment>
            </w:comments>
            """
        );
        WriteEntry(
            archive,
            "word/commentsExtended.xml",
            """
            <w15:commentsEx xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml"><w15:commentEx w15:paraId="C0000001" w15:done="1"/><w15:commentEx w15:paraId="C0000002" w15:paraIdParent="C0000001"/></w15:commentsEx>
            """
        );
        WriteEntry(
            archive,
            "word/commentsIds.xml",
            """
            <w16cid:commentsIds xmlns:w16cid="http://schemas.microsoft.com/office/word/2016/wordml/cid"><w16cid:commentId w16cid:paraId="C0000001" w16cid:durableId="00000001"/><w16cid:commentId w16cid:paraId="C0000002" w16cid:durableId="00000002"/></w16cid:commentsIds>
            """
        );
        WriteEntry(
            archive,
            "word/commentsExtensible.xml",
            """
            <w16cex:commentsExtensible xmlns:w16cex="http://schemas.microsoft.com/office/word/2018/wordml/cex" xmlns:w16="http://schemas.microsoft.com/office/word/2018/wordml" xmlns:or="urn:test-reactions"><w16cex:commentExtensible w16cex:durableId="00000001"><w16:extLst><w16:ext uri="reaction"><or:reactions><or:reaction/></or:reactions></w16:ext></w16:extLst></w16cex:commentExtensible><w16cex:commentExtensible w16cex:durableId="00000002"/></w16cex:commentsExtensible>
            """
        );
        WriteEntry(
            archive,
            "word/people.xml",
            """
            <w15:people xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml"><w15:person w15:author="Alice"/><w15:person w15:author="Bob"/></w15:people>
            """
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdComments" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/>
              <Relationship Id="rIdCommentsEx" Type="http://schemas.microsoft.com/office/2011/relationships/commentsExtended" Target="commentsExtended.xml"/>
              <Relationship Id="rIdCommentsIds" Type="http://schemas.microsoft.com/office/2016/09/relationships/commentsIds" Target="commentsIds.xml"/>
              <Relationship Id="rIdCommentsExtensible" Type="http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible" Target="commentsExtensible.xml"/>
              <Relationship Id="rIdPeople" Type="http://schemas.microsoft.com/office/2011/relationships/people" Target="people.xml"/>
              {settingsRelationship}
            </Relationships>
            """
        );
        if (settingsXml is not null)
        {
            WriteEntry(archive, "word/settings.xml", settingsXml);
        }
        WriteEntry(archive, "custom/opaque.bin", Encoding.UTF8.GetBytes("opaque"));
    }

    private static string SettingsXml(string body) =>
        $"<w:settings xmlns:w=\"{Word}\">{body}</w:settings>";

    private static void WriteEntry(ZipArchive archive, string name, string content) =>
        WriteEntry(archive, name, Encoding.UTF8.GetBytes(content));

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(content);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-comment-body-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
