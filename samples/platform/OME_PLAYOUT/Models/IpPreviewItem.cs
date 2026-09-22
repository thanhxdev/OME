using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace OME_PLAYOUT.Models
{
    /// <summary>
    /// Model đại diện cho một kênh video IP Output Matrix Router (từ SRT DECODE hoặc WEBRTC DECODE).
    /// Hỗ trợ thông số kỹ thuật, trạng thái online, và thumbnail thời gian thực.
    /// </summary>
    public sealed class IpPreviewItem : INotifyPropertyChanged
    {
        private int _slotIndex;
        private string _portName = string.Empty;
        private string _displayName = string.Empty;
        private string _origin = "SRT Decode";
        private string _originBadgeColor = "#00E5FF";
        private int _width;
        private int _height;
        private double _fps;
        private bool _isActive;
        private DateTime _lastSeen = DateTime.UtcNow;
        private WriteableBitmap? _thumbnailBitmap;
        private bool _isSelected;
        private bool _isOnAir;
        private bool _isCued;
        private int _assignedPlayoutPort = -1; // -1 nếu chưa gán vào Port 1..10

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int SlotIndex
        {
            get => _slotIndex;
            set { if (_slotIndex != value) { _slotIndex = value; OnPropertyChanged(); } }
        }

        public string PortName
        {
            get => _portName;
            set { if (_portName != value) { _portName = value; OnPropertyChanged(); } }
        }

        public string DisplayName
        {
            get => _displayName;
            set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
        }

        public string Origin
        {
            get => _origin;
            set { if (_origin != value) { _origin = value; OnPropertyChanged(); } }
        }

        public string OriginBadgeColor
        {
            get => _originBadgeColor;
            set { if (_originBadgeColor != value) { _originBadgeColor = value; OnPropertyChanged(); } }
        }

        public int Width
        {
            get => _width;
            set 
            { 
                if (_width != value) 
                { 
                    _width = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(ResolutionText)); 
                } 
            }
        }

        public int Height
        {
            get => _height;
            set 
            { 
                if (_height != value) 
                { 
                    _height = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(ResolutionText)); 
                } 
            }
        }

        public double Fps
        {
            get => _fps;
            set 
            { 
                if (Math.Abs(_fps - value) > 0.01) 
                { 
                    _fps = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(ResolutionText)); 
                } 
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set 
            { 
                if (_isActive != value) 
                { 
                    _isActive = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(StatusText)); 
                    OnPropertyChanged(nameof(StatusColor)); 
                    OnPropertyChanged(nameof(SignalDotIcon)); 
                    OnPropertyChanged(nameof(SignalDotColor)); 
                    OnPropertyChanged(nameof(DisplayNameColor)); 
                    OnPropertyChanged(nameof(SlotRowBackground)); 
                } 
            }
        }

        public DateTime LastSeen
        {
            get => _lastSeen;
            set { if (_lastSeen != value) { _lastSeen = value; OnPropertyChanged(); } }
        }

        public WriteableBitmap? ThumbnailBitmap
        {
            get => _thumbnailBitmap;
            set { if (_thumbnailBitmap != value) { _thumbnailBitmap = value; OnPropertyChanged(); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public bool IsOnAir
        {
            get => _isOnAir;
            set { if (_isOnAir != value) { _isOnAir = value; OnPropertyChanged(); } }
        }

        public bool IsCued
        {
            get => _isCued;
            set { if (_isCued != value) { _isCued = value; OnPropertyChanged(); } }
        }

        public int AssignedPlayoutPort
        {
            get => _assignedPlayoutPort;
            set 
            { 
                if (_assignedPlayoutPort != value) 
                { 
                    _assignedPlayoutPort = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(AssignedPortText));
                    OnPropertyChanged(nameof(IsAssigned));
                    OnPropertyChanged(nameof(AssignedPortBadgeColor));
                    OnPropertyChanged(nameof(AssignedPortBorderColor));
                    OnPropertyChanged(nameof(AssignedPortTextColor));
                } 
            }
        }

        public bool IsAssigned => _assignedPlayoutPort >= 0;

        public string AssignedPortText => _assignedPlayoutPort >= 0 ? $"Port {_assignedPlayoutPort}" : "Chưa gán";

        public string AssignedPortBadgeColor => _assignedPlayoutPort >= 0 ? "#F59E0B" : "#1E293B";

        public string AssignedPortBorderColor => _assignedPlayoutPort >= 0 ? "#FBBF24" : "#334155";

        public string AssignedPortTextColor => _assignedPlayoutPort >= 0 ? "#000000" : "#94A3B8";

        public string ResolutionText => _width > 0 && _height > 0 ? $"{_width}x{_height} @ {_fps:F2}fps" : "Chờ tín hiệu...";

        public string StatusText => _isActive ? "LIVE (0ms VRAM)" : "STANDBY";

        public string StatusColor => _isActive ? "#00E676" : "#64748B";

        public string SlotBadgeText => $"Slot {_slotIndex:D2}";

        public string SignalDotIcon => _isActive ? "🟢" : "⚪";

        public string SignalDotColor => _isActive ? "#00E676" : "#64748B";

        public string DisplayNameColor => _isActive ? "#FFFFFF" : "#94A3B8";

        public string SlotRowBackground => _isActive ? "#0D1829" : "#080D18";
    }
}
