using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Racks.Services
{
    public static class ThumbnailService
    {
        public static BitmapSource? GetThumbnail(string filePath, int size)
        {
            try
            {
                ShellObject shellObject = ShellObject.FromParsingName(filePath);
                ShellThumbnail shellThumbnail = shellObject.Thumbnail;
                shellThumbnail.CurrentSize = new System.Windows.Size(size, size);
                BitmapSource thumbnail = shellThumbnail.BitmapSource;
                thumbnail.Freeze();
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<BitmapSource?> GetThumbnailAsync(string path, int iconSize, bool showShortcutArrow, double windowsScalingFactor)
        {
            return await Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                {
                    return null;
                }
                BitmapSource? thumbnail = null;
                int actualIconSize = (int)(iconSize * windowsScalingFactor);
                if (Path.GetExtension(path).ToLowerInvariant() == ".svg")
                {
                    try
                    {
                        thumbnail = await LoadSvgThumbnailAsync(path, actualIconSize);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                    return thumbnail;
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                bool isLink = ext == ".lnk" || ext == ".url";

                if (isLink)
                {
                    try
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            thumbnail = GetThumbnail(path, actualIconSize);
                        });
                        if (showShortcutArrow && thumbnail != null)
                        {
                            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                IntPtr[] overlayIcons = new IntPtr[1];
                                int overlayExtracted = Racks.Util.Interop.ExtractIconEx(
                                    Environment.SystemDirectory + "\\shell32.dll",
                                    29,
                                    overlayIcons,
                                    null,
                                    1);

                                if (overlayExtracted > 0 && overlayIcons[0] != IntPtr.Zero)
                                {
                                    var overlay = Imaging.CreateBitmapSourceFromHIcon(
                                                  overlayIcons[0],
                                                  Int32Rect.Empty,
                                                  BitmapSizeOptions.FromEmptyOptions());
                                    Racks.Util.Interop.DestroyIcon(overlayIcons[0]);

                                    var visual = new DrawingVisual();
                                    using (var dc = visual.RenderOpen())
                                    {
                                        double scale = (double)actualIconSize / Math.Max(thumbnail.PixelWidth, thumbnail.PixelHeight);
                                        double thumbnailWidth = thumbnail.PixelWidth * scale;
                                        double thumbnailHeight = thumbnail.PixelHeight * scale;

                                        double thumbnailX = (actualIconSize - thumbnailWidth) / 2.0;
                                        double thumbnailY = (actualIconSize - thumbnailHeight) / 2.0;

                                        dc.DrawImage(
                                            thumbnail,
                                            new Rect(
                                                thumbnailX,
                                                thumbnailY,
                                                thumbnailWidth,
                                                thumbnailHeight)
                                        );
                                        double overlayScale = (actualIconSize < 32 ? actualIconSize / 32.0 : 1.0);
                                        if (windowsScalingFactor != 1.0)
                                        {
                                            overlayScale *= (1 / windowsScalingFactor);
                                        }
                                        if (overlayScale != 1.0)
                                        {
                                            overlay = new TransformedBitmap(overlay, new ScaleTransform(overlayScale, overlayScale));
                                            overlay.Freeze();
                                        }
                                        double overlayX = thumbnailX;
                                        double overlayY = thumbnailY + thumbnailHeight - overlay.PixelHeight;
                                        dc.DrawImage(overlay,
                                            new Rect(
                                            overlayX,
                                            overlayY,
                                            overlay.PixelWidth,
                                            overlay.PixelHeight)
                                        );
                                    }

                                    var rtb = new RenderTargetBitmap(
                                        actualIconSize,
                                        actualIconSize,
                                        thumbnail.DpiX,
                                        thumbnail.DpiY,
                                        PixelFormats.Pbgra32);
                                    rtb.Render(visual);
                                    rtb.Freeze();
                                    return rtb;
                                }
                                return thumbnail;
                            });
                        }
                        return thumbnail;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                else
                {
                    try
                    {
                        int attempt = 0;
                        while (attempt < 3 && thumbnail == null)
                        {
                            ShellObject? shellObj = null;
                            shellObj = Directory.Exists(path) ? ShellObject.FromParsingName(path) : ShellFile.FromFilePath(path);
                            if (shellObj != null)
                            {
                                try
                                {
                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        thumbnail = GetThumbnail(path, actualIconSize);
                                    });
                                    if (thumbnail != null)
                                    {
                                        return thumbnail;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("Failed to fetch thumbnail:" + ex.Message);
                                }
                                finally
                                {
                                    shellObj?.Dispose();
                                }
                            }
                            attempt++;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                if (thumbnail != null)
                {
                    return thumbnail;
                }

                Debug.WriteLine("Failed to retrieve thumbnail after 3 attempts.");
                return null;
            });
        }

        public static async Task<BitmapSource?> LoadSvgThumbnailAsync(string path, int iconSize)
        {
            try
            {
                var svgDocument = Svg.SvgDocument.Open(path);

                using (var bitmap = svgDocument.Draw(iconSize, iconSize))
                {
                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);

                        BitmapImage? bitmapImage = null;
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = ms;
                            bitmapImage.DecodePixelWidth = 64;
                            bitmapImage.DecodePixelHeight = 64;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                        });
                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load SVG thumbnail: {ex.Message}");
                return null;
            }
        }

        public static async Task<BitmapSource?> LoadUrlIconAsync(string path, int iconSize, bool showShortcutArrow, double windowsScalingFactor)
        {
            try
            {
                string iconFile = "";
                int iconIndex = 0;
                bool hasHttp = false;
                bool hasHttps = false;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        iconFile = line.Substring("IconFile=".Length).Trim();
                    }
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Substring("IconIndex=".Length).Trim(), out int i))
                        {
                            iconIndex = i;
                        }
                    }
                    else if (iconFile == "")
                    {
                        if (line.StartsWith("URL=http://"))
                        {
                            hasHttp = true;
                            break;
                        }
                        else if (line.StartsWith("URL=https://"))
                        {
                            hasHttps = true;
                            break;
                        }
                    }
                }
                if (iconFile == "")
                {
                    if (hasHttp)
                    {
                        iconFile = GetDefaultBrowserPath("http");
                    }
                    else if (hasHttps)
                    {
                        iconFile = GetDefaultBrowserPath("https");
                    }
                }
                if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
                {
                    return await Task.Run(() =>
                    {
                        IntPtr[] icons = new IntPtr[1];
                        int extracted = Racks.Util.Interop.ExtractIconEx(iconFile, iconIndex, icons, null, 1);
                        if (extracted > 0 && icons[0] != IntPtr.Zero)
                        {
                            var source = Imaging.CreateBitmapSourceFromHIcon(
                                icons[0],
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            Racks.Util.Interop.DestroyIcon(icons[0]);
                            if (showShortcutArrow)
                            {
                                IntPtr[] overlayIcons = new IntPtr[1];
                                int overlayExtracted = Racks.Util.Interop.ExtractIconEx(
                                    Environment.SystemDirectory + "\\shell32.dll",
                                    29,
                                    overlayIcons,
                                    null,
                                    1);

                                if (overlayExtracted > 0 && overlayIcons[0] != IntPtr.Zero)
                                {
                                    var overlay = Imaging.CreateBitmapSourceFromHIcon(
                                        overlayIcons[0],
                                        Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());
                                    Racks.Util.Interop.DestroyIcon(overlayIcons[0]);

                                    var visual = new DrawingVisual();
                                    using (var dc = visual.RenderOpen())
                                    {
                                        dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                                        dc.DrawImage(overlay, new Rect(
                                            source.PixelWidth - overlay.PixelWidth,
                                            source.PixelHeight - overlay.PixelHeight,
                                            overlay.PixelWidth,
                                            overlay.PixelHeight));
                                    }

                                    var rtb = new RenderTargetBitmap(
                                        source.PixelWidth,
                                        source.PixelHeight,
                                        source.DpiX,
                                        source.DpiY,
                                        PixelFormats.Pbgra32);
                                    rtb.Render(visual);
                                    rtb.Freeze();

                                    return rtb;
                                }
                            }
                            source.Freeze();
                            return source;
                        }
                        return null;
                    });
                }
                return null;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error loading URL icon: " + e.Message);
                return await GetThumbnailAsync(path, iconSize, showShortcutArrow, windowsScalingFactor);
            }
        }

        private static string GetDefaultBrowserPath(string protocol)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{protocol}\UserChoice");
                if (key != null)
                {
                    object? progId = key.GetValue("Progid");

                    if (progId == null)
                    {
                        return "";
                    }
                    using RegistryKey? commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
                    if (commandKey != null)
                    {
                        object? command = commandKey.GetValue("");

                        if (command == null)
                        {
                            return "";
                        }
                        return Regex.Match(command.ToString()!, "^\"([^\"]+)\"").Groups[1].Value;
                    }
                }
            }
            catch
            {
                return "";
            }
            return "";
        }
    }
}
