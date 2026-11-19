using Avalonia.Input.TextInput;
using Foundation;
using UIKit;

namespace Avalonia.iOS;

partial class AvaloniaView
{
    partial class TextInputResponder
    {
        [Export("autocapitalizationType")]
        public UITextAutocapitalizationType AutocapitalizationType { get; private set; }

        [Export("autocorrectionType")]
        public UITextAutocorrectionType AutocorrectionType =>
            _view._options?.ShowSuggestions == false ?
                UITextAutocorrectionType.No :
                UITextAutocorrectionType.Yes;

        [Export("keyboardType")]
        public UIKeyboardType KeyboardType =>
            _view._options == null ?
                UIKeyboardType.Default :
                _view._options.ContentType switch
                {
                    TextInputContentType.Alpha => UIKeyboardType.AsciiCapable,
                    TextInputContentType.Digits => UIKeyboardType.PhonePad,
                    TextInputContentType.Pin => UIKeyboardType.NumberPad,
                    TextInputContentType.Number => UIKeyboardType.DecimalPad,
                    TextInputContentType.Email => UIKeyboardType.EmailAddress,
                    TextInputContentType.Url => UIKeyboardType.Url,
                    TextInputContentType.Name => UIKeyboardType.NamePhonePad,
                    TextInputContentType.Social => UIKeyboardType.Twitter,
                    TextInputContentType.Search => UIKeyboardType.WebSearch,
                    _ => UIKeyboardType.Default
                };

        [Export("returnKeyType")]
        public UIReturnKeyType ReturnKeyType
        {
            get
            {
                if (_view._options != null)
                {
                    return _view._options.ReturnKeyType switch
                    {
                        TextInputReturnKeyType.Done => UIReturnKeyType.Done,
                        TextInputReturnKeyType.Go => UIReturnKeyType.Go,
                        TextInputReturnKeyType.Search => UIReturnKeyType.Search,
                        TextInputReturnKeyType.Next => UIReturnKeyType.Next,
                        TextInputReturnKeyType.Return => UIReturnKeyType.Default,
                        TextInputReturnKeyType.Send => UIReturnKeyType.Send,
                        _ => _view._options.Multiline ? UIReturnKeyType.Default : UIReturnKeyType.Done
                    };
                }

                return UIReturnKeyType.Default;
            }
        } 

        [Export("enablesReturnKeyAutomatically")]
        public bool EnablesReturnKeyAutomatically { get; set; }

        [Export("isSecureTextEntry")] public bool IsSecureEntry =>
            _view._options?.ContentType is TextInputContentType.Password or TextInputContentType.Pin 
            || (_view._options?.IsSensitive ?? false);

        [Export("spellCheckingType")]
        public UITextSpellCheckingType SpellCheckingType => 
            _view._options?.ShowSuggestions == false ?
                UITextSpellCheckingType.No :
                UITextSpellCheckingType.Yes;

        [Export("textContentType")] public NSString TextContentType { get; set; } = new NSString("text/plain");

        [Export("smartQuotesType")]
        public UITextSmartQuotesType SmartQuotesType { get; set; } = UITextSmartQuotesType.Default;

        [Export("smartDashesType")]
        public UITextSmartDashesType SmartDashesType { get; set; } = UITextSmartDashesType.Default;

        [Export("smartInsertDeleteType")]
        public UITextSmartInsertDeleteType SmartInsertDeleteType { get; set; } = UITextSmartInsertDeleteType.Default;

        [Export("passwordRules")] public UITextInputPasswordRules? PasswordRules { get; set; } = null!;

        // iPadOS 26.2+ asks whether a number pad may be the small floating popover instead of the numbers
        // page of the full keyboard. Older versions never call it.
        [Export("allowsNumberPadPopover")]
        public bool AllowsNumberPadPopover => Platform.Options?.AllowNumberPadPopover ?? false;

        public NSObject? WeakInputDelegate
        {
            get;
            set;
        }

        NSObject IUITextInput.WeakTokenizer => _tokenizer;
        
    }
}
