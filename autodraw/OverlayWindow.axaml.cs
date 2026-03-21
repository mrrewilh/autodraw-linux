using System;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;

namespace Autodraw;

public partial class OverlayWindow : Window
{
    public Vector2? CapturedPosition { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        CapturedPosition = new Vector2((int)pos.X, (int)pos.Y);
        Close();
    }
}
