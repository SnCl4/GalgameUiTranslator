using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GalgameUiTranslator;

namespace SmokeTests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var root = Path.Combine(Path.GetTempPath(), "galgame-ui-translator-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestLocalOcrConfiguration();
                TestRendering(root);
                TestTransparentClear();
                TestRepairMask(root);
                TestAdvancedTextRendering();
                TestDdsRoundTrip(root);
                TestAtlasMetadata(root);
                TestBatchTaskCenter();
                TestBatchQueuePersistence(root);
                TestImageBrowserSupport(root);
                TestTranslationResources(root);
                TestTextStylePresets(root);
                TestProjectRoundTrip(root);
                TestWorkspaceLayout();
                TestImageComparison();
                TestNavigationRepaint();
                TestPreflight(root);
                TestHistoryAndRecovery(root);
                Console.WriteLine("PASS: local OCR, masks, advanced text, style presets, DDS, atlas, thumbnails, status filters, resumable batch queue, translation memory, comparison, preflight, recovery and UI layout");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception);
                return 1;
            }
        }

        private static void TestLocalOcrConfiguration()
        {
            var settings = new AppSettings();
            Assert(settings.RecognitionMode == RecognitionModes.Local,
                "local OCR is not the default recognition mode");
            Assert(RecognitionModes.Normalize("unknown") == RecognitionModes.Local,
                "unknown recognition modes do not fall back to local OCR");
            Assert(RecognitionModes.UsesLocal(RecognitionModes.LocalThenCloud) &&
                   RecognitionModes.UsesCloud(RecognitionModes.LocalThenCloud),
                "local-then-cloud mode does not enable both providers");
            Assert(LocalOcrService.ContainsJapaneseText("設定・ロード"),
                "Japanese OCR text filter rejected valid text");
            Assert(!LocalOcrService.ContainsJapaneseText("LOAD 123"),
                "Japanese OCR text filter accepted Latin-only text");

            using (var ocr = new LocalOcrService())
            {
                Assert(ocr.IsAvailable,
                    "local OCR models are missing from the build output: " +
                    string.Join(", ", ocr.GetMissingModelFiles().Select(Path.GetFileName)));
            }
        }

        private static void TestRendering(string root)
        {
            using (var source = new Bitmap(320, 160, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(source))
                using (var background = new LinearGradientBrush(
                           new Rectangle(0, 0, source.Width, source.Height),
                           Color.FromArgb(230, 35, 55, 90),
                           Color.FromArgb(230, 85, 45, 80),
                           0f))
                using (var font = new Font("Microsoft YaHei", 25, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    graphics.FillRectangle(background, 0, 0, source.Width, source.Height);
                    graphics.DrawString("設定・ロード", font, Brushes.White, 53, 58);
                }

                var before = source.GetPixel(8, 8);
                var document = new ImageDocument { Width = source.Width, Height = source.Height };
                document.Regions.Add(new TextRegion
                {
                    X = 45,
                    Y = 48,
                    Width = 235,
                    Height = 55,
                    SourceText = "設定・ロード",
                    Translation = "设置 / 读取存档",
                    FontFamily = "Microsoft YaHei",
                    FontSize = 25,
                    Bold = true,
                    BackgroundMode = "Gradient",
                    HorizontalAlignment = "Center",
                    VerticalAlignment = "Center"
                });

                using (var rendered = ImageProcessor.RenderPreview(source, document))
                {
                    Assert(rendered.Width == 320 && rendered.Height == 160, "render changed image dimensions");
                    Assert(rendered.GetPixel(8, 8).ToArgb() == before.ToArgb(), "render changed pixels outside the region");
                    var output = Path.Combine(root, "rendered.png");
                    rendered.Save(output, ImageFormat.Png);
                    using (var reopened = Image.FromFile(output))
                        Assert(reopened.Width == 320 && reopened.Height == 160, "saved PNG dimensions are invalid");
                }
            }
        }

        private static void TestTransparentClear()
        {
            using (var source = new Bitmap(80, 40, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.FromArgb(255, 20, 30, 40));
                    graphics.FillRectangle(Brushes.White, 20, 10, 40, 20);
                }

                var document = new ImageDocument { Width = 80, Height = 40 };
                document.Regions.Add(new TextRegion
                {
                    X = 20,
                    Y = 10,
                    Width = 40,
                    Height = 20,
                    Translation = string.Empty,
                    BackgroundMode = "Transparent",
                    ClearPadding = 0
                });
                using (var rendered = ImageProcessor.RenderPreview(source, document))
                    Assert(rendered.GetPixel(40, 20).A == 0, "transparent clear did not preserve alpha semantics");
            }
        }

        private static void TestRepairMask(string root)
        {
            using (var source = new Bitmap(64, 32, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.FromArgb(255, 24, 60, 110));
                    graphics.FillRectangle(Brushes.White, 18, 14, 30, 4);
                }

                var region = new TextRegion
                {
                    X = 18,
                    Y = 10,
                    Width = 30,
                    Height = 12,
                    Translation = string.Empty,
                    BackgroundMode = "ContentAware",
                    ClearPadding = 0
                };
                region.RepairMaskStrokes.Add(new RepairMaskStroke
                {
                    Diameter = 9,
                    Points = new System.Collections.Generic.List<MaskPoint>
                    {
                        new MaskPoint(22, 16),
                        new MaskPoint(44, 16)
                    }
                });
                var eraser = new RepairMaskStroke
                {
                    Eraser = true,
                    Diameter = 3,
                    Points = new System.Collections.Generic.List<MaskPoint> { new MaskPoint(33, 16) }
                };
                region.RepairMaskStrokes.Add(eraser);

                var mask = RepairMaskService.BuildMask(source.Width, source.Height, region);
                Assert(mask[16 * source.Width + 22], "mask brush did not paint expected pixels");
                Assert(!mask[16 * source.Width + 33], "mask eraser did not remove expected pixels");

                var document = new ImageDocument { Width = source.Width, Height = source.Height };
                document.Regions.Add(region);
                using (var rendered = ImageProcessor.RenderPreview(source, document))
                {
                    Assert(rendered.GetPixel(22, 16).R < 240, "content-aware repair left the masked text unchanged");
                    Assert(rendered.GetPixel(33, 16).ToArgb() == Color.White.ToArgb(),
                        "content-aware repair changed pixels outside the custom mask");
                }

                var project = new TranslationProject { SourceFolder = root };
                project.Images.Add(document);
                var restored = ProjectService.DeserializeProject(ProjectService.SerializeProject(project));
                Assert(restored.Images[0].Regions[0].RepairMaskStrokes.Count == 2,
                    "repair mask strokes were not preserved by project serialization");
            }
        }

        private static void TestAdvancedTextRendering()
        {
            using (var source = new Bitmap(260, 180, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(255, 20, 30, 45));
                var document = new ImageDocument { Width = source.Width, Height = source.Height };
                var region = new TextRegion
                {
                    X = 40,
                    Y = 20,
                    Width = 180,
                    Height = 140,
                    Translation = "高级\n样式",
                    FontFamily = "Microsoft YaHei",
                    FontSize = 34,
                    LetterSpacing = 3,
                    LineSpacing = 1.2f,
                    VerticalText = true,
                    RotationDegrees = 8,
                    ShadowEnabled = true,
                    GlowWidth = 2,
                    TextFillMode = "VerticalGradient",
                    BackgroundMode = "Keep"
                };
                document.Regions.Add(region);
                Assert(ImageProcessor.CheckTextFits(region, out var fitted) && fitted >= 7,
                    "advanced text layout did not produce a valid fitted size");
                using (var rendered = ImageProcessor.RenderPreview(source, document))
                {
                    var changed = 0;
                    for (var y = 0; y < rendered.Height; y += 4)
                    for (var x = 0; x < rendered.Width; x += 4)
                        if (rendered.GetPixel(x, y).ToArgb() != source.GetPixel(x, y).ToArgb()) changed++;
                    Assert(changed > 0, "advanced text renderer produced no visible output");
                }
            }
        }

        private static void TestDdsRoundTrip(string root)
        {
            var sourcePath = Path.Combine(root, "texture.dds");
            var outputPath = Path.Combine(root, "texture-output.dds");
            using (var bitmap = new Bitmap(32, 16, PixelFormat.Format32bppArgb))
            {
                for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    bitmap.SetPixel(x, y, Color.FromArgb(120 + x * 4, x * 7 % 255, y * 13 % 255, 180));
                DdsCodec.Save(bitmap, sourcePath, new DdsInfo
                {
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    Format = DdsFormat.Bc3,
                    MipMapCount = 5,
                    HasAlpha = true
                });
            }

            var sourceInfo = DdsCodec.ReadInfo(sourcePath);
            Assert(sourceInfo.Width == 32 && sourceInfo.Height == 16, "DDS dimensions changed after encoding");
            Assert(sourceInfo.Format == DdsFormat.Bc3 && sourceInfo.MipMapCount == 5,
                "DDS compression or mipmap count changed after encoding");
            using (var decoded = DdsCodec.Load(sourcePath))
                Assert(decoded.Width == 32 && decoded.Height == 16, "DDS decoder returned invalid dimensions");

            var document = new ImageDocument { RelativePath = "texture.dds", Width = 32, Height = 16 };
            document.Regions.Add(new TextRegion
            {
                X = 2,
                Y = 2,
                Width = 20,
                Height = 10,
                Translation = "UI",
                FontSize = 8,
                BackgroundMode = "Keep"
            });
            ImageProcessor.ExportDocument(sourcePath, outputPath, document);
            var outputInfo = DdsCodec.ReadInfo(outputPath);
            Assert(outputInfo.Format == sourceInfo.Format && outputInfo.MipMapCount == sourceInfo.MipMapCount,
                "DDS export did not preserve compression and mipmap settings");
            Assert(string.IsNullOrEmpty(PreflightService.ValidateExportedFile(sourcePath, outputPath, document)),
                "DDS export validation failed");

            foreach (var format in new[] { DdsFormat.Bc1, DdsFormat.Bc2, DdsFormat.Bgra32 })
            {
                var formatPath = Path.Combine(root, "roundtrip-" + format + ".dds");
                using (var bitmap = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
                {
                    for (var y = 0; y < bitmap.Height; y++)
                    for (var x = 0; x < bitmap.Width; x++)
                        bitmap.SetPixel(x, y, Color.FromArgb(180, x * 30, y * 30, 90));
                    DdsCodec.Save(bitmap, formatPath, new DdsInfo
                    {
                        Width = 8,
                        Height = 8,
                        Format = format,
                        MipMapCount = 1,
                        HasAlpha = true,
                        RedMask = format == DdsFormat.Bgra32 ? 0x00FF0000u : 0,
                        GreenMask = format == DdsFormat.Bgra32 ? 0x0000FF00u : 0,
                        BlueMask = format == DdsFormat.Bgra32 ? 0x000000FFu : 0,
                        AlphaMask = format == DdsFormat.Bgra32 ? 0xFF000000u : 0
                    });
                }
                Assert(DdsCodec.ReadInfo(formatPath).Format == format,
                    "DDS format round-trip failed for " + format);
                using (var decoded = DdsCodec.Load(formatPath))
                    Assert(decoded.Width == 8 && decoded.Height == 8,
                        "DDS decoder failed for " + format);
            }
        }

        private static void TestAtlasMetadata(string root)
        {
            var folder = Path.Combine(root, "atlas-case");
            Directory.CreateDirectory(folder);
            var imagePath = Path.Combine(folder, "sheet.png");
            using (var bitmap = new Bitmap(128, 64, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.DarkSlateBlue);
                bitmap.Save(imagePath, ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(folder, "sheet.json"),
                "{\"frames\":{" +
                "\"button.png\":{\"frame\":{\"x\":4,\"y\":6,\"w\":40,\"h\":18},\"rotated\":false}," +
                "\"icon.png\":{\"frame\":{\"x\":60,\"y\":8,\"w\":20,\"h\":24},\"rotated\":true}" +
                "},\"meta\":{\"image\":\"sheet.png\"}}");
            using (var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
                bitmap.Save(Path.Combine(folder, "spine.png"), ImageFormat.Png);
            File.WriteAllText(Path.Combine(folder, "spine.atlas"),
                "spine.png\nsize: 64,64\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\n\n" +
                "panel\n  rotate: false\n  xy: 7, 9\n  size: 24, 18\n");

            var project = ProjectService.CreateFromFolder(folder);
            var document = project.Images.Single(image => image.RelativePath.EndsWith("sheet.png"));
            Assert(document.AtlasSprites.Count == 2, "TexturePacker atlas metadata was not attached");
            Assert(document.AtlasSprites.Any(sprite => sprite.Name == "icon.png" && sprite.Rotated),
                "atlas sprite name or rotation was lost");
            Assert(document.AtlasMetadataPath.EndsWith("sheet.json"), "atlas metadata path was not recorded");
            var spine = project.Images.Single(image => image.RelativePath.EndsWith("spine.png"));
            Assert(spine.AtlasSprites.Count == 1 && spine.AtlasSprites[0].Name == "panel",
                "Spine atlas metadata was not attached");
            var restored = ProjectService.DeserializeProject(ProjectService.SerializeProject(project));
            Assert(restored.Images.Sum(image => image.AtlasSprites.Count) == 3,
                "atlas metadata was not preserved in the project");
        }

        private static void TestBatchTaskCenter()
        {
            var center = new BatchTaskCenter();
            var first = new BatchTaskItem { Kind = BatchTaskKind.Export, Target = "first.png" };
            var retry = new BatchTaskItem { Kind = BatchTaskKind.Export, Target = "retry.png" };
            var attempts = 0;
            center.RunAsync(
                    new[] { first, retry },
                    (item, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (item == retry && attempts++ == 0)
                            throw new InvalidOperationException("temporary failure");
                        item.Message = "ok";
                        return System.Threading.Tasks.Task.CompletedTask;
                    },
                    System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert(first.Status == BatchTaskStatus.Completed, "batch center did not continue successful items");
            Assert(retry.Status == BatchTaskStatus.Failed, "batch center did not retain a failed item");
            center.RetryFailedAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert(retry.Status == BatchTaskStatus.Completed && retry.Attempts == 2,
                "batch center failed-item retry did not complete");
            Assert(center.CreateReport().Contains("retry.png"), "batch task report omitted task details");

            var pausable = new BatchTaskCenter();
            var started = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var release = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var current = new BatchTaskItem { Kind = BatchTaskKind.Export, Target = "current.png" };
            var waiting = new BatchTaskItem { Kind = BatchTaskKind.Export, Target = "waiting.png" };
            var run = pausable.RunAsync(
                new[] { current, waiting },
                async (item, token) =>
                {
                    if (item == current)
                    {
                        started.TrySetResult(true);
                        await release.Task;
                    }
                    token.ThrowIfCancellationRequested();
                },
                System.Threading.CancellationToken.None);
            started.Task.GetAwaiter().GetResult();
            pausable.Pause();
            release.TrySetResult(true);
            Assert(System.Threading.SpinWait.SpinUntil(
                    () => current.Status == BatchTaskStatus.Completed && pausable.IsPaused, 2000),
                "batch center did not pause before the next item");
            pausable.Cancel();
            run.GetAwaiter().GetResult();
            Assert(waiting.Status == BatchTaskStatus.Cancelled,
                "batch center did not mark waiting items as cancelled");

            var resumable = new BatchTaskCenter();
            var resumeStarted = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var resumableItem = new BatchTaskItem { Kind = BatchTaskKind.Export, Target = "resume.png" };
            var resumableRun = resumable.RunAsync(
                new[] { resumableItem },
                async (item, token) =>
                {
                    resumeStarted.TrySetResult(true);
                    await System.Threading.Tasks.Task.Delay(5000, token);
                },
                System.Threading.CancellationToken.None);
            resumeStarted.Task.GetAwaiter().GetResult();
            resumable.SuspendForShutdown();
            resumableRun.GetAwaiter().GetResult();
            Assert(resumableItem.Status == BatchTaskStatus.Pending && resumable.CanResume,
                "shutdown checkpoint converted a resumable item into a cancelled item");
        }

        private static void TestBatchQueuePersistence(string root)
        {
            var path = Path.Combine(root, "batch-queue.json");
            var source = Path.Combine(root, "queue-source");
            Directory.CreateDirectory(source);
            var original = new BatchTaskItem
            {
                Kind = BatchTaskKind.Translation,
                Target = "第 1 批",
                Status = BatchTaskStatus.Running,
                Attempts = 2,
                Message = "running",
                RegionIds = new System.Collections.Generic.List<string> { "region-a", "region-b" },
                ResultCount = 1,
                MemoryMatchCount = 1
            };
            BatchTaskPersistenceService.Save(path, source, "project.guih.json", new[] { original });
            var restoredItems = BatchTaskPersistenceService.Load(path, source);
            Assert(restoredItems.Count == 1 && restoredItems[0].RegionIds.SequenceEqual(original.RegionIds),
                "batch queue persistence lost the task descriptor");
            Assert(!File.ReadAllText(path).Contains("ApiKey", StringComparison.OrdinalIgnoreCase),
                "batch queue persisted an API key field");

            var center = new BatchTaskCenter();
            center.Restore(restoredItems);
            var restored = center.Items.Single();
            Assert(restored.Status == BatchTaskStatus.Pending,
                "an interrupted running task was not normalized to pending");
            center.AttachExecutor(restored, (item, token) =>
            {
                item.Message = "resumed";
                return System.Threading.Tasks.Task.CompletedTask;
            });
            center.ResumePendingAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert(restored.Status == BatchTaskStatus.Completed && restored.Attempts == 3,
                "persisted batch task did not resume from the checkpoint");
        }

        private static void TestImageBrowserSupport(string root)
        {
            var unrecognized = new ImageDocument();
            var untranslated = new ImageDocument();
            untranslated.Regions.Add(new TextRegion { SourceText = "開始" });
            var needsReview = new ImageDocument();
            needsReview.Regions.Add(new TextRegion { SourceText = "開始", Translation = "开始" });
            var reviewed = new ImageDocument();
            reviewed.Regions.Add(new TextRegion { SourceText = "開始", Translation = "开始", Reviewed = true });
            Assert(ImageWorkflowClassifier.Classify(unrecognized) == ImageWorkflowStatus.Unrecognized,
                "empty image was not classified as unrecognized");
            Assert(ImageWorkflowClassifier.Classify(untranslated) == ImageWorkflowStatus.NeedsTranslation,
                "empty translation was not classified as needing translation");
            Assert(ImageWorkflowClassifier.Classify(needsReview) == ImageWorkflowStatus.NeedsReview,
                "translated text was not classified as needing review");
            Assert(ImageWorkflowClassifier.Classify(reviewed) == ImageWorkflowStatus.Reviewed,
                "reviewed image was not classified as reviewed");

            var imagePath = Path.Combine(root, "thumbnail-source.png");
            using (var bitmap = new Bitmap(240, 120, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
                bitmap.Save(imagePath, ImageFormat.Png);
            }
            using (var thumbnail = ImageThumbnailCache.CreateThumbnail(imagePath, 90, 60))
                Assert(thumbnail.Width == 90 && thumbnail.Height == 45,
                    "thumbnail generator changed the aspect ratio or exceeded its bounds");
        }

        private static void TestTranslationResources(string root)
        {
            var path = Path.Combine(root, "translation-resources.json");
            var service = new TranslationResourceService(new TranslationResourceData
            {
                Glossary = new System.Collections.Generic.List<GlossaryEntry>
                {
                    new GlossaryEntry { Source = "セーブ", Translation = "保存" },
                    new GlossaryEntry { Source = "ロード", Translation = "读取" }
                }
            }, path);
            Assert(service.Remember("設定", "设置"), "translation memory did not accept a valid pair");

            var region = new TextRegion { SourceText = "設定", Translation = string.Empty };
            var matched = new System.Collections.Generic.HashSet<string>();
            Assert(service.ApplyExactMatches(new[] { region }, matched) == 1,
                "translation memory did not reuse an exact match");
            Assert(region.Translation == "设置" && matched.Contains(region.Id),
                "translation memory returned the wrong translation or id");

            var project = new TranslationProject();
            var image = new ImageDocument();
            image.Regions.Add(new TextRegion
            {
                SourceText = "終了",
                Translation = "结束",
                Reviewed = true
            });
            image.Regions.Add(new TextRegion
            {
                SourceText = "未確認",
                Translation = "未确认",
                Reviewed = false
            });
            project.Images.Add(image);
            Assert(service.CollectReviewed(project) == 1,
                "translation memory did not collect the reviewed project entry");
            Assert(service.Data.Memory.All(entry => entry.Source != "未確認"),
                "translation memory collected an unreviewed entry");

            var relevant = service.GetRelevantGlossary(new[]
            {
                new TextRegion { SourceText = "セーブしますか？" }
            });
            Assert(relevant.Count == 1 && relevant[0].Translation == "保存",
                "glossary filtering did not select only terms used by the batch");
            service.Save();
            var restored = TranslationResourceService.Load(path);
            Assert(restored.Data.Memory.Count == 2 && restored.Data.Glossary.Count == 2,
                "translation memory or glossary was not preserved on disk");
        }

        private static void TestImageComparison()
        {
            using (var canvas = new ImageCanvas { Size = new Size(220, 120) })
            {
                canvas.CreateControl();
                var source = new Bitmap(100, 50, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(255, 25, 80, 170));
                var document = new ImageDocument { Width = 100, Height = 50 };
                document.Regions.Add(new TextRegion
                {
                    X = 55,
                    Y = 0,
                    Width = 45,
                    Height = 50,
                    Translation = string.Empty,
                    BackgroundMode = "Transparent",
                    ClearPadding = 0
                });
                canvas.SetDocument(source, document);
                canvas.SetComparisonEnabled(true);
                canvas.SetComparisonPosition(0.5f);
                Assert(canvas.ComparisonEnabled && Math.Abs(canvas.ComparisonPosition - 0.5f) < 0.001f,
                    "comparison mode did not retain the slider position");

                using (var rendered = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb))
                {
                    canvas.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                    Assert(rendered.GetPixel(66, 60).B > 140,
                        "comparison left side did not show the original image");
                    Assert(rendered.GetPixel(160, 60).ToArgb() != Color.FromArgb(255, 25, 80, 170).ToArgb(),
                        "comparison right side did not show the localized preview");
                }

                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(ImageCanvas).GetMethod("OnMouseDown", flags).Invoke(
                    canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 110, 60, 0) });
                typeof(ImageCanvas).GetMethod("OnMouseMove", flags).Invoke(
                    canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 150, 60, 0) });
                typeof(ImageCanvas).GetMethod("OnMouseUp", flags).Invoke(
                    canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 150, 60, 0) });
                Assert(canvas.ComparisonPosition > 0.65f,
                    "comparison divider did not respond to mouse dragging");
            }
        }

        private static void TestTextStylePresets(string root)
        {
            var path = Path.Combine(root, "text-style-presets.json");
            var service = new TextStylePresetService(new TextStylePresetData(), path);
            var source = new TextRegion
            {
                X = 12,
                Y = 14,
                Width = 180,
                Height = 60,
                SourceText = "設定",
                Translation = "设置",
                FontFamily = "Microsoft YaHei",
                FontSize = 31.5f,
                Bold = true,
                AutoFit = false,
                TextColorArgb = Color.Gold.ToArgb(),
                OutlineColorArgb = Color.DarkRed.ToArgb(),
                OutlineWidth = 3.5f,
                LetterSpacing = 2.5f,
                LineSpacing = 1.25f,
                VerticalText = true,
                RotationDegrees = 12f,
                ShadowEnabled = true,
                ShadowColorArgb = Color.FromArgb(160, 0, 0, 0).ToArgb(),
                ShadowOffsetX = 4,
                ShadowOffsetY = 5,
                GlowWidth = 2.5f,
                GlowColorArgb = Color.Cyan.ToArgb(),
                TextFillMode = "VerticalGradient",
                GradientEndColorArgb = Color.Orange.ToArgb(),
                HorizontalAlignment = "Right",
                VerticalAlignment = "Bottom",
                BackgroundMode = "ContentAware",
                ClearPadding = 8,
                Reviewed = true
            };
            var preset = service.Upsert("金色标题", source);
            Assert(service.Presets.Count == 1 && preset.Name == "金色标题",
                "style preset was not added to the library");

            var target = new TextRegion
            {
                X = 2,
                Y = 3,
                Width = 90,
                Height = 30,
                SourceText = "開始",
                Translation = "开始",
                BackgroundMode = "Transparent",
                ClearPadding = 1,
                Reviewed = false
            };
            target.RepairMaskStrokes.Add(new RepairMaskStroke());
            TextStylePresetService.Apply(preset, target);
            Assert(target.FontSize == source.FontSize && target.Bold && target.VerticalText,
                "style preset did not apply typography properties");
            Assert(target.TextColorArgb == source.TextColorArgb &&
                   target.GradientEndColorArgb == source.GradientEndColorArgb &&
                   target.GlowColorArgb == source.GlowColorArgb,
                "style preset did not apply color or effect properties");
            Assert(target.SourceText == "開始" && target.Translation == "开始" &&
                   target.X == 2 && target.Y == 3 && target.BackgroundMode == "Transparent" &&
                   target.ClearPadding == 1 && target.RepairMaskStrokes.Count == 1 && !target.Reviewed,
                "style preset changed text, coordinates, repair settings or review state");

            service.Save();
            var restored = TextStylePresetService.Load(path);
            Assert(restored.Presets.Count == 1 && restored.Presets[0].OutlineWidth == 3.5f,
                "style preset was not preserved on disk");
            source.FontSize = 42f;
            restored.Upsert("金色标题", source);
            Assert(restored.Presets.Count == 1 && restored.Presets[0].FontSize == 42f,
                "style preset overwrite created a duplicate or kept stale values");
            Assert(restored.Delete("金色标题") && restored.Presets.Count == 0,
                "style preset deletion failed");
            Assert(TextStylePresetService.CreateDefaultData().Presets.Count >= 4,
                "built-in style preset library is incomplete");
        }

        private static void TestProjectRoundTrip(string root)
        {
            var path = Path.Combine(root, "test.guih.json");
            var project = new TranslationProject { SourceFolder = root };
            var image = new ImageDocument { RelativePath = "sample.png", Width = 100, Height = 50 };
            image.Regions.Add(new TextRegion { SourceText = "開始", Translation = "开始", FontFamily = "Microsoft YaHei" });
            project.Images.Add(image);
            ProjectService.SaveProject(path, project);
            var loaded = ProjectService.LoadProject(path);
            Assert(loaded.Images.Count == 1, "project image count changed after serialization");
            Assert(loaded.Images[0].Regions[0].Translation == "开始", "translation changed after serialization");
        }

        private static void TestWorkspaceLayout()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new MainForm(true)
            {
                WindowState = FormWindowState.Normal,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-3000, -3000),
                Size = new Size(1200, 800),
                ShowInTaskbar = false,
                Opacity = 0
            })
            {
                form.Show();
                form.ShowWorkspaceForDiagnostics();
                form.PerformLayout();
                Application.DoEvents();
                var editor = form.Controls.Find("WorkspaceEditorCard", true);
                var regionEditor = form.Controls.Find("WorkspaceRegionEditor", true);
                var canvas = form.Controls.Find("WorkspaceCanvasCard", true);
                var batchPage = form.Controls.Find("BatchTaskCenterPage", true);
                var imageFilter = form.Controls.Find("ImageStatusFilter", true);
                var thumbnailList = form.Controls.Find("ImageThumbnailList", true);
                var resumeTasks = form.Controls.Find("ResumeBatchTasksButton", true);
                var presetCombo = form.Controls.Find("StylePresetCombo", true);
                var applyPreset = form.Controls.Find("ApplyStylePresetButton", true);
                var savePreset = form.Controls.Find("SaveStylePresetButton", true);
                var deletePreset = form.Controls.Find("DeleteStylePresetButton", true);
                Assert(editor.Length == 1 && editor[0].Width >= 280,
                    "workspace property editor was clipped by DPI/layout constraints");
                Assert(regionEditor.Length == 1 && regionEditor[0].Enabled,
                    "workspace property editor became unreadable when no image was open");
                Assert(canvas.Length == 1 && canvas[0].Width >= 180,
                    "workspace canvas became too narrow");
                Assert(batchPage.Length == 1, "batch task center page was not created");
                Assert(imageFilter.Length == 1 && thumbnailList.Length == 1 &&
                       ((ListBox)thumbnailList[0]).DrawMode == DrawMode.OwnerDrawFixed,
                    "thumbnail list or image status filter was not created");
                Assert(resumeTasks.Length == 1, "batch resume control was not created");
                Assert(presetCombo.Length == 1 && applyPreset.Length == 1 &&
                       savePreset.Length == 1 && deletePreset.Length == 1,
                    "style preset controls were not created in the property editor");
                form.Close();
            }
        }

        private static void TestNavigationRepaint()
        {
            using (var button = new NavButton { Text = "API 与模型", Size = new Size(210, 54) })
            using (var bitmap = new Bitmap(210, 54, PixelFormat.Format32bppArgb))
            {
                button.CreateControl();
                button.Active = true;
                button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                var activePixel = bitmap.GetPixel(20, 27);
                Assert(activePixel.ToArgb() != UiTheme.SidebarBackground.ToArgb(),
                    "active navigation state was not painted");

                button.Active = false;
                button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                var clearedPixel = bitmap.GetPixel(20, 27);
                Assert(clearedPixel.ToArgb() == UiTheme.SidebarBackground.ToArgb(),
                    "inactive navigation repaint retained pixels from the active state");
            }
        }

        private static void TestPreflight(string root)
        {
            var sourcePath = Path.Combine(root, "preflight.png");
            using (var bitmap = new Bitmap(180, 80, PixelFormat.Format32bppArgb))
            {
                bitmap.SetPixel(0, 0, Color.Transparent);
                bitmap.Save(sourcePath, ImageFormat.Png);
            }

            var project = new TranslationProject { SourceFolder = root };
            var image = new ImageDocument { RelativePath = "preflight.png", Width = 180, Height = 80 };
            var region = new TextRegion
            {
                X = 10,
                Y = 10,
                Width = 150,
                Height = 45,
                SourceText = "開始",
                Translation = string.Empty,
                FontFamily = "Microsoft YaHei",
                FontSize = 20,
                Reviewed = false
            };
            image.Regions.Add(region);
            project.Images.Add(image);

            var invalid = PreflightService.Analyze(project);
            Assert(invalid.ErrorCount > 0, "preflight did not reject an empty translation");
            Assert(invalid.WarningCount > 0, "preflight did not report an unreviewed region");

            region.Translation = "开始";
            region.Reviewed = true;
            var valid = PreflightService.Analyze(project);
            Assert(valid.ErrorCount == 0, "preflight rejected a valid translated region");

            var outputPath = Path.Combine(root, "preflight-output.png");
            ImageProcessor.ExportDocument(sourcePath, outputPath, image);
            Assert(string.IsNullOrEmpty(PreflightService.ValidateExportedFile(sourcePath, outputPath, image)),
                "post-export dimension/alpha validation failed");
        }

        private static void TestHistoryAndRecovery(string root)
        {
            var project = new TranslationProject { SourceFolder = root };
            var image = new ImageDocument { RelativePath = "history.png", Width = 100, Height = 50 };
            var region = new TextRegion { SourceText = "原文", Translation = "初始" };
            image.Regions.Add(region);
            project.Images.Add(image);

            var history = new ProjectHistory(5);
            history.Reset(project);
            region.Translation = "修改";
            history.Capture(project);
            var undone = history.Undo();
            Assert(undone.Images[0].Regions[0].Translation == "初始", "undo did not restore the previous snapshot");
            var redone = history.Redo();
            Assert(redone.Images[0].Regions[0].Translation == "修改", "redo did not restore the next snapshot");

            var projectPath = Path.Combine(root, "atomic.guih.json");
            ProjectService.SaveProject(projectPath, project);
            project.Images[0].Regions[0].Translation = "再次修改";
            ProjectService.SaveProject(projectPath, project);
            var backup = ProjectService.LoadProject(projectPath + ".bak");
            Assert(backup.Images[0].Regions[0].Translation == "修改", "atomic save backup does not contain the prior version");

            var autosavePath = Path.Combine(root, "recovery.guih.autosave.json");
            ProjectService.SaveAutosave(autosavePath, project, projectPath);
            var autosave = ProjectService.LoadAutosave(autosavePath);
            Assert(autosave.OriginalProjectPath == projectPath, "autosave lost the original project path");
            Assert(autosave.Project.Images[0].Regions[0].Translation == "再次修改", "autosave lost project changes");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
