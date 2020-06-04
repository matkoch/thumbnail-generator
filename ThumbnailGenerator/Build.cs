using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.HttpTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Logger;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.GenerateThumbnail);

    [Parameter] readonly AbsolutePath BackgroundImageFile;
    [Parameter] readonly string Title = "Reusable Build Components with\nDefault Interface Implementations";
    [Parameter] readonly string[] Tags = {"csharp", "nuke"};

    string[] FontDownloadUrls =>
        new[]
        {
            "https://github.com/googlefonts/roboto/releases/latest/download/roboto-unhinted.zip",
            "https://github.com/JetBrains/JetBrainsMono/releases/download/v1.0.6/JetBrainsMono-1.0.6.zip"
        };

    AbsolutePath FontDirectory => TemporaryDirectory / "fonts";
    IReadOnlyCollection<AbsolutePath> FontArchives => FontDirectory.GlobFiles("*.*");

    Target DownloadFonts => _ => _
        .OnlyWhenDynamic(() => FontDownloadUrls.Length != FontArchives.Count)
        .Executes(() =>
        {
            FontDownloadUrls.ForEach(x => HttpDownloadFile(x, FontDirectory / new Uri(x).Segments.Last()));
            FontArchives.ForEach(x => Uncompress(x, FontDirectory / Path.GetFileNameWithoutExtension(x)));
        });

    readonly FontCollection FontCollection = new FontCollection();
    IReadOnlyCollection<AbsolutePath> FontFiles => FontDirectory.GlobFiles("**/[!\\.]*.ttf");

    Target InstallFonts => _ => _
        .DependsOn(DownloadFonts)
        .Executes(() =>
        {
            FontFiles.ForEach(x => FontCollection.Install(x));
            FontCollection.Families.ForEach(x => Normal($"Installed font {x.Name.SingleQuote()}"));
        });

    Target GenerateThumbnail => _ => _
        .DependsOn(InstallFonts)
        .Executes(() =>
        {
            const int width = 1200;
            const int height = 675;
            var image = new Image<Rgba64>(width: width, height: height);

            var backgroundImage = Image.Load(BackgroundImageFile);
            var (widthScale, heightScale) = (1f * width / backgroundImage.Width, 1f * height / backgroundImage.Height);
            var scale = backgroundImage.Height * widthScale > height ? widthScale : heightScale;
            backgroundImage.Mutate(x => x
                .Resize((int)(backgroundImage.Width * scale), (int)(backgroundImage.Height * scale))
                .Grayscale(0.8f)
                .Vignette(image.Width * 0.8f, image.Height * 0.7f)
            );

            var linearGradientBrush = new LinearGradientBrush(
                PointF.Empty,
                new PointF(image.Width, image.Height),
                GradientRepetitionMode.Repeat,
                new ColorStop(0, Color.DarkSlateGray.WithAlpha(0.1f)),
                new ColorStop(1, Color.Teal.WithAlpha(0.1f)));

            var robotoFont = FontCollection.Families.Single(x => x.Name == "Roboto Black");

            image.Mutate(x => x
                .BackgroundColor(Color.Black)
                .DrawImage(backgroundImage, Point.Empty, 0.7f)
                // .Fill(linearGradientBrush, new Rectangle(0, 0, image.Width, image.Height))
                .DrawText(
                    new TextGraphicsOptions {TextOptions = new TextOptions { WrapTextWidth = image.Width - 80}},
                    text: Title,
                    font:robotoFont.CreateFont(70),
                    brush: Brushes.Solid(Color.WhiteSmoke), pen: Pens.Solid(Color.Black, 1.2f),
                    location:new PointF(60, image.Height * 0.60f)));

            var right = image.Width - 60;
            foreach (var tagImageName in Tags)
            {
                var tagImage = Image.Load(BackgroundImageFile.Parent / $"{tagImageName}.png");
                var tagScale = 1f * 100 / tagImage.Height;
                tagImage.Mutate(x => x.Resize((int)(tagImage.Width * tagScale), (int)(tagImage.Height * tagScale)));
                right -= tagImage.Width;
                image.Mutate(x => x.DrawImage(tagImage, new Point(right, 60), 1f));
                right -= 40;
            }

            var meImage = Image.Load(BackgroundImageFile.Parent / "me.png");
            var meScale = 1f * 100 / meImage.Height;
            meImage.Mutate(x => x.Resize((int)(meImage.Width * meScale), (int)(meImage.Height * meScale)));
            image.Mutate(x => x.DrawImage(meImage, new Point(60, 60), 1f));

            using var fileStream = new FileStream(BackgroundImageFile.Parent / $"thumbnail.jpeg", FileMode.Create);
            image.SaveAsJpeg(fileStream);
        });
}
