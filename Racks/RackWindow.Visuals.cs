#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8622, CS8625
using Racks.ViewModels;
using Racks.Core;
using Racks.Properties;
using Racks.Shaders;
using Racks.Util;
using static Racks.Util.ThemePresets;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using WindowsDesktop;
using Wpf.Ui.Controls;
using static Racks.Util.Interop;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using File = System.IO.File;
using ListView = Wpf.Ui.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Path = System.IO.Path;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
namespace Racks
{
    public partial class RackWindow
    {
        public void ChangeBackgroundOpacity(int num)
        {
            try
            {
                if (Instance.IsTransparent)
                {
                    WindowBackground.Background = System.Windows.Media.Brushes.Transparent;
                    BackgroundType(false);
                    return;
                }

                // Prefer image background when one is configured and the file still exists.
                if (!string.IsNullOrWhiteSpace(Instance.BackgroundImagePath)
                    && File.Exists(Instance.BackgroundImagePath))
                {
                    try
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.UriSource = new Uri(Instance.BackgroundImagePath, UriKind.Absolute);
                        img.EndInit();
                        img.Freeze();
                        WindowBackground.Background = new ImageBrush(img)
                        {
                            Stretch = Stretch.UniformToFill,
                            Opacity = Math.Clamp(Instance.Opacity / 255.0, 0.0, 1.0),
                        };
                        return;
                    }
                    catch (Exception ex) { Debug.WriteLine($"BG image load failed: {ex.Message}"); }
                }

                if (Instance.DropShadowEnabled)
                {
                    WindowBackground.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 15,
                        ShadowDepth = 3,
                        Direction = 270,
                        Color = Colors.Black,
                        Opacity = 0.4
                    };
                    WindowBackground.Margin = new Thickness(10);
                    // Adjust sizing due to margin
                    this.Width = Instance.Width + 20;
                    this.Height = Instance.Height + 20;
                }
                else
                {
                    WindowBackground.Effect = null;
                    WindowBackground.Margin = new Thickness(0);
                    this.Width = Instance.Width;
                    this.Height = Instance.Height;
                }

                var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(Instance.ListViewBackgroundColor);
                if (Instance.GradientBackgroundEnabled && !Instance.ActiveBackgroundEnabled)
                {
                    var gradient = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(0, 1)
                    };
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Instance.Opacity, c.R, c.G, c.B), 0.0));

                    var bottomColor = c;
                    bottomColor.R = (byte)Math.Max(0, c.R - 30);
                    bottomColor.G = (byte)Math.Max(0, c.G - 30);
                    bottomColor.B = (byte)Math.Max(0, c.B - 30);
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Instance.Opacity, bottomColor.R, bottomColor.G, bottomColor.B), 1.0));

                    WindowBackground.Background = gradient;
                }
                else
                {
                    WindowBackground.Background = new SolidColorBrush(Color.FromArgb((byte)Instance.Opacity, c.R, c.G, c.B));
                }

                BackgroundType(_isTopmost);
            }
            catch
            {

            }
        }

        public void BackgroundType(bool toBlur)
        {
            if (Instance.IsTransparent)
            {
                toBlur = false;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            var accent = new Interop.AccentPolicy
            {
                AccentState = toBlur ? Interop.AccentState.ACCENT_ENABLE_BLURBEHIND :
                                       Interop.AccentState.ACCENT_DISABLED
            };

            var data = new Interop.WindowCompositionAttributeData
            {
                Attribute = Interop.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = Marshal.SizeOf(accent),
                Data = Marshal.AllocHGlobal(Marshal.SizeOf(accent))
            };

            Marshal.StructureToPtr(accent, data.Data, false);
            Interop.SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(data.Data);
        }

        // Subscribe the particle animation to the per-frame render callback. Guarded so
        // repeated DragOver events can't double-subscribe (which would run it twice a frame).
        private void StartParticles()
        {
            if (_particleRenderingActive) return;
            _particleRenderingActive = true;
            ParticleCanvas.Visibility = Visibility.Visible;
            CompositionTarget.Rendering += UpdateParticle!;
        }

        private void StopParticles()
        {
            if (!_particleRenderingActive) return;
            _particleRenderingActive = false;
            CompositionTarget.Rendering -= UpdateParticle;
        }

        private void UpdateParticle(object sender, EventArgs e)
        {
            double cx = ParticleCanvas.ActualWidth / 2;
            double cy = (ParticleCanvas.ActualHeight + titleBar.Height) / 2;

            if (_dragdropIntoFolder && Instance.Folder == "empty")
            {
                for (int i = 0; i < 20 && particles.Count < 20; i++)
                {
                    CreateParticle();
                }
            }
            else if (particles.Count == 0 && !_dragdropIntoFolder)
            {
                // Drag ended and every particle has decayed: stop the render loop so an
                // idle rack costs zero CPU. It restarts on the next drag-over.
                StopParticles();
                if (Instance.Folder != "empty") ParticleCanvas.Visibility = Visibility.Hidden;
                return;
            }

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                p.Update(cx, cy, 400);
                Ellipse v = visuals[i];
                v.Opacity = p.Opacity;
                Canvas.SetLeft(v, p.X);
                Canvas.SetTop(v, p.Y);

                if (!p.ToRemove)
                {
                    ParticleCanvas.Children.Remove(v);
                    visuals.RemoveAt(i);
                    particles.RemoveAt(i);
                }
            }
        }

        private void CreateParticle()
        {
            Particle p = new Particle(ParticleCanvas.ActualWidth, ParticleCanvas.ActualHeight);
            particles.Add(p);

            Ellipse e = new Ellipse
            {
                Width = 6,
                Height = 6,
                Opacity = 1,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Colors.White, 0.0),
                        new GradientStop(Colors.White, 0.6),
                        new GradientStop(Colors.Transparent, 1.0)
                    }
                }
            };
            visuals.Add(e);
            ParticleCanvas.Children.Add(e);
        }
    }
}
