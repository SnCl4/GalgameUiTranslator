using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GalgameUiTranslator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) =>
            {
                WriteCrashLog(eventArgs.Exception);
                MessageBox.Show(
                    "程序遇到异常，详细信息已写入：\r\n" + GetCrashLogPath() + "\r\n\r\n" + eventArgs.Exception.Message,
                    "Galgame UI 图片汉化工具",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                WriteCrashLog(eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                WriteCrashLog(eventArgs.Exception);
                eventArgs.SetObserved();
            };

            try
            {
                var suppressRecovery = Array.Exists(args, value =>
                    value.Equals("--startup-test", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("--ui-snapshot=", StringComparison.OrdinalIgnoreCase));
                var form = new MainForm(suppressRecovery);
                if (Array.Exists(args, value => value.Equals("--workspace", StringComparison.OrdinalIgnoreCase)))
                {
                    form.Shown += (_, __) => form.ShowWorkspaceForDiagnostics();
                }
                var snapshotArgument = Array.Find(args,
                    value => value.StartsWith("--ui-snapshot=", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(snapshotArgument))
                {
                    var snapshotPath = snapshotArgument.Substring("--ui-snapshot=".Length).Trim('"');
                    form.WindowState = FormWindowState.Normal;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(0, 0);
                    form.Size = new Size(1600, 960);
                    form.Shown += (_, __) =>
                    {
                        var timer = new Timer { Interval = 1000 };
                        timer.Tick += (sender, eventArgs) =>
                        {
                            timer.Stop();
                            timer.Dispose();
                            form.PerformLayout();
                            form.Refresh();
                            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format24bppRgb))
                            {
                                using (var graphics = Graphics.FromImage(bitmap))
                                {
                                    graphics.Clear(form.BackColor);
                                }
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                                var directory = Path.GetDirectoryName(snapshotPath);
                                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                                using (var output = new MemoryStream())
                                {
                                    bitmap.Save(output, ImageFormat.Png);
                                    File.WriteAllBytes(snapshotPath, output.ToArray());
                                }
                            }
                            form.Close();
                        };
                        timer.Start();
                    };
                }
                else if (Array.Exists(args, value => value.Equals("--startup-test", StringComparison.OrdinalIgnoreCase)))
                {
                    form.Shown += (_, __) =>
                    {
                        var timer = new Timer { Interval = 600 };
                        timer.Tick += (sender, eventArgs) =>
                        {
                            timer.Stop();
                            timer.Dispose();
                            form.Close();
                        };
                        timer.Start();
                    };
                }

                Application.Run(form);
            }
            catch (Exception exception)
            {
                WriteCrashLog(exception);
                MessageBox.Show(
                    "程序启动失败，详细信息已写入：\r\n" + GetCrashLogPath() + "\r\n\r\n" + exception.Message,
                    "Galgame UI 图片汉化工具",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }

        private static void WriteCrashLog(Exception exception)
        {
            try
            {
                var path = GetCrashLogPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var text = new StringBuilder()
                    .AppendLine(new string('=', 72))
                    .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .AppendLine(exception?.ToString() ?? "Unknown exception")
                    .ToString();
                File.AppendAllText(path, text, Encoding.UTF8);
            }
            catch
            {
                // Crash reporting must never cause another crash.
            }
        }

        private static string GetCrashLogPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "crash.log");
        }
    }
}
