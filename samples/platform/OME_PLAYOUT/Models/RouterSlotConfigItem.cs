using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OME_PLAYOUT.Models
{
    /// <summary>
    /// Model đại diện cho 1 trong 11 Slot đích đến của Router Playout (Slot 00 .. Slot 10).
    /// Hỗ trợ chọn tín hiệu từ SRT Decode và WebRTC Decode, cùng hiển thị trạng thái real-time.
    /// </summary>
    public sealed class RouterSlotConfigItem : INotifyPropertyChanged
    {
        private int _slotIndex;
        private string _slotName = "Slot 00";
        private string _slotRole = "PGM Ingest";
        private string _slotBadgeColor = "#EF4444";
        private int _selectedMatrixSlot = -1;
        private string _selectedSourceTitle = "⚪ [Trống] Chưa gán tín hiệu";
        private string _signalOrigin = "None";
        private bool _isSignalActive;
        private string _resolutionText = "--";
        private string _signalDotColor = "#64748B";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int SlotIndex
        {
            get => _slotIndex;
            set
            {
                if (_slotIndex != value)
                {
                    _slotIndex = value;
                    OnPropertyChanged();
                    SlotName = $"Slot {_slotIndex:D2}";
                    SlotRole = _slotIndex == 0 ? "PGM Ingest" : $"CAM {_slotIndex:D2}";
                    SlotBadgeColor = _slotIndex == 0 ? "#EF4444" : "#0284C7";
                }
            }
        }

        public string SlotName
        {
            get => _slotName;
            set { if (_slotName != value) { _slotName = value; OnPropertyChanged(); } }
        }

        public string SlotRole
        {
            get => _slotRole;
            set { if (_slotRole != value) { _slotRole = value; OnPropertyChanged(); } }
        }

        public string SlotBadgeColor
        {
            get => _slotBadgeColor;
            set { if (_slotBadgeColor != value) { _slotBadgeColor = value; OnPropertyChanged(); } }
        }

        public int SelectedMatrixSlot
        {
            get => _selectedMatrixSlot;
            set { if (_selectedMatrixSlot != value) { _selectedMatrixSlot = value; OnPropertyChanged(); } }
        }

        public string SelectedSourceTitle
        {
            get => _selectedSourceTitle;
            set { if (_selectedSourceTitle != value) { _selectedSourceTitle = value; OnPropertyChanged(); } }
        }

        public string SignalOrigin
        {
            get => _signalOrigin;
            set { if (_signalOrigin != value) { _signalOrigin = value; OnPropertyChanged(); } }
        }

        public bool IsSignalActive
        {
            get => _isSignalActive;
            set
            {
                if (_isSignalActive != value)
                {
                    _isSignalActive = value;
                    OnPropertyChanged();
                    SignalDotColor = _isSignalActive ? "#00E676" : "#64748B";
                }
            }
        }

        public string ResolutionText
        {
            get => _resolutionText;
            set { if (_resolutionText != value) { _resolutionText = value; OnPropertyChanged(); } }
        }

        public string SignalDotColor
        {
            get => _signalDotColor;
            set { if (_signalDotColor != value) { _signalDotColor = value; OnPropertyChanged(); } }
        }
    }
}
