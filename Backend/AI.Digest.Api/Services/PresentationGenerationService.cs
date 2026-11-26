using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
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
    private static readonly string Navy = "142B52";
    private static readonly string Blue = "2563EB";
    private static readonly string Yellow = "FFC928";
    private static readonly string White = "FFFFFF";

    public Models.PresentationDocument Validate(Models.PresentationDocument presentation)
    {
        if (presentation.Slides.Count is < 1 or > 30)
            throw new AnalysisProviderException("A presentation must contain 1 to 30 slides.");
        foreach (var slide in presentation.Slides)
        {
            if (string.IsNullOrWhiteSpace(slide.Title))
                throw new AnalysisProviderException("Every slide must have a title.");
            if (slide.Content.Count > 5)
                throw new AnalysisProviderException($"Slide '{slide.Title}' exceeds five content bullets.");
        }
        return presentation;
    }

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
                slidePart.Slide = CreateSlide(slide, index == 0, presentation.AspectRatio);
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

    private static P.Slide CreateSlide(PresentationSlide slide, bool titleSlide, string aspectRatio)
    {
        var wide = aspectRatio != "4:3";
        var shapeTree = new P.ShapeTree();
        shapeTree.Append(new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties()));
        shapeTree.Append(new GroupShapeProperties(new A.TransformGroup()));

        shapeTree.Append(CreateBackground(wide));
        shapeTree.Append(CreateTextBox(620000, 250000, 10000000, 650000, slide.Title,
            titleSlide ? 34 : 28, Navy, true));

        if (!string.IsNullOrWhiteSpace(slide.Subtitle))
        {
            shapeTree.Append(CreateTextBox(620000, 920000, 10500000, 420000, slide.Subtitle,
                16, "64748B", false));
        }

        if (titleSlide)
        {
            shapeTree.Append(CreateTextBox(620000, 2300000, 10800000, 500000,
                "Enterprise Intelligence Presentation", 18, Blue, false));
            shapeTree.Append(CreateAccentLine(620000, 3100000, 1800000));
        }
        else
        {
            var y = 1550000;
            foreach (var item in slide.Content.Take(5))
            {
                shapeTree.Append(CreateTextBox(720000, y, 670000, 500000, "", 16, Blue, true, Yellow));
                shapeTree.Append(CreateTextBox(1450000, y - 30000, 9300000, 650000, item, 16, "172B4D", false));
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
                        visual.Label, visual.Value));
                    cardX += cardWidth + 250000;
                }
            }

            if (slide.Diagram is not null && !string.IsNullOrWhiteSpace(slide.Diagram.Mermaid))
            {
                shapeTree.Append(CreateTextBox(780000, 5200000, 10400000, 650000,
                    "Diagram: " + (slide.Diagram.Type ?? "Architecture") + " — " + slide.Diagram.Mermaid.Replace("\n", " "),
                    11, "64748B", false));
            }
        }

        shapeTree.Append(CreateTextBox(10880000, 6300000, 850000, 260000,
            slide.SlideNumber.ToString(), 10, "94A3B8", false));

        return new P.Slide(new CommonSlideData(shapeTree), new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    private static P.Shape CreateBackground(bool wide)
    {
        var shape = CreateTextBox(0, 0, wide ? 12192000 : 9144000, 6858000, "", 1, White, false);
        shape.ShapeProperties.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.Background1 }));
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
        run.Append(new A.RunProperties {         Language = "en-US", FontSize = fontSize * 100, Bold = bold });        run.RunProperties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = color }));
        run.Append(new A.Text { Text = text });
        paragraph.Append(run);
        var textBody = new P.TextBody(
            new A.BodyProperties(),
            new A.ListStyle(),
            paragraph);
        shape.Append(textBody);
        return shape;
    }

    private static P.Shape CreateCard(long x, long y, long cx, long cy, string label, string value) =>
        CreateTextBox(x, y, cx, cy, $"{label}\n{value}", 14, Navy, true, "F4F7FC");

    private static P.Shape CreateAccentLine(long x, long y, long cx) =>
        CreateTextBox(x, y, cx, 70000, "", 1, Yellow, false, Yellow);

    public byte[] GeneratePdf(Models.PresentationDocument presentation)
    {
        presentation = Validate(presentation);
        QuestPDF.Settings.License = LicenseType.Community;
        var document = Document.Create(container =>
        {
            foreach (var slide in presentation.Slides)
            {
                container.Page(page =>
                {
                    var isWide = presentation.AspectRatio != "4:3";
                    page.Size(isWide ? PageSizes.A4.Landscape() : PageSizes.A4.Portrait());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontFamily("Aptos").FontColor("172B4D").FontSize(13));
                    page.Background(Colors.White);
                    page.Header().Height(54).Row(row =>
                    {
                        row.RelativeItem().Text(slide.Title).FontSize(24).SemiBold().FontColor(Navy);
                        row.ConstantItem(28).AlignRight().Text(slide.SlideNumber.ToString()).FontSize(9).FontColor(Colors.Grey.Lighten1);
                    });
                    page.Content().Column(column =>
                    {
                        if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                            column.Item().PaddingBottom(16).Text(slide.Subtitle).FontSize(12).FontColor("64748B");
                        foreach (var item in slide.Content)
                        {
                            column.Item().PaddingBottom(9).Row(row =>
                            {
                                row.ConstantItem(8).Height(8).Background(Yellow).CornerRadius(4);
                                row.RelativeItem().PaddingLeft(8).Text(item).FontSize(14);
                            });
                        }
                        if (slide.VisualElements.Count > 0)
                        {
                            column.Item().PaddingTop(18).Row(row =>
                            {
                                foreach (var visual in slide.VisualElements.Take(3))
                                {
                                    row.RelativeItem().PaddingRight(8).Background("F4F7FC").CornerRadius(8).Padding(14).Column(card =>
                                    {
                                        card.Item().Text(visual.Label).FontSize(9).FontColor("64748B");
                                        card.Item().Text(visual.Value).FontSize(16).SemiBold().FontColor(Navy);
                                    });
                                }
                            });
                        }
                        if (slide.Diagram is not null)
                            column.Item().PaddingTop(16).Border(1).BorderColor("E7EDF5").CornerRadius(8).Padding(12)
                                .Text($"{slide.Diagram.Type} diagram: {slide.Diagram.Mermaid}").FontSize(9).FontColor("64748B");
                    });
                    page.Footer().Height(20).AlignRight().Text($"AI Document Digest  •  {slide.LayoutType}")
                        .FontSize(8).FontColor("64748B");
                });
            }
        });
        return document.GeneratePdf();
    }
}
