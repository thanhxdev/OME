using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OME_PLAYOUT.Models;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Master Control 24/7 Rundown Playlist Automation Scheduler.
    /// Manages frame-accurate C-REM (Clip Remaining countdown), Schedule Delta tracking,
    /// seamless Take/Cue transitions, and secondary DSK events.
    /// Fully dynamic: supports add/remove/reorder, serialization, and empty rundown standby.
    /// </summary>
    public sealed class BroadcastPlaylistScheduler : INotifyPropertyChanged
    {
        public ObservableCollection<PlaylistItem> RundownList { get; } = new();

        private PlaylistItem? _currentItem;
        private PlaylistItem? _nextItem;
        private int _currentIndex = -1;
        private int _nextIndex = -1;

        private TimeSpan _clipRemaining = TimeSpan.Zero;
        private TimeSpan _clipElapsed = TimeSpan.Zero;
        private bool _isAutoMode = true;
        private bool _isHold = false;
        private string _scheduleDeltaText = "DELTA: STANDBY";

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<PlaylistItem?, PlaylistItem?>? OnAirSwitched;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public PlaylistItem? CurrentItem
        {
            get => _currentItem;
            private set
            {
                if (_currentItem != value)
                {
                    _currentItem = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentTitle));
                    OnPropertyChanged(nameof(CurrentHouseId));
                }
            }
        }

        public PlaylistItem? NextItem
        {
            get => _nextItem;
            private set
            {
                if (_nextItem != value)
                {
                    _nextItem = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NextTitle));
                    OnPropertyChanged(nameof(NextHouseId));
                }
            }
        }

        public int CurrentIndex => _currentIndex;
        public int NextIndex => _nextIndex;

        public string CurrentTitle => CurrentItem?.Title ?? "NO ON-AIR EVENT (STANDBY)";
        public string CurrentHouseId => CurrentItem?.HouseId ?? "STANDBY";
        public string NextTitle => NextItem?.Title ?? (RundownList.Count == 0 ? "NO CUED EVENT" : "END OF RUNDOWN");
        public string NextHouseId => NextItem?.HouseId ?? "NONE";

        public TimeSpan ClipRemainingTime
        {
            get => _clipRemaining;
            set
            {
                if (_clipRemaining != value)
                {
                    _clipRemaining = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClipRemainingTimecode));
                    OnPropertyChanged(nameof(CountdownColor));
                    OnPropertyChanged(nameof(IsCountdownCritical));
                }
            }
        }

        public TimeSpan ClipElapsedTime
        {
            get => _clipElapsed;
            set
            {
                if (_clipElapsed != value)
                {
                    _clipElapsed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClipElapsedTimecode));
                    OnPropertyChanged(nameof(ProgressFraction));
                }
            }
        }

        public double ProgressFraction
        {
            get
            {
                if (CurrentItem == null || CurrentItem.Duration.TotalSeconds <= 0) return 0.0;
                return Math.Clamp(_clipElapsed.TotalSeconds / CurrentItem.Duration.TotalSeconds, 0.0, 1.0);
            }
        }

        public bool IsAutoMode
        {
            get => _isAutoMode;
            set
            {
                if (_isAutoMode != value)
                {
                    _isAutoMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AutoModeBadge));
                }
            }
        }

        public string AutoModeBadge => _isAutoMode ? "AUTO 🟢" : "MANUAL 🟡";

        public bool IsHold
        {
            get => _isHold;
            set
            {
                if (_isHold != value)
                {
                    _isHold = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ScheduleDeltaText
        {
            get => _scheduleDeltaText;
            set
            {
                if (_scheduleDeltaText != value)
                {
                    _scheduleDeltaText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ClipRemainingTimecode
        {
            get
            {
                if (CurrentItem == null) return "00:00:00:00";
                var ts = _clipRemaining < TimeSpan.Zero ? TimeSpan.Zero : _clipRemaining;
                int frames = (int)((ts.Milliseconds / 1000.0) * 59.94);
                return $"-{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}:{frames:D2}";
            }
        }

        public string ClipElapsedTimecode
        {
            get
            {
                if (CurrentItem == null) return "00:00:00:00";
                var ts = _clipElapsed;
                int frames = (int)((ts.Milliseconds / 1000.0) * 59.94);
                return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}:{frames:D2}";
            }
        }

        public string CountdownColor
        {
            get
            {
                if (CurrentItem == null) return "#94A3B8";
                if (_clipRemaining.TotalSeconds <= 10.0) return "#EF4444"; // Red Alert (< 10s)
                if (_clipRemaining.TotalSeconds <= 30.0) return "#F59E0B"; // Amber Warning (< 30s)
                return "#00E676"; // Normal Broadcast Green
            }
        }

        public bool IsCountdownCritical => CurrentItem != null && _clipRemaining.TotalSeconds <= 10.0;

        public BroadcastPlaylistScheduler()
        {
            // Pure Dynamic List: start in clean Standby mode without hardcoded sample items
            ResetStandbyState();
        }

        public void ResetStandbyState()
        {
            _currentIndex = -1;
            _nextIndex = -1;
            CurrentItem = null;
            NextItem = null;
            ClipRemainingTime = TimeSpan.Zero;
            ClipElapsedTime = TimeSpan.Zero;
            IsHold = false;
            ScheduleDeltaText = "DELTA: STANDBY";
            OnPropertyChanged(nameof(ProgressFraction));
        }

        #region Dynamic Rundown Management

        public void Clear()
        {
            RundownList.Clear();
            ResetStandbyState();
            OnAirSwitched?.Invoke(null, null);
        }

        public void AddClipItem(string filePath, TimeSpan? duration = null)
        {
            string fileName = Path.GetFileName(filePath);
            string houseId = $"VOD-{DateTime.Now:yyyyMMdd}-{RundownList.Count + 1:D3}";

            var item = new PlaylistItem
            {
                HouseId = houseId,
                Title = $"CLIP: {fileName.ToUpperInvariant()}",
                SourceType = PlaylistItemSourceType.ClipFile,
                FilePath = filePath,
                ScheduledTime = CalculateNextScheduledTime(),
                Duration = duration ?? TimeSpan.FromMinutes(2),
                Status = PlaylistItemStatus.Ready,
                IsDskLogo = true,
                IsDskTicker = true
            };

            RundownList.Add(item);
            AutoCueIfIdle();
        }

        public void AddLiveItem(int livePortIndex, string title, TimeSpan duration, string lowerThird = "")
        {
            string houseId = $"LIVE-CAM-{livePortIndex:D2}";
            var item = new PlaylistItem
            {
                HouseId = houseId,
                Title = string.IsNullOrWhiteSpace(title) ? $"TRỰC TIẾP LIVE CAM {livePortIndex:D2}" : title,
                SourceType = PlaylistItemSourceType.LiveIngest,
                LivePortIndex = Math.Clamp(livePortIndex, 1, 10),
                ScheduledTime = CalculateNextScheduledTime(),
                Duration = duration,
                Status = PlaylistItemStatus.Ready,
                IsDskLogo = true,
                IsDskTicker = true,
                IsDskLowerThird = !string.IsNullOrWhiteSpace(lowerThird),
                LowerThirdText = lowerThird
            };

            RundownList.Add(item);
            AutoCueIfIdle();
        }

        public void AddColorBarsItem(string title, TimeSpan duration)
        {
            var item = new PlaylistItem
            {
                HouseId = "SYS-BARS-SMPTE",
                Title = string.IsNullOrWhiteSpace(title) ? "BẢNG MÀU CHUẨN SMPTE COLOR BARS" : title,
                SourceType = PlaylistItemSourceType.ColorBars,
                ScheduledTime = CalculateNextScheduledTime(),
                Duration = duration,
                Status = PlaylistItemStatus.Ready,
                IsDskLogo = false,
                IsDskTicker = false
            };

            RundownList.Add(item);
            AutoCueIfIdle();
        }

        public void AddCommercialBreakItem(string title, TimeSpan duration)
        {
            var item = new PlaylistItem
            {
                HouseId = $"AD-{DateTime.Now:HHmmss}",
                Title = string.IsNullOrWhiteSpace(title) ? "KHỐI QUẢNG CÁO THƯƠNG MẠI SCTE-35" : title,
                SourceType = PlaylistItemSourceType.CommercialBreak,
                ScheduledTime = CalculateNextScheduledTime(),
                Duration = duration,
                Status = PlaylistItemStatus.Ready,
                IsDskLogo = false,
                IsDskTicker = false
            };

            RundownList.Add(item);
            AutoCueIfIdle();
        }

        public bool RemoveItem(int index)
        {
            if (index < 0 || index >= RundownList.Count) return false;

            var itemToRemove = RundownList[index];
            bool isCurrent = index == _currentIndex;
            bool isNext = index == _nextIndex;

            RundownList.RemoveAt(index);

            if (RundownList.Count == 0)
            {
                Clear();
                return true;
            }

            // Adjust indices
            if (index < _currentIndex) _currentIndex--;
            else if (isCurrent) _currentIndex = -1;

            if (index < _nextIndex) _nextIndex--;
            else if (isNext) _nextIndex = -1;

            // Reassign Current & Next pointers safely
            if (_currentIndex >= 0 && _currentIndex < RundownList.Count)
            {
                CurrentItem = RundownList[_currentIndex];
            }
            else
            {
                CurrentItem = null;
            }

            if (_nextIndex >= 0 && _nextIndex < RundownList.Count)
            {
                NextItem = RundownList[_nextIndex];
            }
            else
            {
                // Auto-cue first available ready item
                AutoCueIfIdle();
            }

            OnAirSwitched?.Invoke(CurrentItem, NextItem);
            return true;
        }

        public bool MoveItemUp(int index)
        {
            if (index <= 0 || index >= RundownList.Count) return false;

            int targetIndex = index - 1;
            var item = RundownList[index];
            RundownList.RemoveAt(index);
            RundownList.Insert(targetIndex, item);

            UpdateIndicesAfterReorder(index, targetIndex);
            return true;
        }

        public bool MoveItemDown(int index)
        {
            if (index < 0 || index >= RundownList.Count - 1) return false;

            int targetIndex = index + 1;
            var item = RundownList[index];
            RundownList.RemoveAt(index);
            RundownList.Insert(targetIndex, item);

            UpdateIndicesAfterReorder(index, targetIndex);
            return true;
        }

        private void UpdateIndicesAfterReorder(int oldIndex, int newIndex)
        {
            if (_currentIndex == oldIndex) _currentIndex = newIndex;
            else if (_currentIndex == newIndex) _currentIndex = oldIndex;

            if (_nextIndex == oldIndex) _nextIndex = newIndex;
            else if (_nextIndex == newIndex) _nextIndex = oldIndex;

            CurrentItem = _currentIndex >= 0 && _currentIndex < RundownList.Count ? RundownList[_currentIndex] : null;
            NextItem = _nextIndex >= 0 && _nextIndex < RundownList.Count ? RundownList[_nextIndex] : null;
        }

        private TimeSpan CalculateNextScheduledTime()
        {
            if (RundownList.Count == 0)
            {
                var now = DateTime.Now;
                return new TimeSpan(now.Hour, now.Minute, now.Second);
            }

            var lastItem = RundownList[^1];
            return lastItem.ScheduledTime.Add(lastItem.Duration);
        }

        private void AutoCueIfIdle()
        {
            if (_nextIndex < 0 && RundownList.Count > 0)
            {
                for (int i = 0; i < RundownList.Count; i++)
                {
                    if (i != _currentIndex && RundownList[i].Status != PlaylistItemStatus.Played)
                    {
                        ExecuteCue(i);
                        return;
                    }
                }

                // If all were played or none found, cue item 0
                if (_currentIndex != 0 && RundownList.Count > 0)
                {
                    ExecuteCue(0);
                }
            }
        }

        #endregion

        #region MCR Playout Transport Execution

        public void ExecuteTake()
        {
            if (RundownList.Count == 0) return;

            // If no next item was cued, cue the first candidate
            if (_nextIndex < 0 || _nextIndex >= RundownList.Count)
            {
                for (int i = 0; i < RundownList.Count; i++)
                {
                    if (i != _currentIndex)
                    {
                        _nextIndex = i;
                        break;
                    }
                }

                if (_nextIndex < 0 && RundownList.Count > 0)
                {
                    _nextIndex = 0;
                }
            }

            if (_nextIndex >= 0 && _nextIndex < RundownList.Count)
            {
                // Mark previous on-air item as played
                if (_currentIndex >= 0 && _currentIndex < RundownList.Count && _currentIndex != _nextIndex)
                {
                    RundownList[_currentIndex].Status = PlaylistItemStatus.Played;
                }

                // Switch Next to Current
                _currentIndex = _nextIndex;
                CurrentItem = RundownList[_currentIndex];
                CurrentItem.Status = PlaylistItemStatus.Playing;

                // Advance Next to the subsequent item
                if (RundownList.Count > 1)
                {
                    _nextIndex = (_currentIndex + 1) % RundownList.Count;
                    NextItem = RundownList[_nextIndex];
                    NextItem.Status = PlaylistItemStatus.Cued;
                }
                else
                {
                    // Single item rundown: Loop on itself without re-cueing
                    _nextIndex = _currentIndex;
                    NextItem = CurrentItem;
                }

                // Reset countdown to current clip duration
                ClipRemainingTime = CurrentItem.Duration;
                ClipElapsedTime = TimeSpan.Zero;
                IsHold = false;
                ScheduleDeltaText = "DELTA: ON TIME";

                OnAirSwitched?.Invoke(CurrentItem, NextItem);
                OnPropertyChanged(nameof(ProgressFraction));
            }
        }

        public void ExecuteHold()
        {
            IsHold = !IsHold;
            if (CurrentItem != null)
            {
                CurrentItem.Status = IsHold ? PlaylistItemStatus.Hold : PlaylistItemStatus.Playing;
            }
        }

        public void ExecuteSkip()
        {
            if (RundownList.Count <= 1) return;

            if (_nextIndex >= 0 && _nextIndex < RundownList.Count)
            {
                RundownList[_nextIndex].Status = PlaylistItemStatus.Ready;
                _nextIndex = (_nextIndex + 1) % RundownList.Count;
                if (_nextIndex == _currentIndex && RundownList.Count > 1)
                {
                    _nextIndex = (_nextIndex + 1) % RundownList.Count;
                }

                NextItem = RundownList[_nextIndex];
                NextItem.Status = PlaylistItemStatus.Cued;
                OnAirSwitched?.Invoke(CurrentItem, NextItem);
            }
        }

        public void ExecuteCue(int index)
        {
            if (index < 0 || index >= RundownList.Count || index == _currentIndex) return;

            if (_nextIndex >= 0 && _nextIndex < RundownList.Count && _nextIndex != _currentIndex)
            {
                RundownList[_nextIndex].Status = PlaylistItemStatus.Ready;
            }

            _nextIndex = index;
            NextItem = RundownList[_nextIndex];
            NextItem.Status = PlaylistItemStatus.Cued;
            OnAirSwitched?.Invoke(CurrentItem, NextItem);
        }

        public void TickFrame(double deltaSeconds = 1.0 / 59.94)
        {
            if (CurrentItem == null) return;

            if (!IsHold)
            {
                ClipRemainingTime -= TimeSpan.FromSeconds(deltaSeconds);
                ClipElapsedTime += TimeSpan.FromSeconds(deltaSeconds);

                if (ClipRemainingTime <= TimeSpan.Zero)
                {
                    if (IsAutoMode)
                    {
                        ExecuteTake();
                    }
                    else
                    {
                        ClipRemainingTime = TimeSpan.Zero;
                        IsHold = true;
                    }
                }
            }
        }

        #endregion

        #region JSON Serialization & Persistence

        public sealed class RundownDto
        {
            public List<PlaylistItemDto> Items { get; set; } = new();
        }

        public sealed class PlaylistItemDto
        {
            public string HouseId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string SourceType { get; set; } = "ClipFile";
            public string FilePath { get; set; } = string.Empty;
            public int LivePortIndex { get; set; } = 1;
            public string ScheduledTime { get; set; } = "00:00:00";
            public string Duration { get; set; } = "00:05:00";
            public bool IsDskLogo { get; set; } = true;
            public bool IsDskLowerThird { get; set; } = false;
            public bool IsDskTicker { get; set; } = true;
            public string AudioLayout { get; set; } = "CH 1-2 (Stereo)";
            public string LowerThirdText { get; set; } = string.Empty;
        }

        public void SavePlaylistJson(string filePath)
        {
            var dto = new RundownDto();
            foreach (var item in RundownList)
            {
                dto.Items.Add(new PlaylistItemDto
                {
                    HouseId = item.HouseId,
                    Title = item.Title,
                    SourceType = item.SourceType.ToString(),
                    FilePath = item.FilePath,
                    LivePortIndex = item.LivePortIndex,
                    ScheduledTime = item.ScheduledTime.ToString(),
                    Duration = item.Duration.ToString(),
                    IsDskLogo = item.IsDskLogo,
                    IsDskLowerThird = item.IsDskLowerThird,
                    IsDskTicker = item.IsDskTicker,
                    AudioLayout = item.AudioLayout,
                    LowerThirdText = item.LowerThirdText
                });
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(dto, options);
            File.WriteAllText(filePath, json);
        }

        public bool LoadPlaylistJson(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                string json = File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<RundownDto>(json);
                if (dto == null || dto.Items == null) return false;

                Clear();

                foreach (var d in dto.Items)
                {
                    Enum.TryParse(d.SourceType, out PlaylistItemSourceType st);
                    TimeSpan.TryParse(d.ScheduledTime, out TimeSpan sched);
                    TimeSpan.TryParse(d.Duration, out TimeSpan dur);

                    RundownList.Add(new PlaylistItem
                    {
                        HouseId = d.HouseId,
                        Title = d.Title,
                        SourceType = st,
                        FilePath = d.FilePath,
                        LivePortIndex = d.LivePortIndex,
                        ScheduledTime = sched,
                        Duration = dur,
                        Status = PlaylistItemStatus.Ready,
                        IsDskLogo = d.IsDskLogo,
                        IsDskLowerThird = d.IsDskLowerThird,
                        IsDskTicker = d.IsDskTicker,
                        AudioLayout = d.AudioLayout,
                        LowerThirdText = d.LowerThirdText
                    });
                }

                AutoCueIfIdle();
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
