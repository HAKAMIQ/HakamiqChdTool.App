using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Shell;

namespace HakamiqChdTool.App.ViewModels;

public sealed class TaskbarSessionProgressViewModel : INotifyPropertyChanged
{
    private double _normalizedProgress;
    private TaskbarItemProgressState _state = TaskbarItemProgressState.None;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double NormalizedProgress
    {
        get => _normalizedProgress;
        set
        {
            double clamped = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            if (_normalizedProgress == clamped)
            {
                return;
            }

            _normalizedProgress = clamped;
            OnPropertyChanged();
        }
    }

    public TaskbarItemProgressState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}