using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using AI.Digest.Api.Models;
using OpenXmlPresentationDocument = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace AI.Digest.Api.Services;

public sealed class PresentationGenerationService
{
    private sealed record ThemePalette(string Background, string Text, string Muted, string Accent, string Card);
    private static readonly HashSet<string> SupportedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aptos", "Arial", "Calibri", "Georgia", "Inter", "Noto Sans Tamil", "Segoe UI", "Verdana"
    };

    public Models.PresentationDocument Validate(Models.PresentationDocument presentation)
    {
        if (presentation.Slides.Count is < 1 or > 30)
            throw new AnalysisProviderException("A presentation must contain 1 to 30 slides.");
        var sectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slide in presentation.Slides)
        {
            if (string.IsNullOrWhiteSpace(slide.SectionId) ||
                !sectionIds.Add(slide.SectionId) ||
                string.IsNullOrWhiteSpace(slide.Title))
                throw new AnalysisProviderException("Every slide must have a title.");
            if (slide.Content.Count > 5)
                throw new AnalysisProviderException($"Slide '{slide.Title}' exceeds five content bullets.");
            if (!SupportedFonts.Contains(slide.FontFamily) ||
                !IsSafeColor(slide.BackgroundColor) ||
                !IsSafeColor(slide.TextColor) ||
                !IsSafeColor(slide.AccentColor) ||
                slide.ContentScale is < 0.75 or > 1.5 ||
                slide.ContentAlignment is not ("Left" or "Center" or "Right") ||
                slide.ContentSpacing is < 4 or > 40)
                throw new AnalysisProviderException(
                    $"Slide '{slide.Title}' contains unsupported design settings.",
                    failureCode: AnalysisFailureCode.InvalidInput);
            if (slide.ImageDataUrl is { Length: > 1_500_000 } ||
                (slide.ImageDataUrl is not null &&
                 !Regex.IsMatch(slide.ImageDataUrl, @"^data:image/(png|jpeg|webp|gif);base64,[A-Za-z0-9+/]+=*$")))
                throw new AnalysisProviderException(
                    $"Slide '{slide.Title}' contains an unsupported or oversized image.",
                    failureCode: AnalysisFailureCode.InvalidInput);
            if (slide.Table is not null &&
                (slide.Table.Headers is null ||
                 slide.Table.Rows is null ||
                 slide.Table.Headers.Count is < 1 or > 8 ||
                 slide.Table.Rows.Count > 20 ||
                 slide.Table.Headers.Any(header => header is null || header.Length > 120) ||
                 slide.Table.Rows.Any(row => row is null ||
                     row.Count != slide.Table.Headers.Count ||
                     row.Any(cell => cell is null || cell.Length > 300))))
                throw new AnalysisProviderException(
                    $"Slide '{slide.Title}' contains invalid table data.",
                    failureCode: AnalysisFailureCode.InvalidInput);
        }
        return presentation;
    }

    private static bool IsSafeColor(string color) =>
        string.IsNullOrEmpty(color) || Regex.IsMatch(color, @"^#[0-9a-fA-F]{6}$");

    public byte[] GeneratePptx(Models.PresentationDocument presentation)
    {
        presentation = Validate(presentation);
        using var stream = new MemoryStream();
        using (var package = OpenXmlPresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = package.PresentationPart ?? package.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();

            var slideMasterIdList = new SlideMasterIdList();
            var slideMasterId = new SlideMasterId { Id = 2147483648, RelationshipId = "rId1" };
            slideMasterIdList.Append(slideMasterId);
            presentationPart.Presentation.Append(slideMasterIdList);

            var slideIdList = new SlideIdList();
            presentationPart.Presentation.Append(slideIdList);

            var slideSize = presentation.AspectRatio == "4:3"
                ? new SlideSize { Cx = 9144000, Cy = 6858000, Type = SlideSizeValues.Screen4x3 }
                : new SlideSize { Cx = 12192000, Cy = 6858000, Type = SlideSizeValues.Screen16x9 };
            presentationPart.Presentation.Append(slideSize);
            presentationPart.Presentation.Append(new NotesSize { Cx = 6858000, Cy = 9144000 });

            var masterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
            masterPart.SlideMaster = new P.SlideMaster(
                new CommonSlideData(new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()))),
                new P.ColorMapOverride(new A.MasterColorMapping()));

            var layoutPart = masterPart.AddNewPart<SlideLayoutPart>("rId1");
            layoutPart.SlideLayout = new P.SlideLayout(
                new CommonSlideData(new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()))),
                new P.ColorMapOverride(new A.MasterColorMapping()));
            masterPart.AddPart(layoutPart);

            for (var index = 0; index < presentation.Slides.Count; index++)
            {
                var slide = presentation.Slides[index];
                var slidePart = presentationPart.AddNewPart<SlidePart>("rId" + (index + 2));
                slidePart.Slide = CreateSlide(
                    slide,
                    index == 0,
                    presentation.AspectRatio,
                    GetPalette(presentation.Theme));
                slidePart.AddPart(layoutPart);

                slideIdList.Append(new SlideId
                {
                    Id = (uint)(256 + index),
                    RelationshipId = "rId" + (index + 2)
                });
            }

            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static P.Slide CreateSlide(
        PresentationSlide slide,
        bool titleSlide,
        string aspectRatio,
        ThemePalette palette)
    {
        var wide = aspectRatio != "4:3";
        var shapeTree = new P.ShapeTree();
        shapeTree.Append(new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties()));
        shapeTree.Append(new GroupShapeProperties(new A.TransformGroup()));

        shapeTree.Append(CreateBackground(wide, palette.Background));
        shapeTree.Append(CreateTextBox(620000, 250000, 10000000, 650000, slide.Title,
            titleSlide ? 34 : 28, palette.Text, true));

        if (!string.IsNullOrWhiteSpace(slide.Subtitle))
        {
            shapeTree.Append(CreateTextBox(620000, 920000, 10500000, 420000, slide.Subtitle,
                16, palette.Muted, false));
        }

        if (titleSlide)
        {
            var y = 2300000;
            foreach (var item in slide.Content.Take(3))
            {
                shapeTree.Append(CreateTextBox(720000, y, 670000, 500000, "", 16, palette.Accent, true, palette.Accent));
                shapeTree.Append(CreateTextBox(1450000, y - 30000, 9300000, 650000, item, 16, palette.Text, false));
                y += 780000;
            }
            shapeTree.Append(CreateAccentLine(620000, y + 100000, 1800000, palette.Accent));
        }
        else
        {
            var y = 1550000;
            foreach (var item in slide.Content.Take(5))
            {
                shapeTree.Append(CreateTextBox(720000, y, 670000, 500000, "", 16, palette.Accent, true, palette.Accent));
                shapeTree.Append(CreateTextBox(1450000, y - 30000, 9300000, 650000, item, 16, palette.Text, false));
                y += 780000;
            }

            if (slide.VisualElements.Count > 0)
            {
                var cardY = y + 150000;
                var cardWidth = 2600000;
                var cardX = 700000;
                foreach (var visual in slide.VisualElements.Take(3))
                {
                    shapeTree.Append(CreateCard(cardX, cardY, cardWidth, 1100000,
                        visual.Label, visual.Value, palette));
                    cardX += cardWidth + 250000;
                }
            }

            var diagram = slide.Diagram;
            if (diagram is not null && !string.IsNullOrWhiteSpace(diagram.Mermaid))
            {
                shapeTree.Append(CreateTextBox(780000, 5200000, 10400000, 650000,
                    "Diagram: " + (diagram.Type ?? "Architecture") + " — " + diagram.Mermaid.Replace("\n", " "),
                    11, palette.Muted, false));
            }
        }

        shapeTree.Append(CreateTextBox(10880000, 6300000, 850000, 260000,
            slide.SlideNumber.ToString(), 10, palette.Muted, false));

        return new P.Slide(new CommonSlideData(shapeTree), new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    private static P.Shape CreateBackground(bool wide, string color)
    {
        var shape = CreateTextBox(0, 0, wide ? 12192000 : 9144000, 6858000, "", 1, color, false, color);
        return shape;
    }

    private static P.Shape CreateTextBox(long x, long y, long cx, long cy, string text,
        int fontSize, string color, bool bold, string? fill = null)
    {
        var shape = new P.Shape();
        shape.Append(new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = (uint)Random.Shared.Next(100, 100000), Name = "TextBox" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties()));

        var properties = new P.ShapeProperties
        {
            Transform2D = new A.Transform2D
            {
                Offset = new A.Offset { X = x, Y = y },
                Extents = new A.Extents { Cx = cx, Cy = cy }
            }
        };
        if (!string.IsNullOrWhiteSpace(fill))
        {
            properties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = fill }));
        }
        shape.Append(properties);

        var paragraph = new A.Paragraph();
        var run = new A.Run();
        var runProperties = new A.RunProperties { Language = "en-US", FontSize = fontSize * 100, Bold = bold };
        runProperties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = color }));
        run.Append(runProperties);
        run.Append(new A.Text { Text = text });
        paragraph.Append(run);
        var textBody = new P.TextBody(
            new A.BodyProperties(),
            new A.ListStyle(),
            paragraph);
        shape.Append(textBody);
        return shape;
    }

    private static P.Shape CreateCard(
        long x,
        long y,
        long cx,
        long cy,
        string label,
        string value,
        ThemePalette palette) =>
        CreateTextBox(x, y, cx, cy, $"{label}\n{value}", 14, palette.Text, true, palette.Card);

    private static P.Shape CreateAccentLine(long x, long y, long cx, string color) =>
        CreateTextBox(x, y, cx, 70000, "", 1, color, false, color);

    public byte[] GeneratePdf(Models.PresentationDocument presentation)
    {
        presentation = Validate(presentation);
        QuestPDF.Settings.License = LicenseType.Community;
        var palette = GetPalette(presentation.Theme);
        var document = Document.Create(container =>
        {
            foreach (var slide in presentation.Slides)
            {
                container.Page(page =>
                {
                    var isWide = presentation.AspectRatio != "4:3";
                    page.Size(isWide ? PageSizes.A4.Landscape() : PageSizes.A4.Portrait());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontFamily("Aptos").FontColor(palette.Text).FontSize(13));
                    page.PageColor(palette.Background);
                    page.Header().Height(54).Row(row =>
                    {
                        row.RelativeItem().Text(slide.Title).FontSize(24).SemiBold().FontColor(palette.Text);
                        row.ConstantItem(28).AlignRight().Text(slide.SlideNumber.ToString()).FontSize(9).FontColor(Colors.Grey.Lighten1);
                    });
                    page.Content().Column(column =>
                    {
                        if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                            column.Item().PaddingBottom(16).Text(slide.Subtitle).FontSize(12).FontColor(palette.Muted);
                        foreach (var item in slide.Content)
                        {
                            column.Item().PaddingBottom(9).Row(row =>
                            {
                                row.ConstantItem(8).Height(8).Background(palette.Accent).CornerRadius(4);
                                row.RelativeItem().PaddingLeft(8).Text(item).FontSize(14);
                            });
                        }
                        if (slide.VisualElements.Count > 0)
                        {
                            column.Item().PaddingTop(18).Row(row =>
                            {
                                foreach (var visual in slide.VisualElements.Take(3))
                                {
                                    row.RelativeItem().PaddingRight(8).Background(palette.Card).CornerRadius(8).Padding(14).Column(card =>
                                    {
                                        card.Item().Text(visual.Label).FontSize(9).FontColor(palette.Muted);
                                        card.Item().Text(visual.Value).FontSize(16).SemiBold().FontColor(palette.Text);
                                    });
                                }
                            });
                        }
                        var diagram = slide.Diagram;
                        if (diagram is not null)
                            column.Item().PaddingTop(16).Border(1).BorderColor("E7EDF5").CornerRadius(8).Padding(12)
                                .Text($"{diagram.Type} diagram: {diagram.Mermaid}").FontSize(9).FontColor(palette.Muted);
                    });
                    page.Footer().Height(20).AlignRight().Text($"AI Document Digest  •  {slide.LayoutType}")
                        .FontSize(8).FontColor("64748B");
                });
            }
        });
        return document.GeneratePdf();
    }

    private static ThemePalette GetPalette(string? theme)
    {
        var value = (theme ?? "").ToLowerInvariant();
        if (value.Contains("prism"))
            return new("F3F7FD", "10234D", "64748B", "00CBE8", "FFFFFF");
        if (value.Contains("neural") || value.Contains("dark"))
            return new("080D1B", "F4F8FF", "C1D0E7", "24D8F2", "111D38");
        if (value.Contains("cinematic"))
            return new("17171D", "FFFAF3", "D0C7BC", "E2C47D", "302D34");
        if (value.Contains("gold"))
            return new("071326", "FFF9EB", "D1C9B8", "E2C47D", "182944");
        if (value.Contains("blueprint") || value.Contains("technology"))
            return new("082243", "EEF8FF", "BDD5E8", "70C4FF", "0B2C52");
        if (value.Contains("universe"))
            return new("100D25", "F7F3FF", "D2C8E8", "BCA3FF", "231943");
        if (value.Contains("storytelling"))
            return new("FBF4E9", "2B2B30", "655A51", "9C5D36", "FFFFFF");
        if (value.Contains("light") || value.Contains("minimal"))
            return new("FFFFFF", "142B52", "64748B", "2563EB", "F4F7FC");
        return new("F3F7FD", "10234D", "64748B", "00CBE8", "FFFFFF");
    }
}
