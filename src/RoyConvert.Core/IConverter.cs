namespace RoyConvert.Core;

public interface IConverter
{
    /// <param name="progress">Receives values in [0, 1]; null means progress is indeterminate.</param>
    Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken);

    /// <summary>Writes all inputs into one output; used for presets with <see cref="Preset.CombinesInputs"/>.</summary>
    Task CombineAsync(IReadOnlyList<string> inputs, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{GetType().Name} cannot combine files.");
}

public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
