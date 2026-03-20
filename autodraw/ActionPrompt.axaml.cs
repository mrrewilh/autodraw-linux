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
    }

    public String Speed { get; set; }
    public String Delay { get; set; }
    public String InputData { get; set; }
    public int Selection { get; set; }
    public Action Callback { get; set; }
    public InputAction? Action { get; set; }
    private object? _rawActionData;

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
            case 2: // General Position
                PointerPressed += OnLocalPointerPressed;
                ActionData = "Click on this window to set position";
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
        if (Selection == 3)
        {
            TrackAction.IsVisible = false;
            InputBox.IsVisible = true;
        }
        else
        {
            TrackAction.IsVisible = true;
            InputBox.IsVisible = false;
        }
    }

    private void InputBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (Selection != 3) return;
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
            _ => InputAction.ActionType.LeftClick
        };
        var _ActionData = _rawActionData;
        Action = new InputAction(_ActionType, _ActionData);
        Callback();
    }

    public void Cancel() => Close();
}
