using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OME_PLAYOUT.Models
{
    public enum PlaylistItemSourceType
    {
        ClipFile,
        LiveIngest,
        ColorBars,
        CommercialBreak
    }

    public enum PlaylistTransitionType
    {
        Cut,
        Mix,
        Wipe
    }

    public enum PlaylistItemStatus
    {
        Ready,
        Cued,
        Playing,
        Hold,
        Played,
        Error
    }

    /// <summary>
    /// Represents a broadcast event item within the 24/7 Master Control Rundown.
    /// Supports frame-accurate duration, in/out points, DSK secondary events, and live routing.
    /// </summary>
    public sealed class PlaylistItem : INotifyPropertyChanged
    {
        private string _houseId = string.Empty;
        private string _title = string.Empty;
        private PlaylistItemSourceType _sourceType = PlaylistItemSourceType.ClipFile;
        private string _filePath = string.Empty;
        private int _livePortIndex = 1;
        private TimeSpan _scheduledTime;
        private TimeSpan _duration = TimeSpan.FromMinutes(5);
        private TimeSpan _inPoint = TimeSpan.Zero;
        private TimeSpan _outPoint = TimeSpan.Zero;
        private PlaylistTransitionType _transitionType = PlaylistTransitionType.Cut;
        private int _transitionDurationMs = 1000;
        private PlaylistItemStatus _status = PlaylistItemStatus.Ready;
        private bool _isDskLogo = true;
        private bool _isDskLowerThird = false;
        private bool _isDskTicker = true;
        private string _audioLayout = "CH 1-2 (Stereo)";
        private string _lowerThirdText = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string HouseId
        {
            get => _houseId;
            set { if (_houseId != value) { _houseId = value; OnPropertyChanged(); } }
        }

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public PlaylistItemSourceType SourceType
        {
            get => _sourceType;
            set 
            { 
                if (_sourceType != value) 
                { 
                    _sourceType = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(SourceBadge)); 
                } 
            }
        }

        public string FilePath
        {
            get => _filePath;
            set { if (_filePath != value) { _filePath = value; OnPropertyChanged(); } }
        }

        public int LivePortIndex
        {
            get => _livePortIndex;
            set 
            { 
                if (_livePortIndex != value) 
                { 
                    _livePortIndex = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(SourceBadge)); 
                } 
            }
        }

        public TimeSpan ScheduledTime
        {
            get => _scheduledTime;
            set 
            { 
                if (_scheduledTime != value) 
                { 
                    _scheduledTime = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(ScheduledTimeStr)); 
                } 
            }
        }

        public TimeSpan Duration
        {
            get => _duration;
            set 
            { 
                if (_duration != value) 
                { 
                    _duration = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(DurationTimecode)); 
                } 
            }
        }

        public TimeSpan InPoint
        {
            get => _inPoint;
            set { if (_inPoint != value) { _inPoint = value; OnPropertyChanged(); } }
        }

        public TimeSpan OutPoint
        {
            get => _outPoint;
            set { if (_outPoint != value) { _outPoint = value; OnPropertyChanged(); } }
        }

        public PlaylistTransitionType TransitionType
        {
            get => _transitionType;
            set { if (_transitionType != value) { _transitionType = value; OnPropertyChanged(); } }
        }

        public int TransitionDurationMs
        {
            get => _transitionDurationMs;
            set { if (_transitionDurationMs != value) { _transitionDurationMs = value; OnPropertyChanged(); } }
        }

        public PlaylistItemStatus Status
        {
            get => _status;
            set 
            { 
                if (_status != value) 
                { 
                    _status = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(StatusBadge)); 
                    OnPropertyChanged(nameof(StatusColor)); 
                } 
            }
        }

        public bool IsDskLogo
        {
            get => _isDskLogo;
            set { if (_isDskLogo != value) { _isDskLogo = value; OnPropertyChanged(); } }
        }

        public bool IsDskLowerThird
        {
            get => _isDskLowerThird;
            set { if (_isDskLowerThird != value) { _isDskLowerThird = value; OnPropertyChanged(); } }
        }

        public bool IsDskTicker
        {
            get => _isDskTicker;
            set { if (_isDskTicker != value) { _isDskTicker = value; OnPropertyChanged(); } }
        }

        public string AudioLayout
        {
            get => _audioLayout;
            set { if (_audioLayout != value) { _audioLayout = value; OnPropertyChanged(); } }
        }

        public string LowerThirdText
        {
            get => _lowerThirdText;
            set { if (_lowerThirdText != value) { _lowerThirdText = value; OnPropertyChanged(); } }
        }

        // ─── UI Presentation Helpers ─────────────────────────────

        public string ScheduledTimeStr => ScheduledTime.ToString(@"hh\:mm\:ss");

        public string DurationTimecode => Duration.ToString(@"hh\:mm\:ss") + ":00";

        public string SourceBadge => SourceType switch
        {
            PlaylistItemSourceType.ClipFile => "📁 CLIP FILE",
            PlaylistItemSourceType.LiveIngest => $"📡 LIVE CAM {LivePortIndex:D2}",
            PlaylistItemSourceType.ColorBars => "📊 SMPTE BARS",
            PlaylistItemSourceType.CommercialBreak => "📢 SCTE BREAK",
            _ => "UNKNOWN"
        };

        public string StatusBadge => Status switch
        {
            PlaylistItemStatus.Playing => "▶ ON AIR",
            PlaylistItemStatus.Cued => "⏳ CUED",
            PlaylistItemStatus.Hold => "⏸ HOLD",
            PlaylistItemStatus.Played => "✔ DONE",
            PlaylistItemStatus.Error => "❌ ERROR",
            _ => "READY"
        };

        public string StatusColor => Status switch
        {
            PlaylistItemStatus.Playing => "#EF4444", // Red On-Air
            PlaylistItemStatus.Cued => "#00E676",    // Bright Green Cued
            PlaylistItemStatus.Hold => "#F59E0B",    // Amber Hold
            PlaylistItemStatus.Played => "#64748B",  // Muted Done
            PlaylistItemStatus.Error => "#DC2626",   // Red Alert
            _ => "#94A3B8"                           // Normal Slate
        };
    }
}
