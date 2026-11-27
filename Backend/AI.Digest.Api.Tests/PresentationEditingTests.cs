using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Xunit;

namespace AI.Digest.Api.Tests;

public sealed class PresentationEditingTests
{
    [Fact]
    public void PresentationHistoryKeepsOriginalAndCanRestoreIt()
    {
        var store = new PresentationHistoryStore();
        var id = Guid.NewGuid();
        var original = CreatePresentation(id, "Original");
        store.Save(original);

        var edited = CreatePresentation(id, "Edited");
        store.Save(edited);

        var version = Assert.Single(store.Versions(id));
        Assert.Equal("Original AI draft", version.Label);
        Assert.Equal("Original", store.GetVersion(id, version.Id).Slides[0].Title);

        var restored = store.RestoreVersion(id, version.Id);

        Assert.Equal("Original", restored.Slides[0].Title);
        Assert.Equal("Original", store.Get(id).Slides[0].Title);
        Assert.Equal("Edited", store.GetVersion(id, Assert.Single(store.Versions(id).Where(item =>
            item.Label == "Saved revision")).Id).Slides[0].Title);
    }

    [Fact]
    public void PresentationStyleValidationRejectsUnsafeColorsAndInvalidScales()
    {
        var generator = new PresentationGenerationService();
        var presentation = CreatePresentation(Guid.NewGuid(), "Styled");
        var invalidSlide = new PresentationSlide
        {
            SectionId = presentation.Slides[0].SectionId,
            SlideNumber = 1,
            Title = "Unsafe",
            BackgroundColor = "red; background-image:url(javascript:alert(1))"
        };
        var invalid = new PresentationDocument
        {
            Id = presentation.Id,
            Slides = [invalidSlide]
        };

        Assert.Throws<AnalysisProviderException>(() => generator.Validate(invalid));
    }

    [Fact]
    public void PresentationValidationRejectsTablesWithMismatchedRows()
    {
        var invalid = new PresentationDocument
        {
            Id = Guid.NewGuid(),
            Title = "Table",
            Slides =
            [
                new PresentationSlide
                {
                    SectionId = "section-1",
                    SlideNumber = 1,
                    Title = "Invalid table",
                    Table = new PresentationTable
                    {
                        Headers = ["Phase", "Owner"],
                        Rows = [["Plan"]]
                    }
                }
            ]
        };

        Assert.Throws<AnalysisProviderException>(() => new PresentationGenerationService().Validate(invalid));
    }

    private static PresentationDocument CreatePresentation(Guid id, string title) => new()
    {
        Id = id,
        Title = title,
        Slides =
        [
            new PresentationSlide
            {
                SectionId = "section-1",
                SlideNumber = 1,
                Title = title
            }
        ]
    };
}
