using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaDialogs.Views;

namespace MirageBox.Oasis.Desktop.Views;

/// <summary>
/// A prompt with a single text field and accept/cancel buttons. ShowAsync
/// returns the entered text, or no value when dismissed. An optional
/// <see cref="Validate"/> callback keeps the dialog open and shows the
/// returned message instead of closing.
/// </summary>
public partial class TextInputDialog : BaseDialog<string>
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<TextInputDialog, string>(nameof(Message));

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TextInputDialog, string>(nameof(Text), "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<string> PositiveTextProperty =
        AvaloniaProperty.Register<TextInputDialog, string>(nameof(PositiveText), "Accept");

    public string PositiveText
    {
        get => GetValue(PositiveTextProperty);
        set => SetValue(PositiveTextProperty, value);
    }

    public static readonly StyledProperty<string> NegativeTextProperty =
        AvaloniaProperty.Register<TextInputDialog, string>(nameof(NegativeText), "Nevermind");

    public string NegativeText
    {
        get => GetValue(NegativeTextProperty);
        set => SetValue(NegativeTextProperty, value);
    }

    public static readonly StyledProperty<string?> ErrorTextProperty =
        AvaloniaProperty.Register<TextInputDialog, string?>(nameof(ErrorText));

    public string? ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set
        {
            SetValue(ErrorTextProperty, value);
            HasError = !string.IsNullOrEmpty(value);
        }
    }

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<TextInputDialog, bool>(nameof(HasError));

    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    /// <summary>Returns an error message to reject the value, or null to accept.</summary>
    public Func<string, string?>? Validate { get; set; }

    public TextInputDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    protected override void DoInitialFocus()
    {
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void TryAccept()
    {
        var value = Text?.Trim() ?? "";
        var error = Validate?.Invoke(value);
        if (error != null)
        {
            ErrorText = error;
            return;
        }
        Close(value);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryAccept();
            e.Handled = true;
        }
    }

    public void PositiveButtonClicked(object? sender, RoutedEventArgs e) => TryAccept();

    public void NegativeButtonClicked(object? sender, RoutedEventArgs e) => Close();
}
