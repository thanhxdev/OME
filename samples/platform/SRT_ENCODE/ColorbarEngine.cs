using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SRT_ENCODE
{
    public enum ColorbarPatternType
    {
        SmpteRp219,
        Ebu100Percent,
        GridAlignment
    }

    public enum AudioTestToneType
    {
        Sine1kHzMinus18dBFS,
        Sine1kHzMinus20dBFS,
        Glits400Hz,
        EbuToneIdent
    }

    /// <summary>
    /// Xử lý độc lập toàn bộ logic render mẫu hình kiểm tra (Colorbar / Grid)
    /// và tính toán mức âm thanh kiểm tra chuẩn phát sóng (Audio Test Tone).
    /// </summary>
    public sealed class ColorbarEngine
    {
        private ColorbarPatternType _currentPattern = ColorbarPatternType.SmpteRp219;
        private AudioTestToneType _currentTone = AudioTestToneType.Sine1kHzMinus18dBFS;
        private long _toneTickCounter = 0;

        public ColorbarPatternType CurrentPattern
        {
            get => _currentPattern;
            set => _currentPattern = value;
        }

        public AudioTestToneType CurrentTone
        {
            get => _currentTone;
            set => _currentTone = value;
        }

        public string GetPatternTitle()
        {
            return _currentPattern switch
            {
                ColorbarPatternType.SmpteRp219 => "SMPTE RP 219 COLORBAR PATTERN",
                ColorbarPatternType.Ebu100Percent => "EBU 100% FULL COLOR BARS (EBU R68)",
                ColorbarPatternType.GridAlignment => "GRID ALIGNMENT & CONVERGENCE CHART",
                _ => "BROADCAST TEST PATTERN"
            };
        }

        public string GetToneDescription()
        {
            return _currentTone switch
            {
                AudioTestToneType.Sine1kHzMinus18dBFS => "1 kHz Sinewave @ -18 dBFS (EBU Standard)",
                AudioTestToneType.Sine1kHzMinus20dBFS => "1 kHz Sinewave @ -20 dBFS (SMPTE Standard)",
                AudioTestToneType.Glits400Hz => "400 Hz Tone (GLITS Phased Stereo)",
                AudioTestToneType.EbuToneIdent => "EBU R49 Tone Ident (Pulsed Right Channel)",
                _ => "1 kHz Sinewave @ -18 dBFS"
            };
        }

        /// <summary>
        /// Tạo và nạp toàn bộ cấu trúc đồ họa Vector cho Pattern vào visualHost
        /// </summary>
        public void RenderPattern(Grid visualHost)
        {
            if (visualHost == null) return;
            visualHost.Children.Clear();
            visualHost.RowDefinitions.Clear();
            visualHost.ColumnDefinitions.Clear();

            switch (_currentPattern)
            {
                case ColorbarPatternType.SmpteRp219:
                    RenderSmpteRp219(visualHost);
                    break;

                case ColorbarPatternType.Ebu100Percent:
                    RenderEbu100(visualHost);
                    break;

                case ColorbarPatternType.GridAlignment:
                    RenderGridAlignment(visualHost);
                    break;
            }
        }

        #region SMPTE RP 219 Colorbar Layout

        private void RenderSmpteRp219(Grid container)
        {
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(67, GridUnitType.Star) });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8, GridUnitType.Star) });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25, GridUnitType.Star) });

            // Top 67%: 7 Primary Color Bars (75% White, Yellow, Cyan, Green, Magenta, Red, Blue)
            var topBars = new UniformGrid { Columns = 7 };
            Grid.SetRow(topBars, 0);
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(192, 192, 192)) }); // 75% White
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(192, 192, 0)) });   // Yellow
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 192, 192)) });   // Cyan
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 192, 0)) });     // Green
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(192, 0, 192)) });   // Magenta
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(192, 0, 0)) });     // Red
            topBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 0, 192)) });     // Blue
            container.Children.Add(topBars);

            // Middle 8%: Castellation Bars
            var midBars = new UniformGrid { Columns = 7 };
            Grid.SetRow(midBars, 1);
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 0, 192)) });     // Blue
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(19, 19, 19)) });   // Black
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(192, 0, 192)) });   // Magenta
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(19, 19, 19)) });   // Black
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 192, 192)) });   // Cyan
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(19, 19, 19)) });   // Black
            midBars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(192, 192, 192)) }); // White
            container.Children.Add(midBars);

            // Bottom 25%: -I, 100% White, +Q, Black 0%, Pluge Pulses (-2%, 0%, +2%), Black
            var bottomGrid = new Grid();
            Grid.SetRow(bottomGrid, 2);
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.33, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.33, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.34, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddColRect(bottomGrid, 0, Color.FromRgb(0, 33, 76));    // -I
            AddColRect(bottomGrid, 1, Color.FromRgb(255, 255, 255)); // 100% White
            AddColRect(bottomGrid, 2, Color.FromRgb(50, 0, 106));   // +Q
            AddColRect(bottomGrid, 3, Color.FromRgb(19, 19, 19));   // Black 0%
            AddColRect(bottomGrid, 4, Color.FromRgb(9, 9, 9));      // -2% Pluge
            AddColRect(bottomGrid, 5, Color.FromRgb(19, 19, 19));   // 0% Pluge
            AddColRect(bottomGrid, 6, Color.FromRgb(29, 29, 29));   // +2% Pluge
            AddColRect(bottomGrid, 7, Color.FromRgb(19, 19, 19));   // Black
            AddColRect(bottomGrid, 8, Color.FromRgb(19, 19, 19));   // Black

            container.Children.Add(bottomGrid);
        }

        #endregion

        #region EBU 100% Colorbar Layout

        private void RenderEbu100(Grid container)
        {
            // 8 Full-Height 100% Color Bars: White, Yellow, Cyan, Green, Magenta, Red, Blue, Black
            var bars = new UniformGrid { Columns = 8 };
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(255, 255, 255)) }); // 100% White
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(255, 255, 0)) });   // 100% Yellow
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 255, 255)) });   // 100% Cyan
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 255, 0)) });     // 100% Green
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(255, 0, 255)) });   // 100% Magenta
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)) });     // 100% Red
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 0, 255)) });     // 100% Blue
            bars.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 0, 0)) });       // Black

            container.Children.Add(bars);
        }

        #endregion

        #region Grid Alignment & Convergence Pattern

        private void RenderGridAlignment(Grid container)
        {
            var gridCanvas = new Grid { Background = new SolidColorBrush(Color.FromRgb(16, 16, 18)) };

            // 16x9 Crosshatch Alignment Lines
            for (int r = 0; r < 9; r++)
            {
                gridCanvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
            for (int c = 0; c < 16; c++)
            {
                gridCanvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 16; c++)
                {
                    var cell = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 70)),
                        BorderThickness = new Thickness(0.5)
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    gridCanvas.Children.Add(cell);
                }
            }

            // Safe Title / Safe Action Area Rectangles
            var safeAction = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(40, 25, 40, 25),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRowSpan(safeAction, 9);
            Grid.SetColumnSpan(safeAction, 16);
            gridCanvas.Children.Add(safeAction);

            var safeTitle = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(80, 50, 80, 50),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRowSpan(safeTitle, 9);
            Grid.SetColumnSpan(safeTitle, 16);
            gridCanvas.Children.Add(safeTitle);

            // Center Convergence Circle
            var centerCircle = new Ellipse
            {
                Width = 240,
                Height = 240,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRowSpan(centerCircle, 9);
            Grid.SetColumnSpan(centerCircle, 16);
            gridCanvas.Children.Add(centerCircle);

            container.Children.Add(gridCanvas);
        }

        #endregion

        #region Audio Tone Calculation for VU Meters

        /// <summary>
        /// Tính toán cường độ âm thanh chuẩn phát sóng cho tối đa 16 kênh âm thanh SDI Embedded
        /// </summary>
        public void GetAudioToneLevels16(double[] channels16)
        {
            if (channels16 == null || channels16.Length < 16) return;
            _toneTickCounter++;

            for (int i = 0; i < 16; i++)
            {
                channels16[i] = -60.0;
            }

            switch (_currentTone)
            {
                case AudioTestToneType.Sine1kHzMinus18dBFS:
                    channels16[0] = -18.0; // CH 1 (L)
                    channels16[1] = -18.0; // CH 2 (R)
                    break;

                case AudioTestToneType.Sine1kHzMinus20dBFS:
                    channels16[0] = -20.0; // CH 1 (L)
                    channels16[1] = -20.0; // CH 2 (R)
                    break;

                case AudioTestToneType.Glits400Hz:
                    channels16[0] = -18.0;
                    bool isRightActive = (_toneTickCounter % 20) < 14; // ~1.5s on, 0.5s off
                    channels16[1] = isRightActive ? -18.0 : -60.0;
                    break;

                case AudioTestToneType.EbuToneIdent:
                    channels16[0] = -18.0;
                    bool isEbuBeep = (_toneTickCounter % 15) < 4; // short pulse beep
                    channels16[1] = isEbuBeep ? -18.0 : -60.0;
                    break;

                default:
                    channels16[0] = -18.0;
                    channels16[1] = -18.0;
                    break;
            }
        }

        /// <summary>
        /// Tính toán cường độ âm thanh chuẩn phát sóng theo loại Tone đã chọn (4 kênh tương thích cũ)
        /// </summary>
        public void GetAudioToneLevels(out double ch1Db, out double ch2Db, out double ch3Db, out double ch4Db)
        {
            double[] arr = new double[16];
            GetAudioToneLevels16(arr);
            ch1Db = arr[0];
            ch2Db = arr[1];
            ch3Db = arr[2];
            ch4Db = arr[3];
        }

        #endregion

        private static void AddColRect(Grid grid, int col, Color color)
        {
            var rect = new Rectangle { Fill = new SolidColorBrush(color) };
            Grid.SetColumn(rect, col);
            grid.Children.Add(rect);
        }
    }
}
