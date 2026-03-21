using System;
using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Autodraw;

public partial class ActionPrompt : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string actionData;
    public string ActionData
    {
        get => actionData;
        set
        {
            if (actionData != value)
            {
                actionData = value;
                OnPropertyChanged(nameof(ActionData));
            }
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public ActionPrompt()
    {
        DataContext = this;
        InitializeComponent();
        CloseAppButton.Click += CloseAppButton_Click;
        ActionType.SelectionChanged += ActionTypeOnSelectionChanged;
        MovementStyle.SelectionChanged += MovementStyleOnSelectionChanged;
    }

    public String Speed { get; set; }
    public String Delay { get; set; }
    public String InputData { get; set; }
    public int Selection { get; set; }
    public Action Callback { get; set; }
    public InputAction? Action { get; set; }
    private object? _rawActionData;

    public int MoveStyleIndex { get; set; }
    public string TweenDuration { get; set; } = "100";
    public bool IsTween => MoveStyleIndex == 1;

    private void CloseAppButton_Click(object? sender, RoutedEventArgs e) => Close();

    private bool isTracking = false;

    public void StartTracking()
    {
        if (isTracking)
        {
            // Stop tracking
            KeyDown -= OnLocalKeyDown;
            PointerPressed -= OnLocalPointerPressed;
            isTracking = false;
            return;
        }

        isTracking = true;

        switch (Selection)
        {
            case 0: // Left Click
            case 1: // Right Click
            case 2: // MoveTo
                var overlay = new OverlayWindow();
                overlay.Position = this.Position;
                overlay.Width = 400;
                overlay.Height = 300;
                overlay.ShowActivated = true;
                overlay.Closed += (_, _) =>
                {
                    if (overlay.CapturedPosition.HasValue)
                    {
                        var pos = overlay.CapturedPosition.Value;
                        var x = (int)pos.X;
                        var y = (int)pos.Y;
                        var label = Selection switch
                        {
                            0 => "Left Click",
                            1 => "Right Click",
                            _ => "Move To"
                        };
                        ActionData = $"{label} at X:{x}, Y:{y}";
                        _rawActionData = pos;
                    }
                    else
                    {
                        ActionData = "";
                    }
                    isTracking = false;
                };
                overlay.Show();
                break;
            case 4: // Key Down
            case 5: // Key Up
                KeyDown += OnLocalKeyDown;
                ActionData = "Press a key on this window...";
                break;
            default:
                Console.WriteLine($"Selection type '{Selection}' is not supported for window-local tracking.");
                break;
        }
    }

    private void OnLocalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        var x = (int)pos.X;
        var y = (int)pos.Y;

        Console.WriteLine($"Local Pointer: ({x}, {y})");
        ActionData = $"Click at X:{x}, Y:{y}";
        _rawActionData = new Vector2(x, y);

        PointerPressed -= OnLocalPointerPressed;
        isTracking = false;
    }

    private void OnLocalKeyDown(object? sender, KeyEventArgs e)
    {
        var keyEvent = Selection == 4 ? "Key Down" : "Key Up";

        Console.WriteLine($"{keyEvent}: {e.Key}");
        ActionData = $"{keyEvent}: {e.Key}";
        _rawActionData = e.Key.ToString();

        KeyDown -= OnLocalKeyDown;
        isTracking = false;
    }

    private void ActionTypeOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _rawActionData = null;
        ActionData = "";
        if (Selection == 3 || Selection == 6)
        {
            TrackAction.IsVisible = false;
            InputBox.IsVisible = true;
        }
        else
        {
            TrackAction.IsVisible = true;
            InputBox.IsVisible = false;
        }

        MovementStylePanel.IsVisible = Selection == 2;
        if (Selection == 2)
        {
            MovementStyleOnSelectionChanged(null, null);
        }
        else
        {
            TweenLabel.IsVisible = false;
            TweenSpeedBox.IsVisible = false;
        }
    }

    private void MovementStyleOnSelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        TweenLabel.IsVisible = IsTween;
        TweenSpeedBox.IsVisible = IsTween;
    }

    private void InputBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (Selection != 3 && Selection != 6) return;
        Console.WriteLine(InputData);
        _rawActionData = InputData;
    }

    public void Confirm()
    {
        if (_rawActionData is null) return;
        var _ActionType = Selection switch
        {
            1 => InputAction.ActionType.RightClick,
            2 => InputAction.ActionType.MoveTo,
            3 => InputAction.ActionType.WriteString,
            4 => InputAction.ActionType.KeyDown,
            5 => InputAction.ActionType.KeyUp,
            6 => InputAction.ActionType.Wait,
            _ => InputAction.ActionType.LeftClick
        };
        var _ActionData = _rawActionData;
        Action = new InputAction(_ActionType, _ActionData);
        if (Selection == 2 && IsTween)
        {
            Action.Speed = int.Parse(TweenDuration);
        }
        Callback();
    }

    public void Cancel() => Close();

    public void LoadFrom(InputAction source)
    {
        Selection = source.Action switch
        {
            InputAction.ActionType.LeftClick => 0,
            InputAction.ActionType.RightClick => 1,
            InputAction.ActionType.MoveTo => 2,
            InputAction.ActionType.WriteString => 3,
            InputAction.ActionType.KeyDown => 4,
            InputAction.ActionType.KeyUp => 5,
            InputAction.ActionType.Wait => 6,
            _ => 0
        };
        OnPropertyChanged(nameof(Selection));

        switch (source.Action)
        {
            case InputAction.ActionType.LeftClick:
            case InputAction.ActionType.RightClick:
            case InputAction.ActionType.MoveTo:
                if (source.Position.HasValue)
                {
                    _rawActionData = source.Position.Value;
                    var x = (int)source.Position.Value.X;
                    var y = (int)source.Position.Value.Y;
                    var label = source.Action switch
                    {
                        InputAction.ActionType.LeftClick => "Left Click",
                        InputAction.ActionType.RightClick => "Right Click",
                        _ => "Move To"
                    };
                    ActionData = $"{label} at X:{x}, Y:{y}";
                }
                break;
            case InputAction.ActionType.WriteString:
            case InputAction.ActionType.KeyDown:
            case InputAction.ActionType.KeyUp:
                _rawActionData = source.Data;
                InputData = source.Data ?? "";
                OnPropertyChanged(nameof(InputData));
                break;
            case InputAction.ActionType.Wait:
                _rawActionData = source.Delay.ToString();
                InputData = source.Delay.ToString();
                OnPropertyChanged(nameof(InputData));
                break;
        }

        if (source.Action == InputAction.ActionType.MoveTo && source.Speed.HasValue && source.Speed.Value > 0)
        {
            MoveStyleIndex = 1;
            TweenDuration = source.Speed.Value.ToString();
        }
        else
        {
            MoveStyleIndex = 0;
            TweenDuration = "100";
        }
        OnPropertyChanged(nameof(MoveStyleIndex));
        OnPropertyChanged(nameof(TweenDuration));
        OnPropertyChanged(nameof(ActionData));
    }
}
