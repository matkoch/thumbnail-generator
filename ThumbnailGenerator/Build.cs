using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
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
using static Nuke.Common.ProjectModel.ProjectModelTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;


public interface IGlobalTool
{
    string GlobalToolPackageName => Path.GetFileNameWithoutExtension(NukeBuild.BuildProjectFile);
    string GlobalToolVersion => "1.0.0";

    Target PackGlobalTool => _ => _
        .Unlisted()
        .Executes(() =>
        {
            DotNetPack(_ => _
                .SetProject(NukeBuild.BuildProjectFile)
                .SetOutputDirectory(NukeBuild.TemporaryDirectory));
        });

    Target InstallGlobalTool => _ => _
        .Unlisted()
        .DependsOn(UninstallGlobalTool)
        .DependsOn(PackGlobalTool)
        .Executes(() =>
        {
            DotNetToolInstall(_ => _
                .SetPackageName(GlobalToolPackageName)
                .EnableGlobal()
                .AddSources(NukeBuild.TemporaryDirectory)
                .SetVersion(GlobalToolVersion));
        });

    Target UninstallGlobalTool => _ => _
        .Unlisted()
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetToolUninstall(_ => _
                .SetPackageName(GlobalToolPackageName)
                .EnableGlobal());
        });
}

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild, IGlobalTool
{
    public static int Main() => Execute<Build>(x => x.GenerateThumbnail);
// nuke --image-file ~/code/blog/assets/images/2020-02-11-implementing-wiring-and-debugging-custom-msbuild-tasks/cover.jpg --title "Implementing and Debugging" "Custom MSBuild Tasks" --tags msbuild rider msbuild-log-viewer dotnet --font-size 75 --image-opacity 1 --badge-height 80
    [Parameter] readonly string[] Title;
    [Parameter] readonly string[] Tags;
    [Parameter] readonly string Author = "matthias";

    [Parameter] readonly AbsolutePath ImageFile;
    [Parameter] readonly float ImageGrayscale = 0.8f;
    [Parameter] readonly float ImageOpacity = 0.7f;
    [Parameter] readonly int FontSize = 70;
    [Parameter] readonly int TagBadgeHeight = 80;
    [Parameter] readonly int TagBadgeMargin = 40;
    [Parameter] readonly int AuthorBadgeHeight = 120;
    [Parameter] readonly int BorderPadding = 60;


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
        .Requires(() => Title)
        .Requires(() => ImageFile)
        .Executes(() =>
        {
            const int width = 1200;
            const int height = 675;
            var image = new Image<Rgba64>(width: width, height: height);

            var backgroundImage = Image.Load(ImageFile);
            var (widthScale, heightScale) = (1f * width / backgroundImage.Width, 1f * height / backgroundImage.Height);
            var scale = backgroundImage.Height * widthScale > height ? widthScale : heightScale;
            backgroundImage.Mutate(x => x
                .Resize((int) (backgroundImage.Width * scale), (int) (backgroundImage.Height * scale))
                .Grayscale(ImageGrayscale)
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
                .DrawImage(backgroundImage, Point.Empty, ImageOpacity)
                // .Fill(linearGradientBrush, new Rectangle(0, 0, image.Width, image.Height))
                .DrawText(
                    new TextGraphicsOptions
                    {
                        TextOptions = new TextOptions
                        {
                            WrapTextWidth = image.Width - 2 * BorderPadding
                        }
                    },
                    text: Title.JoinNewLine(),
                    font: robotoFont.CreateFont(FontSize),
                    brush: Brushes.Solid(Color.WhiteSmoke),
                    pen: Pens.Solid(Color.Black, 1.2f),
                    location: new PointF(BorderPadding, image.Height * 0.55f)));

            var right = image.Width - BorderPadding;
            foreach (var tagImage in TagBadgeImages.Reverse())
            {
                right -= tagImage.Width;
                image.Mutate(x => x.DrawImage(tagImage, new Point(right, BorderPadding), 1f));
                right -= TagBadgeMargin;
            }

            image.Mutate(x => x
                .DrawImage(
                    image: GetBadgeImage(BuildProjectDirectory / "authors" / $"{Author}.png", AuthorBadgeHeight),
                    location: new Point(60, 60), 1f));

            using var fileStream = new FileStream(ImageFile.Parent / $"thumbnail.jpeg", FileMode.Create);
            image.SaveAsJpeg(fileStream);
        });

    IEnumerable<Image> TagBadgeImages => Tags
        .Select(x => BuildProjectDirectory / "icons" / $"{x}.png")
        .Select(imageFile => GetBadgeImage(imageFile, TagBadgeHeight));

    Image GetBadgeImage(AbsolutePath imageFile, int height)
    {
        var image = Image.Load(imageFile);
        var scale = 1f * height / image.Height;
        image.Mutate(x => x.Resize((int) (image.Width * scale), (int) (image.Height * scale)));
        return image;
    }
}
