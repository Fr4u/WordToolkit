using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class BatchComplexityTests
{
    [Fact]
    public void StyledEquationCostEstimateGrowsLinearly()
    {
        var eightEquations = WordLiveService.BatchComplexity.FromCounts(
            operationCount: 49,
            equationCount: 8,
            styledEquationCount: 8,
            textCharacters: 12_000,
            formattedRunCount: 20
        );
        var sixteenEquations = WordLiveService.BatchComplexity.FromCounts(
            operationCount: 57,
            equationCount: 16,
            styledEquationCount: 16,
            textCharacters: 12_000,
            formattedRunCount: 20
        );

        Assert.Equal(93, eightEquations.EstimatedStagingContentComCalls);
        Assert.Equal(125, sixteenEquations.EstimatedStagingContentComCalls);
        Assert.Equal(2, eightEquations.BatchBoundaryEquationCountReads);
        Assert.Equal(
            32,
            sixteenEquations.EstimatedStagingContentComCalls
                - eightEquations.EstimatedStagingContentComCalls
        );
    }
}
