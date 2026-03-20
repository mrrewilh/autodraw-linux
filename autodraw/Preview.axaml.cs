using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Autodraw;

public partial class Preview : Window
{
    public bool hasStarted = false;
    public SKBitmap? inputBitmap;
    public long lastMovement;
    public Bitmap? renderedBitmap;
    private double scale = 1;
    public PixelPoint primaryScreenBounds;

    private bool drawingStack;
    private List<InputAction> actions = new();
    private List<SKBitmap> stack = new();

    public Preview()
    {
        InitializeComponent();
        Position = new PixelPoint((int)Drawing.LastPos.X, (int)Drawing.LastPos.Y);
        if (Screens.ScreenFromPoint(Position) is null)
        {
            Position = new PixelPoint(0, 0);
        }
        var currScreen = Screens.ScreenFromWindow(this);
        Position = new PixelPoint(Math.Min(Position.X, currScreen.Bounds.Width - 64), Math.Min(Position.Y, currScreen.Bounds.Height - 64));

        scale = currScreen.Scaling;
        _isUpdatingPosition = true;
        XPos.Text = Position.X.ToString();
        YPos.Text = Position.Y.ToString();
        _isUpdatingPosition = false;
        Closing += OnClosing;

        primaryScreenBounds = Screens.Primary.Bounds.TopLeft;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        PreviewImage.Focus();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        renderedBitmap?.Dispose();
        Closing -= OnClosing;
    }

    // ── Keybind handling via Avalonia KeyDown (no SharpHook) ──────────────

    private void Preview_OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Start drawing: ShiftLeft or ShiftRight
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            if (inputBitmap is null && !drawingStack) return;
            e.Handled = true;

            Thread drawThread = new(async () =>
            {
                if (drawingStack)
                    await Drawing.DrawStack(stack, actions, Drawing.LastPos);
                else
                    await Drawing.Draw(inputBitmap, Drawing.LastPos);
            });
            drawThread.Start();
            Dispatcher.UIThread.Invoke(Close);
            return;
        }

        // Stop drawing: Escape
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dispatcher.UIThread.Invoke(Close);
            Dispatcher.UIThread.Invoke(() =>
            {
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.BringIntoView();
                }
            });
        }
    }

    public void ReadyStackDraw(SKBitmap bitmap, List<SKBitmap> _stack, List<InputAction> _actions)
    {
        drawingStack = true;
        renderedBitmap?.Dispose();
        renderedBitmap = bitmap.ConvertToAvaloniaBitmap();
        PreviewImage.Source = renderedBitmap;

        Width = (bitmap.Width) / scale;
        Height = (bitmap.Height) / scale;

        Show();

        actions.Clear();
        actions = _actions;
        stack.Clear();
        stack = _stack;
    }

    public void ReadyDraw(SKBitmap bitmap)
    {
        drawingStack = false;
        renderedBitmap?.Dispose();
        renderedBitmap = bitmap.ConvertToAvaloniaBitmap();
        PreviewImage.Source = renderedBitmap;

        Width = (bitmap.Width) / scale;
        Height = (bitmap.Height) / scale;

        Show();

        inputBitmap = bitmap;
    }

    private bool isMoving = false;
    private PointerPoint _originalPoint;
    private void PreviewImage_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        isMoving = true;
        _originalPoint = e.GetCurrentPoint(this);
    }

    private void PreviewImage_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        isMoving = false;
        Drawing.LastPos = new Vector2(Position.X, Position.Y);
        Config.SetEntry("Preview_LastLockedX", Drawing.LastPos.X.ToString());
        Config.SetEntry("Preview_LastLockedY", Drawing.LastPos.Y.ToString());
    }

    private void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isMoving) return;
        PointerPoint currentPoint = e.GetCurrentPoint(this);
        Position = new PixelPoint(
            Position.X + (XLock.IsChecked == false ? (int)(currentPoint.Position.X - _originalPoint.Position.X) : 0),
            Position.Y + (YLock.IsChecked == false ? (int)(currentPoint.Position.Y - _originalPoint.Position.Y) : 0)
        );
        Drawing.LastPos = new Vector2(Position.X, Position.Y);
        _isUpdatingPosition = true;
        XPos.Text = Position.X.ToString();
        YPos.Text = Position.Y.ToString();
        _isUpdatingPosition = false;
    }

    private bool _isUpdatingPosition;
    private void XPos_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPosition) return;
        _isUpdatingPosition = true;

        float xValue = 0f;
        if (!string.IsNullOrWhiteSpace(XPos.Text))
        {
            string filteredInput = new string(XPos.Text.Where(char.IsDigit).ToArray());
            _ = float.TryParse(filteredInput, out xValue);
        }

        Drawing.LastPos = new Vector2(xValue, Position.Y);
        Position = new PixelPoint((int)Drawing.LastPos.X, (int)Drawing.LastPos.Y);
        _isUpdatingPosition = false;
    }

    private void YPos_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPosition) return;
        _isUpdatingPosition = true;

        float yValue = 0f;
        if (!string.IsNullOrWhiteSpace(YPos.Text))
        {
            string filteredInput = new string(YPos.Text.Where(char.IsDigit).ToArray());
            _ = float.TryParse(filteredInput, out yValue);
        }

        Drawing.LastPos = new Vector2(Position.X, yValue);
        Position = new PixelPoint((int)Drawing.LastPos.X, (int)Drawing.LastPos.Y);
        _isUpdatingPosition = false;
    }

    private void XLock_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        XPos.IsEnabled = XLock.IsChecked == false;
    }

    private void YLock_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        YPos.IsEnabled = YLock.IsChecked == false;
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        EditPanel.IsVisible = !EditPanel.IsVisible;
    }
}
