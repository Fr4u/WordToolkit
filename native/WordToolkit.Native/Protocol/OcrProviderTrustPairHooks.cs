namespace WordToolkit.Native.Protocol;

internal sealed record OcrProviderTrustPairHooks(
    Action? AfterSecondaryPublish = null,
    Action? BeforeSecondaryPublish = null,
    Action? BeforeJournalWrite = null,
    Action? AfterLockAcquired = null
);
