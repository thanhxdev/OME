using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace OME_PLAYOUT.Models
{
    /// <summary>
    /// Model đại diện cho một luồng NDI tìm thấy trên mạng LAN.
    /// Hỗ trợ thông tin luồng, trạng thái online, và thumbnail preview.
    /// </summary>
    public sealed class NdiPreviewItem : INotifyPropertyChanged
    {
        private string _streamName = string.Empty;
        private string _machineName = string.Empty;
        private string _ipOrUrl = string.Empty;
        private bool _isOnline = true;
        private DateTime _lastSeen = DateTime.UtcNow;
        private WriteableBitmap? _thumbnailBitmap;
        private bool _isSelected;
        private bool _isOnAir;
        private bool _isCued;
        private string _format = "1080p59.94";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string StreamName
        {
            get => _streamName;
            set 
            { 
                if (_streamName != value) 
                { 
                    _streamName = value; 
                    OnPropertyChanged(); 
                    ParseMachineName();
                } 
            }
        }

        public string MachineName
        {
            get => _machineName;
            set { if (_machineName != value) { _machineName = value; OnPropertyChanged(); } }
        }

        public string IpOrUrl
        {
            get => _ipOrUrl;
            set { if (_ipOrUrl != value) { _ipOrUrl = value; OnPropertyChanged(); } }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set 
            { 
                if (_isOnline != value) 
                { 
                    _isOnline = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(StatusText)); 
                    OnPropertyChanged(nameof(StatusColor)); 
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

        public string Format
        {
            get => _format;
            set { if (_format != value) { _format = value; OnPropertyChanged(); } }
        }

        public string StatusText => _isOnline ? "ONLINE (LAN NDI)" : "DISCONNECTED";

        public string StatusColor => _isOnline ? "#38BDF8" : "#64748B";

        private void ParseMachineName()
        {
            // NDI stream format typically: "MACHINE_NAME (StreamName)"
            if (string.IsNullOrEmpty(_streamName)) return;

            int openParen = _streamName.IndexOf('(');
            int closeParen = _streamName.IndexOf(')');
            if (openParen > 0 && closeParen > openParen)
            {
                MachineName = _streamName.Substring(0, openParen).Trim();
            }
            else
            {
                MachineName = "LAN NDI Host";
            }
        }
    }
}
