namespace MirageBox;

/// <summary>
/// Represents the different types of input events from the device.
/// </summary>
public enum DeviceInputType
{
    /// <summary>No input data</summary>
    None = 0,

    /// <summary>Button press or release</summary>
    ButtonStateChange,

    /// <summary>Encoder/knob press or release</summary>
    EncoderPress,

    /// <summary>Encoder/knob rotation</summary>
    EncoderTwist
}

/// <summary>
/// Represents button event data.
/// </summary>
public class ButtonEventArgs : EventArgs
{
    /// <summary>
    /// Gets the button index that changed.
    /// </summary>
    public int ButtonIndex { get; }

    /// <summary>
    /// Gets a value indicating whether the button is pressed (true) or released (false).
    /// </summary>
    public bool IsPressed { get; }

    /// <summary>
    /// Gets the array of all button states.
    /// </summary>
    public bool[] AllButtonStates { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ButtonEventArgs"/> class.
    /// </summary>
    public ButtonEventArgs(int buttonIndex, bool isPressed, bool[] allStates)
    {
        ButtonIndex = buttonIndex;
        IsPressed = isPressed;
        AllButtonStates = allStates;
    }
}

/// <summary>
/// Represents encoder/knob event data.
/// </summary>
public class EncoderEventArgs : EventArgs
{
    /// <summary>
    /// Gets the encoder index that changed.
    /// </summary>
    public int EncoderIndex { get; }

    /// <summary>
    /// Gets a value indicating whether the encoder is pressed (true) or released (false).
    /// Used for EncoderPressed and EncoderReleased events.
    /// </summary>
    public bool IsPressed { get; }

    /// <summary>
    /// Gets the rotation direction and amount. Positive values indicate clockwise rotation,
    /// negative values indicate counter-clockwise rotation.
    /// </summary>
    public int RotationDelta { get; }

    /// <summary>
    /// Gets the array of all encoder press states.
    /// </summary>
    public bool[] AllEncoderPressStates { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EncoderEventArgs"/> class for press/release events.
    /// </summary>
    public EncoderEventArgs(int encoderIndex, bool isPressed, bool[] allStates)
    {
        EncoderIndex = encoderIndex;
        IsPressed = isPressed;
        RotationDelta = 0;
        AllEncoderPressStates = allStates;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EncoderEventArgs"/> class for rotation events.
    /// </summary>
    public EncoderEventArgs(int encoderIndex, int rotationDelta)
    {
        EncoderIndex = encoderIndex;
        IsPressed = false;
        RotationDelta = rotationDelta;
        AllEncoderPressStates = Array.Empty<bool>();
    }
}
