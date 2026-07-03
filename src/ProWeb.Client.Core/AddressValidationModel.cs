namespace ProWeb.Client.Core;

/// <summary>Inline validation state for the address bar, driving error visuals (UT-X-R1-002).</summary>
public enum AddressFieldState
{
    Empty,
    Url,
    Search,
    Invalid,
}

/// <summary>Result of evaluating address-bar text without navigating.</summary>
public sealed class AddressValidationResult
{
    public AddressValidationResult(AddressFieldState state, string message)
    {
        State = state;
        Message = message;
    }

    public AddressFieldState State { get; }

    public string Message { get; }

    /// <summary>True when the field should show an error affordance (red border + message).</summary>
    public bool IsError => State == AddressFieldState.Invalid;
}

/// <summary>Classifies address-bar input for inline UI feedback, reusing <see cref="UrlNormalizer"/>.</summary>
public static class AddressValidationModel
{
    public static AddressValidationResult Evaluate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new AddressValidationResult(AddressFieldState.Empty, string.Empty);

        return UrlNormalizer.Classify(input) switch
        {
            UrlNormalizer.AddressInputKind.Url =>
                new AddressValidationResult(AddressFieldState.Url, string.Empty),
            UrlNormalizer.AddressInputKind.Search =>
                new AddressValidationResult(AddressFieldState.Search, "将作为搜索词打开"),
            _ => new AddressValidationResult(
                AddressFieldState.Invalid, "无法识别的地址，请输入有效的网址或搜索词。"),
        };
    }
}
