using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
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
    /// và tổng hợp phát sóng âm thanh kiểm tra chuẩn phát sóng (Audio Test Tone Synthesis & Playback).
    /// </summary>
    public sealed class ColorbarEngine : IDisposable
    {
        private ColorbarPatternType _currentPattern = ColorbarPatternType.SmpteRp219;
        private AudioTestToneType _currentTone = AudioTestToneType.Sine1kHzMinus18dBFS;
        private long _toneTickCounter = 0;

        // Audio Tone Synthesis & Real Playback
        private SoundPlayer? _soundPlayer;
        private MemoryStream? _audioWavStream;
        private bool _isAudioTonePlaying = false;
        private bool _isAudioMonitorEnabled = true;
        private double _monitorVolume = 0.4;
        private readonly object _audioLock = new();

        public bool IsAudioTonePlaying => _isAudioTonePlaying;

        public ColorbarPatternType CurrentPattern
        {
            get => _currentPattern;
            set => _currentPattern = value;
        }

        public AudioTestToneType CurrentTone
        {
            get => _currentTone;
            set
            {
                if (_currentTone != value)
                {
                    _currentTone = value;
                    if (_isAudioTonePlaying && _isAudioMonitorEnabled)
                    {
                        RestartAudioTone();
                    }
                }
            }
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

        #region Pattern Rendering (XAML Vector Layouts)

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

        private void RenderEbu100(Grid container)
        {
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

        private void RenderGridAlignment(Grid container)
        {
            var gridCanvas = new Grid { Background = new SolidColorBrush(Color.FromRgb(16, 16, 18)) };

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

        private static void AddColRect(Grid grid, int col, Color color)
        {
            var rect = new Rectangle { Fill = new SolidColorBrush(color) };
            Grid.SetColumn(rect, col);
            grid.Children.Add(rect);
        }

        #endregion

        #region Real Audio Test Tone Synthesis & Playback

        /// <summary>
        /// Bật hoặc tắt phát âm thanh Tone kiểm tra ra loa
        /// </summary>
        public void SetAudioToneOutput(bool enabled, double volume)
        {
            lock (_audioLock)
            {
                _isAudioMonitorEnabled = enabled;
                _monitorVolume = Math.Clamp(volume, 0.0, 1.0);

                if (_isAudioMonitorEnabled && _monitorVolume > 0.001)
                {
                    StartAudioTone();
                }
                else
                {
                    StopAudioTone();
                }
            }
        }

        /// <summary>
        /// Cập nhật âm lượng kiểm âm của Test Tone
        /// </summary>
        public void SetVolume(double volume)
        {
            lock (_audioLock)
            {
                _monitorVolume = Math.Clamp(volume, 0.0, 1.0);
                if (_isAudioMonitorEnabled && _isAudioTonePlaying)
                {
                    RestartAudioTone();
                }
            }
        }

        private void StartAudioTone()
        {
            try
            {
                StopAudioTone();

                byte[] wavBytes = GenerateTestToneWav(_currentTone, _monitorVolume);
                _audioWavStream = new MemoryStream(wavBytes);
                _audioWavStream.Position = 0;
                _soundPlayer = new SoundPlayer(_audioWavStream);
                _soundPlayer.Load();
                _soundPlayer.PlayLooping();
                _isAudioTonePlaying = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ColorbarEngine] Audio Tone Playback Error: {ex.Message}");
            }
        }

        public void StopAudioTone()
        {
            try
            {
                if (_soundPlayer != null)
                {
                    _soundPlayer.Stop();
                    _soundPlayer.Dispose();
                    _soundPlayer = null;
                }

                if (_audioWavStream != null)
                {
                    _audioWavStream.Dispose();
                    _audioWavStream = null;
                }

                _isAudioTonePlaying = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ColorbarEngine] Audio Tone Stop Error: {ex.Message}");
            }
        }

        private void RestartAudioTone()
        {
            Task.Run(() =>
            {
                lock (_audioLock)
                {
                    if (_isAudioMonitorEnabled && _monitorVolume > 0.001)
                    {
                        StartAudioTone();
                    }
                    else
                    {
                        StopAudioTone();
                    }
                }
            });
        }

        /// <summary>
        /// Tạo luồng chuẩn WAV PCM 16-bit 48kHz Stereo cho Test Tone
        /// </summary>
        private static byte[] GenerateTestToneWav(AudioTestToneType toneType, double volume)
        {
            const int sampleRate = 48000;
            const short channels = 2; // Stereo
            const short bitsPerSample = 16;
            const short bytesPerSample = bitsPerSample / 8;
            const short blockAlign = (short)(channels * bytesPerSample);
            const int byteRate = sampleRate * blockAlign;

            // Frequency and duration definition
            double freqLeft = 1000.0;
            double freqRight = 1000.0;
            double durationSeconds = 1.0;
            double targetDbFs = -18.0;

            switch (toneType)
            {
                case AudioTestToneType.Sine1kHzMinus18dBFS:
                    freqLeft = 1000.0;
                    freqRight = 1000.0;
                    targetDbFs = -18.0;
                    durationSeconds = 1.0;
                    break;

                case AudioTestToneType.Sine1kHzMinus20dBFS:
                    freqLeft = 1000.0;
                    freqRight = 1000.0;
                    targetDbFs = -20.0;
                    durationSeconds = 1.0;
                    break;

                case AudioTestToneType.Glits400Hz:
                    freqLeft = 400.0;
                    freqRight = 400.0;
                    targetDbFs = -18.0;
                    durationSeconds = 2.0; // 2 seconds pulse cycle
                    break;

                case AudioTestToneType.EbuToneIdent:
                    freqLeft = 1000.0;
                    freqRight = 1000.0;
                    targetDbFs = -18.0;
                    durationSeconds = 2.0; // 2 seconds pulse cycle
                    break;
            }

            int numSamples = (int)(sampleRate * durationSeconds);
            int dataChunkSize = numSamples * blockAlign;
            int totalFileSize = 44 + dataChunkSize;

            double peakAmplitude = 32767.0 * Math.Pow(10.0, targetDbFs / 20.0) * volume;

            using var ms = new MemoryStream(totalFileSize);
            using var writer = new BinaryWriter(ms);

            // RIFF Header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(totalFileSize - 8);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk (16 bytes)
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // SubChunk1Size (16 for PCM)
            writer.Write((short)1); // AudioFormat (1 = PCM)
            writer.Write(channels); // short (2 bytes)
            writer.Write(sampleRate); // int (4 bytes)
            writer.Write(byteRate); // int (4 bytes)
            writer.Write(blockAlign); // short (2 bytes)
            writer.Write(bitsPerSample); // short (2 bytes)

            // data chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataChunkSize);

            // Generate PCM samples
            for (int i = 0; i < numSamples; i++)
            {
                double time = (double)i / sampleRate;

                double leftSample = 0.0;
                double rightSample = 0.0;

                if (toneType == AudioTestToneType.Glits400Hz)
                {
                    // Left continuous 400Hz, Right interrupted
                    leftSample = Math.Sin(2.0 * Math.PI * freqLeft * time) * peakAmplitude;
                    // Interrupted right channel: 1.5s ON, 0.5s OFF
                    bool rightOn = (time % 2.0) < 1.4;
                    rightSample = rightOn ? Math.Sin(2.0 * Math.PI * freqRight * time) * peakAmplitude : 0.0;
                }
                else if (toneType == AudioTestToneType.EbuToneIdent)
                {
                    // Left continuous 1kHz, Right pulsed beep (0.3s beep every 2s)
                    leftSample = Math.Sin(2.0 * Math.PI * freqLeft * time) * peakAmplitude;
                    bool rightBeep = (time % 2.0) < 0.35;
                    rightSample = rightBeep ? Math.Sin(2.0 * Math.PI * freqRight * time) * peakAmplitude : 0.0;
                }
                else
                {
                    // Pure Sine continuous Stereo
                    leftSample = Math.Sin(2.0 * Math.PI * freqLeft * time) * peakAmplitude;
                    rightSample = Math.Sin(2.0 * Math.PI * freqRight * time) * peakAmplitude;
                }

                short lShort = (short)Math.Clamp(Math.Round(leftSample), short.MinValue, short.MaxValue);
                short rShort = (short)Math.Clamp(Math.Round(rightSample), short.MinValue, short.MaxValue);

                writer.Write(lShort);
                writer.Write(rShort);
            }

            return ms.ToArray();
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

        public void Dispose()
        {
            StopAudioTone();
        }

        #endregion
    }
}
