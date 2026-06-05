namespace MirageBox;

/// <summary>Event args for a button with a display screen.</summary>
public class ImageButtonEventArgs : EventArgs
{
    /// <summary>Zero-based index within the device's image buttons.</summary>
    public int ButtonIndex { get; }
    public bool IsPressed { get; }
    /// <summary>State of all image buttons on the device.</summary>
    public bool[] AllButtonStates { get; }

    public ImageButtonEventArgs(int buttonIndex, bool isPressed, bool[] allStates)
    {
        ButtonIndex = buttonIndex;
        IsPressed = isPressed;
        AllButtonStates = allStates;
    }
}

/// <summary>Event args for a physical button without a display screen.</summary>
public class TactileButtonEventArgs : EventArgs
{
    /// <summary>Zero-based index within the device's tactile buttons.</summary>
    public int ButtonIndex { get; }
    public bool IsPressed { get; }
    /// <summary>State of all tactile buttons on the device.</summary>
    public bool[] AllButtonStates { get; }

    public TactileButtonEventArgs(int buttonIndex, bool isPressed, bool[] allStates)
    {
        ButtonIndex = buttonIndex;
        IsPressed = isPressed;
        AllButtonStates = allStates;
    }
}

/// <summary>Event args for an encoder press or release.</summary>
public class EncoderButtonEventArgs : EventArgs
{
    public int EncoderIndex { get; }
    public bool IsPressed { get; }
    public bool[] AllEncoderStates { get; }

    public EncoderButtonEventArgs(int encoderIndex, bool isPressed, bool[] allStates)
    {
        EncoderIndex = encoderIndex;
        IsPressed = isPressed;
        AllEncoderStates = allStates;
    }
}

/// <summary>Event args for encoder rotation.</summary>
public class EncoderEventArgs : EventArgs
{
    public int EncoderIndex { get; }
    /// <summary>Positive = clockwise, negative = counter-clockwise.</summary>
    public int RotationDelta { get; }

    public EncoderEventArgs(int encoderIndex, int rotationDelta)
    {
        EncoderIndex = encoderIndex;
        RotationDelta = rotationDelta;
    }
}
