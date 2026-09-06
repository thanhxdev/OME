using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SRT_DECODE
{
    public partial class FullscreenPlayoutWindow : Window
    {
        private WriteableBitmap? _playoutBitmap;
        private readonly DispatcherTimer _hudTimer;
        private bool _isDisposed;

        public bool IsMultiviewer { get; private set; }
        public event Action? WindowClosed;

        private const int MaxChannels = 10;
        private readonly WriteableBitmap?[] _cellBitmaps = new WriteableBitmap?[MaxChannels];
        private readonly Image?[] _cellImages = new Image?[MaxChannels];
        private readonly Border?[] _cellBorders = new Border?[MaxChannels];
        private readonly Border?[] _cellFallbacks = new Border?[MaxChannels];
        private readonly Border?[] _cellTallyBadges = new Border?[MaxChannels];
        private readonly TextBlock?[] _cellTallyTexts = new TextBlock?[MaxChannels];
        private readonly TextBlock?[] _cellStatusTexts = new TextBlock?[MaxChannels];

        private int _activeChannelCount = 1;
        private string[]? _channelNames;
        private int _currentPgmIndex = 0;
        private int _currentPvwIndex = 1;

        public FullscreenPlayoutWindow(
            DisplayMonitorInfo monitor,
            string sourceTitle,
            bool isMultiviewer = false,
            int activeChannelCount = 1,
            string[]? channelNames = null,
            int currentPgmIndex = 0,
            int currentPvwIndex = 1)
        {
            InitializeComponent();

            IsMultiviewer = isMultiviewer;
            _activeChannelCount = Math.Clamp(activeChannelCount, 1, MaxChannels);
            _channelNames = channelNames;
            _currentPgmIndex = currentPgmIndex;
            _currentPvwIndex = currentPvwIndex;

            TxtHudTitle.Text = $"OME FULLSCREEN PLAYOUT — {sourceTitle}";
            TxtHudInfo.Text = $"[{monitor.FriendlyName} {monitor.Width}x{monitor.Height} @ {monitor.RefreshRateHz}Hz] • Press ESC to exit";

            // Position and size window to exact monitor boundaries
            Left = monitor.Left;
            Top = monitor.Top;
            Width = monitor.Width;
            Height = monitor.Height;

            _hudTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _hudTimer.Tick += (s, e) =>
            {
                _hudTimer.Stop();
                HudBar.Opacity = 0;
            };

            Loaded += (s, e) =>
            {
                WindowState = WindowState.Normal;
                Left = monitor.Left;
                Top = monitor.Top;
                Width = monitor.Width;
                Height = monitor.Height;

                ApplySourceMode();
                ShowHudTemporarily();
            };

            Closed += (s, e) =>
            {
                _isDisposed = true;
                _hudTimer.Stop();
                WindowClosed?.Invoke();
            };
        }

        public void SetSourceMode(bool isMultiviewer, int activeChannelCount, string[]? channelNames, int currentPgmIndex, int currentPvwIndex)
        {
            if (_isDisposed) return;

            IsMultiviewer = isMultiviewer;
            _activeChannelCount = Math.Clamp(activeChannelCount, 1, MaxChannels);
            _channelNames = channelNames;
            _currentPgmIndex = currentPgmIndex;
            _currentPvwIndex = currentPvwIndex;

            string sourceTitle = isMultiviewer ? "MULTIVIEWER GRID" : "MASTER PROGRAM (PGM)";
            TxtHudTitle.Text = $"OME FULLSCREEN PLAYOUT — {sourceTitle}";

            ApplySourceMode();
        }

        public void UpdateLayoutConfig(int activeChannelCount, string[]? channelNames, int currentPgmIndex, int currentPvwIndex)
        {
            if (_isDisposed) return;

            _activeChannelCount = Math.Clamp(activeChannelCount, 1, MaxChannels);
            _channelNames = channelNames;
            _currentPgmIndex = currentPgmIndex;
            _currentPvwIndex = currentPvwIndex;

            if (IsMultiviewer)
            {
                BuildMultiviewerGrid();
            }
        }

        private void ApplySourceMode()
        {
            if (IsMultiviewer)
            {
                PlayoutImage.Visibility = Visibility.Collapsed;
                NoSignalOverlay.Visibility = Visibility.Collapsed;
                MultiviewerGrid.Visibility = Visibility.Visible;
                BuildMultiviewerGrid();
            }
            else
            {
                MultiviewerGrid.Visibility = Visibility.Collapsed;
                PlayoutImage.Visibility = Visibility.Visible;
                if (_playoutBitmap == null)
                {
                    TxtNoSignalTitle.Text = "PROGRAM PLAYOUT STANDBY";
                    TxtNoSignalSubtitle.Text = "Waiting for active Program video frame...";
                    NoSignalOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    NoSignalOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BuildMultiviewerGrid()
        {
            MultiviewerGrid.RowDefinitions.Clear();
            MultiviewerGrid.ColumnDefinitions.Clear();
            MultiviewerGrid.Children.Clear();

            // Sizing rule:
            // 1-4 cams: 2x2
            // 5-9 cams: 3x3
            // 10 cams: 4x3 (12 slots)
            int cols, rows;
            if (_activeChannelCount <= 4)
            {
                cols = 2;
                rows = 2;
            }
            else if (_activeChannelCount <= 9)
            {
                cols = 3;
                rows = 3;
            }
            else
            {
                cols = 4;
                rows = 3;
            }

            for (int c = 0; c < cols; c++)
            {
                MultiviewerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (int r = 0; r < rows; r++)
            {
                MultiviewerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            // Clear control caches
            for (int i = 0; i < MaxChannels; i++)
            {
                _cellImages[i] = null;
                _cellBorders[i] = null;
                _cellFallbacks[i] = null;
                _cellTallyBadges[i] = null;
                _cellTallyTexts[i] = null;
                _cellStatusTexts[i] = null;
            }

            int totalSlots = cols * rows;
            for (int slot = 0; slot < totalSlots; slot++)
            {
                int r = slot / cols;
                int c = slot % cols;

                if (slot < _activeChannelCount)
                {
                    int chIdx = slot;
                    string chName = (_channelNames != null && chIdx < _channelNames.Length && !string.IsNullOrWhiteSpace(_channelNames[chIdx]))
                        ? _channelNames[chIdx]
                        : $"CAM {chIdx + 1}";

                    bool isPgm = (chIdx == _currentPgmIndex);
                    bool isPvw = (chIdx == _currentPvwIndex);

                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)),
                        BorderBrush = isPgm ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) :
                                      isPvw ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)) :
                                              new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x35)),
                        BorderThickness = new Thickness(isPgm ? 3 : (isPvw ? 2 : 1.5)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(4)
                    };

                    var cellGrid = new Grid
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0D))
                    };

                    // Video Image
                    var img = new Image
                    {
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (_cellBitmaps[chIdx] != null)
                    {
                        img.Source = _cellBitmaps[chIdx];
                    }
                    cellGrid.Children.Add(img);

                    // Fallback overlay
                    var fallback = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x16)),
                        Visibility = (_cellBitmaps[chIdx] != null) ? Visibility.Collapsed : Visibility.Visible
                    };
                    var fbPanel = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    fbPanel.Children.Add(new TextBlock
                    {
                        Text = "📹",
                        FontSize = 26,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                    fbPanel.Children.Add(new TextBlock
                    {
                        Text = chName,
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    fbPanel.Children.Add(new TextBlock
                    {
                        Text = "Standby (No Signal)",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    fallback.Child = fbPanel;
                    cellGrid.Children.Add(fallback);

                    // Header bar overlay with Tally & Name
                    var headerBar = new Border
                    {
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x0F, 0x14)),
                        Padding = new Thickness(6, 4, 6, 4)
                    };

                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var leftPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var tallyBadge = new Border
                    {
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = isPgm ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) :
                                     isPvw ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)) :
                                             new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38))
                    };
                    var tallyText = new TextBlock
                    {
                        Text = isPgm ? "PGM" : (isPvw ? "PVW" : $"CAM {chIdx + 1}"),
                        FontWeight = FontWeights.Bold,
                        FontSize = 9.5,
                        Foreground = Brushes.White
                    };
                    tallyBadge.Child = tallyText;
                    leftPanel.Children.Add(tallyBadge);

                    var nameText = new TextBlock
                    {
                        Text = chName,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 11,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    leftPanel.Children.Add(nameText);

                    Grid.SetColumn(leftPanel, 0);
                    headerGrid.Children.Add(leftPanel);

                    var statusText = new TextBlock
                    {
                        Text = "",
                        FontSize = 9.5,
                        FontFamily = new FontFamily("Consolas"),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(statusText, 1);
                    headerGrid.Children.Add(statusText);

                    headerBar.Child = headerGrid;
                    cellGrid.Children.Add(headerBar);

                    border.Child = cellGrid;

                    _cellBorders[chIdx] = border;
                    _cellImages[chIdx] = img;
                    _cellFallbacks[chIdx] = fallback;
                    _cellTallyBadges[chIdx] = tallyBadge;
                    _cellTallyTexts[chIdx] = tallyText;
                    _cellStatusTexts[chIdx] = statusText;

                    Grid.SetRow(border, r);
                    Grid.SetColumn(border, c);
                    MultiviewerGrid.Children.Add(border);
                }
                else
                {
                    // Empty inactive slot
                    var emptyBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0A)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x22)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(4)
                    };
                    var emptyText = new TextBlock
                    {
                        Text = "—",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x33)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    emptyBorder.Child = emptyText;
                    Grid.SetRow(emptyBorder, r);
                    Grid.SetColumn(emptyBorder, c);
                    MultiviewerGrid.Children.Add(emptyBorder);
                }
            }
        }

        public void UpdateFrame(byte[] bgraBytes, int width, int height)
        {
            if (_isDisposed || IsMultiviewer) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_isDisposed) return;

                    if (_playoutBitmap == null || _playoutBitmap.PixelWidth != width || _playoutBitmap.PixelHeight != height)
                    {
                        _playoutBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        PlayoutImage.Source = _playoutBitmap;
                    }

                    int stride = width * 4;
                    _playoutBitmap.WritePixels(new Int32Rect(0, 0, width, height), bgraBytes, stride, 0);

                    if (NoSignalOverlay.Visibility != Visibility.Collapsed)
                    {
                        NoSignalOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                catch { }
            }, DispatcherPriority.Render);
        }

        public void UpdateChannelFrame(int channelIndex, byte[] bgraBytes, int width, int height)
        {
            if (_isDisposed || !IsMultiviewer || channelIndex < 0 || channelIndex >= MaxChannels) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_isDisposed) return;

                    var bmp = _cellBitmaps[channelIndex];
                    if (bmp == null || bmp.PixelWidth != width || bmp.PixelHeight != height)
                    {
                        bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        _cellBitmaps[channelIndex] = bmp;
                        if (_cellImages[channelIndex] != null)
                        {
                            _cellImages[channelIndex]!.Source = bmp;
                        }
                    }

                    int stride = width * 4;
                    bmp.WritePixels(new Int32Rect(0, 0, width, height), bgraBytes, stride, 0);

                    if (_cellFallbacks[channelIndex] != null && _cellFallbacks[channelIndex]!.Visibility != Visibility.Collapsed)
                    {
                        _cellFallbacks[channelIndex]!.Visibility = Visibility.Collapsed;
                    }

                    if (_cellStatusTexts[channelIndex] != null)
                    {
                        _cellStatusTexts[channelIndex]!.Text = $"{width}x{height}";
                    }
                }
                catch { }
            }, DispatcherPriority.Render);
        }

        public void SetProgramChannel(int pgmIndex, int pvwIndex)
        {
            _currentPgmIndex = pgmIndex;
            _currentPvwIndex = pvwIndex;

            if (_isDisposed || !IsMultiviewer) return;

            Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (_cellBorders[i] == null) continue;

                    string chName = (_channelNames != null && i < _channelNames.Length && !string.IsNullOrWhiteSpace(_channelNames[i]))
                        ? _channelNames[i]
                        : $"CAM {i + 1}";

                    if (i == pgmIndex)
                    {
                        _cellBorders[i]!.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                        _cellBorders[i]!.BorderThickness = new Thickness(3);
                        if (_cellTallyBadges[i] != null) _cellTallyBadges[i]!.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                        if (_cellTallyTexts[i] != null) _cellTallyTexts[i]!.Text = "PGM";
                    }
                    else if (i == pvwIndex)
                    {
                        _cellBorders[i]!.BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                        _cellBorders[i]!.BorderThickness = new Thickness(2);
                        if (_cellTallyBadges[i] != null) _cellTallyBadges[i]!.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                        if (_cellTallyTexts[i] != null) _cellTallyTexts[i]!.Text = "PVW";
                    }
                    else
                    {
                        _cellBorders[i]!.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x35));
                        _cellBorders[i]!.BorderThickness = new Thickness(1.5);
                        if (_cellTallyBadges[i] != null) _cellTallyBadges[i]!.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
                        if (_cellTallyTexts[i] != null) _cellTallyTexts[i]!.Text = chName;
                    }
                }
            });
        }

        private void ShowHudTemporarily()
        {
            HudBar.Opacity = 1;
            _hudTimer.Stop();
            _hudTimer.Start();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            ShowHudTemporarily();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            HudBar.Opacity = 0;
            _hudTimer.Stop();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
