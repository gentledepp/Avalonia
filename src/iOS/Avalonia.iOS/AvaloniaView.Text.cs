using System;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using UIKit;

namespace Avalonia.iOS;

public partial class AvaloniaView
{
    private const string ImeLog = "IOSIME";
    private Rect _cursorRect;
    private TextInputOptions? _options;

    private static UIResponder? CurrentAvaloniaResponder { get; set; }
    public override bool BecomeFirstResponder()
    {
        var res = base.BecomeFirstResponder();
        if (res)
            CurrentAvaloniaResponder = this;
        return res;
    }

    public override bool ResignFirstResponder()
    {
        var res = base.ResignFirstResponder();
        if (res && ReferenceEquals(CurrentAvaloniaResponder, this))
            CurrentAvaloniaResponder = null;
        return res;
    }

    private bool IsDrivingText => CurrentAvaloniaResponder is TextInputResponder t && ReferenceEquals(t.NextResponder, this);

    void ITextInputMethodImpl.SetClient(TextInputMethodClient? client)
    {
        if (_client is { } oldClient)
            oldClient.InputPaneActivationRequested -= OnInputPaneActivationRequested;

        _client = client;
        if (_client == null && IsDrivingText)
            BecomeFirstResponder();

        if (_client is { })
        {
            _client.InputPaneActivationRequested += OnInputPaneActivationRequested;
            new TextInputResponder(this, _client).BecomeFirstResponder();
        }
    }

    // The client raises this when the user taps the focused text box. A keyboard that UIKit hid without
    // resigning the responder only comes back if we ask for it: the iPad hide-keyboard key does that, and so
    // does the floating number pad of iPadOS 26, which closes on any tap outside it - and to UIKit every tap
    // in this view is outside it, including the tap that moves the caret inside the text box.
    private void OnInputPaneActivationRequested(object? sender, EventArgs e)
    {
        // Post, so this runs after UIKit has finished with the tap; its own outside-tap dismissal of the
        // number pad would otherwise land after the reload and close the pad again.
        Dispatcher.UIThread.Post(() =>
        {
            if (_client is not { } client || !ReferenceEquals(sender, client))
                return;

            if (CurrentAvaloniaResponder is TextInputResponder responder
                && ReferenceEquals(responder.NextResponder, this)
                && ReferenceEquals(responder.Client, client))
            {
                // A tap that focused the text box has brought the keyboard up already.
                if (responder.FirstResponderTouchSequence != TouchSequence)
                    responder.ShowKeyboard();
            }
            else
            {
                // UIKit resigned our responder while the text box kept its focus.
                new TextInputResponder(this, client).BecomeFirstResponder();
            }
        }, DispatcherPriority.Input);
    }

    void ITextInputMethodImpl.SetCursorRect(Rect rect) => _cursorRect = rect;

    void ITextInputMethodImpl.SetOptions(TextInputOptions options) => _options = options;

    void ITextInputMethodImpl.Reset()
    {
        if (IsDrivingText)
            BecomeFirstResponder();
    }
}
